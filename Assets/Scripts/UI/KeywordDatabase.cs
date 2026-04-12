using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry of keywords that get highlighted in all game text.
/// Lives in Resources/ so it's globally accessible. Keywords are matched
/// case-insensitively against any TMP_Text with a KeywordHighlighter.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Keyword Database")]
public class KeywordDatabase : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("The word or phrase to highlight (case-insensitive).")]
        public string keyword;

        [Tooltip("Which category determines the highlight color.")]
        public KeywordCategory category;
    }

    [Tooltip("All keywords that should be highlighted in game text.")]
    public Entry[] entries;

    // ── Runtime lookup ──────────────────────────────────────────────

    private static KeywordDatabase s_instance;
    private Dictionary<string, KeywordCategory> _lookup;

    public static KeywordDatabase Instance
    {
        get
        {
            if (s_instance == null)
                s_instance = Resources.Load<KeywordDatabase>("KeywordDatabase");
            return s_instance;
        }
    }

    /// <summary>
    /// Try to find a keyword's category. Returns false if the word isn't a keyword.
    /// </summary>
    public bool TryGetCategory(string word, out KeywordCategory category)
    {
        BuildLookup();
        return _lookup.TryGetValue(word.ToLowerInvariant(), out category);
    }

    /// <summary>All registered keywords for scanning text.</summary>
    public IReadOnlyDictionary<string, KeywordCategory> All
    {
        get { BuildLookup(); return _lookup; }
    }

    private void BuildLookup()
    {
        if (_lookup != null) return;
        _lookup = new Dictionary<string, KeywordCategory>();
        if (entries == null) return;
        for (int i = 0; i < entries.Length; i++)
        {
            if (string.IsNullOrEmpty(entries[i].keyword)) continue;
            string key = entries[i].keyword.ToLowerInvariant();
            if (!_lookup.ContainsKey(key))
                _lookup[key] = entries[i].category;
        }
    }

    private void OnEnable() => _lookup = null; // rebuild on hot-reload
}
