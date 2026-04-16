using UnityEngine;

/// <summary>
/// Makes an item pairable with another item. Shoes pair with a specific partner
/// (side-by-side), dishes and bowls pair with any of the same category (stacked).
/// When paired, the secondary item parents to the primary and they move as one.
///
/// For SpecificPartner mode (shoes): when one shoe is picked up, the partner
/// flashes its highlight so the player can find it easily.
/// </summary>
public class PairableItem : MonoBehaviour
{
    public enum PairMode
    {
        SpecificPartner, // shoes — must be this exact partner
        AnyOfCategory    // dishes, bowls — any item with matching ItemCategory
    }

    public enum SnapMode
    {
        SideBySide, // shoes — offset along local X
        Stacked     // dishes, bowls — offset along local Y
    }

    [Header("Pairing")]
    [Tooltip("SpecificPartner for shoes (drag partner). AnyOfCategory for dishes/bowls.")]
    [SerializeField] private PairMode _pairMode = PairMode.AnyOfCategory;

    [Tooltip("Specific partner item (shoes only). Leave null for AnyOfCategory.")]
    [SerializeField] private PairableItem _specificPartner;

    [Header("Group Size")]
    [Tooltip("Maximum items in a paired group (0 = unlimited). Books use 3, shoes use 2.")]
    [SerializeField] private int _maxGroupSize = 0;

    [Header("Snap")]
    [Tooltip("SideBySide for shoes, Stacked for dishes/bowls.")]
    [SerializeField] private SnapMode _snapMode = SnapMode.SideBySide;

    [Tooltip("Local offset when snapped to partner.")]
    [SerializeField] private Vector3 _snapOffset = new Vector3(0.1f, 0f, 0f);

    [Header("Audio")]
    [Tooltip("Sound when items snap together.")]
    [SerializeField] private AudioClip _snapSound;

    [Header("Partner Highlight")]
    [Tooltip("Color to pulse when the partner is being held.")]
    [SerializeField] private Color _partnerPulseColor = new Color(1f, 0.85f, 0.4f, 0.6f);

    [Tooltip("Pulse speed (Hz).")]
    [SerializeField] private float _partnerPulseSpeed = 2f;

    private bool _isPaired;
    private PairableItem _pairedChild; // the item that was snapped TO this one
    private PlaceableObject _placeable;
    private bool _partnerHighlightActive;
    private Renderer _renderer;
    private MaterialPropertyBlock _pulseMPB;
    private Color _originalColor;
    private bool _originalColorCaptured;
    private bool _pairPulseActive; // true while snap-confirmation pulse is playing

    public bool IsPaired => _isPaired;

    /// <summary>Reset paired state. Used by BookCollectionItem.DetachFromStack.</summary>
    public void ResetPaired() { _isPaired = false; _pairedChild = null; }
    public PairableItem PairedChild => _pairedChild;
    public PairMode Mode => _pairMode;
    public PairableItem SpecificPartner => _specificPartner;

    private Color _baseColor; // the true original color, captured once in Awake

    private void Awake()
    {
        _placeable = GetComponent<PlaceableObject>();
        _renderer = GetComponentInChildren<Renderer>();
        _pulseMPB = new MaterialPropertyBlock();

        // Capture the TRUE base color once — never changes
        if (_renderer != null && _renderer.sharedMaterial != null)
            _baseColor = _renderer.sharedMaterial.color;
    }

    private void Start()
    {
        // Unpaired pairables get the PSX glitch shader as a visual hint that
        // they belong with a partner. Removed in SnapPair when they connect.
        // Deferred to Start so PlaceableObject.Awake has created _instanceMat.
        if (!_isPaired && _placeable != null)
            _placeable.SetGlitched(true);
    }

    private void Update()
    {
        if (_pairPulseActive) return;
        if (_renderer == null) return;

        bool shouldPulse = false;
        var held = ObjectGrabber.HeldObject;

        if (held != null && held.gameObject != gameObject)
        {
            if (_pairMode == PairMode.SpecificPartner)
            {
                // Shoes: pulse if partner is held (check both directions)
                var heldPair = held.GetComponent<PairableItem>();
                bool isMyPartner = _specificPartner != null && held.gameObject == _specificPartner.gameObject;
                bool iAmTheirPartner = heldPair != null && heldPair.SpecificPartner == this;
                shouldPulse = !_isPaired && (isMyPartner || iAmTheirPartner);
            }
            else if (_pairMode == PairMode.AnyOfCategory && _placeable != null)
            {
                // Plates: pulse if held item is same category
                var heldPairable = held.GetComponent<PairableItem>();
                shouldPulse = heldPairable != null
                    && heldPairable.Mode == PairMode.AnyOfCategory
                    && held.Category == _placeable.Category;
            }
        }

        if (shouldPulse)
        {
            _originalColorCaptured = true;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _partnerPulseSpeed * Mathf.PI * 2f);
            Color c = Color.Lerp(_baseColor, _partnerPulseColor, pulse);
            SetColor(c);
        }
        else if (_originalColorCaptured)
        {
            RestoreColor();
            _originalColorCaptured = false;
        }
    }

    /// <summary>
    /// Called when this item is picked up. Resets pairing state and
    /// unparents any paired child. Flashes partner highlight for shoes.
    /// </summary>
    public void OnPickedUp()
    {
        if (!_isPaired)
        {
            _isPaired = false; // redundant but clear
        }
        // Both SpecificPartner (shoes) and AnyOfCategory (plates):
        // stay paired, pick up the whole group as a unit.

        // Flash partner highlight for unpaired shoes
        if (!_isPaired && _pairMode == PairMode.SpecificPartner
            && _specificPartner != null && !_specificPartner._isPaired)
        {
            var partnerHL = _specificPartner.GetComponent<InteractableHighlight>();
            if (partnerHL == null)
                partnerHL = _specificPartner.gameObject.AddComponent<InteractableHighlight>();
            partnerHL.SetHighlighted(true);
            _partnerHighlightActive = true;
        }
    }

    /// <summary>
    /// Called when this item is put down or pairing completes. Stop flashing partner.
    /// </summary>
    public void OnPutDown()
    {
        RestoreColor();
        _originalColorCaptured = false;

        // Also restore partner's color and turn off their highlight
        if (_specificPartner != null)
        {
            _specificPartner.RestoreColor();
            _specificPartner._originalColorCaptured = false;

            if (_partnerHighlightActive)
            {
                var partnerHL = _specificPartner.GetComponent<InteractableHighlight>();
                if (partnerHL != null)
                    partnerHL.SetHighlighted(false);
            }
        }

        _partnerHighlightActive = false;
    }

    /// <summary>Force renderer back to the original Awake color.</summary>
    public void RestoreBaseColor()
    {
        RestoreColor();
    }

    // ── Single path for ALL color writes (never touch _renderer.material directly) ──

    private void SetColor(Color c)
    {
        // Use PlaceableObject's instance material if available AND enabled.
        // After pairing, the held item's PlaceableObject is disabled — fall
        // through to the MPB path so the pulse still works on both shoes.
        if (_placeable != null && _placeable.enabled && _placeable.SetInstanceColor(c)) return;

        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
        {
            var mpb = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            _renderer.SetPropertyBlock(mpb);
        }
    }

    private void RestoreColor()
    {
        if (_placeable != null && _placeable.enabled) { _placeable.ForceRestoreMaterial(); return; }

        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
        {
            _renderer.SetPropertyBlock(new MaterialPropertyBlock()); // clear all overrides
        }
    }

    /// <summary>
    /// Can the held item pair with this placed item?
    /// </summary>
    public bool CanPairWith(PairableItem held)
    {
        if (held == null || held == this) return false;

        if (_pairMode == PairMode.SpecificPartner)
        {
            // Specific partners can only pair once
            if (_isPaired || held._isPaired) return false;
            return held == _specificPartner || held._specificPartner == this;
        }

        // AnyOfCategory — stacking (no _isPaired check)
        if (_placeable == null || held._placeable == null) return false;
        if (_placeable.Category != held._placeable.Category) return false;

        // Group size cap (books = 3, 0 = unlimited)
        if (_maxGroupSize > 0 && GetStackSize() + held.GetStackSize() > _maxGroupSize)
            return false;

        return true;
    }

    /// <summary>Count all PairableItems in this stack (walks up to root first).</summary>
    public int GetStackSize()
    {
        Transform root = transform;
        while (root.parent != null && root.parent.GetComponent<PairableItem>() != null)
            root = root.parent;
        return root.GetComponentsInChildren<PairableItem>().Length;
    }

    /// <summary>
    /// Snap the held item onto this placed item. Called by ObjectGrabber.
    /// 'held' is the item being carried, 'this' is the item on the surface.
    /// </summary>
    public void SnapPair(PairableItem held)
    {
        if (held == null) return;

        // Stop all visual cues on both items before snapping
        held.OnPutDown();
        OnPutDown();

        _isPaired = true;
        held._isPaired = true;
        _pairedChild = held;

        // Drop the unpaired-state glitch shader on both items now that they connect.
        if (_placeable != null) _placeable.SetGlitched(false);
        if (held._placeable != null) held._placeable.SetGlitched(false);

        // Kill InteractableHighlight on BOTH items — no lingering glow after pairing
        ClearHighlight(gameObject);
        ClearHighlight(held.gameObject);

        // Remove pixel snap + affine warp from paired items — they've "connected"
        DisablePSXEffects(gameObject);
        DisablePSXEffects(held.gameObject);

        // Play snap sound
        if (_snapSound != null)
            AudioManager.Instance?.PlaySFX(_snapSound);
        else if (held._snapSound != null)
            AudioManager.Instance?.PlaySFX(held._snapSound);

        // Pulse both items to confirm the pairing
        // Re-cache renderer in case it was lost
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (held._renderer == null) held._renderer = held.GetComponentInChildren<Renderer>();
        StartCoroutine(RunPairPulse());
        held.StartCoroutine(held.RunPairPulse());

        // Parent held item to this item
        var heldRb = held.GetComponent<Rigidbody>();
        if (heldRb != null)
        {
            if (!heldRb.isKinematic)
            {
                heldRb.linearVelocity = Vector3.zero;
                heldRb.angularVelocity = Vector3.zero;
            }
            heldRb.isKinematic = true;
        }

        // Find the topmost item in the stack to parent onto
        Transform stackTop = FindStackTop(transform);

        if (_snapMode == SnapMode.Stacked)
        {
            // Stack in world space — place flat on top of the topmost plate's mesh
            var renderer = stackTop.GetComponentInChildren<Renderer>();
            Vector3 stackPos;
            if (renderer != null)
                stackPos = renderer.bounds.center + Vector3.up * renderer.bounds.extents.y;
            else
                stackPos = stackTop.position + Vector3.up * _snapOffset.y;

            // Keep world rotation from the root plate (all plates face the same way)
            Quaternion stackRot = transform.rotation;

            // Parent, then set world pos/rot (worldPositionStays=false would lose it)
            held.transform.SetParent(stackTop, true);
            held.transform.position = stackPos;
            held.transform.rotation = stackRot;
        }
        else
        {
            // SideBySide — parent to the stack ROOT so items fan out from center.
            // Pick the side (+ or - offset) closest to where the held item is,
            // falling back to whichever side is still free.
            Transform root = FindStackRoot(transform);

            // For book collections: straighten root to upright so all books
            // face the same direction when snapped together.
            if (root.GetComponent<BookCollectionItem>() != null)
            {
                Vector3 rootEuler = root.eulerAngles;
                root.rotation = Quaternion.Euler(0f, rootEuler.y, 0f);
            }

            // For shoes: straighten root so both shoes sit flat and face forward.
            // Keeps Y rotation (facing direction) but levels out any tilt.
            if (_pairMode == PairMode.SpecificPartner && root.GetComponent<BookCollectionItem>() == null)
            {
                Vector3 rootEuler = root.eulerAngles;
                root.rotation = Quaternion.Euler(0f, rootEuler.y, 0f);
            }

            Vector3 localPos = GetSideBySideLocalOffset(root, held);

            // Calculate world-space target position so both items sit at the
            // same ground level — prevents one shoe floating above the other.
            Vector3 worldSnapPos = root.TransformPoint(localPos);
            worldSnapPos.y = root.position.y; // match root's Y exactly

            // Preserve world scale — parent may have different scale that deforms the child
            Vector3 heldWorldScale = held.transform.lossyScale;
            held.transform.SetParent(root, true);
            Vector3 parentScale = root.lossyScale;
            held.transform.localScale = new Vector3(
                parentScale.x != 0f ? heldWorldScale.x / parentScale.x : 1f,
                parentScale.y != 0f ? heldWorldScale.y / parentScale.y : 1f,
                parentScale.z != 0f ? heldWorldScale.z / parentScale.z : 1f);

            // Set position in world space then convert back to local, so the
            // Y stays aligned regardless of parent rotation or pivot offset.
            held.transform.position = worldSnapPos;
            held.transform.localRotation = Quaternion.identity;
        }

        // Disable held item's standalone behavior
        var heldCol = held.GetComponent<Collider>();
        if (heldCol != null) heldCol.enabled = false;

        if (held._placeable != null)
            held._placeable.enabled = false;

        Debug.Log($"[PairableItem] {held.name} snapped to {stackTop.name} (stack depth)");

        // Notify any BookCollectionItem on the stack root
        Transform stackRoot = transform;
        while (stackRoot.parent != null && stackRoot.parent.GetComponent<PairableItem>() != null)
            stackRoot = stackRoot.parent;
        var collection = stackRoot.GetComponent<BookCollectionItem>();
        if (collection != null)
            collection.OnBookSnapped();
    }

    /// <summary>
    /// Preview where a held item would land if paired with this item right now.
    /// Used by ObjectGrabber's magnetic pair-snap to lock the held item in place
    /// before the player clicks to confirm.
    /// </summary>
    public Vector3 GetPairSnapPosition(PairableItem held)
    {
        Transform stackTop = FindStackTop(transform);

        if (_snapMode == SnapMode.Stacked)
        {
            var renderer = stackTop.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return renderer.bounds.center + Vector3.up * renderer.bounds.extents.y;
            return stackTop.position + Vector3.up * _snapOffset.y;
        }

        // SideBySide — pick the best free side relative to the stack root
        Transform root = FindStackRoot(transform);
        Vector3 localPos = GetSideBySideLocalOffset(root, held);
        return root.TransformPoint(localPos);
    }

    /// <summary>Walk up to the root of a paired stack.</summary>
    private static Transform FindStackRoot(Transform node)
    {
        var current = node;
        while (current.parent != null && current.parent.GetComponent<PairableItem>() != null)
            current = current.parent;
        return current;
    }

    /// <summary>
    /// For SideBySide mode: pick +offset or -offset from the root, choosing
    /// the side closest to the held item. If one side is occupied, use the other.
    /// </summary>
    private Vector3 GetSideBySideLocalOffset(Transform root, PairableItem held)
    {
        bool positiveOccupied = false;
        bool negativeOccupied = false;

        foreach (Transform child in root)
        {
            if (child.GetComponent<PairableItem>() == null) continue;
            float dot = Vector3.Dot(child.localPosition.normalized, _snapOffset.normalized);
            if (dot >= 0f) positiveOccupied = true;
            else negativeOccupied = true;
        }

        // If both free, pick the side closer to the held item
        if (!positiveOccupied && !negativeOccupied)
        {
            Vector3 posWorld = root.TransformPoint(_snapOffset);
            Vector3 negWorld = root.TransformPoint(-_snapOffset);
            float distPos = Vector3.Distance(held.transform.position, posWorld);
            float distNeg = Vector3.Distance(held.transform.position, negWorld);
            return distPos <= distNeg ? _snapOffset : -_snapOffset;
        }

        if (!negativeOccupied) return -_snapOffset;
        if (!positiveOccupied) return _snapOffset;

        // Both occupied (stack full) — default to positive
        return _snapOffset;
    }

    /// <summary>Walk the transform hierarchy to find the topmost stacked PairableItem.</summary>
    private static Transform FindStackTop(Transform root)
    {
        var current = root;
        while (true)
        {
            bool found = false;
            foreach (Transform child in current)
            {
                if (child.GetComponent<PairableItem>() != null)
                {
                    current = child;
                    found = true;
                    break;
                }
            }
            if (!found) break;
        }
        return current;
    }

    /// <summary>Disable pixel snap + affine warp on a paired item.</summary>
    private static void DisablePSXEffects(GameObject go)
    {
        var psx = go.GetComponent<PSXObjectSettings>();
        if (psx == null) psx = go.AddComponent<PSXObjectSettings>();
        psx.SnapResolution = 0f;
        psx.AffineIntensity = 0f;
    }

    /// <summary>Turn off InteractableHighlight on a GameObject (both highlighted and interact-highlighted).</summary>
    private static void ClearHighlight(GameObject go)
    {
        var hl = go.GetComponent<InteractableHighlight>();
        if (hl != null)
        {
            hl.SetHighlighted(false);
            hl.SetInteractHighlighted(false);
        }
    }

    /// <summary>Brief color pulse on an item to confirm pairing.</summary>
    private System.Collections.IEnumerator RunPairPulse()
    {
        if (_renderer == null) yield break;

        _pairPulseActive = true;

        Color flash = _partnerPulseColor;
        float duration = 0.5f;

        // Two pulses so it's clearly visible
        for (int p = 0; p < 2; p++)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float blend = Mathf.Sin(t / duration * Mathf.PI);
                SetColor(Color.Lerp(_baseColor, flash, blend));
                yield return null;
            }
        }

        // Restore all visual state — color, highlight, pulse flag
        RestoreColor();
        _pairPulseActive = false;
        _originalColorCaptured = false;
        ClearHighlight(gameObject);
    }
}
