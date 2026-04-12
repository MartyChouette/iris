using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Attach to any TMP_Text to auto-highlight keywords from the KeywordDatabase.
/// Matched keywords get per-category colors and a shimmer/wave vertex animation.
///
/// Usage: Add this component alongside TextMeshProUGUI or TextMeshPro.
/// It processes text on change, injects color tags, and animates matched
/// character ranges with a sine-wave wobble each frame.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class KeywordHighlighter : MonoBehaviour
{
    [Header("Wave Animation")]
    [Tooltip("Vertical amplitude of the wave in pixels.")]
    [SerializeField] private float _waveAmplitude = 2f;

    [Tooltip("Speed of the wave animation.")]
    [SerializeField] private float _waveSpeed = 3f;

    [Tooltip("Phase offset per character (higher = more wavy).")]
    [SerializeField] private float _waveCharOffset = 0.3f;

    [Header("Category Colors")]
    [SerializeField] private Color _likeColor = new Color(1f, 0.45f, 0.6f);
    [SerializeField] private Color _dislikeColor = new Color(0.55f, 0.6f, 0.75f);
    [SerializeField] private Color _hobbyColor = new Color(0.95f, 0.75f, 0.3f);
    [SerializeField] private Color _personalityColor = new Color(0.7f, 0.5f, 0.85f);
    [SerializeField] private Color _specialColor = new Color(0.3f, 0.85f, 0.8f);

    private TMP_Text _text;
    private string _lastRawText;
    private readonly List<(int startIndex, int length, KeywordCategory category)> _keywordRanges = new();

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        // Subscribe to text change events
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object obj)
    {
        if (obj != _text) return;
        ProcessText();
    }

    private void ProcessText()
    {
        var db = KeywordDatabase.Instance;
        if (db == null || _text == null) return;

        string raw = _text.text;
        if (raw == _lastRawText) return; // avoid re-processing our own color injection

        _keywordRanges.Clear();

        // Scan for keywords and inject color tags
        string processed = raw;
        bool anyFound = false;

        foreach (var kvp in db.All)
        {
            string keyword = kvp.Key;
            var category = kvp.Value;
            string colorHex = ColorUtility.ToHtmlStringRGB(GetCategoryColor(category));

            // Case-insensitive search in the raw text
            int searchStart = 0;
            string lowerProcessed = processed.ToLowerInvariant();
            while (searchStart < lowerProcessed.Length)
            {
                int idx = lowerProcessed.IndexOf(keyword, searchStart, System.StringComparison.Ordinal);
                if (idx < 0) break;

                // Check word boundaries (don't highlight partial words)
                bool leftBound = idx == 0 || !char.IsLetterOrDigit(lowerProcessed[idx - 1]);
                bool rightBound = idx + keyword.Length >= lowerProcessed.Length
                    || !char.IsLetterOrDigit(lowerProcessed[idx + keyword.Length]);

                if (leftBound && rightBound)
                {
                    // Extract the original-case match
                    string original = processed.Substring(idx, keyword.Length);
                    string tagged = $"<color=#{colorHex}>{original}</color>";
                    processed = processed.Substring(0, idx) + tagged + processed.Substring(idx + keyword.Length);

                    // Record the character range (in the DISPLAYED text, without tags)
                    // We'll recalculate after all tags are injected
                    anyFound = true;

                    // Skip past the injected tags
                    searchStart = idx + tagged.Length;
                    lowerProcessed = processed.ToLowerInvariant();
                }
                else
                {
                    searchStart = idx + keyword.Length;
                }
            }
        }

        if (anyFound)
        {
            _lastRawText = processed;
            _text.text = processed;

            // After text is set, scan TMP's parsed text to find keyword char ranges
            // (needs to happen after TMP processes the rich text tags)
            _text.ForceMeshUpdate();
            BuildAnimationRanges();
        }
    }

    /// <summary>
    /// Scan the parsed (tag-stripped) text to find keyword character ranges for animation.
    /// </summary>
    private void BuildAnimationRanges()
    {
        _keywordRanges.Clear();
        var db = KeywordDatabase.Instance;
        if (db == null || _text.textInfo == null) return;

        // Get the displayed text (rich text tags stripped)
        string displayed = _text.GetParsedText();
        if (string.IsNullOrEmpty(displayed)) return;

        string lowerDisplayed = displayed.ToLowerInvariant();

        foreach (var kvp in db.All)
        {
            string keyword = kvp.Key;
            int searchStart = 0;
            while (searchStart < lowerDisplayed.Length)
            {
                int idx = lowerDisplayed.IndexOf(keyword, searchStart, System.StringComparison.Ordinal);
                if (idx < 0) break;

                bool leftBound = idx == 0 || !char.IsLetterOrDigit(lowerDisplayed[idx - 1]);
                bool rightBound = idx + keyword.Length >= lowerDisplayed.Length
                    || !char.IsLetterOrDigit(lowerDisplayed[idx + keyword.Length]);

                if (leftBound && rightBound)
                {
                    _keywordRanges.Add((idx, keyword.Length, kvp.Value));
                    searchStart = idx + keyword.Length;
                }
                else
                {
                    searchStart = idx + 1;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (_text == null || _keywordRanges.Count == 0) return;
        if (_text.textInfo == null || _text.textInfo.characterCount == 0) return;

        _text.ForceMeshUpdate();
        var textInfo = _text.textInfo;

        for (int k = 0; k < _keywordRanges.Count; k++)
        {
            var (startIdx, length, category) = _keywordRanges[k];

            for (int c = startIdx; c < startIdx + length && c < textInfo.characterCount; c++)
            {
                var charInfo = textInfo.characterInfo[c];
                if (!charInfo.isVisible) continue;

                int meshIdx = charInfo.materialReferenceIndex;
                int vertIdx = charInfo.vertexIndex;

                var verts = textInfo.meshInfo[meshIdx].vertices;

                // Sine wave offset per character
                float wave = Mathf.Sin(Time.time * _waveSpeed + c * _waveCharOffset) * _waveAmplitude;

                verts[vertIdx + 0].y += wave;
                verts[vertIdx + 1].y += wave;
                verts[vertIdx + 2].y += wave;
                verts[vertIdx + 3].y += wave;
            }
        }

        // Push modified vertices back to TMP
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            _text.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }

    public Color GetCategoryColor(KeywordCategory cat)
    {
        return cat switch
        {
            KeywordCategory.Like => _likeColor,
            KeywordCategory.Dislike => _dislikeColor,
            KeywordCategory.Hobby => _hobbyColor,
            KeywordCategory.Personality => _personalityColor,
            KeywordCategory.Special => _specialColor,
            _ => Color.white,
        };
    }
}
