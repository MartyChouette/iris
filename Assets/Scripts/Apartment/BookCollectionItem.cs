using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placed on each book alongside PairableItem. Books in a collection can be
/// snapped together in any order, but the reward only triggers when all books
/// are present AND in the correct sequential order (left-to-right by _orderIndex).
///
/// If books are connected in the wrong order, the player can click an individual
/// book in the set to detach it, then re-attach it on the correct side.
///
/// PairableItem._maxGroupSize should match _requiredCount (typically 3).
/// PairableItem.SnapPair notifies this component via OnBookSnapped().
/// </summary>
public class BookCollectionItem : MonoBehaviour
{
    [Header("Collection")]
    [Tooltip("Number of books needed to complete the collection.")]
    [SerializeField] private int _requiredCount = 3;

    [Tooltip("This book's position in the collection sequence (1-based). Book 1, Book 2, Book 3.")]
    [SerializeField] private int _orderIndex = 1;

    [Header("Reward")]
    [Tooltip("Prefab spawned when the collection is complete and in order.")]
    [SerializeField] private GameObject _rewardPrefab;

    [Tooltip("Caption text shown when the reward appears.")]
    [SerializeField] private string _rewardCaption = "Something fell out of the books...";

    [Header("Animation")]
    [Tooltip("Pause before the celebration starts.")]
    [SerializeField] private float _celebrationDelay = 0.4f;

    [Tooltip("Duration of the reward arc to the table.")]
    [SerializeField] private float _arcDuration = 0.7f;

    [Tooltip("Peak height of the reward arc.")]
    [SerializeField] private float _arcHeight = 0.4f;

    [Header("Audio")]
    [Tooltip("SFX when the collection completes (correct order).")]
    [SerializeField] private AudioClip _completionSFX;

    [Tooltip("SFX when all books are connected but in wrong order.")]
    [SerializeField] private AudioClip _wrongOrderSFX;

    public int OrderIndex => _orderIndex;

    private bool _completed;

    /// <summary>Called by PairableItem.SnapPair when a book is added to the stack.</summary>
    public void OnBookSnapped()
    {
        if (_completed) return;

        int count = GetComponentsInChildren<BookCollectionItem>().Length;
        if (count < _requiredCount) return;

        // All books present — check order
        if (IsInCorrectOrder())
        {
            _completed = true;
            StartCoroutine(CelebrationSequence());
        }
        else
        {
            // Wrong order — nudge the player
            if (_wrongOrderSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_wrongOrderSFX);

            CaptionDisplay.Show("The books aren't in the right order...", 3f);
        }
    }

    /// <summary>
    /// Check if books in the stack are in correct left-to-right sequential order.
    /// Reads the local X positions to determine spatial order, then compares
    /// against _orderIndex values.
    /// </summary>
    private bool IsInCorrectOrder()
    {
        var books = GetComponentsInChildren<BookCollectionItem>();
        if (books.Length < _requiredCount) return false;

        // Build list of (localX, orderIndex) — localX relative to root (this transform)
        var ordered = new List<(float localX, int index)>();
        for (int i = 0; i < books.Length; i++)
        {
            float localX;
            if (books[i].transform == transform)
                localX = 0f; // root is at center
            else
                localX = books[i].transform.localPosition.x;

            ordered.Add((localX, books[i]._orderIndex));
        }

        // Sort by spatial position (left to right in local X)
        ordered.Sort((a, b) => a.localX.CompareTo(b.localX));

        // Check if order indices are sequential (ascending)
        bool ascending = true;
        bool descending = true;
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].index <= ordered[i - 1].index) ascending = false;
            if (ordered[i].index >= ordered[i - 1].index) descending = false;
        }

        return ascending || descending;
    }

    /// <summary>
    /// Detach this book from its paired stack. Called by ObjectGrabber when
    /// clicking an individual book in a connected set.
    /// Returns the PlaceableObject for ObjectGrabber to grab, or null if
    /// this book is the root (can't detach the root — pick up whole set instead).
    /// </summary>
    public PlaceableObject DetachFromStack()
    {
        // Only children can be detached — root stays
        if (transform.parent == null || transform.parent.GetComponent<PairableItem>() == null)
            return null;

        var pairable = GetComponent<PairableItem>();
        var placeable = GetComponent<PlaceableObject>();
        if (pairable == null || placeable == null) return null;

        // Unparent
        transform.SetParent(null, true);

        // Re-enable physics
        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        // Re-enable collider
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Re-enable PlaceableObject
        placeable.enabled = true;

        // Reset paired state
        pairable.ResetPaired();
        placeable.SetGlitched(true);

        Debug.Log($"[BookCollectionItem] {name} detached from stack.");
        return placeable;
    }

    private IEnumerator CelebrationSequence()
    {
        // Frame the books with the moment camera
        MomentCamera.FrameTarget(transform.position, _celebrationDelay + 2f);

        yield return new WaitForSeconds(_celebrationDelay);

        // Completion SFX
        if (_completionSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_completionSFX);

        // Scale pop on the book stack
        yield return ScalePop(transform, 0.35f);

        // Spawn reward
        if (_rewardPrefab == null) yield break;

        Vector3 spawnPos = transform.position + Vector3.up * 0.25f;
        var reward = Instantiate(_rewardPrefab, spawnPos, Quaternion.identity);

        // Strip physics during arc
        var rb = reward.GetComponent<Rigidbody>();
        bool wasKinematic = true;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
        }
        foreach (var col in reward.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Find coffee table position
        Vector3 targetPos = FindCoffeeTablePosition();
        yield return ArcToPosition(reward, spawnPos, targetPos);

        // Re-enable physics so the item can be picked up
        if (rb != null) rb.isKinematic = wasKinematic;
        foreach (var col in reward.GetComponentsInChildren<Collider>())
            col.enabled = true;

        // Snap to nearest surface grid
        var placeable = reward.GetComponent<PlaceableObject>();
        if (placeable != null)
        {
            var surface = PlacementSurface.FindNearest(targetPos, skipVertical: true);
            if (surface != null)
                placeable.OnPlaced(surface, true, targetPos, reward.transform.rotation);
        }

        // Caption
        if (!string.IsNullOrEmpty(_rewardCaption))
            CaptionDisplay.Show(_rewardCaption, 3f);

        Debug.Log($"[BookCollectionItem] Collection complete! Reward spawned at {targetPos}.");
    }

    /// <summary>Quick squish-and-pop scale animation on the book stack.</summary>
    private static IEnumerator ScalePop(Transform target, float duration)
    {
        Vector3 original = target.localScale;
        float half = duration * 0.5f;

        // Squish down
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float ease = t / half;
            float sy = Mathf.Lerp(1f, 0.7f, ease);
            float sx = Mathf.Lerp(1f, 1.15f, ease);
            target.localScale = new Vector3(original.x * sx, original.y * sy, original.z * sx);
            yield return null;
        }

        // Pop back up (overshoot)
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float ease = t / half;
            float sy = Mathf.Lerp(0.7f, 1.1f, ease);
            float sx = Mathf.Lerp(1.15f, 0.95f, ease);
            target.localScale = new Vector3(original.x * sx, original.y * sy, original.z * sx);
            yield return null;
        }

        // Settle
        target.localScale = original;
    }

    /// <summary>Arc the reward from spawn position to the table.</summary>
    private IEnumerator ArcToPosition(GameObject obj, Vector3 from, Vector3 to)
    {
        float elapsed = 0f;
        Quaternion startRot = obj.transform.rotation;

        while (elapsed < _arcDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _arcDuration);

            Vector3 pos = Vector3.Lerp(from, to, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * _arcHeight;
            obj.transform.position = pos;

            // Gentle spin during arc
            obj.transform.rotation = startRot * Quaternion.Euler(0f, t * 360f, 0f);

            yield return null;
        }

        obj.transform.position = to;
    }

    /// <summary>Find the coffee table DropZone position, or fall back to nearest horizontal surface.</summary>
    private static Vector3 FindCoffeeTablePosition()
    {
        // Try DropZone named "CoffeeTable"
        var allZones = DropZone.All;
        for (int i = 0; i < allZones.Count; i++)
        {
            if (allZones[i] != null && allZones[i].ZoneName == "CoffeeTable")
                return allZones[i].transform.position + Vector3.up * 0.05f;
        }

        // Fallback: nearest horizontal placement surface
        var surface = PlacementSurface.FindNearest(Vector3.zero, skipVertical: true);
        if (surface != null)
            return surface.transform.position + Vector3.up * 0.1f;

        // Last resort: world center, slightly elevated
        return new Vector3(0f, 1f, 0f);
    }
}
