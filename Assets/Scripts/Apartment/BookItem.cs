using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight companion to PlaceableObject for books on shelves.
/// References a BookDefinition and optionally drops a hidden item on first pickup.
/// </summary>
public class BookItem : MonoBehaviour
{
    [Header("Book Content")]
    [Tooltip("The book definition (title, pages, hidden item info).")]
    [SerializeField] private BookDefinition _definition;

    [Header("Smart Placement")]
    [Tooltip("Auto-orient: upright at shelf edges or next to upright books, flat everywhere else. Flat books stack.")]
    [SerializeField] private bool _useSmartPlacement = true;

    [Tooltip("Extra rotation applied to the upright pose. Adjust until the book faces correctly.")]
    [SerializeField] private Vector3 _uprightOffset = Vector3.zero;

    [Tooltip("Extra rotation applied to the flat pose.")]
    [SerializeField] private Vector3 _flatOffset = new Vector3(-90f, 0f, 0f);

    [Header("Hidden Item")]
    [Tooltip("Optional prefab dropped at the book's position on first pickup.")]
    [SerializeField] private GameObject _hiddenItemPrefab;

    [Tooltip("Minimum day number before the hidden item can appear (0 = always).")]
    [SerializeField] private int _hiddenItemMinDay;

    public BookDefinition Definition => _definition;
    public bool UseSmartPlacement => _useSmartPlacement;
    public Vector3 UprightOffset => _uprightOffset;
    public Vector3 FlatOffset => _flatOffset;

    /// <summary>True if this book is currently standing upright (spine vertical).</summary>
    public bool IsUpright => Vector3.Dot(transform.up, Vector3.up) > 0.7f;

    /// <summary>True if this book was placed flat by the smart placement system.</summary>
    public bool IsPlacedFlat { get; private set; }

    /// <summary>Books temporarily parented to this one during a stack pickup.</summary>
    private readonly List<BookItem> _stackedAbove = new();

    private bool _hiddenItemDropped;
    private ReactableTag _reactable;

    private void Awake()
    {
        _reactable = GetComponent<ReactableTag>();
        if (_reactable == null)
            _reactable = gameObject.AddComponent<ReactableTag>();

        if (_definition != null)
        {
            _reactable.Setup(
                _definition.reactionTags.Length > 0 ? _definition.reactionTags : new[] { "book" },
                _definition.title);
        }

        // Shelf default: active but private (date ignores books on shelves)
        _reactable.IsActive = true;
        _reactable.IsPrivate = true;

        // Books are "at home" on their bookcase spawn position or the coffee table
        var placeable = GetComponent<PlaceableObject>();
        if (placeable != null)
            placeable.ConfigureHome(altHomeZone: "CoffeeTable", useSpawnAsHome: true);
    }

    /// <summary>
    /// Called by ObjectGrabber after placement. Toggles public/private
    /// based on whether the book landed on a coffee table DropZone.
    /// </summary>
    public void OnBookPlaced(PlacementSurface surface, bool placedFlat = false)
    {
        IsPlacedFlat = placedFlat;

        // Flat books are kinematic to prevent physics jitter in stacks
        if (placedFlat)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        if (_reactable == null) return;

        // If placed inside a cubby, let DrawerController handle privacy
        var cubby = DrawerController.FindByInteriorSurface(surface);
        if (cubby != null) return;

        bool onCoffeeTable = false;
        if (surface != null)
        {
            var zone = surface.GetComponent<DropZone>();
            if (zone != null && zone.ZoneName == "CoffeeTable")
                onCoffeeTable = true;
        }

        _reactable.IsPrivate = !onCoffeeTable;
    }

    /// <summary>
    /// Called by ObjectGrabber after PlaceableObject.OnPickedUp().
    /// Checks for hidden item conditions and spawns if eligible.
    /// </summary>
    public void OnBookPickedUp()
    {
        if (_reactable != null)
            _reactable.IsPrivate = true;

        if (_hiddenItemDropped) return;
        if (_definition == null || !_definition.hasHiddenItem) return;
        if (_hiddenItemPrefab == null) return;

        // Check day condition
        if (_hiddenItemMinDay > 0 && GameClock.Instance != null
            && GameClock.Instance.CurrentDay < _hiddenItemMinDay)
            return;

        _hiddenItemDropped = true;

        // Spawn hidden item at book's last resting position
        var spawnedItem = Instantiate(_hiddenItemPrefab, transform.position, Quaternion.identity);

        // Frame the discovery (if moments enabled)
        if (ApartmentManager.Instance == null || ApartmentManager.Instance.ItemDiscoveryMoments)
            MomentCamera.FrameTarget(transform.position, 2f);

        // Show caption
        string desc = !string.IsNullOrEmpty(_definition.hiddenItemDescription)
            ? _definition.hiddenItemDescription
            : "Something fell out of the book...";
        CaptionDisplay.Show(desc, 3f);

        Debug.Log($"[BookItem] Hidden item dropped from '{_definition.title}'.");
    }

    // ── Stack pickup/release ────────────────────────────────────────

    /// <summary>
    /// Find all smart-placement books above this one at the same XZ position
    /// and parent them so they move together while held.
    /// </summary>
    public void GrabBooksAbove()
    {
        if (!_useSmartPlacement) return;
        _stackedAbove.Clear();

        float cellRadius = ObjectGrabber.GlobalGridSize * 0.5f;
        float myY = transform.position.y;
        var placeable = GetComponent<PlaceableObject>();
        var surface = placeable != null ? placeable.LastPlacedSurface : null;

        for (int i = 0; i < PlaceableObject.All.Count; i++)
        {
            var po = PlaceableObject.All[i];
            if (po == null || po.gameObject == gameObject) continue;
            if (po.CurrentState == PlaceableObject.State.Held) continue;
            if (surface != null && po.LastPlacedSurface != surface) continue;

            var other = po.GetComponent<BookItem>();
            if (other == null || !other._useSmartPlacement) continue;

            float dx = Mathf.Abs(transform.position.x - po.transform.position.x);
            float dz = Mathf.Abs(transform.position.z - po.transform.position.z);
            if (dx > cellRadius || dz > cellRadius) continue;

            if (po.transform.position.y <= myY + 0.001f) continue;

            _stackedAbove.Add(other);
        }

        // Sort bottom to top so parenting order is stable
        _stackedAbove.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

        for (int i = 0; i < _stackedAbove.Count; i++)
        {
            var book = _stackedAbove[i];
            // Disable physics and colliders
            var rb = book.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
            var col = book.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            book.transform.SetParent(transform, true);
        }

        if (_stackedAbove.Count > 0)
            Debug.Log($"[BookItem] Grabbed {_stackedAbove.Count} book(s) above {name}.");
    }

    /// <summary>
    /// Release stacked books back to independent PlaceableObjects,
    /// placed in a stack at the given surface/position.
    /// </summary>
    public void ReleaseBooksAbove(PlacementSurface surface, Vector3 basePos, Quaternion rot)
    {
        float currentY = basePos.y;

        for (int i = 0; i < _stackedAbove.Count; i++)
        {
            var book = _stackedAbove[i];
            book.transform.SetParent(null, true);

            // Stack each book above the previous one
            var col = book.GetComponent<Collider>();
            if (col != null) col.enabled = true;

            // Estimate book thickness from collider bounds
            float thickness = 0f;
            if (col != null)
                thickness = col.bounds.size.y;
            if (thickness < 0.01f) thickness = 0.03f;

            currentY += thickness;
            Vector3 stackPos = new Vector3(basePos.x, currentY, basePos.z);

            var po = book.GetComponent<PlaceableObject>();
            if (po != null)
                po.OnPlaced(surface, true, stackPos, rot);

            book.OnBookPlaced(surface, true);
        }

        _stackedAbove.Clear();
    }

    /// <summary>True if this book is currently carrying others above it.</summary>
    public bool HasStackedBooks => _stackedAbove.Count > 0;
}
