using System.Collections;
using UnityEngine;

/// <summary>
/// Tiny physical play/pause button on the turntable body.
/// ObjectGrabber detects it on click (same pattern as LightSwitch)
/// and calls Press(). Routes to RecordSlot.Play() or Pause().
/// </summary>
public class TurntableButton : MonoBehaviour
{
    public enum ButtonType { Play, Pause }

    [Header("Identity")]
    [Tooltip("Which action this button performs.")]
    [SerializeField] private ButtonType _buttonType = ButtonType.Play;

    public ButtonType Type => _buttonType;

    [Header("References")]
    [Tooltip("The turntable's RecordSlot. Auto-finds from parent if null.")]
    [SerializeField] private RecordSlot _parentSlot;

    [Header("Audio")]
    [Tooltip("Click sound when button is pressed.")]
    [SerializeField] private AudioClip _clickSFX;

    [Header("Animation")]
    [Tooltip("Transform of the button visual (depressed on click).")]
    [SerializeField] private Transform _buttonVisual;

    [Tooltip("How far the button depresses on click (local Y).")]
    [SerializeField] private float _pressDepth = 0.003f;

    [Tooltip("Duration of the press animation.")]
    [SerializeField] private float _pressDuration = 0.12f;

    private void Awake()
    {
        if (_parentSlot == null)
            _parentSlot = GetComponentInParent<RecordSlot>();
    }

    /// <summary>Called by ObjectGrabber on click.</summary>
    public void Press()
    {
        if (_parentSlot == null) return;

        if (_buttonType == ButtonType.Play)
            _parentSlot.Play();
        else
            _parentSlot.Pause();

        // Clear highlight after pressing
        var hl = GetComponent<ItemHighlight>();
        if (hl != null) hl.SetHighlighted(false);

        if (_clickSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_clickSFX);

        if (_buttonVisual != null)
            StartCoroutine(PressAnimation());
    }

    private IEnumerator PressAnimation()
    {
        Vector3 original = _buttonVisual.localPosition;
        Vector3 pressed = original - Vector3.up * _pressDepth;

        float half = _pressDuration * 0.5f;
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            _buttonVisual.localPosition = Vector3.Lerp(original, pressed, t / half);
            yield return null;
        }
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            _buttonVisual.localPosition = Vector3.Lerp(pressed, original, t / half);
            yield return null;
        }
        _buttonVisual.localPosition = original;
    }
}
