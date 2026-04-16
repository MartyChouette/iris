using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Unity.Cinemachine;
using UnityEngine.UI;
using Iris.Apartment;

public class CameraTestController : MonoBehaviour
{
    public static CameraTestController Instance { get; private set; }

    [Header("Presets")]
    [Tooltip("Camera presets to compare (V1–V9).")]
    [SerializeField] private CameraPresetDefinition[] presets;

    [Header("References")]
    [SerializeField] private CinemachineCamera browseCamera;
    [SerializeField] private CinemachineBrain brain;
    [SerializeField] private ApartmentManager apartmentManager;

    [Header("Post-Processing")]
    [Tooltip("Volume component whose profile is swapped per preset.")]
    [SerializeField] private Volume presetVolume;

    [Header("Lighting")]
    [Tooltip("Directional light to apply preset tint/intensity multipliers to.")]
    [SerializeField] private Light directionalLight;

    [Header("Transition")]
    [SerializeField, Range(1f, 15f)] private float transitionSpeed = 5f;

    [Header("UI")]
    [SerializeField] private Button[] presetButtons;
    [SerializeField] private Color activeButtonColor = new Color(0.2f, 0.8f, 0.2f, 0.9f);
    [SerializeField] private Color inactiveButtonColor = new Color(0.3f, 0.3f, 0.3f, 0.85f);

    public int ActivePresetIndex => _activePresetIndex;

    private int _activePresetIndex = -1;
    private int _suspendedPresetIndex = -1;
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private LensSettings _targetLens;
    private bool _isTransitioning;

    private Vector3 _currentPos;
    private Quaternion _currentRot;

    // Light baseline
    private float _baseLightIntensity;
    private Color _baseLightColor;
    private bool _baseLightCaptured;

    // Volume baseline
    private VolumeProfile _baseVolumeProfile;

    // Keyboard: 1-9 for presets, backtick to clear
    private InputAction[] _presetActions;
    private InputAction _clearPresetAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CameraTestController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Create actions for keys 1-9
        _presetActions = new InputAction[9];
        for (int i = 0; i < 9; i++)
        {
            int keyNum = i + 1;
            _presetActions[i] = new InputAction($"Preset{keyNum}", InputActionType.Button,
                $"<Keyboard>/{keyNum}");
        }
        _clearPresetAction = new InputAction("ClearPreset", InputActionType.Button,
            "<Keyboard>/backquote");
    }

    private void OnEnable()
    {
        for (int i = 0; i < _presetActions.Length; i++)
            _presetActions[i].Enable();
        _clearPresetAction.Enable();
    }

    private void OnDisable()
    {
        for (int i = 0; i < _presetActions.Length; i++)
            _presetActions[i].Disable();
        _clearPresetAction.Disable();
    }

    [Header("Visibility")]
#pragma warning disable 0414
    [Tooltip("How many preset buttons to show (hides the rest). 0 = show all.")]
    [SerializeField] private int _visiblePresetCount = 2;
#pragma warning restore 0414

    private void Start()
    {
        // Hide all preset buttons — camera switching disabled for now
        if (presetButtons != null)
        {
            for (int i = 0; i < presetButtons.Length; i++)
            {
                if (presetButtons[i] != null)
                    presetButtons[i].gameObject.SetActive(false);
            }
        }

        if (directionalLight != null)
        {
            _baseLightIntensity = directionalLight.intensity;
            _baseLightColor = directionalLight.color;
            _baseLightCaptured = true;
        }

        if (presetVolume != null)
            _baseVolumeProfile = presetVolume.profile;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Dispose InputActions to free native memory
        if (_presetActions != null)
        {
            for (int i = 0; i < _presetActions.Length; i++)
                _presetActions[i]?.Dispose();
            _presetActions = null;
        }
        _clearPresetAction?.Dispose();
        _clearPresetAction = null;
    }

    /// <summary>
    /// Temporarily clear the active preset so Cinemachine can blend to a perspective camera.
    /// The preset index is remembered — call RestorePreset() to re-apply it.
    /// </summary>
    public void SuspendPreset()
    {
        if (_activePresetIndex < 0) return;
        _suspendedPresetIndex = _activePresetIndex;
        ClearPreset();
        Debug.Log($"[CameraTestController] Preset suspended (was {_suspendedPresetIndex}).");
    }

    /// <summary>
    /// Re-apply the preset that was active before SuspendPreset() was called.
    /// </summary>
    public void RestorePreset()
    {
        if (_suspendedPresetIndex < 0) return;
        ApplyPreset(_suspendedPresetIndex);
        _suspendedPresetIndex = -1;
        Debug.Log($"[CameraTestController] Preset restored.");
    }

    public bool HasSuspendedPreset => _suspendedPresetIndex >= 0;

    public void ApplyPreset(int index)
    {
        if (presets == null || index < 0 || index >= presets.Length) return;

        _activePresetIndex = index;
        int areaIndex = apartmentManager != null ? apartmentManager.CurrentAreaIndex : 0;
        ApplyForArea(presets[index], areaIndex, immediate: true);
        UpdateButtonHighlights();
        Debug.Log($"[CameraTestController] Applied preset: {presets[index].label}");
    }

    public void OnAreaChanged(int areaIndex)
    {
        if (_activePresetIndex < 0 || presets == null) return;
        ApplyForArea(presets[_activePresetIndex], areaIndex, immediate: false);
    }

    public void ClearPreset()
    {
        _activePresetIndex = -1;
        _isTransitioning = false;

        if (browseCamera != null)
        {
            var lens = browseCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.None;
            browseCamera.Lens = lens;
        }

        SetBrainOrthoOverride(false);
        UpdateButtonHighlights();

        if (presetVolume != null && _baseVolumeProfile != null)
            presetVolume.profile = _baseVolumeProfile;

        if (directionalLight != null && _baseLightCaptured)
        {
            directionalLight.intensity = _baseLightIntensity;
            directionalLight.color = _baseLightColor;
        }

        if (apartmentManager != null)
            apartmentManager.ClearPresetBase();
    }

    private void ApplyForArea(CameraPresetDefinition preset, int areaIndex, bool immediate)
    {
        if (preset == null || preset.areaConfigs == null || areaIndex >= preset.areaConfigs.Length) return;

        var config = preset.areaConfigs[areaIndex];
        _targetPos = config.position;
        _targetRot = Quaternion.Euler(config.rotation);
        _targetLens = config.lens;

        bool isOrtho = _targetLens.ModeOverride == LensSettings.OverrideModes.Orthographic;
        SetBrainOrthoOverride(isOrtho);

        if (presetVolume != null && config.volumeProfile != null)
            presetVolume.profile = config.volumeProfile;

        ApplyLightOverrides(config);

        if (immediate)
        {
            _currentPos = _targetPos;
            _currentRot = _targetRot;
            ApplyLens();
            FeedToApartmentManager();
            _isTransitioning = false;
        }
        else
        {
            if (browseCamera != null)
            {
                _currentPos = browseCamera.transform.position;
                _currentRot = browseCamera.transform.rotation;
            }
            _isTransitioning = true;
        }
    }

    private void ApplyLightOverrides(AreaCameraConfig config)
    {
        if (directionalLight == null || !_baseLightCaptured) return;

        float multiplier = config.lightIntensityMultiplier > 0f
            ? config.lightIntensityMultiplier
            : 1f;
        directionalLight.intensity = _baseLightIntensity * multiplier;

        Color tint = config.lightColorTint.a > 0f
            ? config.lightColorTint
            : Color.white;
        directionalLight.color = _baseLightColor * tint;
    }

    private void Update()
    {
        // Camera preset switching disabled for now
        // for (int i = 0; i < _presetActions.Length; i++)
        // {
        //     if (_presetActions[i].WasPressedThisFrame() && i < (presets != null ? presets.Length : 0))
        //     {
        //         ApplyPreset(i);
        //         break;
        //     }
        // }
        // if (_clearPresetAction.WasPressedThisFrame()) ClearPreset();

        if (!_isTransitioning || _activePresetIndex < 0 || browseCamera == null) return;

        float step = transitionSpeed * Time.deltaTime;

        _currentPos = Vector3.Lerp(_currentPos, _targetPos, step);
        _currentRot = Quaternion.Slerp(_currentRot, _targetRot, step);

        // Lerp lens values
        var lens = browseCamera.Lens;
        bool isOrtho = _targetLens.ModeOverride == LensSettings.OverrideModes.Orthographic;
        if (isOrtho)
            lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, _targetLens.OrthographicSize, step);
        else
            lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, _targetLens.FieldOfView, step);
        lens.NearClipPlane = Mathf.Lerp(lens.NearClipPlane, _targetLens.NearClipPlane, step);
        lens.FarClipPlane = Mathf.Lerp(lens.FarClipPlane, _targetLens.FarClipPlane, step);
        lens.Dutch = Mathf.Lerp(lens.Dutch, _targetLens.Dutch, step);
        lens.ModeOverride = _targetLens.ModeOverride;

        // Lerp physical lens properties
        var phys = lens.PhysicalProperties;
        var targetPhys = _targetLens.PhysicalProperties;
        phys.Aperture = Mathf.Lerp(phys.Aperture, targetPhys.Aperture, step);
        phys.FocusDistance = Mathf.Lerp(phys.FocusDistance, targetPhys.FocusDistance, step);
        phys.Iso = (int)Mathf.Lerp(phys.Iso, targetPhys.Iso, step);
        phys.ShutterSpeed = Mathf.Lerp(phys.ShutterSpeed, targetPhys.ShutterSpeed, step);
        phys.Anamorphism = Mathf.Lerp(phys.Anamorphism, targetPhys.Anamorphism, step);
        phys.BarrelClipping = Mathf.Lerp(phys.BarrelClipping, targetPhys.BarrelClipping, step);
        lens.PhysicalProperties = phys;

        browseCamera.Lens = lens;

        FeedToApartmentManager();

        bool posClose = (_currentPos - _targetPos).sqrMagnitude < 0.0001f;
        bool rotClose = Quaternion.Angle(_currentRot, _targetRot) < 0.05f;
        if (posClose && rotClose)
        {
            _currentPos = _targetPos;
            _currentRot = _targetRot;
            ApplyLens();
            FeedToApartmentManager();
            _isTransitioning = false;
        }
    }

    private void FeedToApartmentManager()
    {
        if (apartmentManager != null)
        {
            float fov = _targetLens.ModeOverride == LensSettings.OverrideModes.Orthographic
                ? _targetLens.OrthographicSize
                : _targetLens.FieldOfView;
            apartmentManager.SetPresetBase(_currentPos, _currentRot, fov);
        }
    }

    private void ApplyLens()
    {
        if (browseCamera == null) return;
        browseCamera.Lens = _targetLens;
    }

    private void SetBrainOrthoOverride(bool ortho)
    {
        if (brain == null) return;
        var lmo = brain.LensModeOverride;
        lmo.Enabled = ortho;
        if (ortho)
            lmo.DefaultMode = LensSettings.OverrideModes.Orthographic;
        brain.LensModeOverride = lmo;

        // Cinemachine doesn't always reset the output camera's projection
        // when the override is disabled — force it explicitly
        var cam = brain.GetComponent<UnityEngine.Camera>();
        if (cam != null)
            cam.orthographic = ortho;
    }

    private void UpdateButtonHighlights()
    {
        if (presetButtons == null) return;
        for (int i = 0; i < presetButtons.Length; i++)
        {
            if (presetButtons[i] == null) continue;
            var img = presetButtons[i].GetComponent<Image>();
            if (img != null)
                img.color = (i == _activePresetIndex) ? activeButtonColor : inactiveButtonColor;
        }
    }
}
