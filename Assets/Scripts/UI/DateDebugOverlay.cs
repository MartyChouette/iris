using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Scene-scoped singleton toggled by F1. Shows extensive live debug state:
/// game clock, day phase, date session, preferences, mood, NPC state,
/// cleanliness, smell, items, accumulated reactions, and a scrolling reaction log.
/// </summary>
public class DateDebugOverlay : MonoBehaviour
{
    public static DateDebugOverlay Instance { get; private set; }

    /// <summary>When true, game timers (GameClock, prep timer, arrival timer) stop ticking
    /// but gameplay continues normally (movement, interaction, physics all work).</summary>
    public static bool IsTimePaused { get; set; }

    [SerializeField] private GameObject _overlayRoot;
    [SerializeField] private TMP_Text _debugText;

    private InputAction _toggleAction;
    private InputAction _scrollUpAction;
    private InputAction _scrollDownAction;
    private InputAction _timePauseAction;
    private readonly List<string> _reactionLog = new List<string>();
    private const int MaxLogEntries = 20;
    private int _frameCounter;
    private int _scrollOffset;
    private const int ScrollStep = 8;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DateDebugOverlay] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _toggleAction = new InputAction("DebugToggle", InputActionType.Button, "<Keyboard>/f1");
        _scrollUpAction = new InputAction("DebugScrollUp", InputActionType.Button, "<Keyboard>/pageUp");
        _scrollDownAction = new InputAction("DebugScrollDown", InputActionType.Button, "<Keyboard>/pageDown");
        _timePauseAction = new InputAction("DebugTimePause", InputActionType.Button, "<Keyboard>/p");

        if (_overlayRoot != null)
            _overlayRoot.SetActive(false);
    }

    private void OnEnable()
    {
        _toggleAction.Enable();
        _scrollUpAction.Enable();
        _scrollDownAction.Enable();
        _timePauseAction.Enable();
    }

    private void OnDisable()
    {
        _toggleAction.Disable();
        _scrollUpAction.Disable();
        _scrollDownAction.Disable();
        _timePauseAction.Disable();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsTimePaused = false;
        }

        _toggleAction?.Dispose();
        _scrollUpAction?.Dispose();
        _scrollDownAction?.Dispose();
        _timePauseAction?.Dispose();
        _toggleAction = null;
        _scrollUpAction = null;
        _scrollDownAction = null;
        _timePauseAction = null;
    }

    private void Update()
    {
        if (_toggleAction.WasPressedThisFrame() && _overlayRoot != null)
            _overlayRoot.SetActive(!_overlayRoot.activeSelf);

        if (_overlayRoot == null || !_overlayRoot.activeSelf || _debugText == null) return;

        // Scroll input
        if (_scrollUpAction.WasPressedThisFrame())
            _scrollOffset = Mathf.Max(0, _scrollOffset - ScrollStep);
        if (_scrollDownAction.WasPressedThisFrame())
            _scrollOffset += ScrollStep;

        // Debug time pause (P key) — freezes game timers only, not gameplay
        if (_timePauseAction.WasPressedThisFrame())
            IsTimePaused = !IsTimePaused;

        // Throttle rebuild to every 10 frames for performance
        _frameCounter++;
        if (_frameCounter % 10 != 0) return;

        RebuildText();
    }

    /// <summary>Append a reaction to the scrolling log.</summary>
    public void LogReaction(string text)
    {
        _reactionLog.Add(text);
        if (_reactionLog.Count > MaxLogEntries)
            _reactionLog.RemoveAt(0);
    }

    private void RebuildText()
    {
        var sb = new System.Text.StringBuilder(2048);

        if (IsTimePaused)
            sb.AppendLine("<color=red><b>[TIMERS PAUSED \u2014 P to resume]</b></color>");

        // ── GAME CLOCK ──
        var gc = GameClock.Instance;
        sb.AppendLine("<b>GAME CLOCK</b>");
        if (gc != null)
        {
            int hour = Mathf.FloorToInt(gc.CurrentHour);
            int minute = Mathf.FloorToInt((gc.CurrentHour - hour) * 60f);
            sb.AppendLine($"  Day {gc.CurrentDay}  {hour:D2}:{minute:D2}  Sleeping: {gc.IsSleeping}");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── DAY PHASE ──
        var dpm = DayPhaseManager.Instance;
        sb.AppendLine("<b>DAY PHASE</b>");
        if (dpm != null)
        {
            sb.AppendLine($"  Phase: {dpm.CurrentPhase}");
            sb.AppendLine($"  Interaction: {dpm.IsInteractionPhase}  Drink: {dpm.IsDrinkPhase}");
            if (dpm.PrepTimerActive)
            {
                int m = Mathf.FloorToInt(dpm.PrepTimer / 60f);
                int s = Mathf.FloorToInt(dpm.PrepTimer % 60f);
                sb.AppendLine($"  Prep Timer: <color=yellow>{m}:{s:D2}</color>");
            }
            sb.AppendLine($"  Door: {(DoorGreetingController.Instance != null ? "OK" : "<color=red>MISSING</color>")}");
            sb.AppendLine($"  ScreenFade: {(ScreenFade.Instance != null ? "OK" : "<color=red>MISSING</color>")}");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── DATE SESSION ──
        var dsm = DateSessionManager.Instance;
        sb.AppendLine("<b>DATE SESSION</b>");
        if (dsm != null)
        {
            sb.AppendLine($"  State: {dsm.CurrentState}  Phase: {dsm.CurrentDatePhase}");
            string dateName = dsm.CurrentDate != null ? dsm.CurrentDate.characterName : "None";
            sb.AppendLine($"  Date: {dateName}");

            float delta = dsm.Affection - dsm.StartingAffection;
            string deltaStr = delta >= 0 ? $"<color=green>+{delta:F1}</color>" : $"<color=red>{delta:F1}</color>";
            sb.AppendLine($"  Affection: {dsm.Affection:F1} (start {dsm.StartingAffection:F0}, delta {deltaStr})");

            // Mood multiplier
            float mood = MoodMachine.Instance?.Mood ?? 0f;
            if (dsm.CurrentDate != null)
            {
                var prefs = dsm.CurrentDate.preferences;
                bool inRange = mood >= prefs.preferredMoodMin && mood <= prefs.preferredMoodMax;
                string moodTag = inRange
                    ? $"<color=green>MATCH x{dsm.MoodMatchMultiplier:F1}</color> (mood {mood:F2} in [{prefs.preferredMoodMin:F1}-{prefs.preferredMoodMax:F1}])"
                    : $"<color=red>MISMATCH x{dsm.MoodMismatchMultiplier:F1}</color> (mood {mood:F2} out [{prefs.preferredMoodMin:F1}-{prefs.preferredMoodMax:F1}])";
                sb.AppendLine($"  Mood Mult: {moodTag}");
            }

            // Fail thresholds
            sb.AppendLine($"  Arrival Threshold: {dsm.ArrivalFailThreshold:F0} {PassFail(dsm.Affection, dsm.ArrivalFailThreshold)}");
            sb.AppendLine($"  BgJudge Threshold: {dsm.BgJudgingFailThreshold:F0} {PassFail(dsm.Affection, dsm.BgJudgingFailThreshold)}");
            sb.AppendLine($"  Reveal Threshold:  {dsm.RevealFailThreshold:F0} {PassFail(dsm.Affection, dsm.RevealFailThreshold)}");

            if (dsm.ArrivalTimerActive)
                sb.AppendLine($"  Arrival Timer: {dsm.ArrivalTimer:F1}s");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── DATE PREFERENCES ──
        if (dsm != null && dsm.CurrentDate != null)
        {
            var prefs = dsm.CurrentDate.preferences;
            sb.AppendLine("<b>DATE PREFERENCES</b>");
            sb.AppendLine($"  Liked: {JoinOrNone(prefs.likedTags)}");
            sb.AppendLine($"  Disliked: {JoinOrNone(prefs.dislikedTags)}");
            sb.AppendLine($"  Mood: [{prefs.preferredMoodMin:F1} - {prefs.preferredMoodMax:F1}]");
            sb.AppendLine($"  Outfit Liked: {JoinOrNone(prefs.likedOutfitTags)}");
            sb.AppendLine($"  Outfit Disliked: {JoinOrNone(prefs.dislikedOutfitTags)}");
            sb.AppendLine($"  Reaction Strength: {prefs.reactionStrength:F2}");
            sb.AppendLine();
        }

        // ── MOOD MACHINE ──
        var mm = MoodMachine.Instance;
        sb.AppendLine("<b>MOOD MACHINE</b>");
        if (mm != null)
        {
            sb.AppendLine($"  Mood: {mm.Mood:F3}");
            foreach (var kv in mm.Sources)
                sb.AppendLine($"  [{kv.Key}] = {kv.Value:F3}");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── NPC STATE ──
        var npc = Object.FindAnyObjectByType<DateCharacterController>();
        sb.AppendLine("<b>NPC STATE</b>");
        sb.AppendLine(npc != null ? $"  {npc.CurrentState}" : "  <color=#888>N/A</color>");
        sb.AppendLine();

        // ── CLEANLINESS / TIDINESS ──
        var ts = TidyScorer.Instance;
        var cm = CleaningManager.Instance;
        sb.AppendLine("<b>CLEANLINESS / TIDINESS</b>");
        if (ts != null)
        {
            foreach (ApartmentArea area in System.Enum.GetValues(typeof(ApartmentArea)))
            {
                float tidiness = ts.GetAreaTidiness(area);
                float stainClean = cm != null ? cm.GetAreaCleanPercent(area) : 1f;
                string color = tidiness >= 0.8f ? "green" : tidiness >= 0.5f ? "yellow" : "red";
                sb.AppendLine($"  {area}: <color={color}>{tidiness:P0}</color> (stains {stainClean:P0})");
            }
            float overall = ts.OverallTidiness;
            string oColor = overall >= 0.8f ? "green" : overall >= 0.5f ? "yellow" : "red";
            sb.AppendLine($"  <b>Overall: <color={oColor}>{overall:P0}</color></b>");

            // Show what the date NPC would judge
            var cleanReaction = ReactionEvaluator.EvaluateCleanliness(overall);
            string rColor = cleanReaction == ReactionType.Like ? "green"
                : cleanReaction == ReactionType.Dislike ? "red" : "white";
            sb.AppendLine($"  Date Reaction: <color={rColor}>{cleanReaction}</color>");
        }
        else if (cm != null)
        {
            // Fallback: show stain data without TidyScorer
            foreach (ApartmentArea area in System.Enum.GetValues(typeof(ApartmentArea)))
                sb.AppendLine($"  {area} stains: {cm.GetAreaCleanPercent(area):P0}");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── CLUTTER ──
        sb.AppendLine("<b>CLUTTER</b>");
        if (ts != null)
        {
            int totalFloor = 0;
            foreach (ApartmentArea clutterArea in System.Enum.GetValues(typeof(ApartmentArea)))
            {
                int floorCount = ts.GetFloorItemCount(clutterArea);
                totalFloor += floorCount;
                float clutterClean = ts.GetClutterClean(clutterArea);
                string cColor = clutterClean >= 0.8f ? "green" : clutterClean >= 0.5f ? "yellow" : "red";
                sb.AppendLine($"  {clutterArea}: {floorCount} items <color={cColor}>({clutterClean:P0} clean)</color>");
            }

            // Date clutter reaction preview
            if (dsm != null && dsm.CurrentDate != null)
            {
                float overallClutter = 0f;
                int areaCount = 0;
                foreach (ApartmentArea a in System.Enum.GetValues(typeof(ApartmentArea)))
                {
                    overallClutter += ts.GetClutterClean(a);
                    areaCount++;
                }
                overallClutter = areaCount > 0 ? overallClutter / areaCount : 1f;
                float tolerance = dsm.CurrentDate.preferences.clutterTolerance;
                var clutterReaction = ReactionEvaluator.EvaluateClutter(overallClutter, tolerance);
                string crColor = clutterReaction == ReactionType.Like ? "green"
                    : clutterReaction == ReactionType.Dislike ? "red" : "white";
                sb.AppendLine($"  Date Reaction: <color={crColor}>{clutterReaction}</color> (tolerance: {tolerance:F1}, score: {overallClutter:F2})");
            }
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── SMELL ──
        sb.AppendLine("<b>SMELL</b>");
        float totalSmell = SmellTracker.TotalSmell;
        string smellColor = totalSmell > SmellTracker.SmellThreshold ? "red" : "green";
        sb.AppendLine($"  Total: <color={smellColor}>{totalSmell:F2}</color> (threshold: {SmellTracker.SmellThreshold:F1})");
        sb.AppendLine();

        // ── PUBLIC ITEMS ──
        sb.AppendLine("<b>PUBLIC ITEMS</b>");
        int pubCount = 0;
        foreach (var tag in ReactableTag.All)
        {
            if (!tag.IsPrivate && tag.IsActive)
            {
                sb.AppendLine($"  {tag.gameObject.name} [{string.Join(",", tag.Tags)}]");
                pubCount++;
            }
        }
        if (pubCount == 0) sb.AppendLine("  <color=#888>none</color>");
        sb.AppendLine();

        // ── PRIVATE ITEMS ──
        sb.AppendLine("<b>PRIVATE ITEMS</b>");
        int privCount = 0;
        foreach (var tag in ReactableTag.All)
        {
            if (tag.IsPrivate)
            {
                sb.AppendLine($"  {tag.gameObject.name} [{string.Join(",", tag.Tags)}]");
                privCount++;
            }
        }
        if (privCount == 0) sb.AppendLine("  <color=#888>none</color>");
        sb.AppendLine();

        // ── AUTHORED MESS ──
        sb.AppendLine("<b>AUTHORED MESS</b>");
        var ams = AuthoredMessSpawner.Instance;
        if (ams != null && ams.SpawnedBlueprintNames.Count > 0)
        {
            foreach (var bpName in ams.SpawnedBlueprintNames)
                sb.AppendLine($"  {bpName}");
        }
        else
        {
            sb.AppendLine("  <color=#888>none spawned</color>");
        }
        // Show last date outcome for debugging
        var lastOutcome = DateOutcomeCapture.LastOutcome;
        if (lastOutcome.hadDate)
        {
            string outcomeColor = lastOutcome.succeeded ? "green" : "red";
            sb.AppendLine($"  Last Date: <color={outcomeColor}>{lastOutcome.characterName} ({(lastOutcome.succeeded ? "OK" : "FAIL")}, {lastOutcome.affection:F1})</color>");
            if (lastOutcome.reactionTags != null && lastOutcome.reactionTags.Length > 0)
                sb.AppendLine($"  Tags: {string.Join(", ", lastOutcome.reactionTags)}");
            sb.AppendLine($"  Drink: {lastOutcome.drinkServed}");
        }
        sb.AppendLine();

        // ── ACCUMULATED REACTIONS ──
        if (dsm != null && dsm.AccumulatedReactions.Count > 0)
        {
            sb.AppendLine("<b>ACCUMULATED REACTIONS</b>");
            foreach (var r in dsm.AccumulatedReactions)
            {
                string rColor = r.type == ReactionType.Like ? "green"
                    : r.type == ReactionType.Dislike ? "red" : "white";
                sb.AppendLine($"  {r.itemName} → <color={rColor}>{r.type}</color>");
            }
            sb.AppendLine();
        }

        // ── MID-DATE PENALTIES ──
        var mdw = MidDateActionWatcher.Instance;
        sb.AppendLine("<b>MID-DATE PENALTIES</b>");
        if (mdw != null)
        {
            sb.AppendLine($"  Count: {mdw.PenaltyCount}");
            sb.AppendLine($"  Since Last: {mdw.TimeSinceLastPenalty:F1}s");
            float cd = mdw.CooldownRemaining;
            string cdColor = cd > 0f ? "yellow" : "green";
            sb.AppendLine($"  Cooldown: <color={cdColor}>{cd:F1}s</color>");
        }
        else
        {
            sb.AppendLine("  <color=#888>N/A</color>");
        }
        sb.AppendLine();

        // ── REACTION LOG ──
        sb.AppendLine("<b>REACTION LOG</b>");
        if (_reactionLog.Count == 0)
        {
            sb.AppendLine("  <color=#888>empty</color>");
        }
        else
        {
            for (int i = 0; i < _reactionLog.Count; i++)
                sb.AppendLine($"  {_reactionLog[i]}");
        }

        // Apply scroll offset
        string fullText = sb.ToString();
        string[] lines = fullText.Split('\n');
        if (_scrollOffset >= lines.Length)
            _scrollOffset = Mathf.Max(0, lines.Length - 1);

        var visibleSB = new System.Text.StringBuilder(2048);
        if (_scrollOffset > 0)
            visibleSB.AppendLine($"<color=yellow>\u25b2 PgUp ({_scrollOffset} lines above)</color>");

        int visibleCount = 0;
        for (int i = _scrollOffset; i < lines.Length; i++)
        {
            visibleSB.AppendLine(lines[i]);
            visibleCount++;
        }

        _debugText.text = visibleSB.ToString();
    }

    private static string PassFail(float affection, float threshold)
    {
        return affection >= threshold
            ? "<color=green>OK</color>"
            : "<color=red>FAIL</color>";
    }

    private static string JoinOrNone(string[] arr)
    {
        if (arr == null || arr.Length == 0) return "<color=#888>none</color>";
        return string.Join(", ", arr);
    }
}
