using UnityEngine;
using Obi;

namespace Obi.Samples
{
    [RequireComponent(typeof(ObiRope))]
    public class RopeSweepCut : MonoBehaviour
    {
        [Header("Refs")]
        public Camera cam;

        [Header("Leaf drop on cut")]
        [Tooltip("Small downward nudge so detached leaves start falling decisively.")]
        public float leafDownImpulse = 0.35f;

        [Tooltip("How close a leaf’s pinhole must be to the cut point to count (meters).")]
        public float leafCutRadius = 0.06f;

        private ObiRope rope;
        private RopeBleedOnCut bleeder;     // optional (add RopeBleedOnCut + SapFxPool to same GO)
        private LineRenderer lineRenderer;

        private Vector2 cutStartPosition;
        private Vector2 cutEndPosition;
        private bool cutRequested;

        void Awake()
        {
            rope = GetComponent<ObiRope>();
            bleeder = GetComponent<RopeBleedOnCut>();
            AddMouseLine();
        }

        void OnEnable() { rope.OnSimulationStart += Rope_OnBeginSimulation; }
        void OnDisable() { rope.OnSimulationStart -= Rope_OnBeginSimulation; }

        void OnDestroy()
        {
            if (lineRenderer) Destroy(lineRenderer.gameObject);
        }

        void LateUpdate()
        {
            if (!cam) return;
            ProcessInput();
        }

        void Rope_OnBeginSimulation(ObiActor actor, float stepTime, float substepTime)
        {
            if (!cutRequested) return;
            PerformSweepCut(cutStartPosition, cutEndPosition);
            cutRequested = false;
        }

        // Cut across the rope using screen-space segment tests
        bool PerformSweepCut(Vector2 lineStart, Vector2 lineEnd)
        {
            bool ropeCut = false;

            for (int i = 0; i < rope.elements.Count; ++i)
            {
                var e = rope.elements[i];

                // world endpoints of this rope segment:
                Vector3 p1 = rope.solver.positions[e.particle1];
                Vector3 p2 = rope.solver.positions[e.particle2];

                // project to screen:
                Vector2 s1 = cam.WorldToScreenPoint(p1);
                Vector2 s2 = cam.WorldToScreenPoint(p2);

                // r = location along rope segment p1->p2, s = location along mouse line
                if (SegmentSegmentIntersection(s1, s2, lineStart, lineEnd, out float r, out _))
                {
                    ropeCut = true;

                    // precise world cut point along the rope segment:
                    Vector3 worldCutPoint = Vector3.Lerp(p1, p2, Mathf.Clamp01(r));

                    // 1) bleed at the cut (keep your actor-index fallback)
                    if (bleeder != null)
                    {
                        int aiCut = (e.particle1 + e.particle2) >> 1;
                        // if your RopeBleedOnCut also has a "BleedAtWorld" you can call it here:
                        // bleeder.BleedAtWorld(worldCutPoint);
                        bleeder.BleedAtActorIndex(aiCut);
                    }

                    // 2) actually tear the rope at this element
                    rope.Tear(e);

                    // 3) tell nearby leaves about the cut point (they’ll self-decide via radius)
                    NotifyLeavesNearPoint(worldCutPoint, leafCutRadius);
                }
            }

            if (ropeCut)
                rope.RebuildConstraintsFromElements();

            return ropeCut;
        }

        // tell leaves whose pinhole is near the cut to detach; add a tiny downward kick
        void NotifyLeavesNearPoint(Vector3 worldPoint, float pinholeRadius)
        {
            var leaves = rope.GetComponentsInChildren<LeafPullOff>(true);
            if (leaves == null || leaves.Length == 0) return;

            foreach (var leaf in leaves)
            {
                if (!leaf || !leaf.explicitPinhole) continue;

                // quick distance test to the socket itself
                float d = Vector3.Distance(leaf.explicitPinhole.position, worldPoint);
                if (d > pinholeRadius) continue;

                // ask the leaf to handle its own detach/sap (it checks its own radius too)
                leaf.OnStemCutAt(worldPoint);

                // help it start falling (works whether it detached this frame or next)
                var rb = leaf.GetComponent<Rigidbody>();
                if (rb && !rb.isKinematic)
                    rb.AddForce(Vector3.down * leafDownImpulse, ForceMode.VelocityChange);
            }
        }

        // ---------- Input / dotted line ----------
        void ProcessInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                cutStartPosition = Input.mousePosition;
                if (!lineRenderer) return;

                var a = cam.ScreenToWorldPoint(new Vector3(cutStartPosition.x, cutStartPosition.y, 0.5f));
                lineRenderer.enabled = true;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, a);
                lineRenderer.SetPosition(1, a);
            }

            if (lineRenderer && lineRenderer.enabled)
            {
                var b = cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0.5f));
                lineRenderer.SetPosition(1, b);
            }

            if (Input.GetMouseButtonUp(0))
            {
                cutEndPosition = Input.mousePosition;
                if (lineRenderer) lineRenderer.enabled = false;
                cutRequested = true;
            }
        }

        void AddMouseLine(int dotPx = 50, int gapPx = 1600, float dotsPerUnit = 20f)
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
        }

        // 2D segment intersection in screen space (returns r,s)
        bool SegmentSegmentIntersection(Vector2 A, Vector2 B, Vector2 C, Vector2 D, out float r, out float s)
        {
            float denom = (B.x - A.x) * (D.y - C.y) - (B.y - A.y) * (D.x - C.x);
            float rNum = (A.y - C.y) * (D.x - C.x) - (A.x - C.x) * (D.y - C.y);
            float sNum = (A.y - C.y) * (B.x - A.x) - (A.x - C.x) * (B.y - A.y);

            if (Mathf.Approximately(denom, 0f))
            { r = -1f; s = -1f; return false; }

            r = rNum / denom;   // 0..1 along A->B (rope segment)
            s = sNum / denom;   // 0..1 along C->D (mouse line)
            return (r >= 0f && r <= 1f && s >= 0f && s <= 1f);
        }
    }
}
