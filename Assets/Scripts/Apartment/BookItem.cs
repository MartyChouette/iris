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

    [Header("Hidden Item")]
    [Tooltip("Optional prefab dropped at the book's position on first pickup.")]
    [SerializeField] private GameObject _hiddenItemPrefab;

    [Tooltip("Minimum day number before the hidden item can appear (0 = always).")]
    [SerializeField] private int _hiddenItemMinDay;

    public BookDefinition Definition => _definition;

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
    public void OnBookPlaced(PlacementSurface surface)
    {
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

        // Frame the discovery with moment camera
        MomentCamera.FrameTarget(transform.position, 2f);

        // Show caption
        string desc = !string.IsNullOrEmpty(_definition.hiddenItemDescription)
            ? _definition.hiddenItemDescription
            : "Something fell out of the book...";
        CaptionDisplay.Show(desc, 3f);

        Debug.Log($"[BookItem] Hidden item dropped from '{_definition.title}'.");
    }
}
