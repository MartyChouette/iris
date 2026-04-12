using UnityEngine;

/// <summary>
/// Marker component on the physical watering can object.
/// ObjectGrabber detects this for magnetic snap toward plants.
/// WateringManager uses it to trigger the pour UI.
/// </summary>
public class WateringCan : MonoBehaviour
{
    [Header("Pour Point")]
    [Tooltip("Transform at the spout tip where water visually comes from.")]
    [SerializeField] private Transform _pourPoint;

    [Header("Audio")]
    [Tooltip("Looping pour sound played while watering.")]
    [SerializeField] private AudioClip _pourSFX;

    [Tooltip("Sound when picking up the can.")]
    [SerializeField] private AudioClip _pickupSFX;

    public Transform PourPoint => _pourPoint != null ? _pourPoint : transform;
    public AudioClip PourSFX => _pourSFX;
    public AudioClip PickupSFX => _pickupSFX;
}
