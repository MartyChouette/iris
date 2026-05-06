using UnityEngine;

/// <summary>
/// Press F1 to toggle all UI for clean trailer recording.
/// Hides every Canvas in the scene. Press again to restore.
/// Place on any always-active GameObject (e.g. ApartmentManager).
/// </summary>
public class TrailerMode : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Key to toggle UI visibility.")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.F10;

    [Tooltip("Also hide the cursor when UI is hidden.")]
    [SerializeField] private bool _hideCursor = true;

    private bool _uiHidden;
    private Canvas[] _cachedCanvases;
    private bool[] _cachedStates;
    private CursorLockMode _savedCursorLock;
    private bool _savedCursorVisible;

    private void Update()
    {
        if (Input.GetKeyDown(_toggleKey))
            Toggle();
    }

    private void Toggle()
    {
        _uiHidden = !_uiHidden;

        if (_uiHidden)
        {
            // Cache and hide all canvases
            _cachedCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            _cachedStates = new bool[_cachedCanvases.Length];
            for (int i = 0; i < _cachedCanvases.Length; i++)
            {
                _cachedStates[i] = _cachedCanvases[i].enabled;
                _cachedCanvases[i].enabled = false;
            }

            if (_hideCursor)
            {
                _savedCursorLock = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Confined;
            }

            Debug.Log("[TrailerMode] UI hidden — press " + _toggleKey + " to restore");
        }
        else
        {
            // Restore canvases
            if (_cachedCanvases != null)
            {
                for (int i = 0; i < _cachedCanvases.Length; i++)
                {
                    if (_cachedCanvases[i] != null)
                        _cachedCanvases[i].enabled = _cachedStates[i];
                }
                _cachedCanvases = null;
                _cachedStates = null;
            }

            if (_hideCursor)
            {
                Cursor.visible = _savedCursorVisible;
                Cursor.lockState = _savedCursorLock;
            }

            Debug.Log("[TrailerMode] UI restored");
        }
    }
}
