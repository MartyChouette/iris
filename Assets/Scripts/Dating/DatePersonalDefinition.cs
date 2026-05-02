using UnityEngine;

[CreateAssetMenu(menuName = "Iris/Date Personal")]
public class DatePersonalDefinition : ScriptableObject
{
    // ──────────────────────────────────────────────────────────────
    // Identity
    // ──────────────────────────────────────────────────────────────
    [Header("Identity")]
    [Tooltip("Character name displayed in the personal ad and calling UI.")]
    public string characterName = "Mystery Date";

    [TextArea(2, 5)]
    [Tooltip("The personal ad text shown in the newspaper listing.")]
    public string adText = "Seeking someone who appreciates long walks and existential dread.";

    // ──────────────────────────────────────────────────────────────
    // Timing
    // ──────────────────────────────────────────────────────────────
    [Header("Timing")]
    [Tooltip("Seconds until the date arrives after being called.")]
    public float arrivalTimeSec = 30f;

    // ──────────────────────────────────────────────────────────────
    // Visuals
    // ──────────────────────────────────────────────────────────────
    [Header("Visuals")]
    [Tooltip("Portrait sprite shown in the arrived UI (optional).")]
    public Sprite portrait;

    [Tooltip("Prefab to spawn when the date arrives (optional, for future use).")]
    public GameObject characterPrefab;

    // ──────────────────────────────────────────────────────────────
    // Phone Number Layout
    // ──────────────────────────────────────────────────────────────
    [Header("Phone Number Layout")]
    [Tooltip("Normalized rect (0-1) of the phone number area within this ad slot.")]
    public Rect phoneNumberRect = new Rect(0.1f, 0.05f, 0.8f, 0.15f);

    // ──────────────────────────────────────────────────────────────
    // Character Model
    // ──────────────────────────────────────────────────────────────
    [Header("Character Model")]
    [Tooltip("Full character data prefab for the date scene.")]
    public GameObject characterModelPrefab;

    // ──────────────────────────────────────────────────────────────
    // Ad Appearance
    // ──────────────────────────────────────────────────────────────
    [Header("Ad Appearance")]
    [Tooltip("Font size variation for personality. 0 = use default.")]
    public int fontSizeOverride;

    // ──────────────────────────────────────────────────────────────
    // Keywords
    // ──────────────────────────────────────────────────────────────
    [Header("Keywords")]
    [Tooltip("Hoverable keywords in the ad text with commentary tooltips.")]
    public KeywordEntry[] keywords;

    [System.Serializable]
    public struct KeywordEntry
    {
        [Tooltip("Exact substring in adText to turn into a hoverable link.")]
        public string keyword;

        [TextArea(1, 3)]
        [Tooltip("Commentary shown in the tooltip when hovering this keyword.")]
        public string commentary;

        [Tooltip("Optional tag linking this keyword to a likedTag for the prep checklist (e.g. 'vinyl', 'book', 'perfume').")]
        public string preparationTag;
    }

    // ──────────────────────────────────────────────────────────────
    // Flower Gift
    // ──────────────────────────────────────────────────────────────
    [Header("Flower Gift")]
    [Tooltip("Flower prefab presented as a gift at the end of a successful date (Zelda-style item get).")]
    public GameObject flowerPrefab;

    [Tooltip("Name of the flower trimming scene to load. Must be in Build Settings. " +
             "If empty, uses FlowerTrimmingBridge's default scene.")]
    public string flowerSceneName;

    [Tooltip("If true, flower trimming always results in a high score (tutorial dates).")]
    public bool guaranteeFlowerSuccess;

    // ──────────────────────────────────────────────────────────────
    // Availability
    // ──────────────────────────────────────────────────────────────
    [Header("Availability")]
    [Tooltip("Override to bring back succeeded characters in the newspaper pool.")]
    public bool forceAvailable = false;

    // ──────────────────────────────────────────────────────────────
    // Preferences
    // ──────────────────────────────────────────────────────────────
    [Header("Preferences")]
    [Tooltip("What this date likes and dislikes in the apartment.")]
    public DatePreferences preferences = new DatePreferences();

    // ──────────────────────────────────────────────────────────────
    // Item Reactions (Stage 3)
    // ──────────────────────────────────────────────────────────────
    [Header("Item Reactions")]
    [Tooltip("What this date says when the player clicks on specific items during stage 3.")]
    public ItemReaction[] itemReactions = { };

    [System.Serializable]
    public class ItemReaction
    {
        [Tooltip("Tag to match against ReactableTag (e.g. 'vinyl', 'plant', 'perfume_floral').")]
        public string tag = "";

        [TextArea(2, 4)]
        [Tooltip("What this date says about the item.")]
        public string dialogue = "";
    }

    /// <summary>Look up what this date says about an item with the given tag.</summary>
    public string GetReactionDialogue(string tag)
    {
        if (itemReactions == null || string.IsNullOrEmpty(tag)) return null;
        for (int i = 0; i < itemReactions.Length; i++)
        {
            if (itemReactions[i].tag == tag)
                return itemReactions[i].dialogue;
        }
        return null;
    }

    /// <summary>Look up what this date says about a ReactableTag (checks all tags on the item).</summary>
    public string GetReactionDialogue(ReactableTag reactable)
    {
        if (itemReactions == null || reactable == null) return null;
        var tags = reactable.Tags;
        for (int t = 0; t < tags.Length; t++)
        {
            for (int i = 0; i < itemReactions.Length; i++)
            {
                if (itemReactions[i].tag == tags[t])
                    return itemReactions[i].dialogue;
            }
        }
        return null;
    }
}
