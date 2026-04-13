using System.Collections;
using UnityEngine;

/// <summary>
/// Album sleeve on the shelf. Manages hover tilt, vinyl peek-out animation,
/// vinyl extraction (for grab), and vinyl return (from hand).
/// The vinyl disc is a child transform that slides in/out.
/// </summary>
public class AlbumSleeve : MonoBehaviour
{
    [Header("Record")]
    [Tooltip("The record definition (title, artist, audio, mood).")]
    [SerializeField] private RecordDefinition _definition;

    [Header("Vinyl Child")]
    [Tooltip("The VinylDisc child transform that slides in/out of the sleeve.")]
    [SerializeField] private Transform _vinylChild;

    [Header("Hover Animation")]
    [Tooltip("Euler angles the sleeve tilts to when hovered (local space).")]
    [SerializeField] private Vector3 _tiltEulers = new Vector3(-15f, 0f, 0f);

    [Tooltip("Tilt/untilt animation speed.")]
    [SerializeField] private float _tiltSpeed = 6f;

    [Header("Vinyl Peek")]
    [Tooltip("Local offset the vinyl slides to when peeking out of the sleeve.")]
    [SerializeField] private Vector3 _vinylPeekOffset = new Vector3(0f, 0.08f, 0f);

    [Tooltip("Vinyl slide speed.")]
    [SerializeField] private float _peekSpeed = 5f;

    [Header("Audio")]
    [Tooltip("Subtle sound when vinyl slides out.")]
    [SerializeField] private AudioClip _peekSFX;

    public RecordDefinition Definition => _definition;

    /// <summary>True when the vinyl has been extracted and the sleeve is waiting for return.</summary>
    public bool WaitingForReturn { get; private set; }

    /// <summary>True when the cursor is hovering over this sleeve.</summary>
    public bool IsHovered { get; private set; }

    private Quaternion _restRotation;
    private Quaternion _tiltRotation;
    private Vector3 _vinylRestLocal;
    private Vector3 _vinylRestScale;
    private Quaternion _vinylRestRotation;
    private float _tiltBlend;
    private float _peekBlend;
    private bool _peekSFXPlayed;

    private void Awake()
    {
        _restRotation = transform.localRotation;
        _tiltRotation = _restRotation * Quaternion.Euler(_tiltEulers);

        if (_vinylChild != null)
        {
            _vinylRestLocal = _vinylChild.localPosition;
            _vinylRestScale = _vinylChild.localScale;
            _vinylRestRotation = _vinylChild.localRotation;
        }
    }

    private void Update()
    {
        // Drive tilt animation
        bool shouldTilt = IsHovered || WaitingForReturn;
        _tiltBlend = Mathf.MoveTowards(_tiltBlend, shouldTilt ? 1f : 0f, Time.deltaTime * _tiltSpeed);
        transform.localRotation = Quaternion.Slerp(_restRotation, _tiltRotation, _tiltBlend);

        // Drive vinyl peek animation (only when vinyl is still a child)
        if (_vinylChild != null && !WaitingForReturn)
        {
            _peekBlend = Mathf.MoveTowards(_peekBlend, IsHovered ? 1f : 0f, Time.deltaTime * _peekSpeed);
            _vinylChild.localPosition = Vector3.Lerp(_vinylRestLocal, _vinylRestLocal + _vinylPeekOffset, _peekBlend);

            if (IsHovered && !_peekSFXPlayed && _peekBlend > 0.1f)
            {
                _peekSFXPlayed = true;
                if (_peekSFX != null && AudioManager.Instance != null)
                    AudioManager.Instance.PlaySFX(_peekSFX);
            }
            else if (!IsHovered)
            {
                _peekSFXPlayed = false;
            }
        }
    }

    /// <summary>Called by ObjectGrabber hover detection.</summary>
    public void SetHovered(bool hovered)
    {
        IsHovered = hovered;
    }

    /// <summary>Returns tooltip text for PickupDescriptionHUD.</summary>
    public string GetTooltipText()
    {
        if (_definition == null) return "";
        return $"{_definition.title} \u2014 {_definition.artist}";
    }

    /// <summary>
    /// Detach the vinyl disc from this sleeve for the player to grab.
    /// Returns the PlaceableObject on the vinyl for ObjectGrabber to hold.
    /// </summary>
    public PlaceableObject ExtractVinyl()
    {
        if (_vinylChild == null) return null;

        var vinyl = _vinylChild.GetComponent<VinylDisc>();
        if (vinyl == null) return null;

        vinyl.ConfigureForGrab();
        WaitingForReturn = true;

        // Peek blend stays at 0 since the vinyl is gone
        _peekBlend = 0f;

        return vinyl.GetComponent<PlaceableObject>();
    }

    /// <summary>
    /// Accept the vinyl disc back into the sleeve. Called when the player
    /// clicks this sleeve while holding the matching vinyl.
    /// </summary>
    public void ReturnVinyl(VinylDisc vinyl)
    {
        if (vinyl == null) return;

        vinyl.ConfigureForSleeve();
        vinyl.transform.SetParent(transform, false); // false = reset to local space

        // Restore the vinyl's original local transform so it doesn't carry
        // deformed scale/rotation from the turntable.
        vinyl.transform.localScale = _vinylRestScale;
        vinyl.transform.localRotation = _vinylRestRotation;

        _vinylChild = vinyl.transform;
        WaitingForReturn = false;
        _peekBlend = 1f; // start peeked out, will lerp back in

        StartCoroutine(SlideVinylIn());
    }

    private IEnumerator SlideVinylIn()
    {
        // Quick slide from peek position to rest position
        Vector3 start = _vinylRestLocal + _vinylPeekOffset;
        float t = 0f;
        float duration = 0.3f;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (_vinylChild != null)
                _vinylChild.localPosition = Vector3.Lerp(start, _vinylRestLocal, t / duration);
            yield return null;
        }
        if (_vinylChild != null)
            _vinylChild.localPosition = _vinylRestLocal;
        _peekBlend = 0f;
    }
}
