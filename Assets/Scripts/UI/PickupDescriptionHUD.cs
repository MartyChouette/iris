using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Scene-scoped singleton that shows a brief text description at the bottom of the
/// screen when the player picks up an apartment item. Auto-hides after a timeout.
/// Fades out smoothly instead of popping off.
/// </summary>
public class PickupDescriptionHUD : MonoBehaviour
{
    public static PickupDescriptionHUD Instance { get; private set; }

    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private TMP_Text _descriptionText;

    private float _hideTimer;
    private const float AutoHideDuration = 4f;
    private const float FadeOutDuration = 0.3f;

    private CanvasGroup _canvasGroup;
    private bool _isFadingOut;
    private float _fadeTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PickupDescriptionHUD] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_panelRoot != null)
        {
            _canvasGroup = _panelRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = _panelRoot.AddComponent<CanvasGroup>();

            // Ensure dark background
            var img = _panelRoot.GetComponent<Image>();
            if (img == null)
            {
                img = _panelRoot.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.55f);
            }
            else if (img.color.a < 0.1f)
            {
                img.color = new Color(0f, 0f, 0f, 0.55f);
            }

            // Ensure the panel auto-sizes to fit text content
            var csf = _panelRoot.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = _panelRoot.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Add padding via HorizontalLayoutGroup if not already present
            var hlg = _panelRoot.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = _panelRoot.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(16, 16, 8, 8);
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
            }

            _panelRoot.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_panelRoot == null || !_panelRoot.activeSelf) return;

        if (_isFadingOut)
        {
            _fadeTimer -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_fadeTimer / FadeOutDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = t;
            if (_fadeTimer <= 0f)
            {
                _isFadingOut = false;
                _panelRoot.SetActive(false);
                if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            }
            return;
        }

        _hideTimer -= Time.unscaledDeltaTime;
        if (_hideTimer <= 0f)
            Hide();
    }

    public void Show(string text)
    {
        if (_panelRoot == null || _descriptionText == null) return;

        _descriptionText.text = text;
        _panelRoot.SetActive(true);
        _isFadingOut = false;
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        _hideTimer = AutoHideDuration;
    }

    public void Hide()
    {
        if (_panelRoot == null || !_panelRoot.activeSelf) return;

        // Start fade-out instead of instant hide
        if (!_isFadingOut)
        {
            _isFadingOut = true;
            _fadeTimer = FadeOutDuration;
        }
    }
}
