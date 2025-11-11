// PATCHED: RopeParticleTetherBinder.cs
// Changes:
// - Delay wiring until mainRope is loaded (no more out-of-range at Start).
// - Skip invalid rows safely (no NRE if leaf/tether missing).
// - Start pin = Static, compliance=0; End pin = Dynamic, tiny compliance (tighter).
// - Optional: auto-pick nearest particle if mainActorIndex < 0.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Obi;

[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class RopeParticleTetherBinder : MonoBehaviour
{
    [Header("Main stem rope (must live under an ObiSolver)")]
    public ObiRope mainRope;

    [System.Serializable]
    public class Connection
    {
        [Header("Particle on the main rope (-1 = auto nearest)")]
        public int mainActorIndex = -1;

        [Header("Existing scene objects")]
        public Transform leafObject;      // REQUIRED
        public ObiRope tetherRope;        // REQUIRED (has TWO ObiParticleAttachment)

        [Header("Break / tear")]
        public float attachmentBreakThreshold = 8f;
        public bool destroyTetherOnTear = true;

        // runtime
        [System.NonSerialized] public GameObject anchorGO;
        [System.NonSerialized] public ObiParticleAttachment startAttach;
        [System.NonSerialized] public ObiParticleAttachment endAttach;
        [System.NonSerialized] public bool tornObserved;
        [System.NonSerialized] public bool wiredOK;        // <-- only check these in Update if true
    }

    [Header("Connections (add as many as you want)")]
    public List<Connection> connections = new List<Connection>();

    void OnEnable()
    {
        StartCoroutine(SetupWhenReady());
    }

    IEnumerator SetupWhenReady()
    {
        if (mainRope == null)
        {
            Debug.LogError("[TetherBinder] mainRope is null.");
            yield break;
        }

        // Wait until Obi has loaded the rope into a solver
        while (!mainRope.isLoaded || mainRope.solver == null || mainRope.activeParticleCount <= 0)
            yield return null;

        WireAll();
    }

    void WireAll()
    {
        for (int i = 0; i < connections.Count; i++)
        {
            var c = connections[i];
            c.wiredOK = false;

            if (!ValidateRow(c, i)) continue;

            // Auto-pick nearest particle if requested:
            if (c.mainActorIndex < 0 && c.leafObject != null)
                c.mainActorIndex = FindNearestActorIndex(mainRope, c.leafObject.position);

            // Create / place the particle follower anchor:
            c.anchorGO = new GameObject($"ParticleAnchor_{c.mainActorIndex}_{i}");
            var follower = c.anchorGO.AddComponent<ObiParticleAnchorFollower>();
            follower.rope = mainRope;
            follower.actorIndex = c.mainActorIndex;
            c.anchorGO.transform.position = GetParticleWorld(mainRope, c.mainActorIndex);

            // Get two attachments on the small tether:
            var atts = c.tetherRope.GetComponents<ObiParticleAttachment>();
            if (atts == null || atts.Length < 2)
            {
                Debug.LogError($"[TetherBinder] Row {i}: Tether '{c.tetherRope.name}' needs TWO ObiParticleAttachment components.");
                continue;
            }

            c.startAttach = atts[0];
            c.endAttach = atts[1];
            if (IncludesIndex(c.endAttach, 0) && !IncludesIndex(c.startAttach, 0))
            { var tmp = c.startAttach; c.startAttach = c.endAttach; c.endAttach = tmp; }

            // ---- NEW: try to auto-fill missing leafObject from the “end” attachment target
            if (c.leafObject == null)
            {
                var candidate = c.endAttach != null ? c.endAttach.target : null;
                if (candidate == null && c.startAttach != null && !IncludesIndex(c.startAttach, 0))
                    candidate = c.startAttach.target; // fallback heuristic

                if (candidate != null)
                {
                    c.leafObject = candidate;
                    Debug.Log($"[TetherBinder] Row {i}: auto-assigned leafObject → '{candidate.name}' from tether attachment target.");
                }
                else
                {
                    Debug.LogError($"[TetherBinder] Row {i}: leafObject could not be auto-detected. Please assign it in the Inspector.");
                    // Clean up the anchor we just made for this bad row:
                    if (c.anchorGO) Destroy(c.anchorGO);
                    continue;
                }
            }
            // ---- END NEW

            // Pins: rope-side rigid, leaf-side dynamic (tight but tearable)
            ConfigureAttachmentAsStart(c.startAttach, c.anchorGO.transform, c.attachmentBreakThreshold);
            ConfigureAttachmentAsEnd(c.endAttach, c.leafObject, c.attachmentBreakThreshold);

            c.wiredOK = true;

            if (mainRope.solver != c.tetherRope.solver)
                Debug.LogWarning($"[TetherBinder] Row {i}: '{c.tetherRope.name}' not under the same ObiSolver as mainRope.");

            Debug.Log($"[TetherBinder] Row {i}: particle {c.mainActorIndex} ↔ leaf '{c.leafObject.name}' via '{c.tetherRope.name}'.");
        }
    }


    void Update()
    {
        foreach (var c in connections)
        {
            if (!c.wiredOK || c.tornObserved) continue;

            bool torn = (c.startAttach == null || !c.startAttach.enabled) ||
                        (c.endAttach == null || !c.endAttach.enabled);
            if (torn)
            {
                c.tornObserved = true;
                OnTetherTorn(c);
            }
        }
    }

    void OnTetherTorn(Connection c)
    {
        if (c.leafObject && c.leafObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.useGravity = true;
        }
        if (c.destroyTetherOnTear && c.tetherRope != null)
            Destroy(c.tetherRope.gameObject);
        if (c.anchorGO != null)
            Destroy(c.anchorGO);

        Debug.Log($"[TetherBinder] Tether torn for leaf '{c.leafObject?.name}'.");
    }

    bool ValidateRow(Connection c)
    {
        if (c.leafObject == null)
        {
            Debug.LogError("[TetherBinder] Connection missing leafObject."); return false;
        }
        if (c.tetherRope == null)
        {
            Debug.LogError($"[TetherBinder] Connection for leaf '{c.leafObject.name}' missing tetherRope."); return false;
        }

        // If an explicit index was given, check range now that mainRope is loaded:
        if (c.mainActorIndex >= 0 && c.mainActorIndex >= mainRope.activeParticleCount)
        {
            Debug.LogError($"[TetherBinder] mainActorIndex {c.mainActorIndex} out of range for mainRope '{mainRope.name}'.");
            return false;
        }
        return true;
    }

    // ── Attachment presets ──
    void ConfigureAttachmentAsStart(ObiParticleAttachment a, Transform target, float breakThreshold)
    {
        if (a == null) return;
        a.target = target;
        a.attachmentType = ObiParticleAttachment.AttachmentType.Static; // rope-side: rigid anchor (tight)
        a.compliance = 0f;                                              // 0 = stiffest
        a.breakThreshold = Mathf.Max(0f, breakThreshold);
        a.enabled = true;

        var r = a.GetComponent<ObiRope>(); if (r) r.SetConstraintsDirty(Oni.ConstraintType.Pin);
        WarnEmptyGroup(a, r);
    }

    void ConfigureAttachmentAsEnd(ObiParticleAttachment a, Transform target, float breakThreshold)
    {
        if (a == null) return;
        a.target = target;
        a.attachmentType = ObiParticleAttachment.AttachmentType.Dynamic; // leaf-side: receives forces
        a.compliance = 1e-6f;                                           // extremely stiff but not infinite
        a.breakThreshold = Mathf.Max(0f, breakThreshold);
        a.enabled = true;

        var r = a.GetComponent<ObiRope>(); if (r) r.SetConstraintsDirty(Oni.ConstraintType.Pin);
        WarnEmptyGroup(a, r);
    }

    void WarnEmptyGroup(ObiParticleAttachment a, ObiRope owner)
    {
        if (a.particleGroup == null || a.particleGroup.particleIndices == null || a.particleGroup.particleIndices.Count == 0)
            Debug.LogWarning($"[TetherBinder] Attachment on '{owner?.name ?? a.name}' has empty particle group. " +
                             "Ensure the 'start' group contains index 0 and the 'end' group contains the last index.");
    }

    // ── Utilities ──
    static bool IncludesIndex(ObiParticleAttachment a, int idx)
    {
        return a != null && a.particleGroup != null && a.particleGroup.particleIndices != null &&
               a.particleGroup.particleIndices.Contains(idx);
    }

    int FindNearestActorIndex(ObiRope r, Vector3 worldPos)
    {
        int best = -1; float bestSqr = float.PositiveInfinity;
        var s = r.solver;
        for (int i = 0; i < r.activeParticleCount; i++)
        {
#if OBI_NATIVE_COLLECTIONS
            int si = r.solverIndices[i];
            if (si < 0 || si >= s.renderablePositions.count) continue;
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[si]);
#else
            int si = (r.solverIndices != null && i < r.solverIndices.count) ? r.solverIndices[i] : -1;
            if (si < 0 || s.renderablePositions == null || si >= s.renderablePositions.count) continue;
            Vector3 p = s.transform.TransformPoint((Vector3)s.renderablePositions[si]);
#endif
            float d = (p - worldPos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }
        return Mathf.Max(0, best);
    }

    Vector3 GetParticleWorld(ObiRope r, int actorIdx)
    {
        if (r == null || r.solver == null || actorIdx < 0) return r ? r.transform.position : Vector3.zero;
        var s = r.solver;
        int si;
#if OBI_NATIVE_COLLECTIONS
        si = (actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
        if (si >= 0 && s.renderablePositions.count > si)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[si]);
        if (si >= 0 && s.positions.count > si)
            return s.transform.TransformPoint((Vector3)s.positions[si]);
#else
        si = (r.solverIndices != null && actorIdx < r.solverIndices.count) ? r.solverIndices[actorIdx] : -1;
        if (si >= 0 && s.renderablePositions != null && s.renderablePositions.count > si)
            return s.transform.TransformPoint((Vector3)s.renderablePositions[si]);
        if (si >= 0 && s.positions != null && s.positions.count > si)
            return s.transform.TransformPoint((Vector3)s.positions[si]);
#endif
        return r.transform.position;
    }

    bool ValidateRow(Connection c, int rowIndex)
    {
        if (c.tetherRope == null)
        {
            Debug.LogError($"[TetherBinder] Row {rowIndex}: missing tetherRope.");
            return false;
        }

        // We allow leafObject to be temporarily null; we’ll try to auto-fill it later
        // but warn here so you notice.
        if (c.leafObject == null)
            Debug.LogWarning($"[TetherBinder] Row {rowIndex}: leafObject is empty; will try to auto-detect from tether attachments.");

        if (c.mainActorIndex >= 0 && mainRope != null && mainRope.isLoaded &&
            c.mainActorIndex >= mainRope.activeParticleCount)
        {
            Debug.LogError($"[TetherBinder] Row {rowIndex}: mainActorIndex {c.mainActorIndex} out of range for '{mainRope.name}'.");
            return false;
        }
        return true;
    }

}
