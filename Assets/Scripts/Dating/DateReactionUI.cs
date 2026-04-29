using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// World-space reaction bubble above the date character. Shows a question mark
/// (notice) then the reaction icon (heart/neutral/frown) with SFX.
/// </summary>
public class DateReactionUI : MonoBehaviour
{
    private static readonly WaitForSeconds s_wait07 = new WaitForSeconds(0.7f);
    private static readonly WaitForSeconds s_wait18 = new WaitForSeconds(1.8f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_flashCanvas = null;
    }

    [Header("Icon Sprites")]
    [SerializeField] private Sprite questionSprite;
    [SerializeField] private Sprite heartSprite;
    [SerializeField] private Sprite neutralSprite;
    [SerializeField] private Sprite dislikeSprite;

    [Header("Timing")]
    [Tooltip("Seconds the question mark shows.")]
    [SerializeField] private float noticeDuration = 0.6f;

    [Tooltip("Seconds the reaction icon shows.")]
    [SerializeField] private float reactionDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip likeSFX;
    [SerializeField] private AudioClip dislikeSFX;

    [Header("Offset")]
    [Tooltip("Height above the character's origin for the bubble.")]
    [SerializeField] private float bubbleHeight = 2.2f;

    // Sentiment text arrays for labeled reactions
    private static readonly string[] s_likeTexts = { "Loves it!", "Really into this!", "Great taste!" };
    private static readonly string[] s_neutralTexts = { "Hmm, okay.", "Not bad.", "Sure." };
    private static readonly string[] s_dislikeTexts = { "Not a fan...", "Yikes.", "Hard pass." };

    private SpriteRenderer _iconRenderer;
    private SpriteRenderer _itemIconRenderer;
    private GameObject _bubbleGO;
    private TextMeshPro _bubbleText;
    private Coroutine _activeReaction;
    private Camera _cachedCamera;
    private WaitForSeconds _waitNotice;
    private WaitForSeconds _waitReaction;

    // Screen-space text overlay — always visible regardless of camera focus
    private static Canvas s_screenTextCanvas;
    private TMP_Text _screenText;
    private RectTransform _screenTextRT;

    private void Awake()
    {
        // Create a child object for the reaction bubble
        _bubbleGO = new GameObject("ReactionBubble");
        _bubbleGO.transform.SetParent(transform);
        _bubbleGO.transform.localPosition = new Vector3(0f, bubbleHeight, 0f);

        _iconRenderer = _bubbleGO.AddComponent<SpriteRenderer>();
        _iconRenderer.sortingOrder = 100;

        // Render through walls so reactions are always visible
        var spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            var spriteMat = new Material(spriteShader);
            spriteMat.SetInt("_ZTest", (int)CompareFunction.Always);
            spriteMat.renderQueue = 4000;
            _iconRenderer.material = spriteMat;
        }

        _bubbleGO.SetActive(false);

        _waitNotice = new WaitForSeconds(noticeDuration);
        _waitReaction = new WaitForSeconds(reactionDuration);
    }

    private void LateUpdate()
    {
        // Billboard: face camera
        if (_bubbleGO.activeSelf)
        {
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            if (_cachedCamera != null)
                _bubbleGO.transform.rotation = _cachedCamera.transform.rotation;
        }

        // Track character position for screen-space text
        if (_screenText != null && _screenText.gameObject.activeSelf)
            UpdateScreenTextPosition();
    }

    private void UpdateScreenTextPosition()
    {
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        if (_cachedCamera == null || _screenTextRT == null) return;

        Vector3 worldPos = transform.position + Vector3.up * (bubbleHeight + 0.3f);
        Vector3 screenPos = _cachedCamera.WorldToScreenPoint(worldPos);

        // Clamp to screen edges so text is always visible (with margin)
        const float margin = 80f;
        screenPos.x = Mathf.Clamp(screenPos.x, margin, Screen.width - margin);
        screenPos.y = Mathf.Clamp(screenPos.y, margin, Screen.height - margin);

        // Handle behind-camera: push to top of screen
        if (screenPos.z < 0f)
            screenPos.y = Screen.height - margin;

        _screenTextRT.position = new Vector3(screenPos.x, screenPos.y, 0f);
    }

    /// <summary>Show a reaction sequence: notice → reaction icon → fade out.</summary>
    public void ShowReaction(ReactionType type)
    {
        if (_activeReaction != null)
            StopCoroutine(_activeReaction);

        _activeReaction = StartCoroutine(ReactionSequence(type));
    }

    private IEnumerator ReactionSequence(ReactionType type)
    {
        _bubbleGO.SetActive(true);

        // Notice phase — question mark
        _iconRenderer.sprite = questionSprite;
        _iconRenderer.color = Color.white;
        _bubbleGO.transform.localScale = Vector3.one * 0.5f;

        yield return _waitNotice;

        // Reaction phase — show appropriate icon
        _iconRenderer.sprite = type switch
        {
            ReactionType.Like => heartSprite,
            ReactionType.Dislike => dislikeSprite,
            _ => neutralSprite
        };

        // Color tint
        _iconRenderer.color = type switch
        {
            ReactionType.Like => new Color(1f, 0.4f, 0.5f),
            ReactionType.Dislike => new Color(0.5f, 0.5f, 1f),
            _ => Color.white
        };

        // Play SFX
        AudioClip clip = type switch
        {
            ReactionType.Like => likeSFX,
            ReactionType.Dislike => dislikeSFX,
            _ => null
        };
        if (clip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip);

        // Pop-in scale
        _bubbleGO.transform.localScale = Vector3.one * 0.7f;

        yield return _waitReaction;

        // Fade out
        float fadeTime = 0.3f;
        float elapsed = 0f;
        Color startColor = _iconRenderer.color;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;
            _iconRenderer.color = new Color(startColor.r, startColor.g, startColor.b, 1f - t);
            yield return null;
        }

        _bubbleGO.SetActive(false);
        _activeReaction = null;
    }

    /// <summary>Show a text message above the character for the given duration.</summary>
    public void ShowText(string message, float duration)
    {
        if (_activeReaction != null)
            StopCoroutine(_activeReaction);

        _activeReaction = StartCoroutine(TextSequence(message, duration));

        // Mirror to portrait dialogue box so Nema's face shows with her words
        DialoguePortraitBox.Instance?.Say(message, duration);
    }

    private IEnumerator TextSequence(string message, float duration)
    {
        // Use screen-space text so dialogue is always visible
        EnsureScreenText();
        _screenText.text = message;
        _screenText.color = Color.white;
        _screenText.gameObject.SetActive(true);

        yield return new WaitForSeconds(duration);

        // Fade out
        float fadeTime = 0.3f;
        float elapsed = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;
            _screenText.color = new Color(1f, 1f, 1f, 1f - t);
            yield return null;
        }

        _screenText.gameObject.SetActive(false);
        _screenText.color = Color.white;
        _activeReaction = null;
    }

    /// <summary>Show a labeled reaction: topic label → reaction icon + sentiment text → fade out.</summary>
    public void ShowLabeledReaction(ReactionType type, string topicLabel)
    {
        ShowLabeledReaction(type, topicLabel, null);
    }

    /// <summary>Show a labeled reaction with an optional item icon above the NPC's head.</summary>
    public void ShowLabeledReaction(ReactionType type, string topicLabel, Sprite itemIcon)
    {
        if (_activeReaction != null)
            StopCoroutine(_activeReaction);

        _activeReaction = StartCoroutine(LabeledReactionSequence(type, topicLabel, itemIcon));
    }

    private IEnumerator LabeledReactionSequence(ReactionType type, string topicLabel, Sprite itemIcon = null)
    {
        _bubbleGO.SetActive(true);
        EnsureScreenText();
        EnsureItemIconRenderer();

        // --- Phase 1: Topic label (0.7s) ---
        _iconRenderer.sprite = questionSprite;
        _iconRenderer.color = Color.white;
        _iconRenderer.enabled = true;
        _bubbleGO.transform.localScale = Vector3.one * 0.5f;

        // Show item icon above the reaction icon if provided
        if (itemIcon != null && _itemIconRenderer != null)
        {
            _itemIconRenderer.sprite = itemIcon;
            _itemIconRenderer.color = Color.white;
            _itemIconRenderer.enabled = true;
        }
        else if (_itemIconRenderer != null)
        {
            _itemIconRenderer.enabled = false;
        }

        // Screen-space topic label
        _screenText.text = topicLabel;
        _screenText.color = Color.white;
        _screenText.gameObject.SetActive(true);

        yield return s_wait07;

        // --- Phase 2: Reaction icon + sentiment (1.8s) ---
        _iconRenderer.sprite = type switch
        {
            ReactionType.Like => heartSprite,
            ReactionType.Dislike => dislikeSprite,
            _ => neutralSprite
        };

        Color reactionColor = type switch
        {
            ReactionType.Like => new Color(1f, 0.4f, 0.5f),
            ReactionType.Dislike => new Color(0.5f, 0.5f, 1f),
            _ => Color.white
        };
        _iconRenderer.color = reactionColor;

        // Play SFX
        AudioClip clip = type switch
        {
            ReactionType.Like => likeSFX,
            ReactionType.Dislike => dislikeSFX,
            _ => null
        };
        if (clip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip);

        // Screen-space emoticon flash (fire-and-forget, runs alongside bubble)
        ShowEmoticonFlash(type);

        // Screen-space sentiment text
        _screenText.text = GetRandomSentiment(type);
        _screenText.color = reactionColor;

        _bubbleGO.transform.localScale = Vector3.one * 0.7f;

        yield return s_wait18;

        // --- Phase 3: Fade out (0.3s) ---
        float fadeTime = 0.3f;
        float elapsed = 0f;
        Color iconStart = _iconRenderer.color;
        Color textStart = _screenText.color;
        Color itemIconStart = _itemIconRenderer != null && _itemIconRenderer.enabled
            ? _itemIconRenderer.color : Color.clear;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;
            _iconRenderer.color = new Color(iconStart.r, iconStart.g, iconStart.b, 1f - t);
            _screenText.color = new Color(textStart.r, textStart.g, textStart.b, 1f - t);
            if (_itemIconRenderer != null && _itemIconRenderer.enabled)
                _itemIconRenderer.color = new Color(itemIconStart.r, itemIconStart.g, itemIconStart.b, 1f - t);
            yield return null;
        }

        _screenText.gameObject.SetActive(false);
        _screenText.color = Color.white;
        _iconRenderer.enabled = true;
        if (_itemIconRenderer != null) _itemIconRenderer.enabled = false;
        _bubbleGO.SetActive(false);
        _activeReaction = null;
    }

    private void EnsureBubbleText()
    {
        if (_bubbleText != null) return;

        var textGO = new GameObject("BubbleText");
        textGO.transform.SetParent(_bubbleGO.transform, false);
        textGO.transform.localPosition = Vector3.zero;
        _bubbleText = textGO.AddComponent<TextMeshPro>();
        _bubbleText.fontSize = 5f;
        _bubbleText.alignment = TextAlignmentOptions.Center;
        _bubbleText.color = Color.white;
        _bubbleText.outlineWidth = 0.2f;
        _bubbleText.outlineColor = new Color32(0, 0, 0, 200);
        _bubbleText.rectTransform.sizeDelta = new Vector2(4f, 2f);
        _bubbleText.textWrappingMode = TextWrappingModes.Normal;
        _bubbleText.sortingOrder = 101;

        // Render text through walls
        _bubbleText.renderer.material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        _bubbleText.renderer.material.renderQueue = 4001;
    }

    private void EnsureScreenText()
    {
        if (_screenText != null) return;

        if (s_screenTextCanvas == null)
        {
            var canvasGO = new GameObject("DateScreenTextCanvas");
            DontDestroyOnLoad(canvasGO);
            s_screenTextCanvas = canvasGO.AddComponent<Canvas>();
            s_screenTextCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s_screenTextCanvas.sortingOrder = 95; // above gameplay, below ScreenFade
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        var go = new GameObject("DateScreenText");
        go.transform.SetParent(s_screenTextCanvas.transform, false);
        _screenTextRT = go.AddComponent<RectTransform>();
        _screenTextRT.pivot = new Vector2(0.5f, 0f);
        _screenTextRT.sizeDelta = new Vector2(500f, 60f);

        _screenText = go.AddComponent<TextMeshProUGUI>();
        _screenText.fontSize = 24f;
        _screenText.alignment = TextAlignmentOptions.Center;
        _screenText.color = Color.white;
        _screenText.outlineWidth = 0.3f;
        _screenText.outlineColor = new Color32(0, 0, 0, 180);
        _screenText.textWrappingMode = TextWrappingModes.Normal;
        _screenText.raycastTarget = false;
        go.SetActive(false);
    }

    private void EnsureItemIconRenderer()
    {
        if (_itemIconRenderer != null) return;

        var go = new GameObject("ItemIcon");
        go.transform.SetParent(_bubbleGO.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.8f, 0f);

        _itemIconRenderer = go.AddComponent<SpriteRenderer>();
        _itemIconRenderer.sortingOrder = 102;

        // Render through walls
        var itemShader = Shader.Find("Sprites/Default");
        if (itemShader != null)
        {
            var mat = new Material(itemShader);
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 4002;
            _itemIconRenderer.material = mat;
        }

        go.transform.localScale = Vector3.one * 0.8f;
        _itemIconRenderer.enabled = false;
    }

    private static string GetRandomSentiment(ReactionType type)
    {
        var arr = type switch
        {
            ReactionType.Like => s_likeTexts,
            ReactionType.Dislike => s_dislikeTexts,
            _ => s_neutralTexts
        };
        return arr[UnityEngine.Random.Range(0, arr.Length)];
    }

    // ── Screen-space emoticon flash ──────────────────────────────
    // Brief large emoji in center of screen that scales up and fades out.

    private static Canvas s_flashCanvas;
    private Coroutine _flashCoroutine;

    /// <summary>Show a brief screen-space emoticon flash for the given reaction type.</summary>
    private void ShowEmoticonFlash(ReactionType type)
    {
        Sprite sprite = type switch
        {
            ReactionType.Like => heartSprite,
            ReactionType.Dislike => dislikeSprite,
            _ => neutralSprite
        };
        if (sprite == null) return;

        if (_flashCoroutine != null)
            StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(EmoticonFlashSequence(sprite, type));
    }

    private IEnumerator EmoticonFlashSequence(Sprite sprite, ReactionType type)
    {
        EnsureFlashCanvas();
        if (s_flashCanvas == null) yield break;

        var go = new GameObject("EmoticonFlash");
        go.transform.SetParent(s_flashCanvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(120f, 120f);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.color = type switch
        {
            ReactionType.Like => new Color(1f, 0.4f, 0.5f, 1f),
            ReactionType.Dislike => new Color(0.5f, 0.5f, 1f, 1f),
            _ => new Color(1f, 1f, 1f, 1f)
        };

        // Scale up and fade out over 1 second
        float duration = 1f;
        float elapsed = 0f;
        Vector3 startScale = Vector3.one * 0.5f;
        Vector3 endScale = Vector3.one * 1.5f;
        Color startColor = img.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out quad

            rt.localScale = Vector3.Lerp(startScale, endScale, eased);
            img.color = new Color(startColor.r, startColor.g, startColor.b, 1f - eased);
            yield return null;
        }

        Destroy(go);
        _flashCoroutine = null;
    }

    private static void EnsureFlashCanvas()
    {
        if (s_flashCanvas != null) return;

        var canvasGO = new GameObject("EmoticonFlashCanvas");
        DontDestroyOnLoad(canvasGO);
        s_flashCanvas = canvasGO.AddComponent<Canvas>();
        s_flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        s_flashCanvas.sortingOrder = 150;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
    }
}
