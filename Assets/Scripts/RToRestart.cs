using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;   // <-- for EventSystem.current
using UnityEngine.UI;             // <-- legacy InputField
using TMPro;                      // <-- TMP_InputField

public class RToRestart : MonoBehaviour
{
    [Header("Manual Restart")]
    public KeyCode manualRestartKey = KeyCode.R;

    [Header("Idle Restart")]
    [Tooltip("Enable automatic restart after a period of true inactivity.")]
    public bool enableIdleRestart = true;

    [Tooltip("Seconds of inactivity required before restarting.")]
    public float idleRestartAfterSeconds = 120f;

    [Tooltip("If off, idle time accumulates even when the app is unfocused/minimized.")]
    public bool ignoreWhenUnfocused = true;

    // ── Runtime state ──
    private bool interactedSinceLoad = false; // gate so we don't re-restart untouched scenes
    private float idleTimer = 0f;
    private Vector3 lastMousePos;

    void Awake()
    {
        lastMousePos = Input.mousePosition;
        idleTimer = 0f;
        interactedSinceLoad = false; // only starts counting after first real interaction
    }

    void Update()
    {
        // 1) Manual restart (but NOT while typing in a text field)
        if (Input.GetKeyDown(manualRestartKey))
        {
            if (!IsTypingInTextField())
            {
                ReloadScene();
            }
            return;
        }

        // 2) Track any meaningful player interaction
        if (DetectInteraction())
        {
            interactedSinceLoad = true;
            idleTimer = 0f; // reset idle on any interaction
        }

        // 3) Idle countdown (only after the first interaction this load)
        if (enableIdleRestart && interactedSinceLoad)
        {
            if (!ignoreWhenUnfocused || Application.isFocused)
            {
                idleTimer += Time.unscaledDeltaTime;
                if (idleTimer >= idleRestartAfterSeconds)
                    ReloadScene();
            }
        }
    }

    /// <summary>
    /// True if the player is actively focused on a UI text field (TMP or legacy).
    /// </summary>
    private bool IsTypingInTextField()
    {
        if (EventSystem.current == null) return false;

        var go = EventSystem.current.currentSelectedGameObject;
        if (go == null) return false;

        // TextMeshPro input field
        if (go.TryGetComponent<TMP_InputField>(out var tmp))
            return tmp.enabled && tmp.interactable && tmp.isFocused;

        // Legacy uGUI input field
        if (go.TryGetComponent<InputField>(out var ui))
            return ui.enabled && ui.interactable && ui.isFocused;

        // (Optional) If you later use UI Toolkit, you'd check focus on a TextField there.
        return false;
    }

    /// <summary>Call this from other scripts to count an interaction.</summary>
    public void RegisterInteraction()
    {
        interactedSinceLoad = true;
        idleTimer = 0f;
    }

    private void ReloadScene()
    {
        Scene active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.name);
    }

    // Detects "activity" using the old Input API for broad coverage.
    private bool DetectInteraction()
    {
        // Any key/button pressed this frame
        if (Input.anyKeyDown) return true;

        // Mouse buttons
        if (Input.GetMouseButtonDown(0) ||
            Input.GetMouseButtonDown(1) ||
            Input.GetMouseButtonDown(2)) return true;

        // Mouse movement
        Vector3 mp = Input.mousePosition;
        if ((mp - lastMousePos).sqrMagnitude > 0.5f)
        {
            lastMousePos = mp;
            return true;
        }
        lastMousePos = mp;

        // Scroll wheel
        if (Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.001f) return true;

        // Basic gamepad/keyboard axes (Horizontal/Vertical)
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.001f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.001f) return true;

        // Touch
        if (Input.touchCount > 0) return true;

        return false;
    }
}
