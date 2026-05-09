using UnityEngine;

/// <summary>
/// Spawns a burst of billboard particles, each playing the puff flipbook
/// animation over its lifetime. Particle cloud feel with hand-drawn cards.
/// Self-destructs after particles finish.
/// </summary>
public static class SmokePoof
{
    private static Texture2D s_atlas;
    private static int s_frameCount;

    private static void EnsureAtlas()
    {
        if (s_atlas != null) return;
        var frames = FlipbookAtlas.LoadFrames("Particles", "puff_0", 6);
        if (frames != null && frames.Length > 0)
        {
            s_atlas = FlipbookAtlas.Build(frames);
            s_frameCount = frames.Length;
        }
    }

    public static void Spawn(Vector3 position, float radius = 0.15f, Color? color = null)
    {
        EnsureAtlas();
        var go = new GameObject("SmokePoof");
        go.transform.position = position;

        // Render on the overlay camera so poofs draw on top
        int heldLayer = LayerMask.NameToLayer("HeldItem");
        if (heldLayer >= 0)
            go.layer = heldLayer;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float smokeScale = VisualScaleSettings.Instance.GetSmokeScale();

        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f * smokeScale, 0.1f * smokeScale);
        main.gravityModifier = -0.1f;
        main.maxParticles = 15;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Color c = color ?? new Color(0.85f, 0.82f, 0.78f, 0.7f);
        main.startColor = new ParticleSystem.MinMaxGradient(c, new Color(c.r, c.g, c.b, c.a * 0.5f));

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1f, 0f)
        ));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

        // Material — each particle is a billboard playing the flipbook
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            // Transparent alpha-blended — so PNG transparency works
            mat.SetFloat("_Surface", 1f); // 1 = Transparent
            mat.SetFloat("_Blend", 0f);   // 0 = Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            if (s_atlas != null)
            {
                mat.mainTexture = s_atlas;
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = s_frameCount;
                tsa.numTilesY = 1;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.cycleCount = 1;
                tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            }
            renderer.material = mat;
        }

        ps.Play();
    }
}
