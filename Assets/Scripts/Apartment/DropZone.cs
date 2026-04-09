using System.Collections;
using UnityEngine;

/// <summary>
/// Generic named drop zone. Items whose HomeZoneName matches this zone's name
/// are considered "at home" when placed here. Optionally destroys deposited items
/// (e.g. trash can). Pulses a highlight when the player holds a matching item.
/// </summary>
public class DropZone : MonoBehaviour
{
    // ── Static registry ──
    private static readonly System.Collections.Generic.List<DropZone> s_all = new();
    public static System.Collections.Generic.IReadOnlyList<DropZone> All => s_all;
    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    [Header("Identity")]
    [Tooltip("Name that PlaceableObject.HomeZoneName must match.")]
    [SerializeField] private string _zoneName = "";

    [Header("Behavior")]
    [Tooltip("If true, deposited items are destroyed (e.g. trash can).")]
    [SerializeField] private bool _destroyOnDeposit;

    [Header("Visual")]
    [Tooltip("Renderer for the zone highlight quad.")]
    [SerializeField] private Renderer _zoneRenderer;

    [Tooltip("If true, the renderer is a dedicated highlight (hidden when inactive). If false, it's a shared mesh like a fridge door (always visible, color pulses only).")]
    [SerializeField] private bool _hideRendererWhenInactive = true;

    [Tooltip("Active pulse color (matching item held).")]
    [SerializeField] private Color _activeColor = new Color(0.4f, 0.9f, 1.0f, 0.55f);

    [Tooltip("Pulse speed (oscillations per second).")]
    [SerializeField] private float _pulseSpeed = 2f;

    [Header("Audio")]
    [Tooltip("SFX played on deposit.")]
    [SerializeField] private AudioClip _depositSFX;

    [Tooltip("SFX played when trash is destroyed.")]
    [SerializeField] private AudioClip _trashSFX;

    [Header("Bounce-In Animation (DestroyOnDeposit only)")]
    [Tooltip("Optional override for the bounce target. If null, the deposited item arcs into this DropZone's transform.position.")]
    [SerializeField] private Transform _bounceTarget;

    [Tooltip("Peak height of the parabolic arc above the midpoint between item start and bounce target.")]
    [SerializeField] private float _bounceArcHeight = 0.45f;

    [Tooltip("Seconds the item takes to fly from its drop position to the bounce target.")]
    [SerializeField] private float _bounceArcDuration = 0.55f;

    [Tooltip("Total spin (degrees) applied around Y during the arc — 540 = 1.5 rotations.")]
    [SerializeField] private float _bounceSpinDegrees = 540f;

    [Tooltip("How squished the item gets when it lands on the trash mouth (0 = flat, 1 = no squish).")]
    [SerializeField, Range(0.1f, 1f)] private float _bounceSquishY = 0.45f;

    [Tooltip("Seconds the squish-and-pop bounce takes after landing.")]
    [SerializeField] private float _bounceSettleDuration = 0.25f;

    [Header("Auto-Slotting")]
    [Tooltip("If true, deposits snap to fixed slot positions in a row instead of going to the cursor location. Used by the shoe rack so pairs don't pile up.")]
    [SerializeField] private bool _useSlotting;

    [Tooltip("Local position of the first slot, relative to this DropZone's transform.")]
    [SerializeField] private Vector3 _slotLocalOrigin = Vector3.zero;

    [Tooltip("Local-space direction the slots are laid out along (auto-normalized).")]
    [SerializeField] private Vector3 _slotLocalAxis = Vector3.right;

    [Tooltip("Distance between adjacent slots in world units.")]
    [SerializeField] private float _slotSpacing = 0.22f;

    [Tooltip("Maximum number of slots in the rack.")]
    [SerializeField, Range(1, 12)] private int _slotCount = 4;

    [Tooltip("Radius around each slot used to detect whether it's already occupied. Deposited items are kinematic-locked exactly at slot center, so this should be tight — larger values cause adjacent slots to false-positive on each other's shoes.")]
    [SerializeField] private float _slotOccupancyRadius = 0.03f;

    public string ZoneName => _zoneName;
    public bool DestroyOnDeposit => _destroyOnDeposit;
    public bool UseSlotting => _useSlotting;

    /// <summary>Total slots configured (only meaningful when UseSlotting is true).</summary>
    public int SlotCount => _slotCount;

    /// <summary>
    /// Current deposit count. For slotting zones this is computed live from
    /// slot occupancy each time it's read, so picking up a deposited item
    /// automatically decrements without bookkeeping. For non-slotting zones
    /// it's a simple counter that increments on RegisterDeposit.
    /// </summary>
    public int DepositCount
    {
        get
        {
            if (!_useSlotting) return _legacyDepositCount;
            return CountOccupiedSlots();
        }
    }

    private int _legacyDepositCount;

    private Material _instanceMat;
    private Color _originalColor;
    private bool _playerHoldingMatch;
    private bool _pulsing;

    private void Start()
    {
        if (_zoneRenderer == null)
            _zoneRenderer = GetComponent<Renderer>();

        if (_zoneRenderer != null)
        {
            if (_zoneRenderer.sharedMaterial != null)
            {
                _instanceMat = new Material(_zoneRenderer.sharedMaterial);
                _originalColor = _instanceMat.color;
                _zoneRenderer.material = _instanceMat;
            }
            // Dedicated highlights start hidden; shared meshes stay visible
            if (_hideRendererWhenInactive)
                _zoneRenderer.enabled = false;
        }
    }

    private void Update()
    {
        if (_zoneRenderer == null) return;

        // Check if player is holding an item that matches this zone (static accessor — no scene scan)
        _playerHoldingMatch = false;
        var held = ObjectGrabber.HeldObject;
        if (held != null)
        {
            // Name match (home zone or alt)
            if (!string.IsNullOrEmpty(_zoneName)
                && (held.HomeZoneName == _zoneName || held.AltHomeZoneName == _zoneName))
            {
                // Shoes must be paired to place at the shoe station
                var pairable = held.GetComponent<PairableItem>();
                if (pairable != null && pairable.Mode == PairableItem.PairMode.SpecificPartner)
                    _playerHoldingMatch = pairable.IsPaired;
                else
                    _playerHoldingMatch = true;
            }

            // Trash highlights trash cans (zone name "TrashCan"), dishes highlight sink (zone name "Sink")
            if (!_playerHoldingMatch && _destroyOnDeposit)
            {
                if (held.Category == ItemCategory.Trash && _zoneName == "TrashCan")
                    _playerHoldingMatch = true;
                else if (held.Category == ItemCategory.Dish && _zoneName == "Sink")
                    _playerHoldingMatch = true;
            }
        }

        if (_playerHoldingMatch)
        {
            if (!_zoneRenderer.enabled)
                _zoneRenderer.enabled = true;

            if (_instanceMat != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _pulseSpeed * Mathf.PI * 2f);
                _instanceMat.color = Color.Lerp(_originalColor, _activeColor, pulse);
            }
        }
        else if (_pulsing)
        {
            if (_hideRendererWhenInactive)
            {
                // Dedicated highlight quad — hide it
                if (_zoneRenderer.enabled)
                    _zoneRenderer.enabled = false;
            }
            else
            {
                // Shared mesh (fridge door etc.) — restore color, keep visible
                if (_instanceMat != null)
                    _instanceMat.color = _originalColor;
            }
        }
        _pulsing = _playerHoldingMatch;
    }

    /// <summary>
    /// Register an item deposit. If destroyOnDeposit, the item arcs into the
    /// bounce target with a cute spin + squish, then destroys.
    /// </summary>
    public void RegisterDeposit(PlaceableObject item)
    {
        if (item == null) return;

        // Slot zones derive their count from live occupancy — only the
        // legacy non-slot path needs to bump a counter here.
        if (!_useSlotting) _legacyDepositCount++;
        item.IsAtHome = true;

        if (_destroyOnDeposit)
        {
            if (_trashSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_trashSFX);

            StartCoroutine(ArcBounceAndDestroy(item.gameObject));
        }
        else
        {
            if (_depositSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_depositSFX);
        }

        Debug.Log($"[DropZone] {item.name} deposited at {_zoneName}. Total: {DepositCount}");
    }

    // Reusable buffer for slot occupancy queries — avoids per-call allocation.
    private static readonly Collider[] s_slotOverlapBuffer = new Collider[8];

    /// <summary>
    /// Find the first free slot for a deposit. Returns false if slotting is
    /// disabled, the rack is full, or the slot config is degenerate. The
    /// returned position is in world space; rotation matches this transform.
    /// </summary>
    public bool TryGetNextDepositSlot(out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = default;
        worldRot = default;
        if (!_useSlotting || _slotCount <= 0) return false;

        Vector3 axis = _slotLocalAxis.sqrMagnitude > 0.0001f
            ? _slotLocalAxis.normalized
            : Vector3.right;

        for (int i = 0; i < _slotCount; i++)
        {
            Vector3 localSlot = _slotLocalOrigin + axis * (_slotSpacing * i);
            Vector3 candidate = transform.TransformPoint(localSlot);
            if (!IsSlotOccupied(candidate))
            {
                worldPos = candidate;
                worldRot = transform.rotation;
                return true;
            }
        }
        return false;
    }

    private bool IsSlotOccupied(Vector3 worldPos)
    {
        int count = Physics.OverlapSphereNonAlloc(worldPos, _slotOccupancyRadius, s_slotOverlapBuffer);
        var held = ObjectGrabber.HeldObject;
        for (int i = 0; i < count; i++)
        {
            var po = s_slotOverlapBuffer[i].GetComponentInParent<PlaceableObject>();
            if (po == null) continue;
            if (held != null && (po == held || po.transform.IsChildOf(held.transform))) continue;
            return true;
        }
        return false;
    }

    /// <summary>How many slots currently have a PlaceableObject sitting in them.</summary>
    public int CountOccupiedSlots()
    {
        if (!_useSlotting || _slotCount <= 0) return 0;

        Vector3 axis = _slotLocalAxis.sqrMagnitude > 0.0001f
            ? _slotLocalAxis.normalized
            : Vector3.right;

        int n = 0;
        for (int i = 0; i < _slotCount; i++)
        {
            Vector3 localSlot = _slotLocalOrigin + axis * (_slotSpacing * i);
            Vector3 candidate = transform.TransformPoint(localSlot);
            if (IsSlotOccupied(candidate)) n++;
        }
        return n;
    }

    /// <summary>
    /// Peek at the next free slot WITHOUT claiming it. Used by ObjectGrabber's
    /// hover-snap so the held shoe visually locks to the slot it would land in.
    /// </summary>
    public bool TryPeekNextDepositSlot(out Vector3 worldPos, out Quaternion worldRot)
    {
        // Currently identical to TryGetNextDepositSlot — slots are reserved by
        // physical occupancy, not state, so peek and take are the same op.
        return TryGetNextDepositSlot(out worldPos, out worldRot);
    }

    /// <summary>
    /// Cute bounce-in destroy: arcs the deposited item from its drop position
    /// up over the bounce target with a spin, lands with a squish-pop, then
    /// shrinks and destroys. Disables physics + colliders for the duration so
    /// the item doesn't fall or hit anything during the animation.
    /// </summary>
    private IEnumerator ArcBounceAndDestroy(GameObject go)
    {
        if (go == null) yield break;

        // Freeze physics so the arc isn't fighting gravity / collisions.
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        var cols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].enabled = false;

        // Render on top of the trash can during the arc animation
        var renderers = go.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].material.renderQueue = 4000;

        Vector3 startPos = go.transform.position;
        Quaternion startRot = go.transform.rotation;
        Vector3 startScale = go.transform.localScale;

        Vector3 targetPos = _bounceTarget != null
            ? _bounceTarget.position
            : transform.position;

        // ── Phase A: arc + spin ──────────────────────────────────────
        float elapsed = 0f;
        float arcDur = Mathf.Max(0.05f, _bounceArcDuration);
        while (elapsed < arcDur)
        {
            if (go == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / arcDur);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            // Linear interpolation in XZ, parabolic add in Y
            Vector3 lerped = Vector3.Lerp(startPos, targetPos, ease);
            float arcLift = Mathf.Sin(t * Mathf.PI) * _bounceArcHeight;
            lerped.y += arcLift;
            go.transform.position = lerped;

            // Spin around world Y as it flies
            float spinT = ease * _bounceSpinDegrees;
            go.transform.rotation = startRot * Quaternion.Euler(0f, spinT, 0f);

            yield return null;
        }
        if (go == null) yield break;
        go.transform.position = targetPos;

        // ── Phase B: squish-pop on impact ────────────────────────────
        float settle = Mathf.Max(0.05f, _bounceSettleDuration);
        elapsed = 0f;
        Vector3 squished = new Vector3(
            startScale.x * (1f + (1f - _bounceSquishY) * 0.5f),
            startScale.y * _bounceSquishY,
            startScale.z * (1f + (1f - _bounceSquishY) * 0.5f));

        // Half the duration: squash down
        while (elapsed < settle * 0.5f)
        {
            if (go == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (settle * 0.5f));
            go.transform.localScale = Vector3.Lerp(startScale, squished, t);
            yield return null;
        }
        // Other half: pop back up to slightly oversized then snap to start
        Vector3 popped = startScale * 1.1f;
        elapsed = 0f;
        while (elapsed < settle * 0.5f)
        {
            if (go == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (settle * 0.5f));
            go.transform.localScale = Vector3.Lerp(squished, popped, t);
            yield return null;
        }

        // ── Phase C: drop into the can + shrink to nothing ───────────
        const float dropDur = 0.18f;
        elapsed = 0f;
        Vector3 sinkOffset = Vector3.down * 0.18f;
        Vector3 sinkStart = go.transform.position;
        while (elapsed < dropDur)
        {
            if (go == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dropDur);
            go.transform.position = sinkStart + sinkOffset * t;
            go.transform.localScale = Vector3.Lerp(popped, Vector3.zero, t);
            yield return null;
        }

        if (go != null) Destroy(go);
    }

    private void OnDestroy()
    {
        if (_instanceMat != null)
            Destroy(_instanceMat);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Draw the slot row in the Scene view so it can be tuned without play-testing.
    /// Solid green = origin slot, hollow yellow = subsequent slots, magenta arrow = layout axis.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!_useSlotting || _slotCount <= 0) return;

        Vector3 axis = _slotLocalAxis.sqrMagnitude > 0.0001f
            ? _slotLocalAxis.normalized
            : Vector3.right;

        for (int i = 0; i < _slotCount; i++)
        {
            Vector3 localSlot = _slotLocalOrigin + axis * (_slotSpacing * i);
            Vector3 worldSlot = transform.TransformPoint(localSlot);

            Gizmos.color = (i == 0)
                ? new Color(0.2f, 1f, 0.3f, 0.85f)   // first slot: bright green
                : new Color(1f, 0.85f, 0.2f, 0.7f);  // others: warm yellow

            Gizmos.DrawWireSphere(worldSlot, _slotOccupancyRadius);

            // Slot index label using the Handles API
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label(worldSlot + Vector3.up * (_slotOccupancyRadius + 0.01f),
                $"slot {i}");
        }

        // Layout axis arrow from origin
        Vector3 originWorld = transform.TransformPoint(_slotLocalOrigin);
        Vector3 endWorld = transform.TransformPoint(_slotLocalOrigin + axis * (_slotSpacing * (_slotCount - 1)));
        Gizmos.color = new Color(1f, 0.3f, 1f, 0.6f); // magenta
        Gizmos.DrawLine(originWorld, endWorld);
    }
#endif
}
