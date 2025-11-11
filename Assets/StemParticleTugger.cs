// File: StemParticleTugger.cs
// Click/drag = tug the nearest particle of a stem rope.
// Creates a temporary ObiParticleAttachment to that single particle,
// moves a hidden "tug target" with the cursor, destroys the pin on release.
//
// Works alongside your Leaf3D_PullBreak / NewLeafGrabber. It doesn't touch the leaf;
// it only bends the stem on the clicked side.

using UnityEngine;
using UnityEngine.InputSystem;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class StemParticleTugger : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public ObiRope stem;                    // the main stem rope in a solver

    [Header("Find nearest particle")]
    [Tooltip("Max world distance from the click ray hit to consider a particle tug-able.")]
    public float searchRadius = 0.25f;

    [Header("Tug feel")]
    [Tooltip("How stiff the pin is (0 = rigid). Higher = more rubbery.")]
    [Min(0f)] public float pinCompliance = 0.0f;
    [Tooltip("How fast the tug target follows the pointer (units/sec-ish).")]
    public float targetLerp = 24f;

    [Header("Safety")]
    [Tooltip("Auto-break the pin if the pointer strays really far (meters). 0 = off.")]
    public float maxTugDistance = 1.5f;

    // runtime
    ObiParticleAttachment _tempPin;         // created on the stem at runtime
    ObiParticleGroup _tempGroup;            // group with a single particle
    Transform _tugTarget;                   // hidden target we move around
    int _solverIndex = -1;                  // solver-space particle index we're pinning
    Vector3 _grabPlanePoint;                // plane origin for screen->world
    bool _dragging;

    void Awake()
    {
        if (!cam) cam = Camera.main;
    }

    void OnDisable()
    {
        EndTug();
    }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null || stem == null || stem.solver == null) return;

        if (!_dragging)
        {
            // start tug on press if we can find a nearby particle
            if (m.leftButton.wasPressedThisFrame)
            {
                if (TryBeginTug(m.position.ReadValue()))
                    _dragging = true;
            }
            return;
        }

        // while dragging: move tug target toward pointer on a plane through the original grab point
        if (m.leftButton.isPressed)
        {
            Vector3 cursor = ScreenToWorldOnPlane(m.position.ReadValue(), _grabPlanePoint, cam);

            // lerped motion feels nicer (and avoids solver jitter)
            _tugTarget.position = Vector3.Lerp(
                _tugTarget.position,
                cursor,
                1f - Mathf.Exp(-targetLerp * Time.deltaTime)
            );

            if (maxTugDistance > 0f && Vector3.Distance(cursor, _grabPlanePoint) > maxTugDistance)
                EndTug(); // fail-safe: don't let a runaway drag stretch things forever
        }
        else if (m.leftButton.wasReleasedThisFrame)
        {
            EndTug();
        }
    }

    bool TryBeginTug(Vector2 screenPos)
    {
        // Raycast against *anything* in front of camera to get a plane point.
        Ray r = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(r, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        // Find nearest solver particle to the hit point:
        int bestSolver = FindNearestSolverParticle(stem, hit.point, searchRadius);
        if (bestSolver < 0)
            return false;

        // Create the tug target (once) and place it:
        if (_tugTarget == null)
        {
            var go = new GameObject("~TugTarget");
            go.hideFlags = HideFlags.HideInHierarchy;
            _tugTarget = go.transform;
        }
        _tugTarget.position = hit.point;
        _tugTarget.rotation = Quaternion.identity;

        // Build a single-index particle group for this tug and add a pin on the stem:
        MakeOrReuseTempPin(bestSolver);

        _grabPlanePoint = hit.point;
        _solverIndex = bestSolver;
        return true;
    }

    void EndTug()
    {
        _dragging = false;
        _solverIndex = -1;

        if (_tempPin != null)
        {
            _tempPin.enabled = false;
            // detach and destroy the temp components, but keep the tug target for reuse
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(_tempPin);
            else
#endif
                Destroy(_tempPin);
            _tempPin = null;
        }

        _tempGroup = null; // safe to drop; it belongs to the pin component we destroyed
    }

    void MakeOrReuseTempPin(int solverIndex)
    {
        // Convert solver index → actor (actor-space) index for the group:
        int actorIndex = SolverToActorIndex(stem, solverIndex);
        if (actorIndex < 0) return;

        // Create a transient particle group
        _tempGroup = ScriptableObject.CreateInstance<ObiParticleGroup>();
        _tempGroup.name = "TugGroup";
        _tempGroup.particleIndices.Add(actorIndex);

        // Add a pin attachment to the stem actor
        _tempPin = stem.gameObject.AddComponent<ObiParticleAttachment>();
        _tempPin.target = _tugTarget;
        _tempPin.attachmentType = ObiParticleAttachment.AttachmentType.Dynamic;
        _tempPin.compliance = pinCompliance;
        _tempPin.breakThreshold = 0f; // 0 = never auto-break; we end it manually
        _tempPin.particleGroup = _tempGroup;
        _tempPin.enabled = true;

        stem.SetConstraintsDirty(Oni.ConstraintType.Pin);
    }

    // ---------- helpers ----------

    static int FindNearestSolverParticle(ObiRope r, Vector3 worldPos, float radius)
    {
        if (r == null || r.solver == null) return -1;
        var s = r.solver;
        float bestSqr = radius * radius;
        int best = -1;

#if OBI_NATIVE_COLLECTIONS
        int n = s.renderablePositions.count;
        for (int i = 0; i < n; ++i)
        {
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[i]);
            float d2 = (p - worldPos).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; best = i; }
        }
#else
        int n = (s.renderablePositions != null) ? s.renderablePositions.count : 0;
        for (int i = 0; i < n; ++i)
        {
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[i]);
            float d2 = (p - worldPos).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; best = i; }
        }
#endif
        return best;
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

    static Vector3 ScreenToWorldOnPlane(Vector2 scr, Vector3 planePoint, Camera cam)
    {
        Plane p = new Plane(-cam.transform.forward, planePoint);
        Ray r = cam.ScreenPointToRay(scr);
        return p.Raycast(r, out float d) ? r.GetPoint(d) : planePoint;
    }
}
