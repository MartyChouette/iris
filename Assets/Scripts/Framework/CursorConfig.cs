using UnityEngine;

/// <summary>
/// Assign cursor textures and hotspot positions from the Inspector.
/// Create via Assets → Create → Iris/Cursor Config, place in Resources/.
/// GlobalCursorManager loads this automatically.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Cursor Config")]
public class CursorConfig : ScriptableObject
{
    [System.Serializable]
    public struct CursorEntry
    {
        [Tooltip("Cursor texture (PNG, Read/Write enabled, Cursor type).")]
        public Texture2D texture;

        [Tooltip("Click-point on the texture in pixels from the top-left.")]
        public Vector2 hotspot;
    }

    [Header("Default")]
    public CursorEntry defaultCursor;

    [Header("Interact")]
    public CursorEntry interact;

    [Header("Watering")]
    public CursorEntry watering;

    [Header("Fridge")]
    public CursorEntry fridge;

    [Header("Phone")]
    public CursorEntry phone;

    [Header("Drawer")]
    public CursorEntry drawer;

    [Header("Drink")]
    public CursorEntry drink;

    [Header("Sponge")]
    public CursorEntry sponge;

    [Header("Grab")]
    public CursorEntry grab;

    [Header("Scissors")]
    public CursorEntry scissors;

    [Header("Swatter")]
    public CursorEntry swatter;

    [Header("Light")]
    public CursorEntry light;
}
