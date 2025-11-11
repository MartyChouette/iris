// File: StemTuggerLite.cs
using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
public class StemParticleTugger : MonoBehaviour
{
    public ObiRope stem;              // assign your main stem rope
    public float pinCompliance = 0.0f;
    public float targetLerp = 24f;

    Transform _target;
    ObiParticleAttachment _pin;
    ObiParticleGroup _group;

    void Awake() { if (!stem) stem = GetComponent<ObiRope>(); }

    public void StartTugBySolverIndex(int solverIndex, Vector3 startWorld)
    {
        StopTug(); // just in case

        if (stem == null || stem.solver == null || solverIndex < 0) return;

        // solver→actor index for the pin group
        int actorIndex = SolverToActorIndex(stem, solverIndex);
        if (actorIndex < 0) return;

        // tiny reusable target
        if (_target == null)
        {
            var go = new GameObject("~StemTugTarget"); go.hideFlags = HideFlags.HideInHierarchy;
            _target = go.transform;
        }
        _target.position = startWorld;

        // build single-index group
        _group = ScriptableObject.CreateInstance<ObiParticleGroup>();
        _group.name = "StemTugGroup";
        _group.particleIndices.Add(actorIndex);

        // add a dynamic pin on the stem
        _pin = stem.gameObject.AddComponent<ObiParticleAttachment>();
        _pin.target = _target;
        _pin.attachmentType = ObiParticleAttachment.AttachmentType.Dynamic;
        _pin.compliance = pinCompliance;
        _pin.breakThreshold = 0f;   // we end it manually
        _pin.particleGroup = _group;
        _pin.enabled = true;

        stem.SetConstraintsDirty(Oni.ConstraintType.Pin);
    }

    public void UpdateTarget(Vector3 world, float dt)
    {
        if (_target == null) return;
        _target.position = Vector3.Lerp(_target.position, world, 1f - Mathf.Exp(-targetLerp * dt));
    }

    public void StopTug()
    {
        if (_pin != null) { _pin.enabled = false; Destroy(_pin); _pin = null; }
        _group = null;
    }

    static int SolverToActorIndex(ObiRope r, int solverIdx)
    {
#if OBI_NATIVE_COLLECTIONS
        for (int i = 0; i < r.solverIndices.count; ++i) if (r.solverIndices[i] == solverIdx) return i;
#else
        if (r.solverIndices != null)
            for (int i = 0; i < r.solverIndices.count; ++i) if (r.solverIndices[i] == solverIdx) return i;
#endif
        return -1;
    }
}
