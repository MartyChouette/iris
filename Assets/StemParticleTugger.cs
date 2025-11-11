// File: StemParticleTugger.cs
// Tug a specific solver particle on an ObiRope toward a world target using a spring-damper.
// Also offers a small recoil kick when the leaf is plucked.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32010)]
public class StemParticleTugger : MonoBehaviour
{
    [Header("Obi rope to tug")]
    public ObiRope rope;

    [Header("Spring-damper (world space)")]
    [Tooltip("Stiffness of the tug (accel = spring * error)")]
    public float spring = 80f;
    [Tooltip("Velocity damping (accel -= damper * v)")]
    public float damper = 10f;
    [Tooltip("Clamp per-step acceleration (m/s^2)")]
    public float maxAccel = 120f;

    [Header("Spread to neighbors")]
    [Tooltip("How many actor particles to affect on each side of the main one.")]
    public int neighborWidth = 2;
    [Tooltip("0..1 falloff per neighbor step (1 = no falloff).")]
    [Range(0f, 1f)] public float neighborFalloff = 0.65f;

    [Header("Safety")]
    [Tooltip("Ignore if dt is very small to avoid numeric spikes.")]
    public float minDt = 1e-5f;

    // runtime
    int _solverIndex = -1;      // particle to tug (solver space)
    int _actorIndex = -1;       // same particle (actor space), used to find neighbors
    bool _active = false;
    Vector3 _targetWS;

    public bool IsActive => _active;

    void Reset()
    {
        if (!rope) rope = GetComponentInParent<ObiRope>();
    }

    // Start tugging a specific solver particle (index is in SOLVER space!)
    public void StartTugBySolverIndex(int solverIndex, Vector3 startTargetWorld)
    {
        if (rope == null || rope.solver == null || solverIndex < 0) return;

        _solverIndex = solverIndex;
        _actorIndex = SolverToActorIndex(rope, solverIndex);
        _targetWS = startTargetWorld;
        _active = (_actorIndex >= 0);
    }

    public void UpdateTarget(Vector3 targetWorld, float dt)
    {
        if (!_active) return;
        _targetWS = targetWorld;
        ApplySpringStep(dt);
    }

    public void StopTug()
    {
        _active = false;
        _solverIndex = -1;
        _actorIndex = -1;
    }

    // Small recoil impulse added to the tugged particle and its neighbors (world velocity kick).
    public void KickRecoil(Vector3 worldVelocityChange, int widthOverride = -1)
    {
        if (rope == null || rope.solver == null || _actorIndex < 0) return;

        int width = (widthOverride >= 0) ? widthOverride : neighborWidth;

        var s = rope.solver;
#if OBI_NATIVE_COLLECTIONS
        if (s.velocities.count == 0 || s.renderablePositions.count == 0) return;
#else
        if (s.velocities == null || s.renderablePositions == null) return;
#endif
        var si = rope.solverIndices;
        if (si == null || _actorIndex >= si.count) return;

        // apply to center + neighbors with falloff
        for (int off = -width; off <= width; off++)
        {
            int actor = _actorIndex + off;
            if (actor < 0 || actor >= rope.activeParticleCount) continue;

            int svi = si[actor];
#if OBI_NATIVE_COLLECTIONS
            if (svi < 0 || svi >= s.velocities.count) continue;
#else
            if (svi < 0 || s.velocities == null || svi >= s.velocities.count) continue;
#endif
            float w = Mathf.Pow(neighborFalloff, Mathf.Abs(off));
            Vector3 dv = worldVelocityChange * w;

            Vector4 v4 = s.velocities[svi];
            v4.x += dv.x; v4.y += dv.y; v4.z += dv.z;
            s.velocities[svi] = v4;
        }
    }

    void ApplySpringStep(float dt)
    {
        if (!_active || rope == null || rope.solver == null) return;
        if (dt < minDt) return;

        var s = rope.solver;
        var rp = s.renderablePositions;
        var si = rope.solverIndices;
        var v = s.velocities;

        if (rp == null || si == null || v == null) return;
        if (_actorIndex < 0 || _actorIndex >= rope.activeParticleCount) return;

        // affect center + neighbors
        for (int off = -neighborWidth; off <= neighborWidth; off++)
        {
            int actor = _actorIndex + off;
            if (actor < 0 || actor >= rope.activeParticleCount) continue;

            int svi = si[actor];
#if OBI_NATIVE_COLLECTIONS
            if (svi < 0 || svi >= rp.count || svi >= v.count) continue;
#else
            if (svi < 0 || rp == null || v == null || svi >= rp.count || svi >= v.count) continue;
#endif
            // world pos & vel
            Vector3 p = s.transform.TransformPoint((Vector3)rp[svi]);
            Vector3 vel = (Vector3)v[svi];

            // spring-damper toward _targetWS
            Vector3 error = (_targetWS - p);
            Vector3 accel = spring * error - damper * vel;

            float w = Mathf.Pow(neighborFalloff, Mathf.Abs(off));
            accel *= w;

            // clamp accel
            float aMag = accel.magnitude;
            if (aMag > maxAccel) accel = accel * (maxAccel / Mathf.Max(1e-6f, aMag));

            // integrate velocity
            vel += accel * dt;

            Vector4 v4 = v[svi];
            v4.x = vel.x; v4.y = vel.y; v4.z = vel.z;
            v[svi] = v4;
        }
    }

    // Map solver index back to actor index (linear search is fine here)
    int SolverToActorIndex(ObiRope r, int solverIdx)
    {
        var si = r.solverIndices;
        if (si == null) return -1;
        int n = r.activeParticleCount;
        for (int i = 0; i < n; ++i)
            if (si[i] == solverIdx) return i;
        return -1;
    }
}
