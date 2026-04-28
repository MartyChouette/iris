using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the 3 entrance judgments when a date arrives:
///   1. Music    — is there music playing? (active vinyl/music ReactableTag)
///   2. Perfume  — evaluated against date's preferred mood range + smell check
///   3. Outfit   — evaluated against date's style preferences (null = skip)
/// Each judgment: pause → thought bubble → emote → affection change → brief wait.
/// </summary>
public class EntranceJudgmentSequence : MonoBehaviour
{
    // ── Cached waits (realtime so timeScale=0 can't hang judgments) ──
    private static readonly WaitForSecondsRealtime s_wait25 = new WaitForSecondsRealtime(2.5f);

    [Header("Timing")]
    [Tooltip("Seconds to pause before the first judgment.")]
    [SerializeField] private float _preJudgmentPause = 1.0f;

    [Tooltip("Seconds between judgments (must accommodate 2.8s labeled bubble).")]
    [SerializeField] private float _interJudgmentPause = 3.2f;

    [Header("Behavior")]
    [Tooltip("When true, all reactions are forced to Like (tutorial/early game).")]
    [SerializeField] private bool _alwaysPositive = true;

    [Header("Audio")]
    [Tooltip("SFX played before each judgment evaluation.")]
    [SerializeField] private AudioClip judgingSFX;

    [Tooltip("SFX played on a Dislike judgment (comedic sneeze).")]
    [SerializeField] private AudioClip sneezeSFX;

    // Personality-driven lines when no music is playing
    private static readonly string[] s_noMusicLines =
    {
        "Oh... no music?",
        "It's quiet in here...",
        "Why aren't we listening to music?"
    };

    // Personality-driven lines when a dirty area is noticed
    private static readonly string[] s_dirtyKitchenLines =
    {
        "The kitchen could use some attention...",
        "Is the kitchen always like this?",
        "Maybe clean the kitchen sometime..."
    };
    private static readonly string[] s_dirtyLivingRoomLines =
    {
        "The living room is a bit messy...",
        "You might want to tidy the living room.",
        "The living room could use some love..."
    };
    private static readonly string[] s_dirtyEntranceLines =
    {
        "The entrance is a mess...",
        "Your hallway could use some work.",
        "First impressions matter... the entrance..."
    };

    /// <summary>
    /// Run all 3 entrance judgments. Yields until complete.
    /// </summary>
    public IEnumerator RunJudgments(DateReactionUI reactionUI, DatePersonalDefinition date)
    {
        if (date == null) yield break;

        yield return new WaitForSecondsRealtime(_preJudgmentPause);

        // --- Judgment 1: Music ---
        PlayJudgingSFX();
        var musicReaction = EvaluateMusic(date, out ReactableTag musicTag);
        int musicMultiplier = musicTag != null ? DateSessionManager.GetTagEffectMultiplier(musicTag) : 1;
        // If no music is playing, show a personality comment before the labeled reaction
        if (musicReaction == ReactionType.Neutral && !_alwaysPositive)
        {
            string noMusicLine = s_noMusicLines[UnityEngine.Random.Range(0, s_noMusicLines.Length)];
            reactionUI?.ShowText(noMusicLine, 2f);
            yield return s_wait25;
        }
        if (_alwaysPositive) musicReaction = ReactionType.Like;
        reactionUI?.ShowLabeledReaction(musicReaction, "Your Music");
        DateSessionManager.Instance?.ApplyReaction(musicReaction, musicMultiplier);
        ShowJudgmentJuice(reactionUI, "Your Music", musicReaction, musicMultiplier);
        if (musicReaction == ReactionType.Dislike) PlaySneezeSFX();
        Debug.Log($"[EntranceJudgmentSequence] Music: {musicReaction}");
        DateDebugOverlay.Instance?.LogReaction($"[Entrance] Music → {musicReaction}");
        yield return new WaitForSecondsRealtime(_interJudgmentPause);

        // --- Judgment 2: Perfume ---
        PlayJudgingSFX();
        var sprayed = PerfumeBottle.LastSprayed;
        var perfumeReaction = ReactionEvaluator.EvaluatePerfume(sprayed, date.preferences);

        // Smell downgrade (only when not always-positive)
        if (!_alwaysPositive)
        {
            float totalSmell = SmellTracker.TotalSmell;
            if (totalSmell > SmellTracker.SmellThreshold * 2f)
                perfumeReaction = ReactionType.Dislike;
            else if (totalSmell > SmellTracker.SmellThreshold && perfumeReaction == ReactionType.Like)
                perfumeReaction = ReactionType.Neutral;
        }

        if (_alwaysPositive) perfumeReaction = ReactionType.Like;

        string perfumeLabel = sprayed != null ? sprayed.perfumeName : "No Perfume";
        // Show custom no-perfume comment if configured
        if (sprayed == null && !string.IsNullOrEmpty(date.preferences.noPerfumeComment))
            reactionUI?.ShowText(date.preferences.noPerfumeComment, 2f);

        reactionUI?.ShowLabeledReaction(perfumeReaction, perfumeLabel);
        DateSessionManager.Instance?.ApplyReaction(perfumeReaction);
        ShowJudgmentJuice(reactionUI, perfumeLabel, perfumeReaction);
        if (perfumeReaction == ReactionType.Dislike) PlaySneezeSFX();
        Debug.Log($"[EntranceJudgmentSequence] Perfume: {perfumeLabel} → {perfumeReaction}");
        DateDebugOverlay.Instance?.LogReaction($"[Entrance] Perfume ({perfumeLabel}) → {perfumeReaction}");
        yield return new WaitForSecondsRealtime(_interJudgmentPause);

        // --- Judgment 3: Outfit ---
        PlayJudgingSFX();
        var outfitReaction = EvaluateOutfit(date);
        if (_alwaysPositive) outfitReaction = ReactionType.Like;
        reactionUI?.ShowLabeledReaction(outfitReaction, "Your Outfit");
        DateSessionManager.Instance?.ApplyReaction(outfitReaction);
        ShowJudgmentJuice(reactionUI, "Your Outfit", outfitReaction);
        if (outfitReaction == ReactionType.Dislike) PlaySneezeSFX();
        Debug.Log($"[EntranceJudgmentSequence] Outfit: {outfitReaction}");
        DateDebugOverlay.Instance?.LogReaction($"[Entrance] Outfit → {outfitReaction}");
        yield return new WaitForSecondsRealtime(_interJudgmentPause);

        // --- Judgment 4: Cleanliness ---
        PlayJudgingSFX();
        var cleanReaction = EvaluateCleanliness();
        // If apartment is dirty, comment on the worst area before the labeled reaction
        if ((cleanReaction == ReactionType.Dislike || cleanReaction == ReactionType.Neutral) && !_alwaysPositive)
        {
            string dirtyLine = GetDirtiestAreaComment();
            if (!string.IsNullOrEmpty(dirtyLine))
            {
                reactionUI?.ShowText(dirtyLine, 2f);
                yield return s_wait25;
            }
        }
        if (_alwaysPositive) cleanReaction = ReactionType.Like;
        reactionUI?.ShowLabeledReaction(cleanReaction, "Apartment Cleanliness");
        DateSessionManager.Instance?.ApplyReaction(cleanReaction);
        ShowJudgmentJuice(reactionUI, "Cleanliness", cleanReaction);
        if (cleanReaction == ReactionType.Dislike) PlaySneezeSFX();
        Debug.Log($"[EntranceJudgmentSequence] Cleanliness: {cleanReaction}");
        DateDebugOverlay.Instance?.LogReaction($"[Entrance] Cleanliness → {cleanReaction}");
        yield return new WaitForSecondsRealtime(_interJudgmentPause);
    }

    private void PlayJudgingSFX()
    {
        if (judgingSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(judgingSFX);
    }

    private void PlaySneezeSFX()
    {
        if (sneezeSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(sneezeSFX);
    }

    private ReactionType EvaluateMusic(DatePersonalDefinition date, out ReactableTag foundTag)
    {
        foundTag = null;
        // Scan ReactableTag.All for active tags containing "vinyl" or "music"
        foreach (var tag in ReactableTag.All)
        {
            if (!tag.IsActive) continue;
            foreach (var t in tag.Tags)
            {
                if (t.Contains("vinyl") || t.Contains("music"))
                {
                    foundTag = tag;
                    return ReactionEvaluator.EvaluateReactable(tag, date.preferences);
                }
            }
        }
        // No music playing — neutral
        return ReactionType.Neutral;
    }

    private ReactionType EvaluateOutfit(DatePersonalDefinition date)
    {
        var outfit = OutfitSelector.Instance?.SelectedOutfit;
        return ReactionEvaluator.EvaluateOutfit(outfit, date.preferences);
    }

    // EvaluatePerfumeMood removed — perfume judgment now uses
    // ReactionEvaluator.EvaluatePerfume() with actual perfume tags.

    private ReactionType EvaluateCleanliness()
    {
        float tidiness = TidyScorer.Instance != null ? TidyScorer.Instance.OverallTidiness : 1f;
        return ReactionEvaluator.EvaluateCleanliness(tidiness);
    }

    /// <summary>Find the dirtiest area and return a matching comment line.</summary>
    private string GetDirtiestAreaComment()
    {
        if (TidyScorer.Instance == null) return null;

        float kitchenT = TidyScorer.Instance.GetAreaTidiness(ApartmentArea.Kitchen);
        float livingT = TidyScorer.Instance.GetAreaTidiness(ApartmentArea.LivingRoom);
        float entranceT = TidyScorer.Instance.GetAreaTidiness(ApartmentArea.Entrance);

        // Find dirtiest (lowest tidiness)
        string[] lines;
        if (kitchenT <= livingT && kitchenT <= entranceT)
            lines = s_dirtyKitchenLines;
        else if (livingT <= entranceT)
            lines = s_dirtyLivingRoomLines;
        else
            lines = s_dirtyEntranceLines;

        return lines[UnityEngine.Random.Range(0, lines.Length)];
    }

    /// <summary>Flower gauge popup + particles at NPC for a single judgment.</summary>
    private static void ShowJudgmentJuice(DateReactionUI reactionUI, string label, ReactionType reaction, int multiplier = 1)
    {
        if (reaction == ReactionType.Neutral) return;

        bool liked = reaction == ReactionType.Like;
        string sym = liked ? " \u2665" : " \u2639";
        string popText = label + sym;
        if (multiplier > 1) popText += $" \u00d7{multiplier}";
        AffectionBar.Instance?.ShowPopup(popText, liked);

        if (reactionUI != null)
        {
            Vector3 npcPos = reactionUI.transform.position + Vector3.up * 0.5f;
            DateSessionManager.SpawnReactionParticles(npcPos, reaction);
        }
    }
}
