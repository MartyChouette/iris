using UnityEngine;

/// <summary>
/// Static utility that spawns a quick smoke poof particle burst at a world position.
/// Used when items appear from cushions, book collections, etc.
/// Self-destructs after the particles finish.
/// </summary>
public static class SmokePoof
{
    public static void Spawn(Vector3 position, float radius = 0.15f, Color? color = null)
    {
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
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
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
            renderer.material = mat;
        }

        ps.Play();
    }
}
