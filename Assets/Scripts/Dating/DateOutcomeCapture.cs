using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static bridge that captures date outcome data at date end
/// so the next morning's AuthoredMessSpawner can filter blueprints.
/// </summary>
public static class DateOutcomeCapture
{
    public struct DateOutcome
    {
        public bool hadDate;
        public bool succeeded;
        public float affection;
        public string characterName;
        public DatePersonalDefinition dateCharacter;
        public int dateCount; // how many times this character has visited (cumulative)
        public string[] reactionTags;
        public bool drinkServed;

        // Flower trimming results (populated after flower scene completes)
        public bool hadFlowerTrim;
        public int flowerScore;
        public int flowerDaysAlive;
        public string flowerGrade;
        public bool flowerWasGameOver;
    }

    public static DateOutcome LastOutcome { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset() => LastOutcome = default;

    /// <summary>
    /// Called by DateSessionManager at the end of a date (fail or succeed).
    /// Snapshots the session data for use next morning.
    /// </summary>
    public static void Capture(
        DatePersonalDefinition date,
        float affection,
        bool succeeded,
        IReadOnlyList<DateSessionManager.AccumulatedReaction> reactions)
    {
        var tags = new string[reactions.Count];
        for (int i = 0; i < reactions.Count; i++)
            tags[i] = reactions[i].itemName;

        bool drink = false;
        foreach (var r in reactions)
        {
            if (r.itemName.Contains("drink", System.StringComparison.OrdinalIgnoreCase))
            {
                drink = true;
                break;
            }
        }

        // Count how many times this character has been dated (check DateHistory)
        int visitCount = 0;
        if (date != null && DateHistory.Entries != null)
        {
            for (int i = 0; i < DateHistory.Entries.Count; i++)
                if (DateHistory.Entries[i].name == date.characterName)
                    visitCount++;
        }
        visitCount++; // include this date

        LastOutcome = new DateOutcome
        {
            hadDate = true,
            succeeded = succeeded,
            affection = affection,
            characterName = date != null ? date.characterName : "",
            dateCharacter = date,
            dateCount = visitCount,
            reactionTags = tags,
            drinkServed = drink
        };

        Debug.Log($"[DateOutcomeCapture] Captured: {LastOutcome.characterName}, " +
                  $"succeeded={succeeded}, affection={affection:F1}, " +
                  $"reactions={tags.Length}, drink={drink}");
    }

    /// <summary>
    /// Called by FlowerTrimmingBridge after the flower scene completes.
    /// Populates flower fields on the current outcome.
    /// </summary>
    public static void CaptureFlowerResult(int score, int days, bool gameOver)
    {
        var o = LastOutcome;
        o.hadFlowerTrim = true;
        o.flowerScore = score;
        o.flowerDaysAlive = days;
        o.flowerWasGameOver = gameOver;
        o.flowerGrade = ComputeFlowerGrade(score);
        LastOutcome = o;

        Debug.Log($"[DateOutcomeCapture] Flower result: score={score}, days={days}, " +
                  $"grade={o.flowerGrade}, gameOver={gameOver}");
    }

    private static string ComputeFlowerGrade(int score)
    {
        if (score >= 90) return "S";
        if (score >= 75) return "A";
        if (score >= 60) return "B";
        if (score >= 40) return "C";
        return "D";
    }

    /// <summary>Called after spawning daily mess to reset for the next day.</summary>
    public static void ClearForNewDay() => LastOutcome = default;
}
