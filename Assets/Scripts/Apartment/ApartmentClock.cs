using UnityEngine;

/// <summary>
/// Analog wall clock driven by GameClock.CurrentHour.
/// Rotates hour and minute hand transforms around their local Z axis.
/// Place this on a clock GameObject in the apartment scene, then assign
/// the two hand transforms in the Inspector.
/// </summary>
public class ApartmentClock : MonoBehaviour
{
    [Header("Hands")]
    [Tooltip("Transform that acts as the hour hand. Rotates 360° per 12 game hours.")]
    [SerializeField] private Transform hourHand;

    [Tooltip("Transform that acts as the minute hand. Rotates 360° per 1 game hour.")]
    [SerializeField] private Transform minuteHand;

    [Header("Optional")]
    [Tooltip("Optional second hand. Rotates 360° per game minute (1/60th of a game hour).")]
    [SerializeField] private Transform secondHand;

    [Tooltip("If true, hands snap in discrete ticks instead of sweeping smoothly.")]
    [SerializeField] private bool discreteTicks = false;

    [Header("Audio")]
    [Tooltip("Optional tick SFX played once per game minute.")]
    [SerializeField] private AudioClip tickSFX;

    [Tooltip("AudioSource for the tick. If null, one is added automatically when tickSFX is set.")]
    [SerializeField] private AudioSource tickSource;

    [Tooltip("Volume for the tick SFX.")]
    [Range(0f, 1f)]
    [SerializeField] private float tickVolume = 0.15f;

    private int _lastMinute = -1;

    private void Update()
    {
        if (GameClock.Instance == null) return;

        float hour24 = Mathf.Repeat(GameClock.Instance.CurrentHour, 24f);
        float hour12 = Mathf.Repeat(hour24, 12f);
        float minuteFrac = (hour24 - Mathf.Floor(hour24));  // 0-1 within the hour
        float minute = minuteFrac * 60f;
        float secondFrac = (minute - Mathf.Floor(minute));
        float second = secondFrac * 60f;

        if (discreteTicks)
        {
            minute = Mathf.Floor(minute);
            second = Mathf.Floor(second);
            hour12 = Mathf.Floor(hour12) + minute / 60f;
        }

        // Hour hand: 360° per 12 hours → 30° per hour
        float hourAngle = hour12 * 30f;

        // Minute hand: 360° per 60 minutes → 6° per minute
        float minuteAngle = minute * 6f;

        // Second hand: 360° per 60 seconds
        float secondAngle = second * 6f;

        // Rotate pivot empties around local Z. The child hand meshes carry
        // their own base orientation — the pivot just sweeps the angle.
        if (hourHand != null)
            hourHand.localRotation = Quaternion.Euler(0f, 0f, -hourAngle);

        if (minuteHand != null)
            minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minuteAngle);

        if (secondHand != null)
            secondHand.localRotation = Quaternion.Euler(0f, 0f, -secondAngle);

        // Tick SFX on minute change
        int currentMinute = Mathf.FloorToInt(hour24 * 60f);
        if (tickSFX != null && currentMinute != _lastMinute && _lastMinute >= 0)
        {
            EnsureTickSource();
            tickSource.PlayOneShot(tickSFX, tickVolume);
        }
        _lastMinute = currentMinute;
    }

    private void EnsureTickSource()
    {
        if (tickSource != null) return;
        tickSource = gameObject.AddComponent<AudioSource>();
        tickSource.spatialBlend = 1f; // 3D
        tickSource.playOnAwake = false;
    }
}
