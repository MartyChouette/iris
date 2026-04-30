using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// During active dates (Phases 1-3), handles hover tooltips and click-to-inspect
/// on ReactableTag items. Hovering shows a cursor-following tooltip with the item's
/// name and the date's preference. Clicking "brings it up in conversation" —
/// the item wiggles, the date reacts, and the flower gauge updates.
/// </summary>
public class DateInspectSystem : MonoBehaviour
{
    public static DateInspectSystem Instance { get; private set; }

    [Header("Raycast")]
    [SerializeField] private LayerMask _inspectLayer = ~0;
    [SerializeField] private Camera _cam;

    [Header("Wiggle")]
    [SerializeField] private float _wiggleDuration = 0.4f;

    // Raycast buffer declared in UpdateHover (s_hoverBuffer)

    // Tooltip UI (built at runtime)
    private GameObject _tooltipRoot;
    private RectTransform _tooltipRT;
    private TMP_Text _tooltipText;
    private CanvasGroup _tooltipCG;

    // State
    private ReactableTag _hoveredTag;
    private readonly HashSet<ReactableTag> _inspected = new();

    private static readonly Color s_likeColor = new Color(1f, 0.4f, 0.55f);
    private static readonly Color s_neutralColor = new Color(0.8f, 0.8f, 0.78f);
    private static readonly Color s_dislikeColor = new Color(0.55f, 0.5f, 0.6f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (Instance != null) return;
        var go = new GameObject("[DateInspectSystem]");
        go.AddComponent<DateInspectSystem>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        BuildTooltip();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_tooltipRoot != null) Destroy(_tooltipRoot);
    }

    /// <summary>True if this tag was already inspected by the player this date.</summary>
    public bool IsInspected(ReactableTag tag) => _inspected.Contains(tag);

    /// <summary>Clear inspected items for a new date.</summary>
    public void ResetForNewDate()
    {
        _inspected.Clear();
        _hoveredTag = null;
        HideTooltip();
    }

    private int _camLookupFrame = -1;

    private void Update()
    {
        // Throttle Camera.main lookups — it's O(n) and calling every frame while null is wasteful
        if (_cam == null && Time.frameCount - _camLookupFrame > 30)
        {
            _cam = Camera.main;
            _camLookupFrame = Time.frameCount;
        }

        // Only active during dates
        if (DateSessionManager.Instance == null || !DateSessionManager.Instance.IsDateActive)
        {
            HideTooltip();
            return;
        }

        // Skip when pointer is over UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            HideTooltip();
            return;
        }

        UpdateHover();
    }

    // Larger buffer — need to see through surface triggers to reach items
    private static readonly RaycastHit[] s_hoverBuffer = new RaycastHit[16];

    private void UpdateHover()
    {
        Vector2 screenPos = IrisInput.CursorPosition;
        // Use ApartmentManager's manual ray to avoid stale projection matrices
        Ray ray = ApartmentManager.Instance != null
            ? ApartmentManager.Instance.ScreenPointToRay(screenPos)
            : _cam.ScreenPointToRay(screenPos);

        ReactableTag foundTag = null;

        // RaycastNonAlloc to see through PlacementSurface triggers and find
        // the actual item behind them (same pattern as GlobalCursorManager).
        int hitCount = Physics.RaycastNonAlloc(ray, s_hoverBuffer, 100f, _inspectLayer);

        // Sort nearest first
        if (hitCount > 1)
            System.Array.Sort(s_hoverBuffer, 0, hitCount, s_hitComparer);

        for (int i = 0; i < hitCount; i++)
        {
            var col = s_hoverBuffer[i].collider;
            // Check the hit object and parents for a ReactableTag
            var tag = col.GetComponent<ReactableTag>();
            if (tag == null) tag = col.GetComponentInParent<ReactableTag>();
            // Also check PlaceableObject — many items have ReactableTag on the
            // same GO as PlaceableObject, but the raycast hits a child collider
            if (tag == null)
            {
                var po = col.GetComponent<PlaceableObject>();
                if (po == null) po = col.GetComponentInParent<PlaceableObject>();
                if (po != null) tag = po.GetComponent<ReactableTag>();
            }
            if (tag != null && tag.IsActive && !tag.IsPrivate)
            {
                foundTag = tag;
                break;
            }
        }

        _hoveredTag = foundTag;

        // Hover tooltip disabled — reactions still fire on click
        _hoveredTag = foundTag;
    }

    private static readonly HitDistComparer s_hitComparer = new();
    private class HitDistComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    // ── Click-to-inspect (called from ObjectGrabber) ────────────────

    /// <summary>
    /// Try to inspect the currently hovered tag. Returns true if consumed.
    /// Called by ObjectGrabber when clicking during a date.
    /// </summary>
    public bool TryInspect()
    {
        if (_hoveredTag == null) return false;
        if (_inspected.Contains(_hoveredTag)) return false;

        var dsm = DateSessionManager.Instance;
        if (dsm == null || !dsm.IsDateActive) return false;
        if (dsm.CurrentDate == null) return false;

        // Only allow item inspection during Phase 3 (Reveal)
        if (dsm.CurrentDatePhase != DateSessionManager.DatePhase.Reveal) return false;

        var tag = _hoveredTag;

        // Each item only affects the score once per date
        if (dsm.TryMarkScored(tag)) return false;

        var prefs = dsm.CurrentDate.preferences;
        var reaction = ReactionEvaluator.EvaluateReactable(tag, prefs);
        int multiplier = DateSessionManager.GetTagEffectMultiplier(tag);

        _inspected.Add(tag);

        // 1. Wiggle the item
        StartCoroutine(WiggleItem(tag.transform));

        // 2. Apply affection change with multiplier → flower gauge pulse/wilt + SFX
        dsm.ApplyReaction(reaction, multiplier);

        // 3. Flower gauge popup (with multiplier suffix)
        if (reaction != ReactionType.Neutral)
        {
            bool liked = reaction == ReactionType.Like;
            string sym = liked ? " \u2665" : " \u2639";
            string popText = tag.DisplayName + sym;
            if (multiplier > 1) popText += $" \u00d7{multiplier}";
            AffectionBar.Instance?.ShowPopup(popText, liked);
        }

        // 4. Date character reaction bubble (bespoke line if configured, else random)
        string bespokeLine = prefs.GetBespokeLine(tag.Tags, reaction);
        var reactionUI = dsm.DateCharacter?.GetComponent<DateReactionUI>();
        if (reactionUI != null)
            reactionUI.ShowLabeledReaction(reaction, tag.DisplayName, tag.ReactionIcon, bespokeLine);

        // 5. Particles at item
        DateSessionManager.SpawnReactionParticles(
            tag.transform.position + Vector3.up * 0.3f, reaction);

        // 6. World-space multiplier popup — always shown (green for positive, red for negative)
        if (reaction != ReactionType.Neutral)
            dsm.SpawnMultiplierPopup(
                tag.transform.position + Vector3.up * 0.52f, multiplier, reaction);

        return true;
    }

    // ── Wiggle animation ────────────────────────────────────────────

    private IEnumerator WiggleItem(Transform target)
    {
        if (target == null) yield break;

        Vector3 originalScale = target.localScale;
        float elapsed = 0f;

        while (elapsed < _wiggleDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _wiggleDuration;

            // Squash-stretch: pop out → squish → settle
            float scaleX, scaleY;
            if (t < 0.25f)
            {
                // Pop out
                float e = t / 0.25f;
                scaleX = Mathf.Lerp(1f, 0.9f, e);
                scaleY = Mathf.Lerp(1f, 1.15f, e);
            }
            else if (t < 0.55f)
            {
                // Squish back
                float e = (t - 0.25f) / 0.3f;
                scaleX = Mathf.Lerp(0.9f, 1.08f, e);
                scaleY = Mathf.Lerp(1.15f, 0.94f, e);
            }
            else
            {
                // Settle
                float e = (t - 0.55f) / 0.45f;
                scaleX = Mathf.Lerp(1.08f, 1f, e);
                scaleY = Mathf.Lerp(0.94f, 1f, e);
            }

            target.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z * scaleX);

            yield return null;
        }

        target.localScale = originalScale;
    }

    // ── Tooltip UI ──────────────────────────────────────────────────

    private void BuildTooltip()
    {
        _tooltipRoot = new GameObject("DateInspectTooltip");
        _tooltipRoot.transform.SetParent(transform, false);

        var canvas = _tooltipRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = _tooltipRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        _tooltipCG = _tooltipRoot.AddComponent<CanvasGroup>();
        _tooltipCG.alpha = 0f;
        _tooltipCG.blocksRaycasts = false;
        _tooltipCG.interactable = false;

        // Background panel
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(_tooltipRoot.transform, false);
        _tooltipRT = panelGO.AddComponent<RectTransform>();
        _tooltipRT.anchorMin = Vector2.zero;
        _tooltipRT.anchorMax = Vector2.zero;
        _tooltipRT.pivot = new Vector2(0f, 0f);
        _tooltipRT.sizeDelta = new Vector2(280f, 36f);

        var bg = panelGO.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        bg.raycastTarget = false;

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(panelGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 2f);
        textRT.offsetMax = new Vector2(-10f, -2f);

        _tooltipText = textGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize = 17f;
        _tooltipText.alignment = TextAlignmentOptions.Left;
        _tooltipText.color = Color.white;
        _tooltipText.raycastTarget = false;

        var theme = IrisTextTheme.Active;
        if (theme != null && theme.primaryFont != null)
            _tooltipText.font = theme.primaryFont;

        _tooltipRoot.SetActive(false);
    }

    private void ShowTooltip(ReactableTag tag, Vector2 screenPos)
    {
        if (_tooltipRoot == null || _tooltipText == null) return;

        var dsm = DateSessionManager.Instance;
        if (dsm == null || dsm.CurrentDate == null) return;

        var prefs = dsm.CurrentDate.preferences;
        var reaction = ReactionEvaluator.EvaluateReactable(tag, prefs);

        string suffix;
        Color color;
        switch (reaction)
        {
            case ReactionType.Like:
                suffix = " \u2014 \u2665 Loves it";
                color = s_likeColor;
                break;
            case ReactionType.Dislike:
                suffix = " \u2014 \u2639 Not a fan";
                color = s_dislikeColor;
                break;
            default:
                suffix = " \u2014 Hmm, okay";
                color = s_neutralColor;
                break;
        }

        bool alreadyInspected = _inspected.Contains(tag);
        _tooltipText.text = tag.DisplayName + suffix + (alreadyInspected ? " \u2713" : "");
        _tooltipText.color = color;

        // Convert screen pixels to reference resolution space (1920×1080)
        float scaleX = 1920f / Screen.width;
        float scaleY = 1080f / Screen.height;
        Vector2 refPos = new Vector2(screenPos.x * scaleX, screenPos.y * scaleY);

        // Position near cursor with offset
        Vector2 pos = new Vector2(refPos.x + 20f, refPos.y + 20f);

        // Clamp to reference resolution bounds
        Vector2 size = _tooltipRT.sizeDelta;
        if (pos.x + size.x > 1920f) pos.x = refPos.x - size.x - 10f;
        if (pos.y + size.y > 1080f) pos.y = refPos.y - size.y - 10f;
        _tooltipRT.anchoredPosition = pos;

        _tooltipRoot.SetActive(true);
        _tooltipCG.alpha = 1f;
    }

    private void HideTooltip()
    {
        if (_tooltipRoot != null && _tooltipRoot.activeSelf)
        {
            _tooltipCG.alpha = 0f;
            _tooltipRoot.SetActive(false);
        }
        _hoveredTag = null;
    }
}
