// File: RopeParticleFollowerInitialOffset.cs
// Follow one Obi rope particle, preserving this object's initial world-space offset.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeParticleFollowerInitialOffset : MonoBehaviour
{
    [Header("Refs")]
    public ObiRopeBase rope;
    [Tooltip("Actor-space particle index to follow (0..activeParticleCount-1).")]
    public int actorIndex = 0;

    [Header("Behavior")]
    public bool smooth = true;
    public float posLerp = 24f;

    [Tooltip("If true, when the current particle is not in the solver (solverIndex=-1), we search nearby for a valid one.")]
    public bool autoRebindToNearestValid = true;
    [Tooltip("Max particles to scan left/right when rebinding.")]
    public int rebindScanRadius = 8;

    [Tooltip("Recompute the offset at runtime (keeps your current pose as new offset).")]
    public KeyCode rebindKey = KeyCode.None;

    // runtime
    Vector3 _initialOffsetWS;
    bool _bound;

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

        if (!_bound || rope == null) return;

        // rope/solver can briefly be null/not ready at startup or after resets
        var solver = rope.solver;
        if (solver == null) return;

        // Safeguard: nothing active yet
        if (rope.activeParticleCount <= 0) return;

        int ai = Mathf.Clamp(actorIndex, 0, rope.activeParticleCount - 1);

        // actor -> solver index (can be -1 if particle isn't in the solver)
        int si = (ai >= 0 && ai < rope.solverIndices.count) ? rope.solverIndices[ai] : -1;

        if (si < 0 && autoRebindToNearestValid)
        {
            // Look around for the nearest valid particle present in the solver
            int best = -1;
            for (int d = 1; d <= rebindScanRadius; d++)
            {
                int left = ai - d;
                int right = ai + d;

                if (left >= 0)
                {
                    int s = rope.solverIndices[left];
                    if (s >= 0) { best = left; break; }
                }
                if (right < rope.activeParticleCount)
                {
                    int s = rope.solverIndices[right];
                    if (s >= 0) { best = right; break; }
                }
            }
            if (best >= 0) ai = best;
            si = (ai >= 0 && ai < rope.solverIndices.count) ? rope.solverIndices[ai] : -1;
        }

        // Still invalid? Skip this frame (prevents [-1] access).
        if (si < 0) return;

        Vector3 p = GetSolverWorldPosition(solver, si);
        Vector3 target = p + _initialOffsetWS;

        if (smooth)
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-posLerp * Time.deltaTime));
        else
            transform.position = target;
    }

    /// Capture current offset from the selected particle.
    public void TryBindOffset()
    {
        _bound = false;

        if (rope == null || rope.solver == null) return;
        if (rope.activeParticleCount <= 0) return;

        actorIndex = Mathf.Clamp(actorIndex, 0, rope.activeParticleCount - 1);

        int si = (actorIndex >= 0 && actorIndex < rope.solverIndices.count) ? rope.solverIndices[actorIndex] : -1;
        if (si < 0) return; // can't bind yet; particle not in solver

        Vector3 p = GetSolverWorldPosition(rope.solver, si);
        _initialOffsetWS = transform.position - p;
        _bound = true;
    }

    // Prefer renderable positions when available; fall back to positions.
    static Vector3 GetSolverWorldPosition(ObiSolver solver, int solverIndex)
    {
        Vector3 solverPos;

        if (solver.renderablePositions != null &&
            solverIndex >= 0 && solverIndex < solver.renderablePositions.count)
        {
            solverPos = (Vector3)solver.renderablePositions[solverIndex];
        }
        else
        {
            solverPos = (Vector3)solver.positions[solverIndex];
        }

        return solver.transform.TransformPoint(solverPos);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (rope == null || rope.solver == null || rope.activeParticleCount <= 0) return;

        int ai = Mathf.Clamp(actorIndex, 0, rope.activeParticleCount - 1);
        int si = (ai >= 0 && ai < rope.solverIndices.count) ? rope.solverIndices[ai] : -1;
        if (si < 0) return;

        Vector3 p = GetSolverWorldPosition(rope.solver, si);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(p, 0.01f);
        Gizmos.DrawLine(p, transform.position);
    }
#endif
}
