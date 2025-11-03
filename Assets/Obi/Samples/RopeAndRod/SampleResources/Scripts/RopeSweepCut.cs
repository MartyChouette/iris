using System.Collections;
using UnityEngine;
using Obi;

namespace Obi.Samples
{
    [RequireComponent(typeof(ObiRope))]
    public class RopeSweepCut : MonoBehaviour
    {
        [Header("Refs")]
        public Camera cam;

        [Header("Dormant (version-safe)")]
        public bool startDormant = true;          // rope disabled until first real cut
        public GameObject dormantProxy;           // optional visual mesh to show while dormant

        // runtime
        ObiRope rope;
        RopeBleedOnCut bleeder;                   // <<— bleeder on same GO (assign SapFxPool there)
        LineRenderer lineRenderer;

        Vector2 cutStartPosition;
        Vector2 cutEndPosition;
        bool cutRequested;

        bool physicsActivated = false;            // set true once rope is enabled
        bool pendingCutAfterActivate = false;     // re-issue cut next sim tick after enable

        void Awake()
        {
            rope = GetComponent<ObiRope>();
            bleeder = GetComponent<RopeBleedOnCut>();
            AddMouseLine();
        }

        IEnumerator Start()
        {
            if (startDormant && rope != null)
            {
                rope.enabled = false;                       // hide rope & pause physics
                if (dormantProxy) dormantProxy.SetActive(true);
            }
            yield break;
        }

        void OnEnable()
        {
            // Subscribe to sim start (Unity re-hooks automatically on enable).
            rope.OnSimulationStart += Rope_OnBeginSimulation;
        }

        void OnDisable()
        {
            rope.OnSimulationStart -= Rope_OnBeginSimulation;
        }

        void OnDestroy()
        {
            DeleteMouseLine();
        }

        void LateUpdate()
        {
            if (!cam) return;
            ProcessInput();
        }

        // Called by Obi each simulation step
        void Rope_OnBeginSimulation(ObiActor actor, float stepTime, float substepTime)
        {
            // If we just turned physics on last frame, perform deferred cut now.
            if (pendingCutAfterActivate)
            {
                PerformSweepCut(cutStartPosition, cutEndPosition);
                pendingCutAfterActivate = false;
                return;
            }

            if (!cutRequested) return;

            // If rope is still dormant, enable it first and defer the cut to next tick.
            if (!physicsActivated && startDormant)
            {
                ActivatePhysics();
                pendingCutAfterActivate = true;   // retry same cut next sim step
                cutRequested = false;             // consume this attempt
                return;
            }

            // Normal path: rope already active -> cut now.
            PerformSweepCut(cutStartPosition, cutEndPosition);
            cutRequested = false;
        }

        // Enable rope physics in a version-safe way
        void ActivatePhysics()
        {
            if (physicsActivated) return;
            physicsActivated = true;

            if (dormantProxy) dormantProxy.SetActive(false);
            rope.enabled = true;
        }

        // Core sweep cut: returns true if any element was torn
        bool PerformSweepCut(Vector2 lineStart, Vector2 lineEnd)
        {
            bool ropeCut = false;

            // Iterate elements and test their screen-space segments against the sweep.
            for (int i = 0; i < rope.elements.Count; ++i)
            {
                var e = rope.elements[i];

                Vector3 p1 = rope.solver.positions[e.particle1];
                Vector3 p2 = rope.solver.positions[e.particle2];

                Vector2 s1 = cam.WorldToScreenPoint(p1);
                Vector2 s2 = cam.WorldToScreenPoint(p2);

                if (SegmentSegmentIntersection(s1, s2, lineStart, lineEnd, out _, out _))
                {
                    ropeCut = true;

                    // --- NEW: trigger bleeding right at the cut particle(s) ---
                    if (bleeder != null)
                    {
                        // Use midpoint actor index for nice-looking burst (or call both ends).
                        int aiCut = (e.particle1 + e.particle2) >> 1;
                        bleeder.BleedAtActorIndex(aiCut);
                        // If you want both ends instead, uncomment:
                        // bleeder.BleedAtActorIndex(e.particle1);
                        // bleeder.BleedAtActorIndex(e.particle2);
                    }

                    // Perform the tear on this element
                    rope.Tear(e);
                }
            }

            if (ropeCut)
                rope.RebuildConstraintsFromElements();

            return ropeCut;
        }

        // ---------- Input & UI (dotted aim line) ----------
        void ProcessInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                cutStartPosition = Input.mousePosition;
                Vector3 a = cam.ScreenToWorldPoint(new Vector3(cutStartPosition.x, cutStartPosition.y, 0.5f));
                if (lineRenderer)
                {
                    lineRenderer.enabled = true;
                    lineRenderer.positionCount = 2;
                    lineRenderer.SetPosition(0, a);
                    lineRenderer.SetPosition(1, a);
                }
            }

            if (lineRenderer && lineRenderer.enabled)
            {
                Vector3 b = cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0.5f));
                lineRenderer.SetPosition(1, b);
            }

            if (Input.GetMouseButtonUp(0))
            {
                cutEndPosition = Input.mousePosition;
                if (lineRenderer) lineRenderer.enabled = false;
                cutRequested = true;
            }
        }

        void AddMouseLine(int dotPx = 1, int gapPx = 1600, float dotsPerUnit = 20f)
        {
            var go = new GameObject("Mouse Line");
            lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.005f;
            lineRenderer.numCapVertices = 0;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Tile;
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;

            int w = Mathf.Max(1, dotPx + Mathf.Max(1, gapPx));
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            for (int x = 0; x < w; x++)
                tex.SetPixel(x, 0, x < dotPx ? Color.black : new Color(0, 0, 0, 0));
            tex.Apply();

            var mat = new Material(Shader.Find("Unlit/Texture"));
            mat.mainTexture = tex;
            mat.color = Color.white;
            lineRenderer.sharedMaterial = mat;

            // simple tiling updater
            UpdateTiling(dotsPerUnit, mat);
        }

        void UpdateTiling(float dotsPerUnit, Material mat)
        {
            if (!lineRenderer || lineRenderer.positionCount < 2) return;
            Vector3 a = lineRenderer.GetPosition(0);
            Vector3 b = lineRenderer.GetPosition(lineRenderer.positionCount - 1);
            float length = Vector3.Distance(a, b);
            float cycles = Mathf.Max(0.0001f, length * Mathf.Max(0.0001f, dotsPerUnit));
            mat.mainTextureScale = new Vector2(cycles, 1f);
        }

        void DeleteMouseLine()
        {
            if (lineRenderer) Destroy(lineRenderer.gameObject);
        }

        // 2D segment intersection in screen space
        bool SegmentSegmentIntersection(Vector2 A, Vector2 B, Vector2 C, Vector2 D, out float r, out float s)
        {
            float denom = (B.x - A.x) * (D.y - C.y) - (B.y - A.y) * (D.x - C.x);
            float rNum = (A.y - C.y) * (D.x - C.x) - (A.x - C.x) * (D.y - C.y);
            float sNum = (A.y - C.y) * (B.x - A.x) - (A.x - C.x) * (B.y - A.y);

            if (Mathf.Approximately(denom, 0f))
            { r = -1f; s = -1f; return false; }

            r = rNum / denom;
            s = sNum / denom;
            return (r >= 0f && r <= 1f && s >= 0f && s <= 1f);
        }
    }
}
