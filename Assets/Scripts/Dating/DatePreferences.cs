using UnityEngine;

/// <summary>
/// Serializable data block defining what a date character likes and dislikes.
/// Stored on <see cref="DatePersonalDefinition"/>.
/// </summary>
[System.Serializable]
public class DatePreferences
{
    [Header("Item Tags")]
    [Tooltip("Tags this date likes. Matched against ReactableTag.")]
    public string[] likedTags = { };

    [Tooltip("Tags this date dislikes.")]
    public string[] dislikedTags = { };

    [Header("Mood")]
    [Tooltip("Preferred mood range minimum (0=sunny, 1=stormy).")]
    [Range(0f, 1f)]
    public float preferredMoodMin = 0.2f;

    [Tooltip("Preferred mood range maximum.")]
    [Range(0f, 1f)]
    public float preferredMoodMax = 0.5f;

    [Header("Drinks")]
    [Tooltip("Drink recipes this date enjoys.")]
    public DrinkRecipeDefinition[] likedDrinks = { };

    [Tooltip("Drink recipes this date dislikes.")]
    public DrinkRecipeDefinition[] dislikedDrinks = { };

    [Header("Outfit")]
    [Tooltip("Outfit style tags this date likes.")]
    public string[] likedOutfitTags = { };

    [Tooltip("Outfit style tags this date dislikes.")]
    public string[] dislikedOutfitTags = { };

    [Header("Perfume")]
    [Tooltip("Perfume tags this date likes (e.g. 'floral', 'woody', 'citrus').")]
    public string[] likedPerfumeTags = { };

    [Tooltip("Perfume tags this date dislikes.")]
    public string[] dislikedPerfumeTags = { };

    [Tooltip("Reaction when no perfume was sprayed.")]
    public ReactionType noPerfumeReaction = ReactionType.Neutral;

    [Tooltip("What the date says if no perfume was sprayed. Empty = default.")]
    public string noPerfumeComment = "";

    [Header("Clutter")]
    [Tooltip("How much floor clutter this date tolerates (1 = doesn't care, 0 = hates it).")]
    [Range(0f, 1f)]
    public float clutterTolerance = 0.5f;

    [Header("Personality")]
    [Tooltip("Multiplier on reaction strength. >1 = expressive, <1 = reserved.")]
    public float reactionStrength = 1f;

    [Header("Generic Reaction Lines")]
    [Tooltip("Custom fallback lines for this date (replaces the default 'Loves it!' etc). Leave empty to use defaults.")]
    public string[] likeLines = { };
    public string[] neutralLines = { };
    public string[] dislikeLines = { };

    [Header("Bespoke Reactions")]
    [Tooltip("Custom dialogue lines triggered by specific item tags. Overrides generic lines above.")]
    public BespokeReaction[] bespokeReactions = { };

    [System.Serializable]
    public struct BespokeReaction
    {
        [Tooltip("Item tag that triggers this line (matched against ReactableTag.tags).")]
        public string tag;

        [Tooltip("Positive, negative, or neutral — filters when this line can play.")]
        public ReactionType sentiment;

        [Tooltip("Custom line the date says when they see this tag.")]
        [TextArea(1, 3)]
        public string line;
    }

    /// <summary>
    /// Get a random generic line for this date's reaction type.
    /// Returns null if no custom lines are configured (caller uses hardcoded defaults).
    /// </summary>
    public string GetGenericLine(ReactionType reaction)
    {
        var lines = reaction switch
        {
            ReactionType.Like => likeLines,
            ReactionType.Dislike => dislikeLines,
            _ => neutralLines
        };
        if (lines == null || lines.Length == 0) return null;
        return lines[UnityEngine.Random.Range(0, lines.Length)];
    }

    /// <summary>
    /// Look up a bespoke line for a set of item tags and a reaction type.
    /// Only matches if the reaction sentiment matches (so you can have different
    /// lines for liking vs disliking the same tag). Returns null if no match.
    /// </summary>
    public string GetBespokeLine(string[] itemTags, ReactionType reaction)
    {
        if (bespokeReactions == null || bespokeReactions.Length == 0 || itemTags == null) return null;
        for (int i = 0; i < bespokeReactions.Length; i++)
        {
            if (string.IsNullOrEmpty(bespokeReactions[i].tag)) continue;
            if (bespokeReactions[i].sentiment != reaction) continue;
            for (int j = 0; j < itemTags.Length; j++)
            {
                if (string.Equals(bespokeReactions[i].tag, itemTags[j], System.StringComparison.OrdinalIgnoreCase))
                    return bespokeReactions[i].line;
            }
        }
        return null;
    }
}
