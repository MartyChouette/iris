/**
 * @file ContextCursor.cs
 * @brief ContextCursor script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
*
 * @ingroup ui
 */

using UnityEngine;
/**
 * @class CursorContext
 * @brief CursorContext component.
 * @details
 * Responsibilities:
 * - (Documented) See fields and methods below.
 *
 * Unity lifecycle:
 * - Awake(): cache references / validate setup.
 * - OnEnable()/OnDisable(): hook/unhook events.
 * - Update(): per-frame behavior (if any).
 *
 * Gotchas:
 * - Keep hot paths allocation-free (Update/cuts/spawns).
 * - Prefer event-driven UI updates over per-frame string building.
 *
 * @ingroup ui
 */

public class CursorContext : MonoBehaviour
{
    [Header("Cursor Textures")]
    public Texture2D defaultCursor;
    public Texture2D hoverCursor;

    [Header("Settings")]
    // We changed this from a single string to an array (string[])
    public string[] targetTags;

    public Vector2 hotSpot = Vector2.zero;

    private bool isHovering = false;
    private Camera mainCamera;

    /// <summary>Returns whichever cursor texture is currently active (hover or default).</summary>
    public Texture2D ActiveCursorTexture => isHovering && hoverCursor != null ? hoverCursor : defaultCursor;

    void Start()
    {
        // Defer to GlobalCursorManager if present (it handles all scenes)
        if (GlobalCursorManager.Instance != null)
        {
            enabled = false;
            return;
        }

        mainCamera = Camera.main;
        SetCursor(false);
    }

    void Update()
    {
        CheckUnderMouse();
    }

    void CheckUnderMouse()
    {
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(Input.mousePosition) : mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            // Check the object we hit against ALL our allowed tags
            if (HasValidTag(hit.collider.gameObject))
            {
                if (!isHovering)
                {
                    SetCursor(true);
                }
                return; // Found a match, stop looking
            }
        }

        // If we hit nothing, or hit an object with no matching tag
        if (isHovering)
        {
            SetCursor(false);
        }
    }

    // Helper function to check the list of tags
    bool HasValidTag(GameObject obj)
    {
        foreach (string tag in targetTags)
        {
            if (obj.CompareTag(tag))
            {
                return true;
            }
        }
        return false;
    }

    void SetCursor(bool hovering)
    {
        isHovering = hovering;

        if (isHovering)
        {
            Cursor.SetCursor(hoverCursor, hotSpot, CursorMode.Auto);
        }
        else
        {
            Cursor.SetCursor(defaultCursor, Vector2.zero, CursorMode.Auto);
        }
    }
}