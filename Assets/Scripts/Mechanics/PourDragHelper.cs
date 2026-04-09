using UnityEngine;

/// <summary>
/// Shared drag-to-pour input helper used by WateringManager and SimpleDrinkManager.
/// Click to start, drag mouse down to control pour rate. The further down
/// the mouse moves from the click origin, the faster the pour.
/// Exposes PourRate (0-1) and TiltAngle (degrees) for cursor rotation.
/// </summary>
public static class PourDragHelper
{
    /// <summary>Normalized pour rate based on drag distance (0 = not pouring, 1 = max).</summary>
    public static float PourRate { get; private set; }

    /// <summary>Cursor tilt angle in degrees (0 = upright, 90 = fully tipped).</summary>
    public static float TiltAngle { get; private set; }

    /// <summary>True while the player is actively dragging to pour.</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>Screen-space position where the player clicked to begin pouring.</summary>
    public static Vector2 ClickOrigin { get; private set; }

    private static bool _started;

    // How many pixels of downward drag = max pour rate
    private const float MaxDragPixels = 200f;
    private const float MaxTiltDegrees = 90f;

    /// <summary>Call once when the player clicks to begin pouring.</summary>
    public static void Begin()
    {
        ClickOrigin = IrisInput.CursorPosition;
        _started = true;
        IsDragging = true;
        PourRate = 0f;
        TiltAngle = 0f;
    }

    /// <summary>Call every frame while pouring. Returns the current pour rate (0-1).</summary>
    public static float UpdateDrag()
    {
        if (!_started)
        {
            PourRate = 0f;
            TiltAngle = 0f;
            return 0f;
        }

        float currentY = IrisInput.CursorPosition.y;
        float dragDown = Mathf.Max(ClickOrigin.y - currentY, 0f);
        float t = Mathf.Clamp01(dragDown / MaxDragPixels);

        // Ease-in curve so small movements give fine control
        PourRate = t * t;
        TiltAngle = t * MaxTiltDegrees;
        return PourRate;
    }

    /// <summary>Call when the player releases the mouse.</summary>
    public static void End()
    {
        _started = false;
        IsDragging = false;
        PourRate = 0f;
        TiltAngle = 0f;
    }
}
