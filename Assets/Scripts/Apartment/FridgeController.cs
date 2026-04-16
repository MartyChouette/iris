using System.Collections;
using UnityEngine;

/// <summary>
/// Click-to-open fridge with slotted interior shelves. Items snap to shelf
/// slots when placed inside (magnetic snap like the shoe station). The fridge
/// is pure storage — drink-making lives on the DrinkCartController.
/// </summary>
public class FridgeController : MonoBehaviour
{
    public static FridgeController Instance { get; private set; }

    [Header("French Doors")]
    [Tooltip("Left door transform (rotated by the script). Collider on it or its children is the click target.")]
    [SerializeField] private Transform _doorPivotL;

    [Tooltip("Right door transform.")]
    [SerializeField] private Transform _doorPivotR;

    [Tooltip("Empty GameObject at the LEFT EDGE of the left door (the hinge point). If null, uses the door transform's own position.")]
    [SerializeField] private Transform _hingePointL;

    [Tooltip("Empty GameObject at the RIGHT EDGE of the right door (the hinge point). If null, uses the door transform's own position.")]
    [SerializeField] private Transform _hingePointR;

    [Tooltip("Degrees to rotate the left door (negative = opens outward).")]
    [SerializeField] private float _openAngleL = -110f;

    [Tooltip("Degrees to rotate the right door (mirrored — positive = opens outward).")]
    [SerializeField] private float _openAngleR = 110f;

    [Tooltip("Seconds for the open / close tween.")]
    [SerializeField] private float _tweenDuration = 0.6f;

    [Tooltip("If true, both doors always open and close together regardless of which one is clicked.")]
    [SerializeField] private bool _openBothTogether;

    [Header("Interaction")]
    [Tooltip("Layer mask for the fridge door collider.")]
    [SerializeField] private LayerMask _fridgeLayer;

    [Tooltip("Main camera used for raycasting.")]
    [SerializeField] private Camera _mainCamera;

    [Header("Wall Occlusion")]
    [Tooltip("Layer mask for walls that can block fridge clicks (prevents clicking through walls).")]
    [SerializeField] private LayerMask _wallOcclusionLayer;

    [Header("Interior Shelves")]
    [Tooltip("Slotted DropZones on the interior shelves (hi, mid, lo, drawer). Items snap to slots when placed. Set UseSlotting = true on each.")]
    [SerializeField] private DropZone[] _interiorShelves;

    [Header("Light")]
    [Tooltip("Point light inside the fridge — on when open, off when closed.")]
    [SerializeField] private Light _interiorLight;

    [Header("Audio")]
    [Tooltip("Played when the door opens.")]
    [SerializeField] private AudioClip _openSFX;

    [Tooltip("Played when the door closes.")]
    [SerializeField] private AudioClip _closeSFX;

    // Input managed by IrisInput singleton

    private enum DoorState { Closed, Tweening, Open }
    private DoorState _stateL = DoorState.Closed;
    private DoorState _stateR = DoorState.Closed;

    // Snapshot of each door's closed-state transform (world space)
    private Vector3 _closedPosL, _closedPosR;
    private Quaternion _closedRotL, _closedRotR;
    // Auto-computed hinge positions if no hinge transforms assigned
    private Vector3 _hingePosL, _hingePosR;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FridgeController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Snapshot closed state
        if (_doorPivotL != null)
        {
            _closedPosL = _doorPivotL.position;
            _closedRotL = _doorPivotL.rotation;
            _hingePosL = _hingePointL != null
                ? _hingePointL.position
                : FindHingeEdge(_doorPivotL, left: true);
        }
        if (_doorPivotR != null)
        {
            _closedPosR = _doorPivotR.position;
            _closedRotR = _doorPivotR.rotation;
            _hingePosR = _hingePointR != null
                ? _hingePointR.position
                : FindHingeEdge(_doorPivotR, left: false);
        }

        if (_interiorLight != null)
            _interiorLight.enabled = false;
    }

    /// <summary>
    /// Auto-detect hinge position from the door's renderer bounds.
    /// Left door hinges on its leftmost edge, right door on its rightmost.
    /// Uses world-space bounds so model rotation/scale don't matter.
    /// </summary>
    private static Vector3 FindHingeEdge(Transform door, bool left)
    {
        var renderer = door.GetComponentInChildren<Renderer>();
        if (renderer == null) return door.position;

        Bounds b = renderer.bounds;
        // Hinge at the outer edge — full height of the door, centered vertically
        float x = left ? b.min.x : b.max.x;
        return new Vector3(x, b.center.y, b.center.z);
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>True when either door is open.</summary>
    public bool IsOpen => _stateL == DoorState.Open || _stateR == DoorState.Open;

    /// <summary>True when both doors are fully closed and not tweening.</summary>
    public bool IsFullyClosed => _stateL == DoorState.Closed && _stateR == DoorState.Closed;

    private void Update()
    {
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase) return;
        if (IrisInput.Instance == null || !IrisInput.Instance.Click.WasPressedThisFrame()) return;
        if (ApartmentManager.Instance == null) return;
        if (ApartmentManager.Instance.CurrentState != ApartmentManager.State.Browsing) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;
        if (_mainCamera == null) return;

        // Don't open/close fridge while holding an item — ObjectGrabber's
        // PlacementSurface + DropZone slotting handles shelf deposits naturally
        // (magnetic snap to slot like the shoe rack).
        if (ObjectGrabber.IsHoldingObject) return;

        Vector2 mousePos = IrisInput.CursorPosition;
        var ray = _mainCamera.ScreenPointToRay(mousePos);

        if (!Physics.Raycast(ray, out var fridgeHit, 20f, _fridgeLayer)) return;

        // Wall occlusion
        if (_wallOcclusionLayer.value != 0
            && Physics.Raycast(ray, out var wallHit, 20f, _wallOcclusionLayer)
            && wallHit.distance < fridgeHit.distance)
            return;

        // Determine which door was clicked by checking if the hit collider
        // is a child of the left or right pivot.
        bool hitLeft = _doorPivotL != null && fridgeHit.collider.transform.IsChildOf(_doorPivotL);
        bool hitRight = _doorPivotR != null && fridgeHit.collider.transform.IsChildOf(_doorPivotR);

        // "Both together" mode or clicked the body → treat as both
        if (_openBothTogether || (!hitLeft && !hitRight))
        {
            ToggleDoorIfReady(_doorPivotL, ref _stateL, true);
            ToggleDoorIfReady(_doorPivotR, ref _stateR, false);
        }
        else if (hitLeft)
        {
            ToggleDoorIfReady(_doorPivotL, ref _stateL, true);
        }
        else if (hitRight)
        {
            ToggleDoorIfReady(_doorPivotR, ref _stateR, false);
        }

        UpdateInteriorLight();
    }

    /// <summary>
    /// Try to place the held item into the nearest interior shelf slot.
    /// Rejects with a message if all shelves are full.
    /// </summary>
    private void TryDepositHeldItem()
    {
        var held = ObjectGrabber.HeldObject;
        if (held == null) return;

        // Raycast to confirm player clicked on the fridge
        if (_mainCamera == null) return;
        Vector2 mousePos = IrisInput.CursorPosition;
        var ray = _mainCamera.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out _, 20f, _fridgeLayer)) return;

        // Find the first shelf with a free slot
        DropZone targetShelf = null;
        Vector3 slotPos = Vector3.zero;
        Quaternion slotRot = Quaternion.identity;

        if (_interiorShelves != null)
        {
            for (int i = 0; i < _interiorShelves.Length; i++)
            {
                var shelf = _interiorShelves[i];
                if (shelf == null) continue;
                if (shelf.TryGetNextDepositSlot(out slotPos, out slotRot))
                {
                    targetShelf = shelf;
                    break;
                }
            }
        }

        if (targetShelf == null)
        {
            // All shelves full
            if (DialoguePortraitBox.Instance != null)
                DialoguePortraitBox.Instance.Say("No more space in the fridge.");
            else
                PickupDescriptionHUD.Instance?.Show("No more space.");
            return;
        }

        // Place the item at the shelf slot
        held.OnPlaced(null, true, slotPos, slotRot);

        // Lock in place
        var rb = held.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
        }

        targetShelf.RegisterDeposit(held);
        ObjectGrabber.ForceReleaseHeld();
        ObjectGrabber.ConsumeClickExternal();

        if (_openSFX != null)
            AudioManager.Instance?.PlaySFX(_openSFX);

        Debug.Log($"[FridgeController] Stored {held.name} on shelf '{targetShelf.ZoneName}'.");
    }

    /// <summary>
    /// Returns true if the given surface is a fridge interior shelf
    /// and the fridge doors are closed. ObjectGrabber uses this to
    /// block placement on shelves the player can't reach.
    /// </summary>
    public bool IsShelfAndClosed(PlacementSurface surface)
    {
        if (surface == null || IsOpen) return false;
        if (_interiorShelves == null) return false;
        for (int i = 0; i < _interiorShelves.Length; i++)
        {
            if (_interiorShelves[i] == null) continue;
            var shelfSurface = _interiorShelves[i].GetComponent<PlacementSurface>();
            if (shelfSurface == null)
                shelfSurface = _interiorShelves[i].GetComponentInParent<PlacementSurface>();
            if (shelfSurface == surface) return true;
        }
        return false;
    }

    /// <summary>Total free slots across all interior shelves.</summary>
    public int TotalFreeSlots
    {
        get
        {
            if (_interiorShelves == null) return 0;
            int free = 0;
            for (int i = 0; i < _interiorShelves.Length; i++)
            {
                var shelf = _interiorShelves[i];
                if (shelf == null || !shelf.UseSlotting) continue;
                free += shelf.SlotCount - shelf.DepositCount;
            }
            return free;
        }
    }

    private void ToggleDoorIfReady(Transform pivot, ref DoorState state, bool isLeft)
    {
        if (pivot == null || state == DoorState.Tweening) return;
        float angle = isLeft ? _openAngleL : _openAngleR;
        if (state == DoorState.Closed)
            StartCoroutine(TweenDoor(pivot, angle, true, isLeft));
        else
            StartCoroutine(TweenDoor(pivot, -angle, false, isLeft));
    }

    /// <summary>Close all open doors. Skips doors that are currently tweening to avoid concurrent coroutines fighting over the same transform.</summary>
    public void CloseDoor()
    {
        if (_stateL == DoorState.Open)
            StartCoroutine(TweenDoor(_doorPivotL, -_openAngleL, false, true));
        if (_stateR == DoorState.Open)
            StartCoroutine(TweenDoor(_doorPivotR, -_openAngleR, false, false));
        // Tweening states are intentionally not closed — the open tween will finish
        // and the player can click again. Stacking a close tween on top of an open
        // tween causes overshoot.
    }

    /// <summary>Snap both doors shut immediately.</summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        _stateL = DoorState.Closed;
        _stateR = DoorState.Closed;
        if (_doorPivotL != null) { _doorPivotL.position = _closedPosL; _doorPivotL.rotation = _closedRotL; }
        if (_doorPivotR != null) { _doorPivotR.position = _closedPosR; _doorPivotR.rotation = _closedRotR; }
        if (_interiorLight != null) _interiorLight.enabled = false;
    }

    /// <summary>
    /// Tween a door open or closed using RotateAround.
    /// Works regardless of pivot position — the hinge point is separate.
    /// </summary>
    private IEnumerator TweenDoor(Transform pivot, float totalAngle, bool opening, bool isLeft)
    {
        if (pivot == null) yield break;

        if (isLeft) _stateL = DoorState.Tweening;
        else        _stateR = DoorState.Tweening;

        if (opening && _openSFX != null)
            AudioManager.Instance?.PlaySFX(_openSFX);
        else if (!opening && _closeSFX != null)
            AudioManager.Instance?.PlaySFX(_closeSFX);

        Vector3 hingePos = isLeft ? _hingePosL : _hingePosR;

        float rotated = 0f;
        float elapsed = 0f;
        while (elapsed < _tweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _tweenDuration);
            t = t * t * (3f - 2f * t); // smooth step

            float target = totalAngle * t;
            float delta = target - rotated;
            pivot.RotateAround(hingePos, Vector3.up, delta);
            rotated = target;

            yield return null;
        }

        // Snap to exact final angle
        float remaining = totalAngle - rotated;
        if (Mathf.Abs(remaining) > 0.001f)
            pivot.RotateAround(hingePos, Vector3.up, remaining);

        if (isLeft) _stateL = opening ? DoorState.Open : DoorState.Closed;
        else        _stateR = opening ? DoorState.Open : DoorState.Closed;

        UpdateInteriorLight();
    }

    private void UpdateInteriorLight()
    {
        if (_interiorLight != null)
            _interiorLight.enabled = IsOpen;
    }

}
