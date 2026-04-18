using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Handles mouse input for wiping the spill surface. Raycasts onto the
/// SpillSurface quad, moves the sponge visual, and updates the progress text.
/// Uses inline InputActions (same pattern as MarkerController).
/// </summary>
public class WipeController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The SpillSurface to wipe. Auto-finds in scene if null.")]
    [SerializeField] private SpillSurface spillSurface;

    [Tooltip("Transform for the sponge visual that follows the mouse.")]
    [SerializeField] private Transform spongeVisual;

    [Tooltip("TMP_Text displaying the clean percentage.")]
    [SerializeField] private TMP_Text progressText;

    [Tooltip("Camera used for raycasting. Auto-finds MainCamera if null.")]
    [SerializeField] private Camera cam;

    [Header("Wipe Settings")]
    [Tooltip("Wipe brush radius in UV space (0-1). Larger = bigger wipe area.")]
    [SerializeField] private float wipeRadius = 0.06f;

    [Tooltip("Layer mask for the spill surface.")]
    [SerializeField] private LayerMask spillLayer;

    [Tooltip("Offset above the surface for the sponge visual.")]
    [SerializeField] private float surfaceOffset = 0.005f;

    private InputAction _mousePosition;
    private InputAction _mouseClick;

    // ─── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (spillSurface == null)
            spillSurface = Object.FindAnyObjectByType<SpillSurface>();

        // Inline InputAction fallback (same pattern as MarkerController)
        _mousePosition = new InputAction("MousePos", InputActionType.Value, "<Mouse>/position");
        _mouseClick = new InputAction("MouseClick", InputActionType.Button, "<Mouse>/leftButton");

        if (spongeVisual != null)
            spongeVisual.gameObject.SetActive(false);

        UpdateProgressText();
    }

    private void OnEnable()
    {
        _mousePosition.Enable();
        _mouseClick.Enable();
    }

    private void OnDestroy()
    {
        _mousePosition?.Dispose();
        _mouseClick?.Dispose();
        _mousePosition = null;
        _mouseClick = null;
    }

    private bool _cursorHidden;

    private void Update()
    {
        if (spillSurface == null) return;

        Vector2 screenPos = _mousePosition.ReadValue<Vector2>();
        Ray ray = cam.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, spillLayer))
        {
            Vector3 pos = hit.point + hit.normal * surfaceOffset;

            if (spongeVisual != null)
            {
                spongeVisual.position = pos;
                if (!spongeVisual.gameObject.activeSelf)
                    spongeVisual.gameObject.SetActive(true);
            }

            // Hide system cursor while over the spill — the sponge visual IS the cursor
            if (!_cursorHidden)
            {
                Cursor.visible = false;
                GlobalCursorManager.HideCursorForSponge(true);
                _cursorHidden = true;
            }

            // Wipe while mouse is held
            if (_mouseClick.IsPressed())
            {
                spillSurface.Wipe(hit.textureCoord, wipeRadius);
                UpdateProgressText();
            }
        }
        else
        {
            if (spongeVisual != null && spongeVisual.gameObject.activeSelf)
                spongeVisual.gameObject.SetActive(false);

            // Restore cursor when not over the spill
            if (_cursorHidden)
            {
                Cursor.visible = true;
                GlobalCursorManager.HideCursorForSponge(false);
                _cursorHidden = false;
            }
        }
    }

    private void OnDisable()
    {
        _mousePosition?.Disable();
        _mouseClick?.Disable();

        // Always restore cursor when the wipe controller is disabled
        if (_cursorHidden)
        {
            Cursor.visible = true;
            _cursorHidden = false;
        }
    }

    // ─── Internals ───────────────────────────────────────────────────

    private void UpdateProgressText()
    {
        if (progressText == null) return;

        int pct = Mathf.RoundToInt(spillSurface.CleanPercent * 100f);
        progressText.SetText("Clean: {0}%", pct);
    }
}
