// File: RopeParticleFollowerInitialOffset.cs
// Follows an Obi rope particle, preserving the initial world-space offset.
// Place your object in the scene where you want it relative to the rope,
// set rope + actorIndex, and it will stick with that initial offset.
// Works without subscribing to solver events.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeParticleFollowers : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;
    [Tooltip("Actor-space particle index to follow (0..activeParticleCount-1).")]
    public int actorIndex = 0;

    [Header("Behavior")]
    [Tooltip("If true, we smooth movement towards the target.")]
    public bool smooth = true;
    [Tooltip("How quickly to move towards the target when smoothing is enabled.")]
    public float posLerp = 24f;

    [Tooltip("Recompute offset at runtime (useful if you move the object in play mode).")]
    public KeyCode rebindKey = KeyCode.None;

    // captured at runtime
    private Vector3 _initialOffsetWS;
    private bool _bound;

    void Reset()
    {
        if (!rope) rope = GetComponentInParent<ObiRopeBase>();
    }

    void OnEnable()
    {
        TryBindOffset();
    }

    void LateUpdate()
    {
        if (rebindKey != KeyCode.None && Input.GetKeyDown(rebindKey))
            TryBindOffset();

        if (!_bound || rope == null || rope.solver == null) return;

        Vector3 p = GetParticleWorldPosition(rope, actorIndex);
        Vector3 target = p + _initialOffsetWS;

        if (smooth)
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-posLerp * Time.deltaTime));
        else
            transform.position = target;
        // rotation intentionally untouched (keep your authored rotation)
    }

    // Capture current offset from particle to this object in world space.
    public void TryBindOffset()
    {
        if (rope == null || rope.solver == null) { _bound = false; return; }

        actorIndex = Mathf.Clamp(actorIndex, 0, rope.activeParticleCount );
        Vector3 p = GetParticleWorldPosition(rope, actorIndex);
        _initialOffsetWS = transform.position - p;
        _bound = true;
    }

    // Helper to read a particle's world-space position (renderable if available).
    private static Vector3 GetParticleWorldPosition(ObiRopeBase rope, int actorIdx)
    {
        actorIdx = Mathf.Clamp(actorIdx, 0, rope.activeParticleCount - 1);

        // actor index -> solver index
        var solverIndex = rope.solverIndices[actorIdx];

        // prefer renderable positions for smoother visuals
        var solver = rope.solver;
        Vector3 solverPos;
        if (solver.renderablePositions != null && solver.renderablePositions.count > solverIndex)
            solverPos = (Vector3)solver.renderablePositions[solverIndex];
        else
            solverPos = (Vector3)solver.positions[solverIndex];

        return solver.transform.TransformPoint(solverPos);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (rope == null || rope.solver == null) return;
        var p = GetParticleWorldPosition(rope, Mathf.Clamp(actorIndex, 0, Mathf.Max(0, rope.activeParticleCount - 1)));
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(p, 0.01f);
        Gizmos.DrawLine(p, transform.position);
    }
#endif
}
