using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls a single drawer below the bookcase. Click to slide open,
/// revealing contents inside. Re-click or ESC to close.
/// Aggregates smell from stored items onto its own ReactableTag so
/// TidyScorer can detect smelly contents even when items are hidden.
/// </summary>
public class DrawerController : MonoBehaviour
{
    public enum State { Closed, Opening, Open, Closing }

    /// <summary>
    /// Which local axis the drawer/door slides along.
    /// Forward = local -Z (pull out), Right = local X, Up = local Y.
    /// Positive slideDistance moves in the negative axis direction (pull).
    /// Negative slideDistance moves in the positive axis direction (push/slide).
    /// HingeDown = rotates around bottom edge (flip-down cabinet door).
    /// </summary>
    public enum SlideAxis { Forward, Right, Up, HingeDown }

    [Header("Settings")]
    [Tooltip("Which local axis the drawer slides along, or HingeDown for flip-down doors.")]
    [SerializeField] private SlideAxis _slideAxis = SlideAxis.Forward;

    [Tooltip("For slide axes: distance the drawer slides. For HingeDown: open angle in degrees (default 90).")]
    [SerializeField] private float slideDistance = 0.3f;

    [Tooltip("For HingeDown: pivot at top edge instead of bottom (overhead cabinets).")]
    [SerializeField] private bool _hingeAtTop;

    [Tooltip("Time to open/close in seconds.")]
    [SerializeField] private float slideDuration = 0.3f;

    [Header("Contents")]
    [Tooltip("Root GameObject for drawer contents (activated on open).")]
    [SerializeField] private GameObject contentsRoot;

    [Header("Interior Surface")]
    [Tooltip("PlacementSurface inside the cubby. Items on this surface become private when door is closed.")]
    [SerializeField] private PlacementSurface _interiorSurface;

    [Header("Storage")]
    [Tooltip("Maximum number of PlaceableObjects that can be stored in this drawer.")]
    [SerializeField] private int _maxCapacity = 3;

    public State CurrentState { get; private set; } = State.Closed;
    public bool HasCapacity => _storedItems.Count < _maxCapacity;

    // ── Static registry ─────────────────────────────────────────────
    private static readonly List<DrawerController> s_all = new();
    public static IReadOnlyList<DrawerController> All => s_all;

    private readonly List<PlaceableObject> _storedItems = new();

    private Vector3 _closedPosition;
    private Quaternion _closedRotation;
    private Material _instanceMaterial;
    private Color _baseColor;
    private bool _isHovered;

    // Smell aggregation — drawer's own ReactableTag proxies stored items' smell
    private ReactableTag _drawerTag;

    private Coroutine _slideRoutine;

    private void OnEnable() { s_all.Add(this); }
    private void OnDisable()
    {
        s_all.Remove(this);
        if (_slideRoutine != null) { StopCoroutine(_slideRoutine); _slideRoutine = null; }
    }

    private void Awake()
    {
        // Guard against stale serialized 0 (field added after component existed in scene)
        if (_maxCapacity <= 0) _maxCapacity = 3;

        _closedPosition = transform.localPosition;
        _closedRotation = transform.localRotation;

        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            _instanceMaterial = rend.material;
            _baseColor = _instanceMaterial.color;
        }

        if (contentsRoot != null)
            contentsRoot.SetActive(false);

        // Cache or create the drawer's own ReactableTag for smell aggregation
        _drawerTag = GetComponent<ReactableTag>();
    }

    private void Start()
    {
        if (_interiorSurface == null) return;

        // Claim items physically sitting on the interior surface at startup.
        // Editor-placed items don't go through OnPlaced(), so LastPlacedSurface is null.
        ClaimItemsOnInteriorSurface();

        // Items already on interior surface start private if door is closed
        if (CurrentState == State.Closed)
            SetInteriorItemsPrivate(true);
    }

    /// <summary>
    /// Find all PlaceableObjects physically on the interior surface and claim them.
    /// Sets LastPlacedSurface so privacy toggling works, and ensures a ReactableTag exists.
    /// </summary>
    private void ClaimItemsOnInteriorSurface()
    {
        var all = PlaceableObject.All;
        for (int i = 0; i < all.Count; i++)
        {
            // Skip items already claimed by a surface
            if (all[i].LastPlacedSurface != null) continue;

            if (!_interiorSurface.ContainsWorldPoint(all[i].transform.position)) continue;

            // Claim this item for the interior surface
            all[i].SetLastPlacedSurface(_interiorSurface);

            // Ensure it has a ReactableTag
            var tag = all[i].GetComponent<ReactableTag>();
            if (tag == null)
                tag = all[i].gameObject.AddComponent<ReactableTag>();
        }
    }

    public void SetContentsRoot(GameObject root)
    {
        contentsRoot = root;
    }

    public void OnHoverEnter()
    {
        if (_isHovered) return;
        _isHovered = true;

        if (_instanceMaterial != null)
            _instanceMaterial.color = _baseColor * 1.2f;
    }

    public void OnHoverExit()
    {
        if (!_isHovered) return;
        _isHovered = false;

        if (_instanceMaterial != null)
            _instanceMaterial.color = _baseColor;
    }

    public void Open()
    {
        if (CurrentState != State.Closed) return;
        _slideRoutine = StartCoroutine(SlideRoutine(true));
    }

    public void Close()
    {
        if (CurrentState != State.Open) return;
        _slideRoutine = StartCoroutine(SlideRoutine(false));
    }

    private IEnumerator SlideRoutine(bool opening)
    {
        CurrentState = opening ? State.Opening : State.Closing;

        // Clear hover visual
        _isHovered = false;
        if (_instanceMaterial != null)
            _instanceMaterial.color = _baseColor;

        if (_slideAxis == SlideAxis.HingeDown)
            yield return HingeRoutine(opening);
        else
            yield return TranslateRoutine(opening);

        if (opening)
        {
            CurrentState = State.Open;
            CameraNudge.Nudge(0.15f); // subtle nudge when drawer thumps open
            SmokePoof.Spawn(transform.position, 0.08f);
            if (contentsRoot != null)
                contentsRoot.SetActive(true);
            PositionStoredItems();
            SetInteriorItemsPrivate(false);
        }
        else
        {
            CurrentState = State.Closed;
            HideStoredItems();
            if (contentsRoot != null)
                contentsRoot.SetActive(false);
            SetInteriorItemsPrivate(true);
        }
    }

    private IEnumerator TranslateRoutine(bool opening)
    {
        // Use pure local-space axis so the open position is always consistent
        Vector3 localSlideDir = _slideAxis switch
        {
            SlideAxis.Right => Vector3.right,
            SlideAxis.Up    => Vector3.up,
            _               => Vector3.forward
        };
        Vector3 openPosition = _closedPosition - localSlideDir * slideDistance;
        Vector3 startPos = transform.localPosition;
        Vector3 endPos = opening ? openPosition : _closedPosition;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Smoothstep(elapsed / slideDuration);
            transform.localPosition = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        transform.localPosition = endPos;
    }

    /// <summary>
    /// Rotates the door around its bottom edge (local X axis).
    /// slideDistance is used as the open angle in degrees (default 90).
    /// Pivot is the bottom edge of the door mesh.
    /// </summary>
    private IEnumerator HingeRoutine(bool opening)
    {
        // Simple local rotation — parent the door mesh under an empty at the hinge edge.
        // This component rotates around its own local origin, so position is untouched.
        float openAngle = Mathf.Abs(slideDistance) > 0.01f ? slideDistance : 90f;
        Quaternion openRotation = _closedRotation * Quaternion.AngleAxis(openAngle, Vector3.right);

        Quaternion startRot = transform.localRotation;
        Quaternion endRot = opening ? openRotation : _closedRotation;

        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Smoothstep(elapsed / slideDuration);
            transform.localRotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }

        // Snap to exact target to prevent drift
        transform.localRotation = endRot;
        if (!opening)
        {
            transform.localPosition = _closedPosition;
            transform.localRotation = _closedRotation;
        }
    }

    // ── Interior surface privacy ────────────────────────────────────

    /// <summary>Fired after a drawer opens or closes. Args: (DrawerController drawer, bool isClosed, PlaceableObject[] affectedItems).</summary>
    public static event System.Action<DrawerController, bool, PlaceableObject[]> OnDrawerPrivacyChanged;

    /// <summary>
    /// Toggle IsPrivate on all PlaceableObjects sitting on the interior surface.
    /// Called on open (private=false) and close (private=true).
    /// </summary>
    private void SetInteriorItemsPrivate(bool isPrivate)
    {
        if (_interiorSurface == null) return;

        var affected = new System.Collections.Generic.List<PlaceableObject>();
        var all = PlaceableObject.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null) continue;
            // Match by surface reference OR physical containment
            bool onSurface = all[i].LastPlacedSurface == _interiorSurface
                          || _interiorSurface.ContainsWorldPoint(all[i].transform.position);
            if (!onSurface) continue;

            var tag = all[i].GetComponent<ReactableTag>();
            if (tag != null)
            {
                tag.IsPrivate = isPrivate;
                all[i].SetLastPlacedSurface(_interiorSurface);
                affected.Add(all[i]);
            }
        }

        if (affected.Count > 0)
            OnDrawerPrivacyChanged?.Invoke(this, isPrivate, affected.ToArray());
    }

    /// <summary>
    /// Returns true if the given surface is this cubby's interior and the door is closed.
    /// ObjectGrabber can call this after placement to set the item private.
    /// </summary>
    public bool IsInteriorAndClosed(PlacementSurface surface)
    {
        return _interiorSurface != null
            && surface == _interiorSurface
            && CurrentState == State.Closed;
    }

    /// <summary>The interior PlacementSurface, if any.</summary>
    public PlacementSurface InteriorSurface => _interiorSurface;

    /// <summary>Count items currently on the interior surface (for capacity enforcement).</summary>
    public int InteriorItemCount
    {
        get
        {
            if (_interiorSurface == null) return 0;
            int count = 0;
            var all = PlaceableObject.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].LastPlacedSurface == _interiorSurface) count++;
            return count;
        }
    }

    /// <summary>True if the cubby interior has room for another item.</summary>
    public bool HasInteriorCapacity => InteriorItemCount < _maxCapacity;

    /// <summary>
    /// Find the DrawerController that owns the given surface as its interior.
    /// Returns null if no drawer claims it.
    /// </summary>
    public static DrawerController FindByInteriorSurface(PlacementSurface surface)
    {
        if (surface == null) return null;
        for (int i = 0; i < s_all.Count; i++)
            if (s_all[i]._interiorSurface == surface)
                return s_all[i];
        return null;
    }

    // ── Item storage ────────────────────────────────────────────────

    /// <summary>Store a PlaceableObject inside this drawer. Hides it and marks ReactableTag as private.</summary>
    public void StoreItem(PlaceableObject item)
    {
        if (item == null || _storedItems.Contains(item)) return;
        if (!HasCapacity)
        {
            Debug.Log($"[DrawerController] Drawer full — cannot store {item.name}.");
            return;
        }

        _storedItems.Add(item);
        item.gameObject.SetActive(false);

        if (contentsRoot != null)
            item.transform.SetParent(contentsRoot.transform, true);

        var tag = item.GetComponent<ReactableTag>();
        if (tag != null)
        {
            tag.IsPrivate = true;
            tag.IsActive = false;
        }

        RecalculateDrawerSmell();

        Debug.Log($"[DrawerController] Stored {item.name}. Count: {_storedItems.Count}/{_maxCapacity}");
    }

    /// <summary>Remove a PlaceableObject from storage (called on pickup from drawer).</summary>
    public void RemoveStoredItem(PlaceableObject item)
    {
        if (item == null || !_storedItems.Remove(item)) return;

        item.gameObject.SetActive(true);
        item.transform.SetParent(null);

        var tag = item.GetComponent<ReactableTag>();
        if (tag != null)
        {
            tag.IsPrivate = false;
            tag.IsActive = true;
        }

        RecalculateDrawerSmell();

        Debug.Log($"[DrawerController] Removed {item.name} from storage. Count: {_storedItems.Count}/{_maxCapacity}");
    }

    /// <summary>
    /// Sum up SmellAmount from all stored items and set it on the drawer's
    /// own ReactableTag. Smell travels through drawers — hiding something
    /// smelly doesn't hide the smell.
    /// </summary>
    private void RecalculateDrawerSmell()
    {
        if (_drawerTag == null) return;

        float totalSmell = 0f;
        for (int i = 0; i < _storedItems.Count; i++)
        {
            if (_storedItems[i] == null) continue;
            var tag = _storedItems[i].GetComponent<ReactableTag>();
            if (tag != null)
                totalSmell += tag.SmellAmount;
        }

        _drawerTag.SmellAmount = totalSmell;
    }

    private void PositionStoredItems()
    {
        if (contentsRoot == null) return;

        for (int i = 0; i < _storedItems.Count; i++)
        {
            var item = _storedItems[i];
            if (item == null) continue;

            item.gameObject.SetActive(true);
            // Grid layout inside drawer
            float x = (i % 3 - 1) * 0.12f;
            float z = (i / 3) * 0.12f;
            item.transform.localPosition = new Vector3(x, 0.02f, z);
        }
    }

    private void HideStoredItems()
    {
        for (int i = 0; i < _storedItems.Count; i++)
        {
            if (_storedItems[i] != null)
                _storedItems[i].gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (_instanceMaterial != null)
            Destroy(_instanceMaterial);
    }

    private static float Smoothstep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
