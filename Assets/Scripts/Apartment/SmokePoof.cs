using UnityEngine;

/// <summary>
/// Static utility that spawns a quick smoke poof particle burst at a world position.
/// Used when items appear from cushions, book collections, etc.
/// Self-destructs after the particles finish.
/// </summary>
public static class SmokePoof
{
    /// <summary>Assign a sprite to use for smoke particles instead of the default blob. Set via SmokePoof.ParticleSprite = yourSprite.</summary>
    public static Texture2D ParticleSprite { get; set; }

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

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        float smokeScale = VisualScaleSettings.Instance.GetSmokeScale();
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

        // Velocity — all axes same mode to avoid Unity warning
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

        // Material
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            if (s_atlas != null)
            {
                mat.mainTexture = s_atlas;
                renderer.material = mat;
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = s_frameCount;
                tsa.numTilesY = 1;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.cycleCount = 1;
                tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            }
            else
            {
                if (ParticleSprite != null)
                    mat.mainTexture = ParticleSprite;
                renderer.material = mat;
            }
        }

        ps.Play();
    }
}
