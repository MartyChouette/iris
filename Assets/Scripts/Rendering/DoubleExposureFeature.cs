using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// Feedback-buffer double exposure effect. Keeps a decaying history of previous
/// frames and blends it over the current frame with a screen blend, creating
/// luminous ghosting trails — like photographic double exposure + bloom.
/// </summary>
public class DoubleExposureFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("How fast old frames fade out. Lower = longer ghosting.")]
        [Range(0.01f, 0.5f)] public float feedbackDecay = 0.05f;

        [Tooltip("How strongly the ghost layer blends over the scene.")]
        [Range(0f, 1f)] public float blendIntensity = 0.35f;

        [Tooltip("Blur radius on the feedback accumulation (softer = dreamier).")]
        [Range(0f, 6f)] public float blurRadius = 2f;

        [Tooltip("Luminance threshold for extra bloom glow on bright ghost areas.")]
        [Range(0f, 1.5f)] public float bloomThreshold = 0.4f;

        [Tooltip("Softness of the bloom threshold falloff.")]
        [Range(0f, 1f)] public float bloomSoftness = 0.5f;

        [Tooltip("Resolution divisor for the feedback buffer (2 = half-res, saves perf).")]
        [Range(1, 4)] public int bufferDivisor = 2;
    }

    public Settings settings = new Settings();

    [Header("Shader Reference")]
    [Tooltip("Drag DoubleExposure shader here for builds.")]
    [SerializeField] private Shader _shader;

    private DoubleExposurePass _pass;
    private Material _material;

    // Persistent feedback buffers owned by the feature (survive across frames)
    private RTHandle _feedbackA;
    private RTHandle _feedbackB;
    private bool _useA = true;
    private int _fbWidth, _fbHeight;

    public override void Create()
    {
        var shader = _shader != null ? _shader : Shader.Find("Iris/Fullscreen/DoubleExposure");
        if (shader == null)
        {
            Debug.LogWarning("[DoubleExposureFeature] Shader not found.");
            return;
        }

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new DoubleExposurePass(_material, settings);
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null) return;
        if (settings.blendIntensity <= 0f) return;

        var desc = renderingData.cameraData.cameraTargetDescriptor;
        int w = Mathf.Max(1, desc.width / settings.bufferDivisor);
        int h = Mathf.Max(1, desc.height / settings.bufferDivisor);
        EnsureFeedbackBuffers(w, h);

        var read = _useA ? _feedbackA : _feedbackB;
        var write = _useA ? _feedbackB : _feedbackA;

        _pass.Setup(settings, read, write);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);

        _useA = !_useA;
    }

    private void EnsureFeedbackBuffers(int w, int h)
    {
        if (_feedbackA != null && _fbWidth == w && _fbHeight == h)
            return;

        _feedbackA?.Release(); _feedbackA = null;
        _feedbackB?.Release(); _feedbackB = null;

        _fbWidth = w;
        _fbHeight = h;
        var format = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
        _feedbackA = RTHandles.Alloc(w, h, colorFormat: format, filterMode: FilterMode.Bilinear, name: "_DblExpFBA");
        _feedbackB = RTHandles.Alloc(w, h, colorFormat: format, filterMode: FilterMode.Bilinear, name: "_DblExpFBB");

        // Clear
        var cmd = CommandBufferPool.Get("ClearDoubleExposure");
        cmd.SetRenderTarget(_feedbackA);
        cmd.ClearRenderTarget(false, true, Color.black);
        cmd.SetRenderTarget(_feedbackB);
        cmd.ClearRenderTarget(false, true, Color.black);
        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
        _feedbackA?.Release(); _feedbackA = null;
        _feedbackB?.Release(); _feedbackB = null;
    }

    // ─────────────────────────────────────────────────────────────────
    class DoubleExposurePass : ScriptableRenderPass
    {
        private Material _material;
        private Settings _settings;
        private RTHandle _readFeedback;
        private RTHandle _writeFeedback;

        private static readonly int FeedbackTexID = Shader.PropertyToID("_FeedbackTex");
        private static readonly int FeedbackDecayID = Shader.PropertyToID("_FeedbackDecay");
        private static readonly int BlendIntensityID = Shader.PropertyToID("_BlendIntensity");
        private static readonly int BlurRadiusID = Shader.PropertyToID("_BlurRadius");
        private static readonly int BloomThresholdID = Shader.PropertyToID("_BloomThreshold");
        private static readonly int BloomSoftnessID = Shader.PropertyToID("_BloomSoftness");

        public DoubleExposurePass(Material material, Settings settings)
        {
            _material = material;
            _settings = settings;
            profilingSampler = new ProfilingSampler("Double Exposure");
        }

        public void Setup(Settings settings, RTHandle readFeedback, RTHandle writeFeedback)
        {
            _settings = settings;
            _readFeedback = readFeedback;
            _writeFeedback = writeFeedback;
        }

        private class FeedbackPassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle readFeedback;
            public TextureHandle writeFeedback;
            public float decay;
            public float blurRadius;
        }

        private class CompositePassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle feedback;
            public TextureHandle dest;
            public float blendIntensity;
            public float bloomThreshold;
            public float bloomSoftness;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            var cameraColor = resourceData.activeColorTexture;
            var cameraDesc = renderGraph.GetTextureDesc(cameraColor);

            // Import persistent feedback buffers into the render graph
            var readFB = renderGraph.ImportTexture(_readFeedback);
            var writeFB = renderGraph.ImportTexture(_writeFeedback);

            // Create temp texture for composite output
            var tempDesc = new TextureDesc(cameraDesc.width, cameraDesc.height)
            {
                colorFormat = cameraDesc.colorFormat,
                filterMode = FilterMode.Bilinear,
                name = "_DblExpTemp"
            };
            var tempTex = renderGraph.CreateTexture(tempDesc);

            // ── Pass 1: Update feedback buffer ──
            using (var builder = renderGraph.AddRasterRenderPass<FeedbackPassData>("Double Exposure Feedback", out var passData))
            {
                passData.material = _material;
                passData.source = cameraColor;
                passData.readFeedback = readFB;
                passData.writeFeedback = writeFB;
                passData.decay = _settings.feedbackDecay;
                passData.blurRadius = _settings.blurRadius;

                builder.UseTexture(cameraColor);
                builder.UseTexture(readFB);
                builder.SetRenderAttachment(writeFB, 0);

                builder.SetRenderFunc((FeedbackPassData data, RasterGraphContext ctx) =>
                {
                    data.material.SetFloat(FeedbackDecayID, data.decay);
                    data.material.SetFloat(BlurRadiusID, data.blurRadius);
                    data.material.SetTexture(FeedbackTexID, data.readFeedback);
                    Blitter.BlitTexture(ctx.cmd, data.source, Vector2.one, data.material, 1);
                });
            }

            // ── Pass 2: Composite feedback over scene ──
            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("Double Exposure Composite", out var passData))
            {
                passData.material = _material;
                passData.source = cameraColor;
                passData.feedback = writeFB;
                passData.dest = tempTex;
                passData.blendIntensity = _settings.blendIntensity;
                passData.bloomThreshold = _settings.bloomThreshold;
                passData.bloomSoftness = _settings.bloomSoftness;

                builder.UseTexture(cameraColor);
                builder.UseTexture(writeFB);
                builder.SetRenderAttachment(tempTex, 0);

                builder.SetRenderFunc((CompositePassData data, RasterGraphContext ctx) =>
                {
                    data.material.SetFloat(BlendIntensityID, data.blendIntensity);
                    data.material.SetFloat(BloomThresholdID, data.bloomThreshold);
                    data.material.SetFloat(BloomSoftnessID, data.bloomSoftness);
                    data.material.SetTexture(FeedbackTexID, data.feedback);
                    Blitter.BlitTexture(ctx.cmd, data.source, Vector2.one, data.material, 0);
                });
            }

            // ── Pass 3: Copy result back to camera color ──
            renderGraph.AddBlitPass(tempTex, cameraColor, Vector2.one, Vector2.zero, passName: "Double Exposure Copy Back");

            resourceData.cameraColor = cameraColor;
        }
    }
}
