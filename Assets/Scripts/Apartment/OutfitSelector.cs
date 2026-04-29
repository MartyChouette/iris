using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Click wardrobe object to open outfit selection overlay.
/// Player picks an outfit for the date's entrance judgment.
/// </summary>
public class OutfitSelector : MonoBehaviour
{
    public static OutfitSelector Instance { get; private set; }

    [Header("Outfits")]
    [Tooltip("Available outfits for the player.")]
    [SerializeField] private OutfitDefinition[] _availableOutfits;

    [Header("UI")]
    [Tooltip("Root panel for outfit selection overlay.")]
    [SerializeField] private GameObject _selectionPanel;

    [Tooltip("Parent transform for outfit option buttons.")]
    [SerializeField] private Transform _buttonContainer;

    [Tooltip("Text showing currently selected outfit name.")]
    [SerializeField] private TMP_Text _selectedOutfitText;

    [Header("Interaction")]
    [Tooltip("Layer mask for the wardrobe collider.")]
    [SerializeField] private LayerMask _wardrobeLayer;

    [SerializeField] private Camera _mainCamera;

    [Header("Audio")]
    [SerializeField] private AudioClip _openSFX;
    [SerializeField] private AudioClip _selectSFX;

    private OutfitDefinition _selectedOutfit;
    private bool _isOpen;

    public OutfitDefinition SelectedOutfit => _selectedOutfit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[OutfitSelector] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_selectionPanel != null)
            _selectionPanel.SetActive(false);

        // Default to first outfit
        if (_availableOutfits != null && _availableOutfits.Length > 0)
            _selectedOutfit = _availableOutfits[0];
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase) return;
        if (_isOpen) return;
        if (IrisInput.Instance == null || !IrisInput.Instance.Click.WasPressedThisFrame()) return;

        // Only during Browsing apartment state
        if (ApartmentManager.Instance == null) return;
        if (ApartmentManager.Instance.CurrentState != ApartmentManager.State.Browsing)
            return;

        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Vector2 mousePos = IrisInput.CursorPosition;
        var ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(mousePos) : _mainCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out var hit, 20f, _wardrobeLayer))
        {
            Debug.Log("[OutfitSelector] Wardrobe clicked — opening outfit panel.");
            OpenPanel();
        }
    }

    private void OpenPanel()
    {
        _isOpen = true;

        if (_openSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_openSFX);

        if (_selectionPanel != null)
            _selectionPanel.SetActive(true);

        PopulateButtons();
    }

    private void ClosePanel()
    {
        _isOpen = false;
        if (_selectionPanel != null)
            _selectionPanel.SetActive(false);
    }

    private void PopulateButtons()
    {
        if (_buttonContainer == null || _availableOutfits == null) return;

        // Clear existing buttons
        for (int i = _buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(_buttonContainer.GetChild(i).gameObject);

        foreach (var outfit in _availableOutfits)
        {
            if (outfit == null) continue;

            var btnGO = new GameObject(outfit.outfitName);
            btnGO.transform.SetParent(_buttonContainer, false);

            var rt = btnGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 60f);

            var img = btnGO.AddComponent<Image>();
            img.color = _selectedOutfit == outfit
                ? new Color(0.3f, 0.7f, 0.3f, 0.8f)
                : new Color(0.2f, 0.2f, 0.2f, 0.8f);

            var btn = btnGO.AddComponent<Button>();
            var captured = outfit;
            btn.onClick.AddListener(() => SelectOutfit(captured));

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(btnGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = outfit.outfitName;
            tmp.fontSize = 18f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }
    }

    private void SelectOutfit(OutfitDefinition outfit)
    {
        _selectedOutfit = outfit;
        Debug.Log($"[OutfitSelector] Selected outfit: {outfit.outfitName}");

        if (_selectSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_selectSFX);

        if (_selectedOutfitText != null)
            _selectedOutfitText.SetText(outfit.outfitName);

        ClosePanel();
    }
}
