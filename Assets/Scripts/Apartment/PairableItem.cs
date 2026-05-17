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

    [Header("Description")]
    [Tooltip("Description to use after pairing (replaces PlaceableObject.ItemDescription). Leave empty to keep original.")]
    [SerializeField] private string _pairedDescription = "";

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
    private ItemHighlight _cachedHighlight;
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

        // Capture the TRUE base color once — never changes.
        // URP Lit uses _BaseColor, not _Color. .color returns _Color which
        // may be white even when the material has a colored _BaseColor.
        if (_renderer != null && _renderer.sharedMaterial != null)
        {
            if (_renderer.sharedMaterial.HasProperty("_BaseColor"))
                _baseColor = _renderer.sharedMaterial.GetColor("_BaseColor");
            else
                _baseColor = _renderer.sharedMaterial.color;
        }
    }

    private void Start()
    {
        // Unpaired pairables get the PSX glitch shader as a visual hint that
        // they belong with a partner. Removed in SnapPair when they connect.
        // Deferred to Start so PlaceableObject.Awake has created _instanceMat.
        // Only glitch items that are fully PSX — non-PSX materials (URP Lit)
        // lose their textures when swapped to PSXLitGlitch.
        if (!_isPaired && _placeable != null && _placeable.IsFullyPSX)
        {
            _placeable.SetGlitched(true);
#if UNITY_EDITOR
            Debug.Log($"[PairableItem] Start: applied glitch to '{name}'");
#endif
        }
        else
        {
            Debug.LogWarning($"[PairableItem] Start: skipped glitch on '{name}' — _isPaired={_isPaired}, _placeable={(_placeable != null ? "ok" : "NULL")}");
        }
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
            if (!_originalColorCaptured)
            {
                _originalColorCaptured = true;
                if (_cachedHighlight == null)
                    _cachedHighlight = GetComponent<ItemHighlight>();
                if (_cachedHighlight == null)
                    _cachedHighlight = gameObject.AddComponent<ItemHighlight>();
                _cachedHighlight.SetHighlighted(true);
            }
        }
        else if (_originalColorCaptured)
        {
            _originalColorCaptured = false;
            if (_cachedHighlight != null) _cachedHighlight.SetHighlighted(false);
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

        // Flash partner highlight every time a shoe is picked up (bright)
        if (_pairMode == PairMode.SpecificPartner && _specificPartner != null)
        {
            var partnerHL = _specificPartner.GetComponent<ItemHighlight>();
            if (partnerHL == null)
                partnerHL = _specificPartner.gameObject.AddComponent<ItemHighlight>();
            partnerHL.SetDisplayHighlighted(true);
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
                var partnerHL = _specificPartner.GetComponent<ItemHighlight>();
                if (partnerHL != null)
                    partnerHL.SetDisplayHighlighted(false);
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
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
        {
            // Fresh MPB — never GetPropertyBlock first, it copies ALL material
            // properties including _BaseColor which contaminates multi-material objects.
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            _renderer.SetPropertyBlock(mpb);
        }
    }

    private void RestoreColor()
    {
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null)
        {
            _renderer.SetPropertyBlock(null); // clear all overrides

            // Re-apply PSXObjectSettings so snap=0 survives the MPB clear
            var psx = GetComponent<PSXObjectSettings>();
            if (psx != null) psx.Apply();
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

        // Update description to paired version on the root item
        if (!string.IsNullOrEmpty(_pairedDescription) && _placeable != null)
            _placeable.ItemDescription = _pairedDescription;
        else if (!string.IsNullOrEmpty(held._pairedDescription) && _placeable != null)
            _placeable.ItemDescription = held._pairedDescription;

        // Kill ItemHighlight on BOTH items — no lingering glow after pairing
        ClearHighlight(gameObject);
        ClearHighlight(held.gameObject);

        // Remove pixel snap + affine warp from the ENTIRE stack — walk all
        // PairableItems in the hierarchy so a 3rd plate joining a 2-stack
        // also clears the root and any previous children.
        var stackRoot = FindStackRoot(transform);
        var allInStack = stackRoot.GetComponentsInChildren<PairableItem>(true);
        for (int i = 0; i < allInStack.Length; i++)
            DisablePSXEffects(allInStack[i].gameObject);
        // Held item isn't parented yet — clear it explicitly
        DisablePSXEffects(held.gameObject);

        // Play snap sound + smoke poof
        if (_snapSound != null)
            AudioManager.Instance?.PlaySFX(_snapSound);
        else if (held._snapSound != null)
            AudioManager.Instance?.PlaySFX(held._snapSound);
        SmokePoof.Spawn(held.transform.position, 0.1f);

        // Pulse both items to confirm the pairing
        // Re-cache renderer in case it was lost
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (held._renderer == null) held._renderer = held.GetComponentInChildren<Renderer>();
        StartCoroutine(RunPairPulse());
        held.StartCoroutine(held.RunPairPulse());

        // Keep child kinematic so it doesn't interfere with root physics
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
            // Match rotation first so bounds are comparable
            Transform root = FindStackRoot(transform);
            Quaternion stackRot = root.rotation;
            held.transform.rotation = stackRot;

            // Align the held book's center XZ with the bottom book's center XZ,
            // then sit it on top of the bottom book's bounds.
            var botRenderer = stackTop.GetComponentInChildren<Renderer>();
            var topRenderer = held.GetComponentInChildren<Renderer>();

            float topY = botRenderer != null ? botRenderer.bounds.max.y : stackTop.position.y;
            Vector3 botCenter = botRenderer != null ? botRenderer.bounds.center : stackTop.position;
            Vector3 topCenter = topRenderer != null ? topRenderer.bounds.center : held.transform.position;
            Vector3 topPivotOffset = held.transform.position - topCenter;

            // Place held book so its visual center aligns with bottom book's center
            Vector3 stackPos = new Vector3(botCenter.x, topY, botCenter.z) + topPivotOffset;
            stackPos.y = topY + (held.transform.position.y - (topRenderer != null ? topRenderer.bounds.min.y : held.transform.position.y));

            Vector3 heldWorldScale = held.transform.lossyScale;
            held.transform.SetParent(stackTop, true);
            Vector3 parentScale = stackTop.lossyScale;
            held.transform.localScale = new Vector3(
                parentScale.x != 0f ? heldWorldScale.x / parentScale.x : 1f,
                parentScale.y != 0f ? heldWorldScale.y / parentScale.y : 1f,
                parentScale.z != 0f ? heldWorldScale.z / parentScale.z : 1f);
            held.transform.position = stackPos;
        }
        else
        {
            // SideBySide — center both shoes symmetrically around root's position.
            Transform sbsRoot = FindStackRoot(transform);

            // Auto-compute offset from local mesh bounds (rotation-stable).
            // Use the SMALLEST horizontal dimension as shoe "width" — shoes are
            // longer than wide, so min(sizeX, sizeZ) avoids oversized gaps.
            Vector3 effectiveOffset = _snapOffset;
            if (effectiveOffset.sqrMagnitude < 0.0001f)
            {
                var rootMF = GetComponentInChildren<MeshFilter>();
                var heldMF = held.GetComponentInChildren<MeshFilter>();
                if (rootMF != null && heldMF != null
                    && rootMF.sharedMesh != null && heldMF.sharedMesh != null)
                {
                    Vector3 rootSize = rootMF.sharedMesh.bounds.size;
                    Vector3 heldSize = heldMF.sharedMesh.bounds.size;
                    float rootW = Mathf.Min(rootSize.x, rootSize.z);
                    float heldW = Mathf.Min(heldSize.x, heldSize.z);
                    float totalWidth = (rootW + heldW) * 0.5f + 0.005f;
                    effectiveOffset = new Vector3(totalWidth, 0f, 0f);
                }
                else
                {
                    effectiveOffset = new Vector3(0.12f, 0f, 0f);
                }
            }

            Vector3 localPos = GetSideBySideLocalOffset(sbsRoot, held, effectiveOffset);

            // Keep held shoe's rigidbody kinematic
            if (heldRb != null)
            {
                if (!heldRb.isKinematic)
                {
                    heldRb.linearVelocity = Vector3.zero;
                    heldRb.angularVelocity = Vector3.zero;
                }
                heldRb.isKinematic = true;
            }

            // Parent held to root, preserve scale
            Vector3 heldWorldScale = held.transform.lossyScale;
            held.transform.SetParent(sbsRoot, true);
            Vector3 parentScale = sbsRoot.lossyScale;
            held.transform.localScale = new Vector3(
                parentScale.x != 0f ? heldWorldScale.x / parentScale.x : 1f,
                parentScale.y != 0f ? heldWorldScale.y / parentScale.y : 1f,
                parentScale.z != 0f ? heldWorldScale.z / parentScale.z : 1f);

            // Place child at offset, same rotation as root
            held.transform.localPosition = new Vector3(localPos.x, 0f, localPos.z);
            held.transform.localRotation = Quaternion.identity;

            // Align soles — L/R meshes often have different pivot heights.
            // Uses sharedMesh.bounds (constant) + TransformPoint (always current),
            // NOT Renderer.bounds (which can be stale within the same frame).
            var rootFilter = GetComponentInChildren<MeshFilter>();
            var heldFilter = held.GetComponentInChildren<MeshFilter>();
            if (rootFilter != null && heldFilter != null
                && rootFilter.sharedMesh != null && heldFilter.sharedMesh != null)
            {
                float rootBottom = sbsRoot.TransformPoint(
                    new Vector3(0f, rootFilter.sharedMesh.bounds.min.y, 0f)).y;
                float heldBottom = held.transform.TransformPoint(
                    new Vector3(0f, heldFilter.sharedMesh.bounds.min.y, 0f)).y;
                float diff = rootBottom - heldBottom;
                if (Mathf.Abs(diff) > 0.001f)
                    held.transform.position += Vector3.up * diff;
            }

            // Shift root by half offset so pair is centered on original position
            Vector3 halfShift = sbsRoot.TransformDirection(localPos * 0.5f);
            halfShift.y = 0f;
            sbsRoot.position -= halfShift;

            // Resize root's BoxCollider to encompass both shoes
            ResizeColliderToChildren(sbsRoot);
        }

        // Disable held item's standalone PlaceableObject but keep collider
        // so clicks on the child route up to the parent for pickup.
        if (held._placeable != null)
            held._placeable.enabled = false;

        Debug.Log($"[PairableItem] {held.name} snapped to {stackTop.name} (stack depth)");

        // Notify any BookCollectionItem on the stack root
        Transform bookRoot = transform;
        while (bookRoot.parent != null && bookRoot.parent.GetComponent<PairableItem>() != null)
            bookRoot = bookRoot.parent;
        var collection = bookRoot.GetComponent<BookCollectionItem>();
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

        // SideBySide — return half-offset (pair centers around root)
        Transform root = FindStackRoot(transform);
        Vector3 effectiveOffset = _snapOffset;
        if (effectiveOffset.sqrMagnitude < 0.0001f)
        {
            var rootRend = GetComponentInChildren<Renderer>();
            var heldRend = held.GetComponentInChildren<Renderer>();
            if (rootRend != null && heldRend != null)
            {
                float rootW = Mathf.Min(rootRend.bounds.size.x, rootRend.bounds.size.z);
                float heldW = Mathf.Min(heldRend.bounds.size.x, heldRend.bounds.size.z);
                effectiveOffset = new Vector3((rootW + heldW) * 0.5f + 0.005f, 0f, 0f);
            }
            else
                effectiveOffset = new Vector3(0.12f, 0f, 0f);
        }
        Vector3 localPos = GetSideBySideLocalOffset(root, held, effectiveOffset);
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
    private Vector3 GetSideBySideLocalOffset(Transform root, PairableItem held, Vector3 offset)
    {
        bool positiveOccupied = false;
        bool negativeOccupied = false;

        foreach (Transform child in root)
        {
            if (child.GetComponent<PairableItem>() == null) continue;
            float dot = Vector3.Dot(child.localPosition.normalized, offset.normalized);
            if (dot >= 0f) positiveOccupied = true;
            else negativeOccupied = true;
        }

        // If both free, pick the side closer to the held item
        if (!positiveOccupied && !negativeOccupied)
        {
            Vector3 posWorld = root.TransformPoint(offset);
            Vector3 negWorld = root.TransformPoint(-offset);
            float distPos = Vector3.Distance(held.transform.position, posWorld);
            float distNeg = Vector3.Distance(held.transform.position, negWorld);
            return distPos <= distNeg ? offset : -offset;
        }

        if (!negativeOccupied) return -offset;
        if (!positiveOccupied) return offset;

        // Both occupied (stack full) — default to positive
        return offset;
    }



    /// <summary>Component-wise absolute value of a Vector3.</summary>
    private static Vector3 Abs(Vector3 v) =>
        new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>
    /// Resize the root's BoxCollider so it encompasses all child Renderers.
    /// Called after SideBySide pairing so the collider covers both shoes.
    /// </summary>
    private static void ResizeColliderToChildren(Transform root)
    {
        var box = root.GetComponent<BoxCollider>();
        if (box == null) return;

        // Gather world-space bounds from all renderers in the hierarchy
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        // Convert world bounds to root's local space
        box.center = root.InverseTransformPoint(combined.center);
        // InverseTransformVector handles rotation but not non-uniform scale correctly,
        // so divide each axis by lossyScale manually.
        Vector3 ls = root.lossyScale;
        box.size = new Vector3(
            ls.x != 0f ? combined.size.x / Mathf.Abs(ls.x) : combined.size.x,
            ls.y != 0f ? combined.size.y / Mathf.Abs(ls.y) : combined.size.y,
            ls.z != 0f ? combined.size.z / Mathf.Abs(ls.z) : combined.size.z);
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

    /// <summary>
    /// Fully remove all PSX lo-fi effects from a paired item:
    ///   1. Clear + destroy PSXObjectSettings (so global PSX pass can't re-apply)
    ///   2. Restore the original hi-fi shader via SetGlitched(false)
    /// After this, the item renders at full quality.
    /// </summary>
    private static void DisablePSXEffects(GameObject go)
    {
        var psx = go.GetComponent<PSXObjectSettings>();
        if (psx == null) psx = go.AddComponent<PSXObjectSettings>();
        psx.SnapResolution = 0f;
        psx.AffineIntensity = 0f;
        psx.ShadowDither = 0f;

        var placeable = go.GetComponent<PlaceableObject>();
        if (placeable != null)
            placeable.SetGlitched(false);
    }

    /// <summary>Turn off and disable ItemHighlight on a GameObject so it can't re-trigger.</summary>
    private static void ClearHighlight(GameObject go)
    {
        var hl = go.GetComponent<ItemHighlight>();
        if (hl != null)
        {
            hl.SetHighlighted(false);
            hl.SetInteractHighlighted(false);
            hl.enabled = false;
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
