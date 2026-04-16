using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;

public class NewspaperManager : MonoBehaviour, IStationManager
{
    public enum State { ReadingPaper, Calling, Done }

    public static NewspaperManager Instance { get; private set; }

    public bool IsAtIdleState => CurrentState == State.Done;

    // ─── References ───────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private DayManager dayManager;
    [SerializeField] private NewspaperSurface surface;
    [SerializeField] private Camera mainCamera;

    [Tooltip("WorldSpace Canvas overlay with newspaper text + images (shown when reading).")]
    [SerializeField] private GameObject newspaperOverlay;

    // ─── Cameras (Cinemachine 3) ──────────────────────────────────
    [Header("Cameras")]
    [Tooltip("First-person newspaper reading view (held up in front of player).")]
    [SerializeField] private CinemachineCamera readCamera;

    [SerializeField] private CinemachineBrain brain;

    // ─── Newspaper Object ─────────────────────────────────────────
    [Header("Newspaper Object")]
    [Tooltip("The newspaper quad/plane.")]
    [SerializeField] private Transform newspaperTransform;

    // ─── Dynamic Layout ─────────────────────────────────────────
    [Header("Dynamic Layout")]
    [Tooltip("RectTransform parent for dynamically built ad slots (inside the canvas).")]
    [SerializeField] private RectTransform _contentParent;

    [Tooltip("Background Image on the canvas (for swappable sprite from pool SO).")]
    [SerializeField] private Image _backgroundImage;

    [Tooltip("Optional portrait sprite for Nema's ad.")]
    [SerializeField] private Sprite nemaPortrait;

    // ─── Calling Phase ────────────────────────────────────────────
    [Header("Calling Phase")]
    [SerializeField] private float callingDuration = 2f;
    [SerializeField] private AudioClip phoneRingSFX;

    [Tooltip("SFX played when the player selects a date's personal ad.")]
    [SerializeField] private AudioClip dateSelectedSFX;

    // ─── UI ───────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private GameObject callingUI;
    [SerializeField] private TMP_Text callingText;

    // ─── Events ───────────────────────────────────────────────────
    [Header("Events")]
    public UnityEvent<DatePersonalDefinition> OnDateSelected;
    public UnityEvent OnNewspaperDone;

    // Input managed by IrisInput singleton

    // ─── State ────────────────────────────────────────────────────
    public State CurrentState { get; private set; } = State.Done;

    /// <summary>The read camera for external priority control (DayPhaseManager).</summary>
    public CinemachineCamera ReadCamera => readCamera;

    /// <summary>The newspaper quad transform for repositioning (tossed position).</summary>
    public Transform NewspaperTransform => newspaperTransform;

    private DatePersonalDefinition _selectedDefinition;

    // Dynamic slot tracking
    private readonly List<NewspaperAdSlot> _personalSlotsList = new List<NewspaperAdSlot>();

    // Shared tooltip panel (created once, reused across rebuilds)
    private GameObject _tooltipPanel;
    private TMP_Text _tooltipText;

    // ─── Layout — top half of newspaper (landscape) ─────────────
    // Proportions: wide and short, like a real folded broadsheet.
    private const float CanvasW = 900f;
    private const float CanvasH = 400f;
    private const float HeaderH = 48f;
    private const float Pad = 10f;
    private const float Gap = 8f;

    // Puzzle-piece grid coordinates (relative to canvas center).
    // Content area: y from (CanvasH/2 - HeaderH) to (-CanvasH/2 + Pad)
    //
    //  ┌──────────────┬───────────────────────────────┐
    //  │  NEMA (your  │         DATE #1               │
    //  │   profile)   │      (wide, top-right)        │
    //  │              ├──────────────┬────────────────┤
    //  ├──────────────┤              │                │
    //  │  [comm filler]│   DATE #2   │   DATE #3      │
    //  └──────────────┴──────────────┴────────────────┘

    // Left column width (Nema + commercial fillers)
    private const float LeftColW = 250f;
    // Top row heights — Nema extends lower than Date1 for puzzle stagger
    private const float NemaH = 190f;
    private const float Date1H = 145f; // smaller than Nema, same top edge

    // Cached coroutine waits
    private static readonly WaitForSeconds s_waitCutPause = new WaitForSeconds(0.3f);
    private WaitForSeconds _waitCallingDuration;

    // ─── Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[NewspaperManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-discover layout references from existing canvas if not serialized
        if (newspaperOverlay != null)
        {
            if (_contentParent == null)
                _contentParent = newspaperOverlay.GetComponent<RectTransform>();

            if (_backgroundImage == null)
                _backgroundImage = newspaperOverlay.GetComponent<Image>();
        }

        HideAllUI();
        _waitCallingDuration = new WaitForSeconds(callingDuration);

        if (dayManager != null)
            dayManager.OnNewNewspaper.AddListener(OnNewNewspaper);
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (dayManager != null)
            dayManager.OnNewNewspaper.RemoveListener(OnNewNewspaper);
    }

    private void Start()
    {
        // No-op — state is managed by OnNewNewspaper / DayPhaseManager
    }

    // ─── State Transitions ────────────────────────────────────────

    private void EnterReadingPaper()
    {
        CurrentState = State.ReadingPaper;
        Debug.Log("[NewspaperManager] Reading paper. Hold a personal ad to select!");

        if (newspaperOverlay != null)
            newspaperOverlay.SetActive(true);
    }

    private void EnterDone()
    {
        CurrentState = State.Done;
        Debug.Log("[NewspaperManager] Newspaper done — handing off to DayPhaseManager.");

        if (newspaperOverlay != null)
            newspaperOverlay.SetActive(false);

        OnNewspaperDone?.Invoke();
    }

    /// <summary>
    /// Called by NewspaperAdSlot when the player holds a personal ad.
    /// </summary>
    public void SelectPersonalAd(DatePersonalDefinition def)
    {
        if (CurrentState != State.ReadingPaper) return;
        if (def == null) return;

        _selectedDefinition = def;
        OnDateSelected?.Invoke(def);

        if (dateSelectedSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(dateSelectedSFX);

        if (newspaperOverlay != null)
            newspaperOverlay.SetActive(false);

        if (surface != null)
            surface.PlayPoofEffect();

        Debug.Log($"[NewspaperManager] Selected {def.characterName}'s ad!");
        StartCoroutine(SelectionThenCalling());
    }

    private IEnumerator SelectionThenCalling()
    {
        yield return s_waitCutPause;
        EnterCalling();
    }

    private void EnterCalling()
    {
        CurrentState = State.Calling;

        if (phoneRingSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(phoneRingSFX);

        if (callingUI != null) callingUI.SetActive(true);
        if (callingText != null)
            callingText.text = $"Calling {_selectedDefinition.characterName}...";

        StartCoroutine(CallingSequence());
    }

    private IEnumerator CallingSequence()
    {
        yield return _waitCallingDuration;

        if (callingUI != null) callingUI.SetActive(false);

        DateSessionManager.Instance?.ScheduleDate(_selectedDefinition);
        PhoneController.Instance?.SetPendingDate(_selectedDefinition);

        EnterDone();
    }

    // ─── Newspaper Regeneration (Self-Constructing Layout) ──────

    private void OnNewNewspaper()
    {
        if (dayManager == null) return;

        Debug.Log($"[NewspaperManager] Generating newspaper for day {dayManager.CurrentDay}.");

        // Resize canvas to landscape top-half proportions
        if (_contentParent != null)
            _contentParent.sizeDelta = new Vector2(CanvasW, CanvasH);

        ClearDynamicSlots();

        // Background sprite swap
        var pool = dayManager.Pool;
        if (_backgroundImage != null && pool != null && pool.backgroundSprite != null)
            _backgroundImage.sprite = pool.backgroundSprite;

        EnsureTooltipPanel();

        BuildHeader(pool?.newspaperTitle ?? "The Daily Bloom", dayManager.CurrentDay);
        BuildPuzzleLayout(dayManager.TodayPersonals, dayManager.TodayCommercials);

        if (surface != null)
            surface.ResetSurface();

        _selectedDefinition = null;
        HideAllUI();

        EnterReadingPaper();
    }

    private void ClearDynamicSlots()
    {
        _personalSlotsList.Clear();

        if (_contentParent == null) return;

        for (int i = _contentParent.childCount - 1; i >= 0; i--)
        {
            var child = _contentParent.GetChild(i);
            if (_backgroundImage != null && child.gameObject == _backgroundImage.gameObject)
                continue;
            Destroy(child.gameObject);
        }

        // Tooltip panel lives on the content parent — rebuild it next time
        _tooltipPanel = null;
        _tooltipText = null;
    }

    // ─── Header ─────────────────────────────────────────────────

    private void BuildHeader(string title, int day)
    {
        if (_contentParent == null) return;

        float topY = CanvasH * 0.5f;

        // Title (left-aligned, large)
        CreateTMP("Header_Title", _contentParent,
            new Vector2(-80f, topY - 18f),
            new Vector2(500f, 32f),
            title, 28f, FontStyles.Bold, TextAlignmentOptions.Center);

        // Day label (right-aligned)
        CreateTMP("Header_Day", _contentParent,
            new Vector2(300f, topY - 18f),
            new Vector2(200f, 26f),
            $"Day {day}", 20f, FontStyles.Italic, TextAlignmentOptions.Center);

        // Rule line under header
        var ruleGO = new GameObject("Header_Rule");
        ruleGO.transform.SetParent(_contentParent, false);
        var ruleRT = ruleGO.AddComponent<RectTransform>();
        ruleRT.anchorMin = ruleRT.anchorMax = new Vector2(0.5f, 0.5f);
        ruleRT.anchoredPosition = new Vector2(0f, topY - HeaderH + 4f);
        ruleRT.sizeDelta = new Vector2(CanvasW - Pad * 2f, 2f);
        ruleRT.localScale = Vector3.one;
        ruleGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);
    }

    // ─── Puzzle Layout ──────────────────────────────────────────
    //
    //  Content area starts at y = topY - HeaderH, ends at y = -CanvasH/2 + Pad
    //  Left edge: -CanvasW/2 + Pad,  Right edge: +CanvasW/2 - Pad
    //
    //  The borders between rows are STAGGERED — Nema's box is taller than
    //  Date1, so Date2/Date3 start at a different y than the commercial
    //  filler under Nema. This creates the puzzle / classifieds look.

    private void BuildPuzzleLayout(List<DatePersonalDefinition> personals,
                                   List<CommercialAdDefinition> commercials)
    {
        if (_contentParent == null) return;

        float contentTop = CanvasH * 0.5f - HeaderH;
        float contentBot = -CanvasH * 0.5f + Pad;
        float contentH = contentTop - contentBot;
        float leftX = -CanvasW * 0.5f + Pad;
        float rightX = CanvasW * 0.5f - Pad;
        float totalW = rightX - leftX; // ~880

        float rightAreaW = totalW - LeftColW - Gap;
        float bottomRowH = contentH - Mathf.Max(NemaH, Date1H) - Gap;

        // ── Nema's ad (top-left, taller) ────────────────────────
        float nemaH = NemaH;
        float nemaW = LeftColW;
        float nemaCX = leftX + nemaW * 0.5f;
        float nemaCY = contentTop - nemaH * 0.5f;
        BuildNemaSlot(new Vector2(nemaCX, nemaCY), new Vector2(nemaW, nemaH));

        // ── Top-right row: Date #1 + Comm #2 side by side ────────
        int pCount = personals?.Count ?? 0;
        int cCount = commercials?.Count ?? 0;
        float date1H = Date1H;
        float rightStartX = leftX + LeftColW + Gap;

        // Split top-right: Date1 gets ~65%, Comm2 gets ~35%
        bool hasComm2 = cCount >= 2;
        float comm2W = hasComm2 ? rightAreaW * 0.30f : 0f;
        float date1W = hasComm2 ? rightAreaW - comm2W - Gap : rightAreaW;

        float date1CX = rightStartX + date1W * 0.5f;
        float date1CY = contentTop - date1H * 0.5f;

        if (pCount > 0)
        {
            var slot = CreatePersonalSlotUI(0, personals[0],
                new Vector2(date1CX, date1CY), new Vector2(date1W, date1H));
            if (DayManager.Instance != null && DayManager.Instance.IsLocked(personals[0]))
                slot.SetLocked(true);
            _personalSlotsList.Add(slot);
        }

        // Comm #2 — right of Date1, same height
        if (hasComm2)
        {
            float c2CX = rightStartX + date1W + Gap + comm2W * 0.5f;
            float c2CY = contentTop - date1H * 0.5f;
            BuildCommercialFiller(1, commercials[1],
                new Vector2(c2CX, c2CY), new Vector2(comm2W, date1H));
        }

        // ── Bottom row: Date2, Date3 (below Date1+Comm2, full right width) ──
        float bottomTopRight = contentTop - date1H - Gap;
        float bottomHRight = bottomTopRight - contentBot;
        float halfRightW = (rightAreaW - Gap) * 0.5f;

        if (pCount > 1)
        {
            float d2CX = rightStartX + halfRightW * 0.5f;
            float d2CY = bottomTopRight - bottomHRight * 0.5f;
            var slot = CreatePersonalSlotUI(1, personals[1],
                new Vector2(d2CX, d2CY), new Vector2(halfRightW, bottomHRight));
            if (DayManager.Instance != null && DayManager.Instance.IsLocked(personals[1]))
                slot.SetLocked(true);
            _personalSlotsList.Add(slot);
        }

        if (pCount > 2)
        {
            float d3CX = rightStartX + halfRightW + Gap + halfRightW * 0.5f;
            float d3CY = bottomTopRight - bottomHRight * 0.5f;
            var slot = CreatePersonalSlotUI(2, personals[2],
                new Vector2(d3CX, d3CY), new Vector2(halfRightW, bottomHRight));
            if (DayManager.Instance != null && DayManager.Instance.IsLocked(personals[2]))
                slot.SetLocked(true);
            _personalSlotsList.Add(slot);
        }

        // ── Comm #1 — below Nema, left column ────────────────────
        float commTop = contentTop - nemaH - Gap;
        float commH = commTop - contentBot;

        if (cCount > 0 && commH > 20f)
        {
            float commCX = leftX + nemaW * 0.5f;
            float commCY = commTop - commH * 0.5f;
            BuildCommercialFiller(0, commercials[0],
                new Vector2(commCX, commCY), new Vector2(nemaW, commH));
        }
    }

    // ─── Nema's Ad (Decorative) ─────────────────────────────────

    private void BuildNemaSlot(Vector2 center, Vector2 size)
    {
        var containerGO = new GameObject("NemaAd");
        containerGO.transform.SetParent(_contentParent, false);
        var rt = containerGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;

        // Warm-tinted background
        var bg = containerGO.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.15f, 0.10f, 0.12f);
        bg.raycastTarget = false;

        // "YOUR AD" badge
        CreateTMP("Nema_Badge", rt,
            new Vector2(0f, size.y * 0.38f),
            new Vector2(size.x - 12f, 18f),
            "YOUR AD", 11f, FontStyles.Bold | FontStyles.Italic,
            TextAlignmentOptions.Left);

        // Name
        float nameFontSize = Mathf.Clamp(size.y * 0.10f, 14f, 22f);
        CreateTMP("Nema_Name", rt,
            new Vector2(0f, size.y * 0.22f),
            new Vector2(size.x - 12f, nameFontSize + 4f),
            PlayerData.PlayerName, nameFontSize, FontStyles.Bold,
            TextAlignmentOptions.Left);

        // Portrait
        float portraitSize = Mathf.Clamp(size.x * 0.30f, 32f, 56f);
        var portraitGO = new GameObject("Nema_Portrait");
        portraitGO.transform.SetParent(rt, false);
        var prt = portraitGO.AddComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(size.x * 0.5f - portraitSize * 0.5f - 6f, size.y * 0.26f);
        prt.sizeDelta = new Vector2(portraitSize, portraitSize);
        prt.localScale = Vector3.one;
        var pImg = portraitGO.AddComponent<Image>();
        pImg.raycastTarget = false;
        if (nemaPortrait != null)
        {
            pImg.sprite = nemaPortrait;
            pImg.color = Color.white;
        }
        else
        {
            pImg.color = new Color(0.7f, 0.7f, 0.7f, 0.4f);
        }

        // Body text
        float bodyFontSize = Mathf.Clamp(size.y * 0.065f, 9f, 14f);
        CreateTMP("Nema_Body", rt,
            new Vector2(0f, -size.y * 0.05f),
            new Vector2(size.x - 12f, size.y * 0.38f),
            "Seeking beauty in the everyday.\nLoves flowers, quiet mornings,\nand sharp scissors.",
            bodyFontSize, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        // Phone number
        float phoneFontSize = Mathf.Clamp(size.y * 0.07f, 10f, 14f);
        string phone = "555-" + PlayerData.PlayerName.GetHashCode().ToString("X4").Substring(0, 4);
        CreateTMP("Nema_Phone", rt,
            new Vector2(0f, -size.y * 0.38f),
            new Vector2(size.x - 12f, phoneFontSize + 4f),
            phone, phoneFontSize, FontStyles.Italic, TextAlignmentOptions.Left);
    }

    // ─── Personal Ad Slot (Interactive) ─────────────────────────

    private NewspaperAdSlot CreatePersonalSlotUI(int index, DatePersonalDefinition def,
        Vector2 center, Vector2 size)
    {
        string prefix = $"Personal_{index}";
        float nameFontSize = Mathf.Clamp(size.y * 0.12f, 13f, 24f);
        float bodyFontSize = Mathf.Clamp(size.y * 0.08f, 9f, 15f);
        float phoneFontSize = Mathf.Clamp(size.y * 0.09f, 10f, 18f);
        float portraitSize = Mathf.Clamp(Mathf.Min(size.y, size.x) * 0.25f, 28f, 52f);

        // Container
        var containerGO = new GameObject(prefix);
        containerGO.transform.SetParent(_contentParent, false);
        var containerRT = containerGO.AddComponent<RectTransform>();
        containerRT.anchorMin = containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = center;
        containerRT.sizeDelta = size;
        containerRT.localScale = Vector3.one;

        var bgImg = containerGO.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.06f);
        bgImg.raycastTarget = true;

        // Name (top-left, next to portrait)
        float nameY = size.y * 0.35f;
        var nameGO = CreateTMP($"{prefix}_Name", containerRT,
            new Vector2(-portraitSize * 0.25f, nameY),
            new Vector2(size.x - portraitSize - 16f, nameFontSize + 6f),
            "Name", nameFontSize, FontStyles.Bold, TextAlignmentOptions.Left);
        var nameTMP = nameGO.GetComponent<TMP_Text>();

        // Portrait (top-right corner)
        var portraitGO = new GameObject($"{prefix}_Portrait");
        portraitGO.transform.SetParent(containerRT, false);
        var portraitRT = portraitGO.AddComponent<RectTransform>();
        portraitRT.anchorMin = portraitRT.anchorMax = new Vector2(0.5f, 0.5f);
        portraitRT.anchoredPosition = new Vector2(size.x * 0.5f - portraitSize * 0.5f - 6f, nameY);
        portraitRT.sizeDelta = new Vector2(portraitSize, portraitSize);
        portraitRT.localScale = Vector3.one;
        var portraitImg = portraitGO.AddComponent<Image>();
        portraitImg.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
        portraitImg.raycastTarget = false;
        portraitGO.SetActive(false);

        // Ad body
        var adGO = CreateTMP($"{prefix}_Ad", containerRT,
            new Vector2(0f, 0f),
            new Vector2(size.x - 14f, size.y * 0.40f),
            "Ad text...", bodyFontSize, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        var adTMP = adGO.GetComponent<TMP_Text>();

        // KeywordTooltip
        var kwTooltip = adGO.AddComponent<KeywordTooltip>();
        kwTooltip.InitReferences(adTMP, _tooltipPanel, _tooltipText, mainCamera);

        // Phone number (bottom)
        float phoneY = -size.y * 0.38f;
        var phoneGO = CreateTMP($"{prefix}_Phone", containerRT,
            new Vector2(0f, phoneY),
            new Vector2(size.x - 14f, phoneFontSize + 4f),
            "555-0000", phoneFontSize, FontStyles.Italic, TextAlignmentOptions.Left);
        var phoneTMP = phoneGO.GetComponent<TMP_Text>();

        // Hold-to-select progress fill — covers the full ad slot with a tinted overlay
        var fillGO = new GameObject($"{prefix}_Fill");
        fillGO.transform.SetParent(containerRT, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        fillRT.localScale = Vector3.one;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.75f, 0.55f, 0.35f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Vertical;
        fillImg.fillOrigin = (int)Image.OriginVertical.Bottom;
        fillImg.fillAmount = 0f;
        fillImg.raycastTarget = false;
        fillGO.SetActive(false);

        // NewspaperAdSlot component
        var slot = containerGO.AddComponent<NewspaperAdSlot>();
        slot.InitReferences(containerRT, nameTMP, adTMP, phoneTMP,
            portraitImg, null, fillImg, 1f);

        slot.AssignPersonal(def);
        return slot;
    }

    // ─── Commercial Filler (Decorative) ─────────────────────────

    private void BuildCommercialFiller(int index, CommercialAdDefinition def,
        Vector2 center, Vector2 size)
    {
        string prefix = $"CommFiller_{index}";

        var containerGO = new GameObject(prefix);
        containerGO.transform.SetParent(_contentParent, false);
        var containerRT = containerGO.AddComponent<RectTransform>();
        containerRT.anchorMin = containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = center;
        containerRT.sizeDelta = size;
        containerRT.localScale = Vector3.one;

        var bg = containerGO.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.10f, 0.08f, 0.08f);
        bg.raycastTarget = false;

        float nameFontSize = Mathf.Clamp(size.y * 0.14f, 10f, 16f);
        float bodyFontSize = Mathf.Clamp(size.y * 0.11f, 8f, 13f);

        // Business name
        CreateTMP($"{prefix}_Name", containerRT,
            new Vector2(0f, size.y * 0.28f),
            new Vector2(size.x - 10f, nameFontSize + 4f),
            def.businessName, nameFontSize, FontStyles.Bold, TextAlignmentOptions.Center);

        // Ad text
        CreateTMP($"{prefix}_Body", containerRT,
            new Vector2(0f, -size.y * 0.05f),
            new Vector2(size.x - 10f, size.y * 0.50f),
            def.adText, bodyFontSize, FontStyles.Italic, TextAlignmentOptions.Center);
    }

    // ─── Tooltip Panel ──────────────────────────────────────────

    private void EnsureTooltipPanel()
    {
        if (_tooltipPanel != null) return;
        if (_contentParent == null) return;

        _tooltipPanel = new GameObject("KeywordTooltip");
        _tooltipPanel.transform.SetParent(_contentParent, false);
        var tooltipRT = _tooltipPanel.AddComponent<RectTransform>();
        tooltipRT.anchorMin = tooltipRT.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRT.sizeDelta = new Vector2(220f, 60f);
        tooltipRT.anchoredPosition = Vector2.zero;
        tooltipRT.localScale = Vector3.one;
        var tooltipBg = _tooltipPanel.AddComponent<Image>();
        tooltipBg.color = new Color(0.08f, 0.08f, 0.10f, 0.9f);
        tooltipBg.raycastTarget = false;

        var tooltipTextGO = new GameObject("TooltipText");
        tooltipTextGO.transform.SetParent(_tooltipPanel.transform, false);
        var tooltipTextRT = tooltipTextGO.AddComponent<RectTransform>();
        tooltipTextRT.anchorMin = Vector2.zero;
        tooltipTextRT.anchorMax = Vector2.one;
        tooltipTextRT.offsetMin = new Vector2(12f, 8f);
        tooltipTextRT.offsetMax = new Vector2(-12f, -8f);
        tooltipTextRT.localScale = Vector3.one;
        _tooltipText = tooltipTextGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.text = "";
        _tooltipText.fontSize = 12f;
        _tooltipText.color = new Color(0.9f, 0.9f, 0.9f);
        _tooltipText.alignment = TextAlignmentOptions.TopLeft;
        _tooltipText.textWrappingMode = TextWrappingModes.Normal;

        _tooltipPanel.SetActive(false);
    }

    // ─── Helpers ────────────────────────────────────────────────

    private static GameObject CreateTMP(string name, Transform parent,
        Vector2 anchoredPos, Vector2 size, string text,
        float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = new Color(0.12f, 0.12f, 0.12f);
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return go;
    }

    private void HideAllUI()
    {
        if (callingUI != null) callingUI.SetActive(false);
    }
}
