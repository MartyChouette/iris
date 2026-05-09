using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// URP ScriptableRendererFeature that handles PSX-style post-processing:
/// resolution downscale (pixelation) + color depth reduction + ordered dithering
/// + tilt-shift blur. All settings come from the PSXPostProcessVolume on the
/// volume stack — add it to any Volume profile.
/// </summary>
public class PSXPostProcessFeature : ScriptableRendererFeature
{
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

        private static readonly int ColorDepthID = Shader.PropertyToID("_ColorDepth");
        private static readonly int DitherIntensityID = Shader.PropertyToID("_DitherIntensity");
        private static readonly int DitherResolutionID = Shader.PropertyToID("_DitherResolution");
        private static readonly int DitherShadowBiasID = Shader.PropertyToID("_DitherShadowBias");

        public PSXPostProcessPass(Material material)
        {
            _material = material;
            profilingSampler = new ProfilingSampler("PSX Post Process");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            // Read settings from the volume stack
            var vol = VolumeManager.instance.stack.GetComponent<PSXPostProcessVolume>();
            if (vol == null || !vol.active) return;

            float resDivisor = vol.resolutionDivisor.value;
            float colorDepth = vol.colorDepth.value;
            float ditherIntensity = vol.ditherIntensity.value;
            float ditherShadowBias = vol.ditherShadowBias.value;

            var source = resourceData.activeColorTexture;
            var sourceDesc = renderGraph.GetTextureDesc(source);

            // Update material properties
            _material.SetFloat(ColorDepthID, colorDepth);
            _material.SetFloat(DitherIntensityID, ditherIntensity);
            _material.SetFloat(DitherShadowBiasID, ditherShadowBias);

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
