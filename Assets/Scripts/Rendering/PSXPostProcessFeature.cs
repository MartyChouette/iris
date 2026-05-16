using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>Bayer dither pattern size.</summary>
public enum DitherPattern
{
    Bayer2x2 = 0,
    Bayer4x4 = 1,
    Bayer8x8 = 2
}

/// <summary>
/// URP ScriptableRendererFeature that handles PSX-style post-processing:
/// resolution downscale (pixelation) + color depth reduction + ordered dithering
/// + tilt-shift blur. Settings on the feature are the defaults; a PSXPostProcessVolume
/// on the volume stack overrides them when present.
/// </summary>
public class PSXPostProcessFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Resolution")]
        [Tooltip("Divide screen resolution by this value. Higher = chunkier pixels.")]
        [Range(1f, 6f)] public float resolutionDivisor = 3f;

        [Header("Color")]
        [Tooltip("Color levels per channel (lower = more posterized).")]
        [Range(4, 256)] public float colorDepth = 32f;

        [Tooltip("Ordered dither strength.")]
        [Range(0, 1)] public float ditherIntensity = 0.5f;

        [Tooltip("Dither pattern for mid-shadows.")]
        public DitherPattern finePattern = DitherPattern.Bayer4x4;

        [Tooltip("Dither pattern for deep shadows.")]
        public DitherPattern coarsePattern = DitherPattern.Bayer2x2;

        [Tooltip("Luminance above which no dither appears.")]
        [Range(0, 1)] public float shadowThreshold = 0.5f;

        [Tooltip("Luminance below which the coarse pattern fully takes over.")]
        [Range(0, 1)] public float deepShadowThreshold = 0.15f;

        [Header("Zoom Scaling")]
        [Tooltip("Camera ortho size at which dither is 1:1. Dither scales with zoom relative to this. 0 = no zoom scaling.")]
        public float ditherZoomReference = 5f;
    }

    public Settings settings = new Settings();

    [Header("Shader Reference")]
    [Tooltip("Drag PSXPost shader here. Shader.Find does not work in builds.")]
    [SerializeField] private Shader _postShader;

    private PSXPostProcessPass _pass;
    private Material _material;

    public override void Create()
    {
        var shader = _postShader != null ? _postShader : Shader.Find("Iris/Fullscreen/PSXPost");
        if (shader == null)
        {
            Debug.LogWarning("[PSXPostProcessFeature] Shader 'Iris/Fullscreen/PSXPost' not found. " +
                             "Assign it in the Renderer asset's feature settings.");
            return;
        }

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new PSXPostProcessPass(_material);
        _pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null || _pass == null)
            return;

        _pass.UpdateSettings(settings);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
    }

    // ─────────────────────────────────────────────────────────────────
    // Render Pass (RenderGraph API for Unity 6 URP)
    // ─────────────────────────────────────────────────────────────────
    class PSXPostProcessPass : ScriptableRenderPass
    {
        private Material _material;
        private Settings _settings;

        private static readonly int ColorDepthID = Shader.PropertyToID("_ColorDepth");
        private static readonly int DitherIntensityID = Shader.PropertyToID("_DitherIntensity");
        private static readonly int DitherResolutionID = Shader.PropertyToID("_DitherResolution");
        private static readonly int FinePatternID = Shader.PropertyToID("_FinePattern");
        private static readonly int CoarsePatternID = Shader.PropertyToID("_CoarsePattern");
        private static readonly int ShadowThresholdID = Shader.PropertyToID("_ShadowThreshold");
        private static readonly int DeepShadowThresholdID = Shader.PropertyToID("_DeepShadowThreshold");
        private static readonly int DitherZoomID = Shader.PropertyToID("_DitherZoom");

        public PSXPostProcessPass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler("PSX Post Process");
        }

        public void UpdateSettings(Settings s) => _settings = s;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _settings == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            // Read from volume stack if available, otherwise use feature defaults
            float resDivisor = _settings.resolutionDivisor;
            float colorDepth = _settings.colorDepth;
            float ditherIntensity = _settings.ditherIntensity;
            int finePattern = (int)_settings.finePattern;
            int coarsePattern = (int)_settings.coarsePattern;
            float shadowThreshold = _settings.shadowThreshold;
            float deepShadowThreshold = _settings.deepShadowThreshold;

            var vol = VolumeManager.instance.stack.GetComponent<PSXPostProcessVolume>();
            if (vol != null && vol.active)
            {
                if (vol.resolutionDivisor.overrideState) resDivisor = vol.resolutionDivisor.value;
                if (vol.colorDepth.overrideState) colorDepth = vol.colorDepth.value;
                if (vol.ditherIntensity.overrideState) ditherIntensity = vol.ditherIntensity.value;
                if (vol.finePattern.overrideState) finePattern = (int)vol.finePattern.value;
                if (vol.coarsePattern.overrideState) coarsePattern = (int)vol.coarsePattern.value;
                if (vol.shadowThreshold.overrideState) shadowThreshold = vol.shadowThreshold.value;
                if (vol.deepShadowThreshold.overrideState) deepShadowThreshold = vol.deepShadowThreshold.value;
            }

            var source = resourceData.activeColorTexture;
            var sourceDesc = renderGraph.GetTextureDesc(source);

            // Update material properties
            _material.SetFloat(ColorDepthID, colorDepth);
            _material.SetFloat(DitherIntensityID, ditherIntensity);
            _material.SetFloat(FinePatternID, finePattern);
            _material.SetFloat(CoarsePatternID, coarsePattern);
            _material.SetFloat(ShadowThresholdID, shadowThreshold);
            _material.SetFloat(DeepShadowThresholdID, deepShadowThreshold);

            // Compute zoom factor from camera ortho size
            float ditherZoom = 1f;
            float zoomRef = _settings.ditherZoomReference;
            var vol2 = vol; // reuse vol from above
            if (vol2 != null && vol2.active && vol2.ditherZoomReference.overrideState)
                zoomRef = vol2.ditherZoomReference.value;
            if (zoomRef > 0f)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var cam = cameraData.camera;
                float camSize = cam.orthographic
                    ? cam.orthographicSize
                    : cam.fieldOfView * 0.5f;
                ditherZoom = camSize / zoomRef;
            }
            _material.SetFloat(DitherZoomID, ditherZoom);

            float div = Mathf.Max(resDivisor, 1f);
            int lowW = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.width / div));
            int lowH = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.height / div));
            _material.SetVector(DitherResolutionID, new Vector4(lowW, lowH, 0f, 0f));

            // ── Step 1: Downscale camera → low-res (plain copy) ──
            var lowResDesc = new TextureDesc(lowW, lowH)
            {
                colorFormat = sourceDesc.colorFormat,
                filterMode = FilterMode.Point,
                name = "_PSXLowRes"
            };
            var lowRes = renderGraph.CreateTexture(lowResDesc);

            renderGraph.AddBlitPass(source, lowRes, Vector2.one, Vector2.zero,
                passName: "PSX Downscale");

            // ── Step 2: Apply shader + upscale low-res → destination ──
            var destDesc = new TextureDesc(sourceDesc.width, sourceDesc.height)
            {
                colorFormat = sourceDesc.colorFormat,
                filterMode = FilterMode.Point,
                name = "_PSXOutput"
            };
            var dest = renderGraph.CreateTexture(destDesc);

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(
                lowRes, dest, _material, 0);
            renderGraph.AddBlitPass(blitParams, passName: "PSX Color Reduce");

            resourceData.cameraColor = dest;
        }
    }
}
