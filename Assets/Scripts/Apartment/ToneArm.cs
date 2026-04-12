using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Animates the turntable tone arm between rest (off record) and play (on record)
/// positions. Pivots around local Y axis. RecordSlot calls SwingToPlay/SwingToRest
/// when the player presses play/pause.
/// </summary>
public class ToneArm : MonoBehaviour
{
    [Header("Pivot")]
    [Tooltip("Transform that rotates (the arm base). If null, uses this transform.")]
    [SerializeField] private Transform _armPivot;

    [Header("Angles")]
    [Tooltip("Local Y rotation when the arm is at rest (off the record).")]
    [SerializeField] private float _restAngle = -30f;

    [Tooltip("Local Y rotation when the arm is on the record (playing).")]
    [SerializeField] private float _playAngle = 5f;

    [Header("Timing")]
    [Tooltip("Seconds for the arm to swing between rest and play positions.")]
    [SerializeField] private float _swingDuration = 1.2f;

    [Tooltip("Easing curve for the swing (default: smooth in/out).")]
    [SerializeField] private AnimationCurve _swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio")]
    [Tooltip("SFX when needle touches the record.")]
    [SerializeField] private AudioClip _needleDropSFX;

    [Tooltip("SFX when needle lifts off the record.")]
    [SerializeField] private AudioClip _needleLiftSFX;

    private Coroutine _swingRoutine;

    private Transform Pivot => _armPivot != null ? _armPivot : transform;

    private void Awake()
    {
        SnapToRest();
    }

    /// <summary>Swing the arm from rest to play position. Callback fires when needle lands.</summary>
    public void SwingToPlay(Action onComplete = null)
    {
        StopSwing();
        _swingRoutine = StartCoroutine(SwingRoutine(_restAngle, _playAngle, _needleDropSFX, true, onComplete));
    }

    /// <summary>Swing the arm from play to rest position. Callback fires when arm is parked.</summary>
    public void SwingToRest(Action onComplete = null)
    {
        StopSwing();
        _swingRoutine = StartCoroutine(SwingRoutine(_playAngle, _restAngle, _needleLiftSFX, false, onComplete));
    }

    /// <summary>Instantly snap the arm to rest position (no animation).</summary>
    public void SnapToRest()
    {
        StopSwing();
        SetArmAngle(_restAngle);
    }

    private void StopSwing()
    {
        if (_swingRoutine != null)
        {
            StopCoroutine(_swingRoutine);
            _swingRoutine = null;
        }
    }

    private void SetArmAngle(float yAngle)
    {
        Vector3 euler = Pivot.localEulerAngles;
        euler.y = yAngle;
        Pivot.localEulerAngles = euler;
    }

    private IEnumerator SwingRoutine(float fromAngle, float toAngle, AudioClip sfx, bool sfxAtEnd, Action onComplete)
    {
        // Play SFX at start for lift, at end for drop
        if (!sfxAtEnd && sfx != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(sfx);

        float elapsed = 0f;
        while (elapsed < _swingDuration)
        {
            elapsed += Time.deltaTime;
            float t = _swingCurve.Evaluate(Mathf.Clamp01(elapsed / _swingDuration));
            SetArmAngle(Mathf.Lerp(fromAngle, toAngle, t));
            yield return null;
        }

        SetArmAngle(toAngle);

        if (sfxAtEnd && sfx != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(sfx);

        _swingRoutine = null;
        onComplete?.Invoke();
    }
}
