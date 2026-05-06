using System.Collections;
using UnityEngine;

/// <summary>
/// Turntable receiver. Accepts a physical VinylDisc placed by the player,
/// manages playback via AudioManager, drives the tone arm and disc spin,
/// feeds MoodMachine, and toggles ReactableTag.
///
/// Play/Pause are triggered by clicking the turntable body.
/// Clicking the vinyl on the platter ejects it back into the player's hand.
///
/// Scene-scoped singleton (one turntable per apartment).
/// </summary>
public class RecordSlot : MonoBehaviour
{
    public static RecordSlot Instance { get; private set; }

    /// <summary>Fired when a record starts playing (for MidDateActionWatcher).</summary>
    public static event System.Action OnRecordChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Clear static event subscribers from any prior editor play session
        OnRecordChanged = null;
    }

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

    [Header("Lid")]
    [Tooltip("Transform of the lid (hinges from back). Null = no lid.")]
    [SerializeField] private Transform _lid;

    [Tooltip("Closed local rotation of the lid.")]
    [SerializeField] private Vector3 _lidClosedAngle = new Vector3(-80f, 0f, 0f);

    [Tooltip("Seconds for the lid to open/close.")]
    [SerializeField] private float _lidTweenDuration = 0.4f;

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
    private Coroutine _lidTween;
    private Quaternion _lidOpenRot; // captured from the model's posed position
    private ReactableTag _cachedReactable;
    private bool _lidOpen;

    public bool IsPlaying => _isPlaying;
    public bool IsLoaded => _loadedVinyl != null;
    public bool IsLidOpen => _lidOpen;
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

        _cachedReactable = GetComponent<ReactableTag>();

        // Capture the lid's model pose as the open position, then snap closed
        if (_lid != null)
        {
            _lidOpenRot = _lid.localRotation;
            _lid.localRotation = Quaternion.Euler(_lidClosedAngle);
        }

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

        if (Mathf.Abs(_currentSpinSpeed) > 0.01f)
        {
            float delta = _currentSpinSpeed * Time.deltaTime;
            if (_discVisual != null)
                _discVisual.Rotate(Vector3.up, delta, Space.Self);
            if (_loadedPlaceable != null)
                _loadedPlaceable.transform.Rotate(Vector3.up, delta, Space.Self);
        }
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

        // Auto-swap: if a different record is already loaded, return it to its sleeve first
        if (_loadedVinyl != null)
        {
            if (_loadedVinyl == vinyl) return false; // same record, no-op
            EjectToSleeve();
        }

        _loadedPlaceable = held;
        _loadedVinyl = vinyl;

        vinyl.ConfigureForTurntable();

        // Position at the placement point without parenting — avoids scale
        // deformation from non-uniform ancestor transforms.
        Transform anchor = _platePlacementPoint != null ? _platePlacementPoint : transform;
        held.transform.SetParent(null, true);
        held.transform.position = anchor.position;
        held.transform.rotation = anchor.rotation;

        // Label color swap removed — use the stock black record as-is

        // Auto-open lid when accepting a record (player must close it manually)
        OpenLid();
        Play();

        // Clear turntable highlight
        var slotHL = GetComponent<InteractableHighlight>();
        if (slotHL != null) slotHL.SetHighlighted(false);

        Debug.Log($"[RecordSlot] Loaded: {vinyl.Definition.title} by {vinyl.Definition.artist}");
        return true;
    }

    // ── Playback controls ────────────────────────────────────────────

    /// <summary>Start playback. Called when vinyl is placed or turntable clicked.</summary>
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

        if (_cachedReactable != null) _cachedReactable.IsActive = true;

        if (_playSFX != null)
            AudioManager.Instance?.PlaySFX(_playSFX);

        OnRecordChanged?.Invoke();

        Debug.Log($"[RecordSlot] Playing: {def.title} by {def.artist}");
    }

    /// <summary>Pause playback. Called when turntable clicked while playing.</summary>
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

        if (_cachedReactable != null) _cachedReactable.IsActive = false;

        Debug.Log("[RecordSlot] Paused.");
    }

    /// <summary>Toggle between play and pause. Called when clicking the turntable body.</summary>
    public void TogglePlayPause()
    {
        if (_loadedVinyl == null) return;

        if (_isPlaying)
            Pause();
        else
        {
            // Resume from where paused instead of restarting
            if (_toneArm != null)
                _toneArm.SwingToPlay(OnResumed);
            else
                OnResumed();
        }
    }

    private void OnResumed()
    {
        _isPlaying = true;
        AudioManager.Instance?.ResumeMusic();

        if (_cachedReactable != null) _cachedReactable.IsActive = true;

        if (_playSFX != null)
            AudioManager.Instance?.PlaySFX(_playSFX);

        Debug.Log("[RecordSlot] Resumed.");
    }

    /// <summary>
    /// Eject the vinyl and return it directly to its sleeve. Stops playback.
    /// Called when the player clicks the vinyl on the platter.
    /// </summary>
    public void EjectToSleeve()
    {
        if (_loadedVinyl == null) return;

        StopPlaybackInternal();

        var vinyl = _loadedVinyl;
        if (_loadedPlaceable != null)
            _loadedPlaceable.transform.SetParent(null, true);

        if (vinyl.HomeSleeve != null)
            vinyl.HomeSleeve.ReturnVinyl(vinyl);
        else
            vinyl.ConfigureForSleeve();

        _loadedPlaceable = null;
        _loadedVinyl = null;

        if (_stopSFX != null)
            AudioManager.Instance?.PlaySFX(_stopSFX);

        Debug.Log("[RecordSlot] Vinyl ejected to sleeve.");
    }

    // ── Eject ────────────────────────────────────────────────────────

    /// <summary>
    /// Eject the vinyl from the platter into the player's hand.
    /// Returns the PlaceableObject for ObjectGrabber to grab.
    /// Returns null if nothing is loaded.
    /// </summary>
    public PlaceableObject EjectVinyl()
    {
        if (_loadedPlaceable == null || _loadedVinyl == null) return null;

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
        if (_loadedPlaceable == null && _loadedVinyl == null) return;

        StopPlaybackInternal();

        // Return vinyl to its sleeve
        if (_loadedVinyl != null && _loadedVinyl.HomeSleeve != null)
        {
            if (_loadedPlaceable != null)
                _loadedPlaceable.transform.SetParent(null, true);
            _loadedVinyl.HomeSleeve.ReturnVinyl(_loadedVinyl);
        }
        else
        {
            // No sleeve — just unparent
            if (_loadedPlaceable != null)
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

        if (_cachedReactable != null) _cachedReactable.IsActive = false;

        if (_toneArm != null) _toneArm.SnapToRest();
    }

    // ── Lid ──────────────────────────────────────────────────────────

    private void OpenLid() => OpenLidExternal();

    /// <summary>True if the given collider belongs to the lid transform (for click detection).</summary>
    public bool IsLidTarget(Collider col)
    {
        if (_lid == null || col == null) return false;
        return col.transform == _lid || col.transform.IsChildOf(_lid);
    }

    /// <summary>Toggle lid open/closed. Called when the player clicks the lid.</summary>
    public void ToggleLid()
    {
        if (_lidOpen)
            CloseLidExternal();
        else
            OpenLidExternal();
    }

    public void OpenLidExternal()
    {
        if (_lid == null) return;
        if (_lidOpen) return;
        _lidOpen = true;
        if (_lidTween != null) StopCoroutine(_lidTween);
        _lidTween = StartCoroutine(TweenLid(_lidOpenRot));
    }

    public void CloseLidExternal()
    {
        if (_lid == null) return;
        if (!_lidOpen) return;
        _lidOpen = false;
        if (_lidTween != null) StopCoroutine(_lidTween);
        _lidTween = StartCoroutine(TweenLid(Quaternion.Euler(_lidClosedAngle)));
    }

    private IEnumerator TweenLid(Quaternion targetRot)
    {
        Quaternion startRot = _lid.localRotation;
        float elapsed = 0f;
        while (elapsed < _lidTweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _lidTweenDuration;
            t = t * t * (3f - 2f * t);
            _lid.localRotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }
        _lid.localRotation = targetRot;
        _lidTween = null;
    }
}
