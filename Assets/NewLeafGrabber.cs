// File: NewLeafGrabber.cs  (only change: add recoil on break)
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

    [Header("Cooperation with 3D leaf")]
    public bool cooperateWith3DLeaf = true;

    [Header("Distance gating")]
    [Min(0f)] public float pullToStartGrabDistance = 0.02f;
    public bool useDistanceOnlyPop = true;
    [Min(0f)] public float popDeadZone = 0.03f;
    [Min(0.001f)] public float popDistance = 0.12f;
    [Min(0f)] public float popDwellSeconds = 0f;

    [Header("Break Juice (time freeze)")]
    public bool enableBreakFreeze = true;
    [Min(0f)] public float freezeDuration = 0.12f;
    [Range(0f, 1f)] public float freezeTimeScale = 0.05f;

    [Header("Sap emit override (optional)")]
    public Transform sapEmitPointOverride;

    [Header("Cull safety")]
    public bool enableCull = true;
    [Min(0.1f)] public float cullDistance = 20f;
    public Transform cullOrigin;

    // --- runtime ---
    Leaf _leafLegacy;
    Vector3 _anchor;
    float _over;

    Leaf3D_PullBreak _leaf3D;
    bool _pendingGrab;
    Vector3 _grabOffset;

    [Header("Stem tugger hookup")]
    public StemParticleTugger stemTugger;
    public float tugSearchRadius = 0.2f;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!cullOrigin && cam) cullOrigin = cam.transform;
    }

    void OnDisable()
    {
        UnsubscribeBreakEvent();
        if (stemTugger != null) stemTugger.StopTug();
    }

    void Update()
    {
        var m = Mouse.current; if (m == null || cam == null) return;

        if (_leafLegacy == null && _leaf3D == null)
        {
            if (m.leftButton.wasPressedThisFrame && RaycastLeaf(m.position.ReadValue(), out _leafLegacy, out _anchor))
            {
                _leaf3D = (cooperateWith3DLeaf ? _leafLegacy.GetComponent<Leaf3D_PullBreak>() : null);
                _pendingGrab = true;
            }
            return;
        }

        if (m.leftButton.isPressed)
        {
            Vector3 target = ScreenToWorldOnPlane(m.position.ReadValue(), _anchor, cam);

            if (_pendingGrab)
            {
                float pulled = Vector3.Distance(target, _anchor);
                if (pulled < pullToStartGrabDistance) return;

                if (_leaf3D != null && _leaf3D.enabled)
                {
                    if (useDistanceOnlyPop)
                    {
                        _leaf3D.ConfigureFromGrabber(
                            popDeadZone, popDistance, popDwellSeconds,
                            _leaf3D.useReleaseDelay, _leaf3D.releaseDelaySeconds,
                            _leaf3D.stayInHandAfterBreak, _leaf3D.holdLifetimeAfterRelease
                        );
                        _leaf3D.requireGrabToBreak = true;
                        _leaf3D.armOnProximity = false;
                    }

                    _leaf3D.sapEmitOverride = sapEmitPointOverride;
                    _leaf3D.enableAutoCull = enableCull;
                    _leaf3D.autoCullDistance = cullDistance;
                    _leaf3D.autoCullOrigin = cullOrigin ? cullOrigin : (cam ? cam.transform : null);

                    SubscribeBreakEvent();

                    _leaf3D.BeginGrab(_anchor);
                    _grabOffset = _leaf3D.transform.position - target;

                    // Start stem tug on the nearest solver particle to the pin/anchor
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
                    _over = 0f;
                }

                _pendingGrab = false;
            }

            if (_leaf3D != null && _leaf3D.enabled)
            {
                _leaf3D.SetHand(target + _grabOffset);

                if (stemTugger != null) stemTugger.UpdateTarget(target, Time.deltaTime);

                TryCullCurrentLeaf();
                return;
            }

            // legacy fallback
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
        if (stemTugger != null)
        {
            stemTugger.StopTug();

            // >>> STEM RECOIL on actual break <<<
            // Nudge the stem opposite the leaf's break impulse (gentle kick).
            // If your Leaf3D_PullBreak.breakImpulse points up/out, invert a bit:
            Vector3 recoil = -leaf.breakImpulse * 0.35f;   // tune factor
            stemTugger.KickRecoil(recoil, 2);
        }

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
