// File: ObiParticleAnchorFollower.cs
// Follows a specific particle on an ObiRope in world space.

using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
public class ObiParticleAnchorFollower : MonoBehaviour
{
    public ObiRope rope;
    public int actorIndex = 0;

    int _solverIndex = -1;
    bool _ready = false;

    void OnEnable()
    {
        TryInit();
        if (rope != null)
            rope.OnSimulationStart += OnSimStart;
    }

    void OnDisable()
    {
        if (rope != null)
            rope.OnSimulationStart -= OnSimStart;
        _ready = false;
        _solverIndex = -1;
    }

    void OnSimStart(ObiActor actor, float step, float substep) => TryInit();

    void TryInit()
    {
        if (rope == null || !rope.isLoaded || rope.solver == null) return;
#if OBI_NATIVE_COLLECTIONS
        _solverIndex = (actorIndex >= 0 && actorIndex < rope.solverIndices.count) ? rope.solverIndices[actorIndex] : -1;
#else
        _solverIndex = (rope.solverIndices != null && actorIndex >= 0 && actorIndex < rope.solverIndices.count) ? rope.solverIndices[actorIndex] : -1;
#endif
        _ready = _solverIndex >= 0;
    }

    void LateUpdate()
    {
        if (!_ready || rope == null || rope.solver == null) return;

        var s = rope.solver;
#if OBI_NATIVE_COLLECTIONS
        if (s.renderablePositions.count > _solverIndex)
            transform.position = s.transform.TransformPoint((Vector3)s.renderablePositions[_solverIndex]);
        else if (s.positions.count > _solverIndex)
            transform.position = s.transform.TransformPoint((Vector3)s.positions[_solverIndex]);
#else
        if (s.renderablePositions != null && s.renderablePositions.count > _solverIndex)
            transform.position = s.transform.TransformPoint((Vector3)s.renderablePositions[_solverIndex]);
        else if (s.positions != null && s.positions.count > _solverIndex)
            transform.position = s.transform.TransformPoint((Vector3)s.positions[_solverIndex]);
#endif
    }
}
