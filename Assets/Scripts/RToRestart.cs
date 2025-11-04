using UnityEngine;
using UnityEngine.SceneManagement;

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
        // 1) Manual restart
        if (Input.GetKeyDown(manualRestartKey))
        {
            ReloadScene();
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
    /// Call this from other scripts when you consider something an interaction
    /// (e.g., UI buttons, controller events using the new Input System).
    /// </summary>
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
    // Works fine alongside the new Input System; you can also call RegisterInteraction() from your input actions.
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
