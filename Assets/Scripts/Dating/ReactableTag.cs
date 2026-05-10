using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight marker component that identifies an object as something a date
/// character can notice and react to. Tags describe what the object is
/// (e.g. "vinyl", "plant", "perfume_floral").
/// </summary>
public class ReactableTag : MonoBehaviour
{
    [Tooltip("Human-readable name shown in reaction bubbles (falls back to GameObject name).")]
    [SerializeField] private string displayName = "";

    [Tooltip("Tags identifying what this is. E.g. 'vinyl', 'plant', 'perfume_floral'.")]
    [SerializeField] private string[] tags = { };

    [Tooltip("Is this currently active? (e.g. record only active when playing)")]
    [SerializeField] private bool isActive = true;

    [Tooltip("Optional icon shown in the reaction bubble when the date reacts to this item.")]
    [SerializeField] private Sprite reactionIcon;

    // Not serialized — always starts public at runtime.
    // BookItem.Awake and DrawerController.Start set correct initial values.
    [System.NonSerialized] private bool isPrivate = false;

    [Tooltip("Smell contribution of this item. Smell travels through drawers.")]
    [SerializeField] private float smellAmount = 0f;

    private ItemHighlight _highlight;

    public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : gameObject.name;
    public string[] Tags => tags;
    public Sprite ReactionIcon => reactionIcon;
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value) return;
            isActive = value;
            UpdateDisplayHighlight();
        }
    }

    /// <summary>Fired when IsPrivate changes. Args: (ReactableTag tag, bool isPrivate).</summary>
    public static event System.Action<ReactableTag, bool> OnPrivacyChanged;

    public bool IsPrivate
    {
        get => isPrivate;
        set
        {
            if (isPrivate == value) return;
            isPrivate = value;
            UpdateDisplayHighlight();
            OnPrivacyChanged?.Invoke(this, isPrivate);
        }
    }

    public float SmellAmount
    {
        get => smellAmount;
        set => smellAmount = value;
    }

    /// <summary>
    /// Configure tags and display name at runtime (e.g. from BookDefinition).
    /// </summary>
    public void Setup(string[] newTags, string newDisplayName)
    {
        tags = newTags;
        displayName = newDisplayName;
    }

    // ──────────────────────────────────────────────────────────────
    // Static registry
    // ──────────────────────────────────────────────────────────────
    private static readonly List<ReactableTag> s_allActive = new List<ReactableTag>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset() => s_allActive.Clear();

    public static IReadOnlyList<ReactableTag> All => s_allActive;

    private void OnEnable()
    {
        s_allActive.Add(this);
        UpdateDisplayHighlight();
    }

    private void OnDisable()
    {
        // Clear display highlight before unregistering
        if (_highlight != null)
            _highlight.SetDisplayHighlighted(false);

        s_allActive.Remove(this);
    }

    private void UpdateDisplayHighlight()
    {
        if (_highlight == null)
            _highlight = GetComponent<ItemHighlight>();

        if (_highlight != null)
            _highlight.SetDisplayHighlighted(isActive && !isPrivate);
    }
}
