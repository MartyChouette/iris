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

    [Header("Door")]
    [Tooltip("Empty at the hinge edge — door mesh is a child of this.")]
    [SerializeField] private Transform _doorPivot;

    [Tooltip("Degrees to rotate (negative = opens outward).")]
    [SerializeField] private float _openAngle = -110f;

    [Tooltip("Seconds for the open / close tween.")]
    [SerializeField] private float _tweenDuration = 0.6f;

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

    private enum DoorState { Closed, Opening, Open, Closing }
    private DoorState _state = DoorState.Closed;

    private Quaternion _closedRotation;
    private Quaternion _openRotation;

    private Coroutine _blinkCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FridgeController] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_doorPivot != null)
        {
            _closedRotation = _doorPivot.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, _openAngle, 0f);
        }

        if (_interiorLight != null)
            _interiorLight.enabled = false;
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>True when the fridge door is open.</summary>
    public bool IsOpen => _state == DoorState.Open;

    private void Update()
    {
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase) return;
        if (_state != DoorState.Closed && _state != DoorState.Open) return;
        if (IrisInput.Instance == null || !IrisInput.Instance.Click.WasPressedThisFrame()) return;
        if (ApartmentManager.Instance == null) return;
        if (ApartmentManager.Instance.CurrentState != ApartmentManager.State.Browsing) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;
        if (_mainCamera == null) return;

        // Holding an item while fridge is open → try to deposit
        if (ObjectGrabber.IsHoldingObject && _state == DoorState.Open)
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

        if (_state == DoorState.Closed)
        {
            Debug.Log("[FridgeController] Fridge clicked — opening door.");
            StartCoroutine(OpenDoorSequence());
        }
        else if (_state == DoorState.Open)
        {
            Debug.Log("[FridgeController] Fridge clicked — closing door.");
            StartCoroutine(CloseDoorSequence());
        }
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

    private IEnumerator OpenDoorSequence()
    {
        _state = DoorState.Opening;

        if (_openSFX != null)
            AudioManager.Instance?.PlaySFX(_openSFX);

        yield return TweenDoor(_closedRotation, _openRotation);
        _state = DoorState.Open;

        if (_interiorLight != null)
            _interiorLight.enabled = true;
    }

    /// <summary>
    /// Called when exiting the DrinkMaking station to close the fridge door.
    /// </summary>
    public void CloseDoor()
    {
        if (_state != DoorState.Open) return;
        StartCoroutine(CloseDoorSequence());
    }

    /// <summary>
    /// Immediately snap the fridge shut regardless of current state.
    /// Used by sleep/reset sequences where we can't wait for tweens.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        _state = DoorState.Closed;
        if (_doorPivot != null)
            _doorPivot.localRotation = _closedRotation;
        if (_interiorLight != null)
            _interiorLight.enabled = false;
    }

    private IEnumerator CloseDoorSequence()
    {
        _state = DoorState.Closing;

        if (_closeSFX != null)
            AudioManager.Instance?.PlaySFX(_closeSFX);

        yield return TweenDoor(_openRotation, _closedRotation);
        _state = DoorState.Closed;

        if (_interiorLight != null)
            _interiorLight.enabled = false;
    }

    private IEnumerator TweenDoor(Quaternion from, Quaternion to)
    {
        if (_doorPivot == null) yield break;

        float elapsed = 0f;
        while (elapsed < _tweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _tweenDuration);
            // Smooth step for a nicer feel
            t = t * t * (3f - 2f * t);
            _doorPivot.localRotation = Quaternion.Lerp(from, to, t);
            yield return null;
        }
        _doorPivot.localRotation = to;
    }

}
