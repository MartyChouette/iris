using System.Collections;
using UnityEngine;

/// <summary>
/// Turntable receiver. Accepts a physical VinylDisc placed by the player,
/// manages playback via AudioManager, drives the tone arm and disc spin,
/// feeds MoodMachine, and toggles ReactableTag.
///
/// Play/Pause are triggered by tiny TurntableButton children, not by
/// clicking the turntable itself. Clicking the vinyl on the platter
/// ejects it back into the player's hand.
///
/// Scene-scoped singleton (one turntable per apartment).
/// </summary>
public class RecordSlot : MonoBehaviour
{
    public static RecordSlot Instance { get; private set; }

    /// <summary>Fired when a record starts playing (for MidDateActionWatcher).</summary>
    public static event System.Action OnRecordChanged;

    [Header("Visuals")]
    [Tooltip("Transform of the disc visual (rotated during playback).")]
    [SerializeField] private Transform _discVisual;

    [Tooltip("Renderer on the disc visual for changing label color.")]
    [SerializeField] private Renderer _discRenderer;

    [Tooltip("Target rotation speed in degrees/second while playing.")]
    [SerializeField] private float _rotationSpeed = 33.3f;

    [Tooltip("Seconds for the disc to accelerate/decelerate.")]
    [SerializeField] private float _spinTransitionDuration = 1.5f;

    [Header("Record Placement")]
    [Tooltip("Where the vinyl snaps to on the platter.")]
    [SerializeField] private Transform _platePlacementPoint;

    [Tooltip("Seconds for the vinyl to interpolate onto the platter.")]
    [SerializeField] private float _placementLerpDuration = 0.4f;

    [Header("Tone Arm")]
    [Tooltip("Reference to the ToneArm component.")]
    [SerializeField] private ToneArm _toneArm;

    [Header("Magnetic Snap")]
    [Tooltip("Radius for ObjectGrabber's magnetic snap when carrying vinyl nearby.")]
    public float snapRadius = 0.6f;

    [Header("Audio")]
    [Tooltip("SFX played when a record starts playing.")]
    [SerializeField] private AudioClip _playSFX;

    [Tooltip("SFX played when a record is ejected/stopped.")]
    [SerializeField] private AudioClip _stopSFX;

    private PlaceableObject _loadedPlaceable;
    private VinylDisc _loadedVinyl;
    private Material _labelMat;
    private bool _isPlaying;
    private float _currentSpinSpeed;
    private Coroutine _placementLerp;

    public bool IsPlaying => _isPlaying;
    public bool IsLoaded => _loadedVinyl != null;
    public RecordDefinition CurrentRecord => _loadedVinyl != null ? _loadedVinyl.Definition : null;

    /// <summary>World position vinyl snaps to (for magnetic snap in ObjectGrabber).</summary>
    public Vector3 SnapPoint => _platePlacementPoint != null
        ? _platePlacementPoint.position
        : transform.position;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RecordSlot] Duplicate instance destroyed.");
            Destroy(this);
            return;
        }
        Instance = this;

        if (_discRenderer != null)
        {
            _labelMat = new Material(_discRenderer.sharedMaterial);
            _discRenderer.material = _labelMat;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_labelMat != null)
            Object.Destroy(_labelMat);
    }

    private void Update()
    {
        // Gradual spin acceleration/deceleration
        float targetSpeed = _isPlaying ? _rotationSpeed : 0f;
        float lerpRate = _spinTransitionDuration > 0f ? Time.deltaTime / _spinTransitionDuration : 1f;
        _currentSpinSpeed = Mathf.MoveTowards(_currentSpinSpeed, targetSpeed, _rotationSpeed * lerpRate);

        if (_discVisual != null && Mathf.Abs(_currentSpinSpeed) > 0.01f)
            _discVisual.Rotate(Vector3.up, _currentSpinSpeed * Time.deltaTime, Space.Self);
    }

    // ── Vinyl acceptance ─────────────────────────────────────────────

    /// <summary>
    /// Accept a held vinyl disc onto the platter. Called by ObjectGrabber.Place()
    /// when the player clicks the turntable while holding a VinylDisc.
    /// Returns true if accepted.
    /// </summary>
    public bool TryAcceptVinyl(PlaceableObject held)
    {
        if (held == null) return false;

        var vinyl = held.GetComponent<VinylDisc>();
        if (vinyl == null || vinyl.Definition == null) return false;

        // Don't accept if already loaded — player must eject first
        if (_loadedVinyl != null)
        {
            PickupDescriptionHUD.Instance?.Show("Eject the current record first.");
            return false;
        }

        _loadedPlaceable = held;
        _loadedVinyl = vinyl;

        vinyl.ConfigureForTurntable();

        // Preserve the vinyl's world scale before parenting — the turntable
        // may have a non-uniform scale that would deform the disc.
        Vector3 vinylWorldScale = held.transform.lossyScale;
        held.transform.SetParent(transform, true);
        // Recompute local scale to maintain original world scale under new parent
        Vector3 parentScale = transform.lossyScale;
        held.transform.localScale = new Vector3(
            parentScale.x != 0f ? vinylWorldScale.x / parentScale.x : 1f,
            parentScale.y != 0f ? vinylWorldScale.y / parentScale.y : 1f,
            parentScale.z != 0f ? vinylWorldScale.z / parentScale.z : 1f);

        // Apply label color
        if (_labelMat != null)
            _labelMat.color = vinyl.Definition.labelColor;

        // Interpolate vinyl onto platter
        if (_placementLerp != null) StopCoroutine(_placementLerp);
        _placementLerp = StartCoroutine(LerpVinylToPlatter(held.transform));

        // Clear turntable highlight, light up the Play button
        var slotHL = GetComponent<InteractableHighlight>();
        if (slotHL != null) slotHL.SetHighlighted(false);
        HighlightPlayButton(true);

        Debug.Log($"[RecordSlot] Loaded: {vinyl.Definition.title} by {vinyl.Definition.artist}");
        return true;
    }

    /// <summary>Turn the Play button highlight on or off.</summary>
    private void HighlightPlayButton(bool on)
    {
        var buttons = GetComponentsInChildren<TurntableButton>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i].Type != TurntableButton.ButtonType.Play) continue;
            var hl = buttons[i].GetComponent<InteractableHighlight>();
            if (hl == null) hl = buttons[i].gameObject.AddComponent<InteractableHighlight>();
            hl.SetHighlighted(on);
            break;
        }
    }

    private IEnumerator LerpVinylToPlatter(Transform vinyl)
    {
        Vector3 startPos = vinyl.position;
        Quaternion startRot = vinyl.rotation;
        Vector3 targetPos = _platePlacementPoint != null ? _platePlacementPoint.position : transform.position;
        Quaternion targetRot = _platePlacementPoint != null ? _platePlacementPoint.rotation : transform.rotation;

        float elapsed = 0f;
        while (elapsed < _placementLerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _placementLerpDuration);
            vinyl.position = Vector3.Lerp(startPos, targetPos, t);
            vinyl.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        vinyl.position = targetPos;
        vinyl.rotation = targetRot;
        _placementLerp = null;
    }

    // ── Playback controls ────────────────────────────────────────────

    /// <summary>Start playback. Called by TurntableButton (Play).</summary>
    public void Play()
    {
        if (_loadedVinyl == null || _loadedVinyl.Definition == null) return;
        if (_isPlaying) return;

        if (_toneArm != null)
            _toneArm.SwingToPlay(OnNeedleDropped);
        else
            OnNeedleDropped();
    }

    private void OnNeedleDropped()
    {
        _isPlaying = true;
        var def = _loadedVinyl.Definition;

        var clip = def.MusicClip;
        if (clip != null)
            AudioManager.Instance?.PlayMusic(clip, def.volume);

        MoodMachine.Instance?.SetSource("Music", def.moodValue);

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = true;

        if (_playSFX != null)
            AudioManager.Instance?.PlaySFX(_playSFX);

        OnRecordChanged?.Invoke();

        Debug.Log($"[RecordSlot] Playing: {def.title} by {def.artist}");
    }

    /// <summary>Pause playback. Called by TurntableButton (Pause).</summary>
    public void Pause()
    {
        if (!_isPlaying) return;

        if (_toneArm != null)
            _toneArm.SwingToRest(OnNeedleLifted);
        else
            OnNeedleLifted();
    }

    private void OnNeedleLifted()
    {
        _isPlaying = false;

        AudioManager.Instance?.PauseMusic();

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = false;

        Debug.Log("[RecordSlot] Paused.");
    }

    // ── Eject ────────────────────────────────────────────────────────

    /// <summary>
    /// Eject the vinyl from the platter into the player's hand.
    /// Returns the PlaceableObject for ObjectGrabber to grab.
    /// Returns null if nothing is loaded.
    /// </summary>
    public PlaceableObject EjectVinyl()
    {
        if (_loadedPlaceable == null) return null;

        StopPlaybackInternal();

        _loadedPlaceable.transform.SetParent(null, true);
        _loadedVinyl.ConfigureForGrab();

        // Signal the home sleeve to show "ready to receive"
        if (_loadedVinyl.HomeSleeve != null)
            _loadedVinyl.HomeSleeve.SetHovered(false); // reset hover, WaitingForReturn was set on extract

        var result = _loadedPlaceable;
        _loadedPlaceable = null;
        _loadedVinyl = null;

        if (_stopSFX != null)
            AudioManager.Instance?.PlaySFX(_stopSFX);

        Debug.Log("[RecordSlot] Vinyl ejected to hand.");
        return result;
    }

    /// <summary>
    /// Full stop without returning vinyl to player (phase transitions, sleep).
    /// Ejects vinyl back to its home sleeve if one exists.
    /// </summary>
    public void Stop()
    {
        if (_loadedPlaceable == null) return;

        StopPlaybackInternal();

        // Return vinyl to its sleeve
        if (_loadedVinyl != null && _loadedVinyl.HomeSleeve != null)
        {
            _loadedPlaceable.transform.SetParent(null, true);
            _loadedVinyl.HomeSleeve.ReturnVinyl(_loadedVinyl);
        }
        else
        {
            // No sleeve — just unparent
            _loadedPlaceable.transform.SetParent(null, true);
            _loadedVinyl?.ConfigureForSleeve();
        }

        _loadedPlaceable = null;
        _loadedVinyl = null;
    }

    private void StopPlaybackInternal()
    {
        _isPlaying = false;
        _currentSpinSpeed = 0f;

        AudioManager.Instance?.StopMusic();
        MoodMachine.Instance?.RemoveSource("Music");

        var reactable = GetComponent<ReactableTag>();
        if (reactable != null) reactable.IsActive = false;

        if (_toneArm != null) _toneArm.SnapToRest();
    }
}
