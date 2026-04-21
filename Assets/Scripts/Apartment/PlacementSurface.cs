using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component placed on tables, shelves, and walls to define valid placement bounds.
/// ObjectGrabber raycasts against the auto-generated trigger collider on the Surfaces layer
/// and uses ProjectOntoSurface / SnapToGrid for positioning.
/// </summary>
public class PlacementSurface : MonoBehaviour
{
    // ── Static registry (avoids FindObjectsByType) ──────────────────
    private static readonly List<PlacementSurface> s_all = new();
    public static IReadOnlyList<PlacementSurface> All => s_all;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    public enum SurfaceAxis { Up, Forward }

    /// <summary>
    /// How much a placed item's effects are amplified by the surface it sits on.
    /// Applies to date affection reactions, smell accumulation, tidiness scoring,
    /// Nema's gaze selection, and reveal wave ordering. Values 1–3 are meant for
    /// normal authoring; 4 and 5 are reserved for future "secret" surfaces.
    /// </summary>
    public enum SurfaceEffectMultiplier
    {
        [InspectorName("1× (normal)")]      OneX   = 1,
        [InspectorName("2× (prominent)")]   TwoX   = 2,
        [InspectorName("3× (centerpiece)")] ThreeX = 3,
        [InspectorName("4× (reserved — secret)")] FourX  = 4,
        [InspectorName("5× (reserved — secret)")] FiveX  = 5,
    }

    public struct SurfaceHitResult
    {
        public Vector3 worldPosition;
        public Vector3 surfaceNormal;
        public bool isVertical;
        public PlacementSurface surface;
    }

    [Header("Surface Bounds")]
    [Tooltip("Local-space bounds of the placement area (center + extents).")]
    [SerializeField] private Bounds localBounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));

    [Header("Orientation")]
    [Tooltip("Up for tables/shelves (normal = transform.up), Forward for walls (normal = transform.forward).")]
    [SerializeField] private SurfaceAxis normalAxis = SurfaceAxis.Up;

    [Tooltip("Layer index for the auto-generated raycast trigger.")]
    [SerializeField] private int surfaceLayerIndex = 0;

    [Tooltip("Mark as floor — trash items can be placed here.")]
    [SerializeField] private bool _isFloor;

    [Header("Effect Multiplier")]
    [Tooltip("Amplifies the impact of items placed here. Applies to date reactions, smell accumulation, tidiness scoring, Nema's gaze, and reveal-wave ordering.")]
    [SerializeField] private SurfaceEffectMultiplier _effectMultiplier = SurfaceEffectMultiplier.OneX;

    /// <summary>True if this surface is marked as a floor in the Inspector.</summary>
    public bool IsFloor => _isFloor;

    /// <summary>Integer multiplier (1-5) applied to effects of items sitting on this surface.</summary>
    public int EffectMultiplier => (int)_effectMultiplier;

    /// <summary>World-space surface normal based on orientation axis.</summary>
    public Vector3 SurfaceNormal =>
        normalAxis == SurfaceAxis.Up ? transform.up : transform.forward;

    /// <summary>True if the surface is roughly vertical (wall).</summary>
    public bool IsVertical =>
        Vector3.Dot(SurfaceNormal, Vector3.up) < 0.3f;

    private void Awake()
    {
        BuildTrigger();
    }

    private void BuildTrigger()
    {
        var triggerGO = new GameObject("SurfaceTrigger");
        triggerGO.transform.SetParent(transform, false);
        triggerGO.layer = surfaceLayerIndex;

        // Counteract negative scale on parent — BoxCollider can't handle it
        Vector3 parentScale = transform.lossyScale;
        Vector3 localScale = new Vector3(
            parentScale.x < 0f ? -1f : 1f,
            parentScale.y < 0f ? -1f : 1f,
            parentScale.z < 0f ? -1f : 1f);
        triggerGO.transform.localScale = localScale;

        var box = triggerGO.AddComponent<BoxCollider>();
        box.isTrigger = true;

        Vector3 center = localBounds.center;
        Vector3 size = localBounds.size;
        // Ensure positive size
        size.x = Mathf.Abs(size.x);
        size.y = Mathf.Abs(size.y);
        size.z = Mathf.Abs(size.z);

        if (normalAxis == SurfaceAxis.Up)
            size.y = Mathf.Max(size.y, 0.05f);
        else
            size.z = Mathf.Max(size.z, 0.15f);

        box.center = center;
        box.size = size;
    }

    // ── Tangent helpers ──────────────────────────────────────────────

    /// <summary>
    /// Returns the two local-space axis indices that are tangent to the surface.
    /// Up → X(0),Z(2); Forward → X(0),Y(1).
    /// </summary>
    private void GetTangentAxes(out int a, out int b, out int normalIdx)
    {
        if (normalAxis == SurfaceAxis.Up)
        {
            a = 0; b = 2; normalIdx = 1; // X, Z tangent; Y normal
        }
        else
        {
            a = 0; b = 1; normalIdx = 2; // X, Y tangent; Z normal
        }
    }

    // ── Projection / Snapping ────────────────────────────────────────

    private const float EdgeMargin = 0.03f;

    /// <summary>
    /// Project a world point onto this surface, clamped within bounds (with edge margin).
    /// Returns the clamped world position at the surface face.
    /// </summary>
    /// <summary>
    /// Project a world point onto this surface. For walls, pass viewOrigin
    /// (camera position) to reliably pick the camera-facing side instead
    /// of flickering between faces when the point is near the wall center.
    /// </summary>
    public SurfaceHitResult ProjectOntoSurface(Vector3 worldPoint, Vector3? viewOrigin = null)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        GetTangentAxes(out int a, out int b, out int n);

        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        // Clamp tangent axes within bounds with edge margin
        float marginA = (max[a] - min[a] > EdgeMargin * 4f) ? EdgeMargin : 0f;
        float marginB = (max[b] - min[b] > EdgeMargin * 4f) ? EdgeMargin : 0f;
        local[a] = Mathf.Clamp(local[a], min[a] + marginA, max[a] - marginA);
        local[b] = Mathf.Clamp(local[b], min[b] + marginB, max[b] - marginB);

        // For walls, pick the face the camera can see.
        // Uses dot product between wall normal and camera→wall direction
        // so it's stable regardless of camera height or angle.
        bool frontFace = true;
        if (IsVertical)
        {
            if (viewOrigin.HasValue)
            {
                // Which face does the camera see? Check if camera is on the
                // positive-normal side of the wall surface (world space).
                Vector3 wallCenter = transform.TransformPoint(localBounds.center);
                Vector3 camToWall = wallCenter - viewOrigin.Value;
                frontFace = Vector3.Dot(camToWall, SurfaceNormal) < 0f;
            }
            else
            {
                float front = localBounds.center[n] + localBounds.extents[n];
                float back = localBounds.center[n] - localBounds.extents[n];
                frontFace = Mathf.Abs(local[n] - front) <= Mathf.Abs(local[n] - back);
            }
        }

        local[n] = frontFace
            ? localBounds.center[n] + localBounds.extents[n]
            : localBounds.center[n] - localBounds.extents[n];

        return new SurfaceHitResult
        {
            worldPosition = transform.TransformPoint(local),
            surfaceNormal = frontFace ? SurfaceNormal : -SurfaceNormal,
            isVertical = IsVertical,
            surface = this
        };
    }

    /// <summary>
    /// Snap a world point to a grid on this surface's tangent plane, clamped within bounds (with edge margin).
    /// </summary>
    public Vector3 SnapToGrid(Vector3 worldPoint, float gridSize, Vector3? viewOrigin = null)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        GetTangentAxes(out int a, out int b, out int n);

        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        float marginA = (max[a] - min[a] > EdgeMargin * 4f) ? EdgeMargin : 0f;
        float marginB = (max[b] - min[b] > EdgeMargin * 4f) ? EdgeMargin : 0f;

        // Convert world-space grid size to local space so the grid is consistent
        // regardless of the surface object's scale.
        Vector3 scale = transform.lossyScale;
        float localGridA = Mathf.Abs(scale[a]) > 0.001f ? gridSize / Mathf.Abs(scale[a]) : gridSize;
        float localGridB = Mathf.Abs(scale[b]) > 0.001f ? gridSize / Mathf.Abs(scale[b]) : gridSize;

        local[a] = Mathf.Clamp(Mathf.Round(local[a] / localGridA) * localGridA, min[a] + marginA, max[a] - marginA);
        local[b] = Mathf.Clamp(Mathf.Round(local[b] / localGridB) * localGridB, min[b] + marginB, max[b] - marginB);

        // Use same dot-product face detection as ProjectOntoSurface
        bool frontFace = true;
        if (IsVertical)
        {
            if (viewOrigin.HasValue)
            {
                Vector3 wallCenter = transform.TransformPoint(localBounds.center);
                Vector3 camToWall = wallCenter - viewOrigin.Value;
                frontFace = Vector3.Dot(camToWall, SurfaceNormal) < 0f;
            }
            else
            {
                float front = localBounds.center[n] + localBounds.extents[n];
                float back = localBounds.center[n] - localBounds.extents[n];
                frontFace = Mathf.Abs(local[n] - front) <= Mathf.Abs(local[n] - back);
            }
        }

        local[n] = frontFace
            ? localBounds.center[n] + localBounds.extents[n]
            : localBounds.center[n] - localBounds.extents[n];

        return transform.TransformPoint(local);
    }

    // ── Containment (axis-agnostic) ──────────────────────────────────

    /// <summary>
    /// Check whether a world-space point falls within this surface's tangent bounds.
    /// </summary>
    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        GetTangentAxes(out int a, out int b, out _);

        return Mathf.Abs(local[a] - localBounds.center[a]) <= localBounds.extents[a]
            && Mathf.Abs(local[b] - localBounds.center[b]) <= localBounds.extents[b];
    }

    /// <summary>
    /// Clamp a world-space point to the nearest valid position on this surface (with edge margin).
    /// </summary>
    public Vector3 ClampToSurface(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        GetTangentAxes(out int a, out int b, out int n);

        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        float marginA = (max[a] - min[a] > EdgeMargin * 4f) ? EdgeMargin : 0f;
        float marginB = (max[b] - min[b] > EdgeMargin * 4f) ? EdgeMargin : 0f;
        local[a] = Mathf.Clamp(local[a], min[a] + marginA, max[a] - marginA);
        local[b] = Mathf.Clamp(local[b], min[b] + marginB, max[b] - marginB);

        // Preserve the face the point is nearest to (for walls)
        bool frontFace = true;
        if (IsVertical)
        {
            float front = localBounds.center[n] + localBounds.extents[n];
            float back = localBounds.center[n] - localBounds.extents[n];
            frontFace = Mathf.Abs(local[n] - front) <= Mathf.Abs(local[n] - back);
        }

        local[n] = frontFace
            ? localBounds.center[n] + localBounds.extents[n]
            : localBounds.center[n] - localBounds.extents[n];

        return transform.TransformPoint(local);
    }

    /// <summary>
    /// The world-space Y of the surface top (for horizontal surfaces).
    /// </summary>
    public float SurfaceY =>
        transform.TransformPoint(new Vector3(0f, localBounds.center.y + localBounds.extents.y, 0f)).y;

    // ── Book edge detection ───────────────────────────────────────────

    /// <summary>
    /// Returns the local-space axis index of the longer tangent dimension.
    /// This is the "shelf run" — its min/max are the left/right edges.
    /// </summary>
    public int GetLengthAxis()
    {
        GetTangentAxes(out int a, out int b, out _);
        Vector3 size = localBounds.size;
        return Mathf.Abs(size[a]) >= Mathf.Abs(size[b]) ? a : b;
    }

    /// <summary>
    /// True if a world-space point is near the left or right edge of the
    /// surface's length axis. edgeThreshold is in world-space meters.
    /// </summary>
    public bool IsNearLengthEdge(Vector3 worldPoint, float edgeThreshold)
    {
        if (IsVertical) return false;
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        int axis = GetLengthAxis();
        float min = localBounds.min[axis];
        float max = localBounds.max[axis];
        float scale = Mathf.Abs(transform.lossyScale[axis]);
        float localThreshold = scale > 0.001f ? edgeThreshold / scale : edgeThreshold;
        return local[axis] <= min + localThreshold || local[axis] >= max - localThreshold;
    }

    // ── Static utility ─────────────────────────────────────────────────

    /// <summary>
    /// Find the nearest PlacementSurface to a world position.
    /// Optionally skip vertical or horizontal surfaces.
    /// </summary>
    public static PlacementSurface FindNearest(Vector3 worldPos, bool skipVertical = false, bool skipHorizontal = false)
    {
        PlacementSurface best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < s_all.Count; i++)
        {
            var surface = s_all[i];
            if (surface == null) continue;
            if (skipVertical && surface.IsVertical) continue;
            if (skipHorizontal && !surface.IsVertical) continue;

            Vector3 clamped = surface.ClampToSurface(worldPos);
            float dist = (clamped - worldPos).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = surface;
            }
        }

        return best;
    }

    // ── Gizmo ────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(localBounds.center, localBounds.size);
        Gizmos.DrawWireCube(localBounds.center, localBounds.size);

        // Normal direction arrow
        Gizmos.color = Color.cyan;
        Vector3 center = localBounds.center;
        Vector3 normalDir = normalAxis == SurfaceAxis.Up ? Vector3.up : Vector3.forward;
        Gizmos.DrawRay(center, normalDir * 0.5f);
    }
}
