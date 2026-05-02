using UnityEngine;

/// <summary>
/// Central configuration for placement preview visuals (shadow disc, ghost
/// preview, silhouette style). Place on any GameObject in the scene —
/// ObjectGrabber reads from the singleton at runtime.
/// </summary>
public class PlacementPreviewSettings : MonoBehaviour
{
    public static PlacementPreviewSettings Instance { get; private set; }

    public enum GhostStyle { Filled, Outline }

    [Header("Shadow Disc")]
    [Tooltip("Show the circular shadow disc under the placement position.")]
    public bool showShadow = true;

    [Tooltip("Shadow color when placement is valid.")]
    public Color shadowValidColor = new Color(0.25f, 0.9f, 0.3f, 0.75f);

    [Tooltip("Shadow color when placement is blocked.")]
    public Color shadowInvalidColor = new Color(0.95f, 0.18f, 0.2f, 0.75f);

    [Header("Ghost Preview")]
    [Tooltip("Show a translucent duplicate of the held item at the placement position.")]
    public bool showGhost = true;

    [Tooltip("Ghost visual style.")]
    public GhostStyle ghostStyle = GhostStyle.Filled;

    [Tooltip("Ghost tint when placement is valid.")]
    [ColorUsage(true, false)]
    public Color ghostValidColor = new Color(0.5f, 0.95f, 0.55f, 0.35f);

    [Tooltip("Ghost tint when placement is blocked.")]
    [ColorUsage(true, false)]
    public Color ghostInvalidColor = new Color(0.95f, 0.2f, 0.2f, 0.35f);

    [Header("Outline (when Ghost Style = Outline)")]
    [Tooltip("Outline thickness in world units.")]
    [Range(0.001f, 0.05f)]
    public float outlineThickness = 0.005f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
