using UnityEngine;

/// <summary>
/// Subtle rhythmic bounce on speaker objects while music is playing.
/// Reads playback state from RecordSlot. Attach to each speaker in the scene.
/// </summary>
public class SpeakerBounce : MonoBehaviour
{
    [Tooltip("How much the speaker scales up on each beat pulse (e.g. 0.03 = 3%).")]
    [SerializeField] private float _bounceAmount = 0.03f;

    [Tooltip("Beats per second (pulses per second). Match to music tempo or leave as a vibe.")]
    [SerializeField] private float _bounceRate = 2f;

    [Tooltip("How quickly the bounce snaps back (higher = punchier).")]
    [SerializeField] private float _bounceFalloff = 8f;

    private Vector3 _baseScale;
    private float _phase;
    private float _currentBounce;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void Update()
    {
        bool playing = RecordSlot.Instance != null && RecordSlot.Instance.IsPlaying;

        if (playing)
        {
            _phase += Time.deltaTime * _bounceRate;
            // Sharp pulse: sin wave clamped to positive half only
            float pulse = Mathf.Max(0f, Mathf.Sin(_phase * Mathf.PI * 2f));
            _currentBounce = Mathf.Lerp(_currentBounce, pulse * _bounceAmount, Time.deltaTime * _bounceFalloff * 2f);
        }
        else
        {
            // Settle back to base
            _currentBounce = Mathf.Lerp(_currentBounce, 0f, Time.deltaTime * _bounceFalloff);
            _phase = 0f;
        }

        if (Mathf.Abs(_currentBounce) > 0.0001f)
            transform.localScale = _baseScale * (1f + _currentBounce);
        else if (!playing)
            transform.localScale = _baseScale;
    }
}
