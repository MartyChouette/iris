using UnityEngine;

/// <summary>
/// Clickable light switch that toggles one or more lights on/off.
/// Placed on the switch mesh (must be on Placeables layer for ObjectGrabber click detection).
/// ObjectGrabber routes clicks here when no PlaceableObject or DrawerController is found.
/// </summary>
public class LightSwitch : MonoBehaviour
{
    [Header("Lights")]
    [Tooltip("Lights controlled by this switch.")]
    [SerializeField] private Light[] _lights;

    [Header("State")]
    [Tooltip("Whether the light starts on. False = room starts dark, player must click switch.")]
    [SerializeField] private bool _startsOn = false;

    [Header("Visual")]
    [Tooltip("Renderer for the switch plate (optional — tints to show on/off state).")]
    [SerializeField] private Renderer _switchRenderer;

    [Tooltip("Color when on.")]
    [SerializeField] private Color _onColor = new Color(0.95f, 0.9f, 0.7f);

    [Tooltip("Color when off.")]
    [SerializeField] private Color _offColor = new Color(0.3f, 0.3f, 0.3f);

    [Header("Audio")]
    [Tooltip("SFX played when toggled.")]
    [SerializeField] private AudioClip _toggleSFX;

    private Material _instanceMat;
    private bool _isOn;

    public bool IsOn => _isOn;

    private void Awake()
    {
        _isOn = _startsOn;

        if (_switchRenderer != null && _switchRenderer.sharedMaterial != null)
        {
            _instanceMat = new Material(_switchRenderer.sharedMaterial);
            _switchRenderer.material = _instanceMat;
        }

        ApplyState();
    }

    /// <summary>Toggle the light on/off. Called by ObjectGrabber on click.</summary>
    public void Toggle()
    {
        _isOn = !_isOn;
        ApplyState();

        AudioManager.Instance?.PlaySFX(_toggleSFX);
        Debug.Log($"[LightSwitch] {name} toggled {(_isOn ? "ON" : "OFF")}.");
    }

    private void ApplyState()
    {
        if (_lights != null)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                    _lights[i].enabled = _isOn;
            }
        }

        if (_instanceMat != null)
            _instanceMat.color = _isOn ? _onColor : _offColor;
    }

    private void OnDestroy()
    {
        if (_instanceMat != null)
            Destroy(_instanceMat);
    }
}
