// File: FadeFromWhiteSlow.cs
// Fullscreen UI Image fades from white to transparent with optional easing.
// Add this to a Canvas > UI > Image that covers the screen.

using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Image))]
public class FadeFromWhite : MonoBehaviour
{
    [Header("Fade")]
    [Tooltip("Seconds the fade should take (slow by default).")]
    public float duration = 5f;

    [Tooltip("Seconds to wait before starting the fade.")]
    public float delay = 0f;

    [Tooltip("Use unscaled time (ignores Time.timeScale).")]
    public bool useUnscaledTime = true;

    [Tooltip("Animation curve for easing (0→1 over time).")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Appearance")]
    [Tooltip("Start color (alpha is used as starting opacity).")]
    public Color startColor = new Color(1, 1, 1, 1);  // opaque white

    [Tooltip("If true, disables the Image after fade completes.")]
    public bool disableAfterFade = true;

    private Image _img;
    private Coroutine _co;

    void Awake()
    {
        _img = GetComponent<Image>();
        _img.raycastTarget = false; // don't block input
        _img.color = startColor;    // ensure we start from white
    }

    void OnEnable()
    {
        // auto-run at scene start or when enabled
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(FadeRoutine());
    }

    public void RestartFade()
    {
        if (_co != null) StopCoroutine(_co);
        _img.color = startColor;
        _img.enabled = true;
        _co = StartCoroutine(FadeRoutine());
    }

    IEnumerator FadeRoutine()
    {
        if (delay > 0f)
        {
            float d = 0f;
            while (d < delay)
            {
                d += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        float t = 0f;
        Color c0 = startColor;

        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, duration));
            float k = ease != null ? ease.Evaluate(u) : u;

            // lerp alpha from startColor.a -> 0
            float a = Mathf.Lerp(c0.a, 0f, k);
            _img.color = new Color(c0.r, c0.g, c0.b, a);

            yield return null;
        }

        // snap fully clear at end
        _img.color = new Color(c0.r, c0.g, c0.b, 0f);

        if (disableAfterFade) _img.enabled = false;
        _co = null;
    }
}
