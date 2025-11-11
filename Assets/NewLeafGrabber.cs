using Obi;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class NewLeafGrabber : MonoBehaviour
{
    public Camera cam;
    public LayerMask leafMask = ~0;

    [Header("Pluck feel (legacy fallback only)")]
    public float spring = 40f;
    public float damping = 10f;
    public float maxDragSpeed = 4f;
    public float tearDistance = 0.11f; // legacy-only
    public float tearDwell = 0.12f;    // legacy-only

    // -------- Cooperation switches --------
    [Header("Cooperation with 3D leaf")]
    [Tooltip("If ON, when a hit object has Leaf3D_PullBreak we delegate grab + pop to it.")]
    public bool cooperateWith3DLeaf = true;

    // -------- Distance gating knobs --------
    [Header("Distance gating (your two knobs)")]
    [Tooltip("How far the pointer must pull from the initial hit BEFORE the grab begins (meters). Prevents click-only grabs.")]
    [Min(0f)] public float pullToStartGrabDistance = 0.02f;

    [Tooltip("If ON, we configure the 3D leaf for distance-only popping (no click pops).")]
    public bool useDistanceOnlyPop = true;

    [Tooltip("Ignored give before counting toward pop (meters) – used when distance-only pop is ON.")]
    [Min(0f)] public float popDeadZone = 0.03f;

    [Tooltip("Extra distance (beyond the dead-zone) required to pop (meters).")]
    [Min(0.001f)] public float popDistance = 0.12f;

    [Tooltip("Optional dwell after exceeding popDistance (seconds). 0 = pure distance-only.")]
    [Min(0f)] public float popDwellSeconds = 0f;

    // -------- Break juice: time freeze --------
    [Header("Break Juice (time freeze)")]
    public bool enableBreakFreeze = true;
    [Tooltip("Freeze length in seconds (unscaled).")]
    [Min(0f)] public float freezeDuration = 0.12f;
    [Tooltip("Timescale during the freeze window (0 = full stop, 0.05–0.2 = slo-mo).")]
    [Range(0f, 1f)] public float freezeTimeScale = 0.05f;

    // -------- Sap emit override --------
    [Header("Sap emit override (optional)")]
    [Tooltip("If set, sap will emit from this transform instead of the leaf pivot when it pops.")]
    public Transform sapEmitPointOverride;

    // -------- Cull safety --------
    [Header("Cull safety")]
    public bool enableCull = true;
    [Min(0.1f)] public float cullDistance = 20f;
    [Tooltip("If null, camera transform is used.")]
    public Transform cullOrigin;

    // --- runtime ---
    Leaf _leafLegacy;          // optional legacy helper
    Vector3 _anchor;           // initial click world point
    float _over;               // dwell accumulator (legacy)

    Leaf3D_PullBreak _leaf3D;  // cooperating 3D leaf, if any
    bool _pendingGrab;         // clicked but not pulled far enough yet
    Vector3 _grabOffset;       // keep zero snap at grab start

    public StemParticleTugger stemTugger;     // assign in Inspector (same stem rope)
    public float tugSearchRadius = 0.2f;      // how far from pin to pick the particle

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!cullOrigin && cam) cullOrigin = cam.transform;
    }

    void OnDisable()
    {
        UnsubscribeBreakEvent();
        if (stemTugger != null) stemTugger.StopTug();   // ensure tug ends if we disable mid-grab
    }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        // -------- acquire on click --------
        if (_leafLegacy == null && _leaf3D == null)
        {
            if (m.leftButton.wasPressedThisFrame && RaycastLeaf(m.position.ReadValue(), out _leafLegacy, out _anchor))
            {
                // prefer 3D controller if present
                _leaf3D = (cooperateWith3DLeaf ? _leafLegacy.GetComponent<Leaf3D_PullBreak>() : null);
                _pendingGrab = true;
            }
            return;
        }

        // -------- while mouse held --------
        if (m.leftButton.isPressed)
        {
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _anchor, cam);

            // enforce pull-to-start for both paths
            if (_pendingGrab)
            {
                float pulled = Vector3.Distance(target, _anchor);
                if (pulled < pullToStartGrabDistance) return; // resistant to click-only

                // threshold crossed → begin the grab now
                if (_leaf3D != null && _leaf3D.enabled)
                {
                    if (useDistanceOnlyPop)
                    {
                        _leaf3D.ConfigureFromGrabber(
                            popDeadZone,
                            popDistance,
                            popDwellSeconds,
                            _leaf3D.useReleaseDelay,
                            _leaf3D.releaseDelaySeconds,
                            _leaf3D.stayInHandAfterBreak,
                            _leaf3D.holdLifetimeAfterRelease
                        );
                        _leaf3D.requireGrabToBreak = true;  // distance-only; no click pops
                        _leaf3D.armOnProximity = false;
                    }

                    // pass optional sap override + auto-cull settings
                    _leaf3D.sapEmitOverride = sapEmitPointOverride;
                    _leaf3D.enableAutoCull = enableCull;
                    _leaf3D.autoCullDistance = cullDistance;
                    _leaf3D.autoCullOrigin = cullOrigin ? cullOrigin : (cam ? cam.transform : null);

                    // subscribe to pop event for freeze juice
                    SubscribeBreakEvent();

                    _leaf3D.BeginGrab(_anchor);
                    _grabOffset = _leaf3D.transform.position - target;

                    // >>> START STEM TUG right when grab truly begins <<<
                    if (stemTugger != null && _leaf3D.rope is ObiRope stemRope)
                    {
                        Vector3 pinWorld = _anchor;
                        int solverIdx = FindNearestStemSolverParticle(stemRope, pinWorld, tugSearchRadius);
                        if (solverIdx >= 0)
                            stemTugger.StartTugBySolverIndex(solverIdx, pinWorld);
                    }
                }
                else
                {
                    // legacy path: start accumulating
                    _over = 0f;
                }

                _pendingGrab = false;
            }

            // actively dragging
            if (_leaf3D != null && _leaf3D.enabled)
            {
                _leaf3D.SetHand(target + _grabOffset);

                if (stemTugger != null) stemTugger.UpdateTarget(target, Time.deltaTime);

                TryCullCurrentLeaf(); // safety
                return;
            }

            // -------- legacy fallback --------
            Transform t = _leafLegacy.transform;
            Vector3 vel = Vector3.zero;
            t.position = Vector3.SmoothDamp(
                t.position, target, ref vel,
                Mathf.Max(0.0001f, damping / spring), maxDragSpeed
            );

            float stretch = Vector3.Distance(t.position, _anchor);
            if (stretch >= Mathf.Max(pullToStartGrabDistance, 0f))
            {
                float effectiveTearDistance = useDistanceOnlyPop
                    ? (pullToStartGrabDistance + popDeadZone + popDistance)
                    : tearDistance;

                if (stretch >= effectiveTearDistance)
                {
                    _over += Time.deltaTime;
                    float dwell = useDistanceOnlyPop ? popDwellSeconds : tearDwell;

                    if (_over >= dwell)
                    {
                        _leafLegacy.TearOff();
                        DoFreezeJuice();
                        _leafLegacy = null;
                        return;
                    }
                }
                else _over = 0f;
            }

            return;
        }

        // -------- mouse released --------
        if (m.leftButton.wasReleasedThisFrame)
        {
            if (_leaf3D != null)
            {
                _leaf3D.EndGrab();
                TryCullCurrentLeaf();
                UnsubscribeBreakEvent();
                if (stemTugger != null) stemTugger.StopTug();
                _leaf3D = null;
            }

            _pendingGrab = false;
            _leafLegacy = null;
        }
    }

    int FindNearestStemSolverParticle(ObiRope rope, Vector3 pinWorld, float radius)
    {
        if (rope == null || rope.solver == null) return -1;
        var s = rope.solver;
        float bestSqr = radius * radius;
        int best = -1;

#if OBI_NATIVE_COLLECTIONS
        int n = s.renderablePositions.count;
        for (int i = 0; i < n; ++i)
        {
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[i]);
            float d2 = (p - pinWorld).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; best = i; }
        }
#else
        int n = (s.renderablePositions != null) ? s.renderablePositions.count : 0;
        for (int i = 0; i < n; ++i)
        {
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[i]);
            float d2 = (p - pinWorld).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; best = i; }
        }
#endif
        return best;
    }

    void TryCullCurrentLeaf()
    {
        if (!enableCull || _leaf3D == null) return;
        Transform origin = cullOrigin ? cullOrigin : (cam ? cam.transform : null);
        if (!origin) return;

        if (Vector3.Distance(origin.position, _leaf3D.transform.position) > cullDistance)
        {
            Destroy(_leaf3D.gameObject);
            _leaf3D = null;
            if (stemTugger != null) stemTugger.StopTug();
        }
    }

    // ---- Break freeze juice ----
    void SubscribeBreakEvent()
    {
        if (_leaf3D != null) _leaf3D.OnDetached += OnLeaf3DBroke;
    }
    void UnsubscribeBreakEvent()
    {
        if (_leaf3D != null) _leaf3D.OnDetached -= OnLeaf3DBroke;
    }
    void OnLeaf3DBroke(Leaf3D_PullBreak leaf)
    {
        if (stemTugger != null) stemTugger.StopTug();
        DoFreezeJuice();
    }
    void DoFreezeJuice()
    {
        if (!enableBreakFreeze || freezeDuration <= 0f) return;
        StopAllCoroutines();
        StartCoroutine(CoFreeze());
    }
    IEnumerator CoFreeze()
    {
        float prevScale = Time.timeScale;
        Time.timeScale = Mathf.Clamp(freezeTimeScale, 0f, 1f);
        float t = 0f;
        while (t < freezeDuration) { yield return null; t += Time.unscaledDeltaTime; }
        Time.timeScale = prevScale;
    }

    // ---- ray/plane helpers ----
    bool RaycastLeaf(Vector2 scr, out Leaf leaf, out Vector3 hit)
    {
        leaf = null; hit = default;
        Ray r = cam.ScreenPointToRay(scr);
        if (Physics.Raycast(r, out var h, 50f, leafMask, QueryTriggerInteraction.Ignore))
        {
            leaf = h.collider.GetComponentInParent<Leaf>();
            if (leaf != null) { hit = h.point; return true; }
        }
        return false;
    }

    static Vector3 ScreenToWorldOnPlane(Vector2 scr, Vector3 planePoint, Camera cam)
    {
        Plane p = new Plane(-cam.transform.forward, planePoint);
        Ray r = cam.ScreenPointToRay(scr);
        return p.Raycast(r, out float d) ? r.GetPoint(d) : planePoint;
    }
}
