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
    [Tooltip("Left door pivot (hinge on left edge). Collider on its child determines click target.")]
    [SerializeField] private Transform _doorPivotL;

    [Tooltip("Right door pivot (hinge on right edge). Collider on its child determines click target.")]
    [SerializeField] private Transform _doorPivotR;

    [Tooltip("Degrees to rotate the left door (negative = opens outward).")]
    [SerializeField] private float _openAngleL = -110f;

    [Tooltip("Degrees to rotate the right door (mirrored — positive = opens outward).")]
    [SerializeField] private float _openAngleR = 110f;

    [Tooltip("Axis the doors rotate around in WORLD space. (0,1,0) = vertical hinge for standard fridges. Ignores pivot's local orientation so imported models with baked rotations still work.")]
    [SerializeField] private Vector3 _hingeAxis = Vector3.up;

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

    private Quaternion _closedRotL, _openRotL;
    private Quaternion _closedRotR, _openRotR;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FridgeController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_doorPivotL != null)
        {
            _closedRotL = _doorPivotL.localRotation;
            // Convert world-space hinge axis into the pivot's local space so
            // the rotation works regardless of baked import rotations/scale.
            Vector3 localAxis = _doorPivotL.parent != null
                ? _doorPivotL.parent.InverseTransformDirection(_hingeAxis.normalized)
                : _hingeAxis.normalized;
            _openRotL = _closedRotL * Quaternion.AngleAxis(_openAngleL, localAxis);
        }
        if (_doorPivotR != null)
        {
            _closedRotR = _doorPivotR.localRotation;
            Vector3 localAxis = _doorPivotR.parent != null
                ? _doorPivotR.parent.InverseTransformDirection(_hingeAxis.normalized)
                : _hingeAxis.normalized;
            _openRotR = _closedRotR * Quaternion.AngleAxis(_openAngleR, localAxis);
        }

        if (_interiorLight != null)
            _interiorLight.enabled = false;
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

        // Holding an item while a door is open → try to deposit
        if (ObjectGrabber.IsHoldingObject && IsOpen)
        {
            TryDepositHeldItem();
            return;
        }

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
            ToggleDoorIfReady(_doorPivotL, ref _stateL, _closedRotL, _openRotL);
            ToggleDoorIfReady(_doorPivotR, ref _stateR, _closedRotR, _openRotR);
        }
        else if (hitLeft)
        {
            ToggleDoorIfReady(_doorPivotL, ref _stateL, _closedRotL, _openRotL);
        }
        else if (hitRight)
        {
            ToggleDoorIfReady(_doorPivotR, ref _stateR, _closedRotR, _openRotR);
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

    private void ToggleDoorIfReady(Transform pivot, ref DoorState state, Quaternion closedRot, Quaternion openRot)
    {
        if (pivot == null || state == DoorState.Tweening) return;
        if (state == DoorState.Closed)
            StartCoroutine(TweenDoor(pivot, closedRot, openRot, true));
        else
            StartCoroutine(TweenDoor(pivot, openRot, closedRot, false));
    }

    /// <summary>Close all open doors.</summary>
    public void CloseDoor()
    {
        if (_stateL == DoorState.Open)
            StartCoroutine(TweenDoor(_doorPivotL, _openRotL, _closedRotL, false));
        if (_stateR == DoorState.Open)
            StartCoroutine(TweenDoor(_doorPivotR, _openRotR, _closedRotR, false));
    }

    /// <summary>Snap both doors shut immediately.</summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        _stateL = DoorState.Closed;
        _stateR = DoorState.Closed;
        if (_doorPivotL != null) _doorPivotL.localRotation = _closedRotL;
        if (_doorPivotR != null) _doorPivotR.localRotation = _closedRotR;
        if (_interiorLight != null) _interiorLight.enabled = false;
    }

    /// <summary>Tween a single door open or closed.</summary>
    private IEnumerator TweenDoor(Transform pivot, Quaternion from, Quaternion to, bool opening)
    {
        if (pivot == null) yield break;

        // Track which door this is
        bool isLeft = pivot == _doorPivotL;
        if (isLeft) _stateL = DoorState.Tweening;
        else        _stateR = DoorState.Tweening;

        if (opening && _openSFX != null)
            AudioManager.Instance?.PlaySFX(_openSFX);
        else if (!opening && _closeSFX != null)
            AudioManager.Instance?.PlaySFX(_closeSFX);

        float elapsed = 0f;
        while (elapsed < _tweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _tweenDuration);
            t = t * t * (3f - 2f * t); // smooth step
            pivot.localRotation = Quaternion.Lerp(from, to, t);
            yield return null;
        }
        pivot.localRotation = to;

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
