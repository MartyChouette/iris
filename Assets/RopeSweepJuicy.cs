// File: RopeSweepCutJuicy.cs  (diff: safer hit test + tear-after-search + single rebuild)
using Obi;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Obi.Samples
{
    [RequireComponent(typeof(ObiRope))]
    public class RopeSweepCutJuicy : MonoBehaviour
    {
        [Header("Core")]
        public Camera cam;

        // --- stock fields ---
        private ObiRope rope;
        private LineRenderer lineRenderer;
        private Vector3 cutStartPosition;   // screen space
        private Vector3 cutEndPosition;     // screen space

        [Header("Juice: Time Freeze / SloMo")]
        public float freezeDuration = 0.15f;
        [Range(0, 1f)] public float freezeTimeScale = 0.05f;
        public float postFreezeSloMoDuration = 0.20f;
        [Range(0, 1f)] public float postFreezeTimeScale = 0.2f;

        [Header("Juice: UI Elements")]
        public RectTransform splashImage;
        public TMP_Text splashText;

        [Header("UI Anim: Image Slide (top-left → center)")]
        public float imageInDuration = 0.18f;
        public float imageHoldDuration = 0.20f;
        public float imageOutDuration = 0.18f;
        public Vector2 imageStartOffset = new Vector2(-280f, +220f);
        public Vector2 imageEndOffset = Vector2.zero;
        public AnimationCurve imageInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve imageOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("UI Anim: Text Drop (from above)")]
        public float textInDuration = 0.16f;
        public float textHoldDuration = 0.25f;
        public float textOutDuration = 0.20f;
        public Vector2 textStartOffset = new Vector2(0f, +240f);
        public Vector2 textEndOffset = Vector2.zero;
        public AnimationCurve textInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve textOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);


        // ADD near your other headers:
        [Header("Juice Toggles")]
        public bool enableUIJuice = true;   // turn TMP + splash anims on/off
        public bool enableTimeJuice = true; // turn freeze/slo-mo on/off


        [Header("Grading")]
        public RopeCutGrader grader;
        public string perfectText = "PERFECT!";
        public string goodText = "GOOD";
        public string badText = "BAD";
        public LayerMask gradeMask = ~0;
        public float gradeRayMaxDistance = 40f;

        [Header("Juice: Liquid (Sap)")]
        public SapFxPool sapPool;
        public bool faceSapTowardCamera = true;

        [Header("Leaf Detach Notify")]
        public float leafCutNotifyRadius = 0.06f;
        public LayerMask leafMask = ~0;
        public bool notifyLeaves = true;
        private readonly Collider[] _leafOverlap = new Collider[32];

        [Header("Cleanup Stem Attachments")]
        [FormerlySerializedAs("destroyAttachmentsOnFirstCut")]
        public bool enableAttachmentCleanup = true;   // uncheck to never delete attachments on first cut
        public Object[] attachmentsToDestroy;
        private bool attachmentsDestroyed;


        // OPTIONAL: if UI is disabled, make sure they stay hidden on Awake()
        void Awake()
        {
            rope = GetComponent<ObiRope>();
            if (!cam) cam = Camera.main;
            AddMouseLine();

            var img = splashImage ? splashImage.GetComponent<Image>() : null;
            if (img) img.raycastTarget = false;
            if (splashText) splashText.raycastTarget = false;

            if (!enableUIJuice)
            {
                if (splashImage) splashImage.gameObject.SetActive(false);
                if (splashText) splashText.gameObject.SetActive(false);
            }
        }


        void OnEnable() { rope.OnSimulationStart += Rope_OnBeginSimulation; }
        void OnDisable() { rope.OnSimulationStart -= Rope_OnBeginSimulation; }
        void OnDestroy() { DeleteMouseLine(); }

        void LateUpdate()
        {
            if (!cam) return;
            ProcessInput();
        }

        private void AddMouseLine(int dotPx = 1, int gapPx = 1600, float dotsPerUnit = 20f)
        {
            GameObject line = new GameObject("Mouse Line");
            lineRenderer = line.AddComponent<LineRenderer>();

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

        private void DeleteMouseLine()
        {
            if (lineRenderer != null)
                Destroy(lineRenderer.gameObject);
        }

        private void ProcessInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                cutStartPosition = Input.mousePosition;
                lineRenderer.SetPosition(0, cam.ScreenToWorldPoint(new Vector3(cutStartPosition.x, cutStartPosition.y, 0.5f)));
                lineRenderer.enabled = true;
            }

            if (lineRenderer.enabled)
                lineRenderer.SetPosition(1, cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0.5f)));

            if (Input.GetMouseButtonUp(0))
            {
                cutEndPosition = Input.mousePosition;
                lineRenderer.enabled = false;

                if (ScreenSpaceCut(cutStartPosition, cutEndPosition))
                    AfterSuccessfulCut();
            }
        }

        private void Rope_OnBeginSimulation(ObiActor actor, float stepTime, float substepTime) { }

        private bool ScreenSpaceCut(Vector2 lineStart, Vector2 lineEnd)
        {
            // --- 1) Find the best (closest-to-swipe-end) intersected segment, don't tear yet ---
            int bestElement = -1;
            float bestS = float.PositiveInfinity; // param along swipe (C->D), smaller ~ closer to start; we'll prefer closer to END, so we’ll invert later.

            // Take a snapshot count so we aren't confused if something else changes the list
            int elemCount = rope.elements.Count;

            for (int i = elemCount - 1; i >= 0; --i) // backwards is safer w.r.t. future mutations
            {
                var e = rope.elements[i];

                // world positions
                Vector3 p1World = SolverToWorld(RenderableAt(e.particle1));
                Vector3 p2World = SolverToWorld(RenderableAt(e.particle2));

                // screen space
                Vector2 s1 = cam.WorldToScreenPoint(p1World);
                Vector2 s2 = cam.WorldToScreenPoint(p2World);

                if (SegmentSegmentIntersection2D(s1, s2, lineStart, lineEnd, out float rSeg, out float sSwipe))
                {
                    // prefer intersections closer to the swipe end (so multiple overlaps feel natural)
                    float inv = 1f - Mathf.Clamp01(sSwipe);
                    if (inv < bestS)
                    {
                        bestS = inv;
                        bestElement = i;
                    }
                }
            }

            if (bestElement < 0) return false;

            // --- 2) Now tear once, then rebuild once ---
            var hitElem = rope.elements[bestElement];

            Vector3 a = SolverToWorld(RenderableAt(hitElem.particle1));
            Vector3 b = SolverToWorld(RenderableAt(hitElem.particle2));

            // approximate hit point using the *current* swipe param:
            // recompute r along the hit segment for a decent sap location
            Vector2 A = cam.WorldToScreenPoint(a);
            Vector2 B = cam.WorldToScreenPoint(b);
            SegmentSegmentIntersection2D(A, B, lineStart, lineEnd, out float rSeg2, out _);
            Vector3 hitWorld = Vector3.Lerp(a, b, Mathf.Clamp01(rSeg2));


            rope.Tear(hitElem);
            rope.RebuildConstraintsFromElements();

            LeafRetargetUtil.RetargetFollowersNear(rope, hitWorld, leafCutNotifyRadius, leafMask);




            // FX
            Vector3 segDir = (b - a).normalized;
            Vector3 normal = faceSapTowardCamera
                             ? cam.transform.forward
                             : Vector3.Cross(segDir, cam.transform.right).normalized;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            FireSap(hitWorld, normal);

            if (notifyLeaves) NotifyLeavesOfCut(hitWorld, leafCutNotifyRadius);

            return true;
        }

        // Prefer renderable positions so screen projection matches visuals during substeps
        private Vector3 RenderableAt(int solverIndex)
        {
            // Obi 7: renderablePositions exists; fall back to positions if needed.
            var rp = rope.solver.renderablePositions;
            if (rp != null && rp.count > solverIndex) return (Vector3)rp[solverIndex];
            return (Vector3)rope.solver.positions[solverIndex];
        }

        private Vector3 SolverToWorld(Vector3 solverPos)
        {
            return rope.solver.transform.TransformPoint(solverPos);
        }

        private void FireSap(Vector3 pos, Vector3 normal)
        {
            if (sapPool == null) return;
            sapPool.Play(pos, normal);
        }

        private int NotifyLeavesOfCut(Vector3 worldPoint, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                worldPoint, radius, _leafOverlap, leafMask, QueryTriggerInteraction.Collide);

            int notified = 0;
            for (int i = 0; i < count; i++)
            {
                var col = _leafOverlap[i];
                if (!col) continue;

                var leaf = col.GetComponentInParent<LeafPullOff>();
                if (leaf != null)
                {
                    leaf.OnStemCutAt(worldPoint);
                    notified++;
                }
                _leafOverlap[i] = null;
            }
            return notified;
        }

        // Robust 2D segment/segment with eps; returns r (A->B) and s (C->D)
        private bool SegmentSegmentIntersection2D(Vector2 A, Vector2 B, Vector2 C, Vector2 D, out float r, out float s)
        {
            const float EPS = 1e-6f;

            Vector2 AB = B - A;
            Vector2 CD = D - C;
            Vector2 AC = C - A;

            float denom = AB.x * CD.y - AB.y * CD.x;
            if (Mathf.Abs(denom) < EPS) { r = s = -1; return false; } // parallel or nearly so

            float rNum = AC.x * CD.y - AC.y * CD.x;
            float sNum = AC.x * AB.y - AC.y * AB.x;

            r = rNum / denom;
            s = sNum / denom;

            return (r >= -EPS && r <= 1f + EPS && s >= -EPS && s <= 1f + EPS);
        }

        // ===== UI / time =====

        // CHANGE: DoFreezeAndUI signature + logic
        IEnumerator DoFreezeAndUI(bool withUI, bool withTimeEffects)
        {
            float originalScale = Time.timeScale;

            if (withTimeEffects && freezeDuration > 0f)
            {
                Time.timeScale = Mathf.Clamp01(freezeTimeScale);
                yield return new WaitForSecondsRealtime(freezeDuration);
            }

            if (withTimeEffects && postFreezeSloMoDuration > 0f)
            {
                Time.timeScale = Mathf.Clamp01(postFreezeTimeScale);
                Coroutine ui = null;
                if (withUI) ui = StartCoroutine(PlaySplashUI());
                yield return new WaitForSecondsRealtime(postFreezeSloMoDuration);
                if (ui != null) yield return ui;
            }
            else
            {
                if (withUI) yield return PlaySplashUI();
            }

            Time.timeScale = originalScale;
        }


        IEnumerator PlaySplashUI()
        {
            Coroutine img = null, txt = null;

            if (splashImage)
                img = StartCoroutine(AnimateRectFromTo(splashImage, imageStartOffset, imageEndOffset, imageInDuration, imageInCurve, true));

            if (splashText)
                txt = StartCoroutine(AnimateRectFromTo(splashText.rectTransform, textStartOffset, textEndOffset, textInDuration, textInCurve, true));

            if (img != null) yield return img;
            if (txt != null) yield return txt;

            yield return new WaitForSecondsRealtime(Mathf.Max(imageHoldDuration, textHoldDuration));

            img = txt = null;

            if (splashImage)
                img = StartCoroutine(AnimateRectFromTo(splashImage, imageEndOffset, imageStartOffset, imageOutDuration, imageOutCurve, false));

            if (splashText)
                txt = StartCoroutine(AnimateRectFromTo(splashText.rectTransform, textEndOffset, textStartOffset, textOutDuration, textOutCurve, false));

            if (img != null) yield return img;
            if (txt != null) yield return txt;
        }

        IEnumerator AnimateRectFromTo(RectTransform rt, Vector2 startOffset, Vector2 endOffset, float dur, AnimationCurve curve, bool makeActive)
        {
            if (rt == null) yield break;
            if (makeActive) rt.gameObject.SetActive(true);

            Vector2 baseAnchor = rt.anchoredPosition;
            float t = 0f;
            dur = Mathf.Max(0.0001f, dur);

            while (t < dur)
            {
                float u = t / dur;
                float k = curve != null ? curve.Evaluate(u) : u;

                rt.anchoredPosition = baseAnchor + Vector2.LerpUnclamped(startOffset, endOffset, k);
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            rt.anchoredPosition = baseAnchor + endOffset;
            if (!makeActive) rt.gameObject.SetActive(false);
        }

        // CHANGE: AfterSuccessfulCut()
        void AfterSuccessfulCut()
        {
            MaybeDestroyAttachmentsOnce();

            string label = "CUT!";
            if (enableUIJuice && grader && RaycastWorldFromScreen(cutEndPosition, out var hit))
            {
                var g = grader.GradeCutFromWorldPoint(hit.point, out _, out _);
                label = g == RopeCutGrader.Grade.Perfect ? perfectText :
                    g == RopeCutGrader.Grade.Good ? goodText : badText;
            }

            if (enableUIJuice && splashText) splashText.text = label;

            // Run time freeze/slomo + (optionally) UI
            StartCoroutine(DoFreezeAndUI(enableUIJuice, enableTimeJuice));
        }


        private RaycastHit _lastRayHit;
        private bool RaycastWorldFromScreen(Vector2 screen, out RaycastHit hit)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out hit, gradeRayMaxDistance, gradeMask, QueryTriggerInteraction.Ignore))
            {
                _lastRayHit = hit;
                return true;
            }
            return false;
        }

        private void MaybeDestroyAttachmentsOnce()
        {
            if (attachmentsDestroyed || !enableAttachmentCleanup) return;
            attachmentsDestroyed = true;
            if (attachmentsToDestroy == null) return;

            foreach (var item in attachmentsToDestroy)
            {
                if (!item) continue;
                if (item is GameObject go) { Destroy(go); continue; }
                if (item is Component comp) { Destroy(comp); continue; }
            }
        }
    }
}
