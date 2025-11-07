// File: RopeSweepCutJuicy.cs
// Same behavior, but using the Unity 6 Input System (no legacy Input.*).

using System.Collections;
using Obi;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.InputSystem;   // ← new input system

namespace Obi.Samples
{
    [RequireComponent(typeof(ObiRope))]
    public class RopeSweepCutJuicy : MonoBehaviour
    {
        [Header("Core")]
        public Camera cam;

        // --- Input System actions ---
        [Header("Input (Unity 6 Input System)")]
        [Tooltip("Vector2 action bound to <Pointer>/position")]
        public InputActionReference pointerPositionAction;
        [Tooltip("Button action bound to <Pointer>/press (or <Mouse>/leftButton)")]
        public InputActionReference pointerPressAction;

        // --- Obi / internals ---
        private ObiRope rope;
        private LineRenderer lineRenderer;
        private Vector3 cutStartPosition;   // screen space
        private Vector3 cutEndPosition;     // screen space

        // Press edge tracking
        private bool _wasPressedLastFrame;

        // ======================== JUICE: TIME ========================
        [Header("Juice: Time Freeze / SloMo")]
        [Tooltip("How long to freeze/slo-mo when a cut lands (seconds, unscaled).")]
        public float freezeDuration = 0.15f;

        [Tooltip("Timescale during the freeze window. 0 = full freeze, 0.05–0.2 = buttery slo-mo.")]
        [Range(0, 1f)] public float freezeTimeScale = 0.05f;

        [Tooltip("After the freeze, keep a little slo-mo for this many seconds (unscaled).")]
        public float postFreezeSloMoDuration = 0.20f;

        [Tooltip("Timescale during post-freeze slo-mo.")]
        [Range(0, 1f)] public float postFreezeTimeScale = 0.2f;

        [Header("Global Juice (optional shared toggles)")]
        public JuiceSettings juiceSettings;   // if present, overrides the two toggles below

        [Header("Juice Toggles (local)")]
        public bool enableUIJuice = true;     // TMP + splash anims on/off
        public bool enableTimeJuice = true;   // freeze/slo-mo on/off

        // ======================== JUICE: UI ========================
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

        // ======================== GRADING ========================
        [Header("Grading")]
        public RopeCutGrader grader;
        public string perfectText = "PERFECT!";
        public string goodText = "GOOD";
        public string badText = "BAD";
        public LayerMask gradeMask = ~0;
        public float gradeRayMaxDistance = 40f;

        // ======================== LIQUID FX ========================
        [Header("Juice: Liquid (Sap)")]
        public SapFxPool sapPool;
        public bool faceSapTowardCamera = true;

        // ======================== LEAF NOTIFY ========================
        [Header("Leaf Detach Notify")]
        [Tooltip("Radius around hit point to look for leaves to notify (meters).")]
        public float leafCutNotifyRadius = 0.06f;
        public LayerMask leafMask = ~0;
        public bool notifyLeaves = true;
        private readonly Collider[] _leafOverlap = new Collider[32];

        // ======================== ATTACHMENT CLEANUP ========================
        [Header("Cleanup Stem Attachments")]
        [FormerlySerializedAs("destroyAttachmentsOnFirstCut")]
        public bool enableAttachmentCleanup = true;   // uncheck to never delete attachments on first cut
        public Object[] attachmentsToDestroy;
        private bool attachmentsDestroyed;

        // ======================== LINE STYLE (CLASSIC) ========================
        [Header("Line Style")]
        [Tooltip("Draw the guideline exactly like the old script (fixed z=0.5 ScreenToWorld + dotted Unlit).")]
        public bool useClassicLineDrawing = true;

        [Tooltip("Optional: assign a URP-safe material (e.g., URP/Unlit). If null, a classic Unlit/Texture dotted mat is generated.")]
        public Material lineMaterial;

        // ====================================================================
        // LIFECYCLE
        // ====================================================================
        void Awake()
        {
            rope = GetComponent<ObiRope>();
            cam = ResolveCamera();

            if (useClassicLineDrawing) AddMouseLine_Classic();
            else AddMouseLine_Modern(); // visually similar

            var img = splashImage ? splashImage.GetComponent<Image>() : null;
            if (img) img.raycastTarget = false;
            if (splashText) splashText.raycastTarget = false;

            if (!EnableUI())
            {
                if (splashImage) splashImage.gameObject.SetActive(false);
                if (splashText) splashText.gameObject.SetActive(false);
            }


        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        string _diag;
        void Update()
        {
            _diag =
                $"Rope? {(rope ? "ok" : "MISSING")}\n" +
                $"Solver? {(rope && rope.solver ? "ok" : "MISSING")}\n" +
                $"Cam? {(cam ? "ok" : "MISSING")}\n" +
                $"Line? {(lineRenderer ? (lineRenderer.sharedMaterial ? "ok" : "NO MAT") : "MISSING")}\n" +
                $"PosAction? {(pointerPositionAction ? "set" : "null")}\n" +
                $"PressAction? {(pointerPressAction ? "set" : "null")}\n";
        }
        void OnGUI()
        {
            if (!string.IsNullOrEmpty(_diag))
                GUI.Label(new Rect(10, 10, 600, 200), _diag);
        }
#endif


        void OnEnable()
        {
            rope.OnSimulationStart += Rope_OnBeginSimulation;

            // enable input actions
            if (pointerPositionAction != null) pointerPositionAction.action.Enable();
            if (pointerPressAction != null) pointerPressAction.action.Enable();
            _wasPressedLastFrame = false;
        }

        void OnDisable()
        {
            rope.OnSimulationStart -= Rope_OnBeginSimulation;

            if (pointerPositionAction != null) pointerPositionAction.action.Disable();
            if (pointerPressAction != null) pointerPressAction.action.Disable();
        }

        void OnDestroy()
        {
            if (lineRenderer) Destroy(lineRenderer.gameObject);
        }

        void LateUpdate()
        {
            if (!ResolveCamera()) return;
            ProcessInput_NewInputSystem();
        }

        private Camera ResolveCamera()
        {
            if (cam) return cam;
            if (Camera.main) return cam = Camera.main;
            if (Camera.allCamerasCount > 0) return cam = Camera.allCameras[0];
            return null;
        }

        // ====================================================================
        // INPUT / LINE DRAWING (Input System)
        // ====================================================================
        private void ProcessInput_NewInputSystem()
        {
            Vector2 pointerPos = ReadPointerPosition();
            bool pressed = ReadPointerPressed();

            // press started
            if (pressed && !_wasPressedLastFrame)
            {
                cutStartPosition = pointerPos;

                if (lineRenderer)
                {
                    Vector3 a = useClassicLineDrawing
                        ? ClassicScreenToWorld(cutStartPosition)
                        : cam.ScreenToWorldPoint(new Vector3(cutStartPosition.x, cutStartPosition.y, 0.5f));

                    lineRenderer.enabled = true;
                    lineRenderer.positionCount = 2;
                    lineRenderer.SetPosition(0, a);
                    lineRenderer.SetPosition(1, a);
                }
            }

            // while pressed, update line end
            if (lineRenderer && lineRenderer.enabled && pressed)
            {
                Vector3 b = useClassicLineDrawing
                    ? ClassicScreenToWorld(pointerPos)
                    : cam.ScreenToWorldPoint(new Vector3(pointerPos.x, pointerPos.y, 0.5f));

                lineRenderer.SetPosition(1, b);
            }

            // press released
            if (!pressed && _wasPressedLastFrame)
            {
                cutEndPosition = pointerPos;
                if (lineRenderer) lineRenderer.enabled = false;

                if (ScreenSpaceCut(cutStartPosition, cutEndPosition))
                    AfterSuccessfulCut();
            }

            _wasPressedLastFrame = pressed;
        }

        private Vector2 ReadPointerPosition()
        {
            if (pointerPositionAction != null)
                return pointerPositionAction.action.ReadValue<Vector2>();

            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.position.ReadValue();

            if (UnityEngine.InputSystem.Touchscreen.current != null)
                return UnityEngine.InputSystem.Touchscreen.current.primaryTouch.position.ReadValue();

            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f); // safe center fallback
        }


        private bool ReadPointerPressed()
        {
            // Prefer the action if present
            if (pointerPressAction != null)
            {
                // Use analog button value; more robust across devices/builds
                float v = pointerPressAction.action.ReadValue<float>();
                return v > 0.5f;
            }

            // Fallbacks
            if (UnityEngine.InputSystem.Mouse.current != null)
                return UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;

            if (UnityEngine.InputSystem.Touchscreen.current != null)
                return UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.isPressed;

            return false;
        }


        // Classic z=0.5 screen→world like the older script
        private Vector3 ClassicScreenToWorld(Vector2 screen)
        {
            return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0.5f));
        }

        // Build a dotted line (classic Unlit/Texture, or URP-safe override if assigned)
        private void AddMouseLine_Classic(int dotPx = 50, int gapPx = 1600)
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

            // 0) If you assigned a material in the Inspector, just use it and bail.
            if (lineMaterial != null) { lineRenderer.sharedMaterial = lineMaterial; return; }

            // 1) Make the dotted texture (optional; comment out if you don’t want dots)
            int w = Mathf.Max(1, dotPx + Mathf.Max(1, gapPx));
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, 0, x < dotPx ? Color.black : new Color(0, 0, 0, 0));
            tex.Apply();

            // 2) Try URP/Unlit first, then Sprites/Default. Never construct with null.
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null)
            {
                var m = new Material(s);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
                lineRenderer.sharedMaterial = m;
                return;
            }

            s = Shader.Find("Sprites/Default");
            if (s != null)
            {
                var m = new Material(s);
                m.mainTexture = tex;
                m.color = Color.white;
                lineRenderer.sharedMaterial = m;
                return;
            }

            // 3) Last resort: don’t crash; disable the line but keep cutting functional.
            Debug.LogWarning("[RopeSweepCutJuicy] No shader found for line in Player; line will be invisible but cutting will still work.");
        }



        // Optional modern tiny-dot builder (kept for completeness)
        private void AddMouseLine_Modern(int dotPx = 1, int gapPx = 1600)
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

            if (lineMaterial != null)
            {
                lineRenderer.sharedMaterial = lineMaterial;
                return;
            }

            int w = Mathf.Max(1, dotPx + Mathf.Max(1, gapPx));
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, 0, x < dotPx ? Color.black : new Color(0, 0, 0, 0));
            tex.Apply();

            Material mat = null;

            if (lineMaterial != null)
            {
                mat = lineMaterial;
            }
            else
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                if (mat != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetColor("_BaseColor", Color.white);
                }
                else
                {
                    mat = new Material(Shader.Find("Sprites/Default"));
                    if (mat != null) mat.color = Color.white;
                }
            }

            lineRenderer.sharedMaterial = mat;
        }

        // ====================================================================
        // OBI CUT LOGIC (unchanged)
        // ====================================================================
        private void Rope_OnBeginSimulation(ObiActor actor, float stepTime, float substepTime) { }

        private bool ScreenSpaceCut(Vector2 lineStart, Vector2 lineEnd)
        {
            int bestElement = -1;
            float bestS = float.PositiveInfinity;

            int elemCount = rope.elements.Count;
            for (int i = elemCount - 1; i >= 0; --i)
            {
                var e = rope.elements[i];

                Vector3 p1World = SolverToWorld(RenderableAt(e.particle1));
                Vector3 p2World = SolverToWorld(RenderableAt(e.particle2));

                Vector2 s1 = cam.WorldToScreenPoint(p1World);
                Vector2 s2 = cam.WorldToScreenPoint(p2World);

                if (SegmentSegmentIntersection2D(s1, s2, lineStart, lineEnd, out float rSeg, out float sSwipe))
                {
                    float inv = 1f - Mathf.Clamp01(sSwipe);
                    if (inv < bestS)
                    {
                        bestS = inv;
                        bestElement = i;
                    }
                }
            }

            if (bestElement < 0) return false;

            var hitElem = rope.elements[bestElement];

            Vector3 a = SolverToWorld(RenderableAt(hitElem.particle1));
            Vector3 b = SolverToWorld(RenderableAt(hitElem.particle2));

            Vector2 A = cam.WorldToScreenPoint(a);
            Vector2 B = cam.WorldToScreenPoint(b);
            SegmentSegmentIntersection2D(A, B, lineStart, lineEnd, out float rSeg2, out _);
            Vector3 hitWorld = Vector3.Lerp(a, b, Mathf.Clamp01(rSeg2));

            rope.Tear(hitElem);
            rope.RebuildConstraintsFromElements();

            LeafRetargetUtil.RetargetFollowersNear(rope, hitWorld, leafCutNotifyRadius, leafMask);

            Vector3 segDir = (b - a).normalized;
            Vector3 normal = faceSapTowardCamera
                             ? cam.transform.forward
                             : Vector3.Cross(segDir, cam.transform.right).normalized;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;

            FireSap(hitWorld, normal);
            if (notifyLeaves) NotifyLeavesOfCut(hitWorld, leafCutNotifyRadius);

            return true;
        }

        private Vector3 RenderableAt(int solverIndex)
        {
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

        private bool SegmentSegmentIntersection2D(Vector2 A, Vector2 B, Vector2 C, Vector2 D, out float r, out float s)
        {
            const float EPS = 1e-6f;

            Vector2 AB = B - A;
            Vector2 CD = D - C;
            Vector2 AC = C - A;

            float denom = AB.x * CD.y - AB.y * CD.x;
            if (Mathf.Abs(denom) < EPS) { r = s = -1; return false; }

            float rNum = AC.x * CD.y - AC.y * CD.x;
            float sNum = AC.x * AB.y - AC.y * AB.x;

            r = rNum / denom;
            s = sNum / denom;

            return (r >= -EPS && r <= 1f + EPS && s >= -EPS && s <= 1f + EPS);
        }

        // ====================================================================
        // JUICE: UI/TIME & AFTER-CUT (unchanged)
        // ====================================================================
        private bool EnableUI() => juiceSettings ? juiceSettings.enableUIJuice : enableUIJuice;
        private bool EnableTime() => juiceSettings ? juiceSettings.enableTimeJuice : enableTimeJuice;

        private IEnumerator DoFreezeAndUI(bool withUI, bool withTimeEffects)
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

        private IEnumerator PlaySplashUI()
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

        private IEnumerator AnimateRectFromTo(RectTransform rt, Vector2 startOffset, Vector2 endOffset, float dur, AnimationCurve curve, bool makeActive)
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

        private void AfterSuccessfulCut()
        {
            MaybeDestroyAttachmentsOnce();

            string label = "CUT!";
            if (EnableUI() && grader && RaycastWorldFromScreen(cutEndPosition, out var hit))
            {
                var g = grader.GradeCutFromWorldPoint(hit.point, out _, out _);
                label = g == RopeCutGrader.Grade.Perfect ? perfectText :
                        g == RopeCutGrader.Grade.Good ? goodText : badText;
            }

            if (EnableUI() && splashText) splashText.text = label;

            StartCoroutine(DoFreezeAndUI(EnableUI(), EnableTime()));
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
