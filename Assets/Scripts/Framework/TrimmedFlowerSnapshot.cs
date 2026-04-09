using UnityEngine;

/// <summary>
/// Static utility that deep-clones a trimmed flower into a visual-only
/// GameObject. Instantiates the entire hierarchy, then strips every
/// non-rendering component (Rigidbody, Joint, Collider, MonoBehaviour)
/// so the result is a static, gravity-free copy of exactly what the
/// player sees at the end of trimming.
/// </summary>
public static class TrimmedFlowerSnapshot
{
    /// <summary>
    /// Deep-clone the flower brain's entire hierarchy and strip it down
    /// to pure visuals — no physics, no logic, no particles.
    /// </summary>
    public static GameObject Capture(FlowerGameBrain brain)
    {
        if (brain == null)
        {
            Debug.LogWarning("[TrimmedFlowerSnapshot] Brain is null — returning empty GO.");
            return new GameObject("TrimmedFlower_Empty");
        }

        // 1. Deep clone the entire flower hierarchy as-is
        var clone = Object.Instantiate(brain.gameObject);
        clone.name = "TrimmedFlower";

        // 2. Strip everything that isn't a visual component
        StripNonVisuals(clone);

        int rendererCount = clone.GetComponentsInChildren<Renderer>().Length;
        Debug.Log($"[TrimmedFlowerSnapshot] Captured {rendererCount} renderer(s) from '{brain.name}'.");

        return clone;
    }

    /// <summary>
    /// Remove all physics, logic, and particle components from the clone,
    /// keeping only Transform, MeshFilter, MeshRenderer, and SkinnedMeshRenderer.
    /// </summary>
    private static void StripNonVisuals(GameObject root)
    {
        // Order matters: joints reference rigidbodies, so destroy joints first.

        // Joints (all types inherit from Joint)
        foreach (var j in root.GetComponentsInChildren<Joint>(true))
            Object.DestroyImmediate(j);

        // Rigidbodies
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(rb);

        // Colliders
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(col);

        // Particle systems + their renderers
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Object.DestroyImmediate(ps);
        }
        foreach (var psr in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            Object.DestroyImmediate(psr);

        // All MonoBehaviours (scripts) — renderers are Components, not MonoBehaviours,
        // so MeshRenderer / SkinnedMeshRenderer survive this pass.
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            Object.DestroyImmediate(mb);

        // Animators (would keep playing bone animations)
        foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(anim);

        // Animations (legacy)
        foreach (var anim in root.GetComponentsInChildren<Animation>(true))
            Object.DestroyImmediate(anim);

        // Clean up empty GameObjects with no renderers (leftover anchors, etc.)
        // Do a bottom-up pass so children are checked before parents.
        PruneEmptyChildren(root.transform);
    }

    /// <summary>
    /// Recursively destroy child GameObjects that have no Renderer anywhere
    /// in their subtree. Keeps the hierarchy lean.
    /// </summary>
    private static void PruneEmptyChildren(Transform parent)
    {
        // Iterate backwards since we may destroy children
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            PruneEmptyChildren(child);

            // After pruning grandchildren, if this child has no renderers left, remove it
            if (child.GetComponentInChildren<Renderer>(true) == null)
                Object.DestroyImmediate(child.gameObject);
        }
    }
}
