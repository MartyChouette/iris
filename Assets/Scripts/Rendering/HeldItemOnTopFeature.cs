using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Custom URP render feature that draws held items on top of the scene
/// while preserving self-occlusion.
///
/// How it works:
///   1. Clears the depth buffer (scene depth is wiped).
///   2. Draws held-item layer with normal ZTest (LEqual).
///
/// Because depth was cleared, held items always appear in front of the
/// scene. Because ZTest is normal, front faces correctly occlude back
/// faces — no ghosting, no see-through.
/// </summary>
public class HeldItemOnTopFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Layer mask for held items (should match the HeldItem layer).")]
        public LayerMask layerMask = 0;
    }

    public Settings settings = new Settings();
    private HeldItemPass _pass;

    public override void Create()
    {
        _pass = new HeldItemPass(settings.layerMask);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }

    class HeldItemPass : ScriptableRenderPass
    {
        private readonly int _layerMask;

        private static readonly List<ShaderTagId> s_tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public HeldItemPass(LayerMask mask)
        {
            _layerMask = mask;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("HeldItemOnTop");

            // Wipe depth — held items become the only depth reference
            cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1.0f, 0);
            ctx.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Opaque held items: front-to-back, normal depth
            var opaqueSettings = CreateDrawingSettings(s_tags, ref data, SortingCriteria.CommonOpaque);
            var opaqueFilter = new FilteringSettings(RenderQueueRange.opaque, _layerMask);
            ctx.DrawRenderers(data.cullResults, ref opaqueSettings, ref opaqueFilter);

            // Transparent held items: back-to-front, normal depth
            var transSettings = CreateDrawingSettings(s_tags, ref data, SortingCriteria.CommonTransparent);
            var transFilter = new FilteringSettings(RenderQueueRange.transparent, _layerMask);
            ctx.DrawRenderers(data.cullResults, ref transSettings, ref transFilter);

            CommandBufferPool.Release(cmd);
        }
    }
}
