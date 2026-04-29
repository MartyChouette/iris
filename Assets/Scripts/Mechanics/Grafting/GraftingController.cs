using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-scoped singleton for the grafting mechanic. Player picks parts off
/// a donor flower and places them onto slots on a target flower.
/// State machine: Idle -> Holding -> Placing -> Idle.
/// </summary>
[DisallowMultipleComponent]
public class GraftingController : MonoBehaviour
{
    public static GraftingController Instance { get; private set; }

    public enum State { Idle, Holding }

    [Header("References")]
    [Tooltip("The donor flower brain (parts come from here).")]
    public FlowerGameBrain donorBrain;

    [Tooltip("The target flower brain (parts are placed here).")]
    public FlowerGameBrain targetBrain;

    [Tooltip("Camera for raycasting.")]
    public Camera mainCamera;

    [Header("Placement")]
    [Tooltip("Layer mask for picking up flower parts.")]
    public LayerMask partLayer = ~0;

    [Tooltip("Max distance from slot center to accept a graft.")]
    public float placementTolerance = 0.1f;

    [Header("Audio")]
    public AudioClip graftSFX;
    public AudioClip failSFX;

    [Header("Debug")]
    public State currentState = State.Idle;

    // Input
    private InputAction _clickAction;
    private InputAction _pointerAction;
    private InputAction _cancelAction;

    // Holding state
    private FlowerPartRuntime _heldPart;
    private Transform _heldOriginalParent;
    private Vector3 _heldOriginalLocalPos;
    private Quaternion _heldOriginalLocalRot;
    private Rigidbody _heldRb;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        _clickAction = new InputAction("GraftClick", InputActionType.Button, "<Mouse>/leftButton");
        _pointerAction = new InputAction("GraftPointer", InputActionType.Value, "<Mouse>/position");
        _cancelAction = new InputAction("GraftCancel", InputActionType.Button, "<Mouse>/rightButton");

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        _clickAction.Enable();
        _pointerAction.Enable();
        _cancelAction.Enable();
    }

    void OnDisable()
    {
        _clickAction.Disable();
        _pointerAction.Disable();
        _cancelAction.Disable();
    }

    void Update()
    {
        switch (currentState)
        {
            case State.Idle:
                UpdateIdle();
                break;
            case State.Holding:
                UpdateHolding();
                break;
        }
    }

    private void UpdateIdle()
    {
        if (!_clickAction.WasPressedThisFrame()) return;

        Vector2 pointer = _pointerAction.ReadValue<Vector2>();
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(pointer) : mainCamera.ScreenPointToRay(pointer);

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, partLayer)) return;

        var part = hit.collider.GetComponentInParent<FlowerPartRuntime>();
        if (part == null) return;

        // Must be on the donor flower
        if (donorBrain == null || !donorBrain.parts.Contains(part)) return;
        if (!part.isAttached) return;
        if (part.kind == FlowerPartKind.Crown) return; // can't graft the crown

        PickUpPart(part);
    }

    private void UpdateHolding()
    {
        if (_heldPart == null)
        {
            currentState = State.Idle;
            return;
        }

        // Cancel
        if (_cancelAction.WasPressedThisFrame())
        {
            ReturnPartToDonor();
            return;
        }

        // Follow cursor in world space
        Vector2 pointer = _pointerAction.ReadValue<Vector2>();
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(pointer) : mainCamera.ScreenPointToRay(pointer);
        Plane worldPlane = new Plane(Vector3.forward, _heldPart.transform.position);
        if (worldPlane.Raycast(ray, out float enter))
        {
            _heldPart.transform.position = ray.GetPoint(enter);
        }

        // Place on click
        if (_clickAction.WasPressedThisFrame())
        {
            TryPlace();
        }
    }

    private void PickUpPart(FlowerPartRuntime part)
    {
        _heldPart = part;
        _heldOriginalParent = part.transform.parent;
        _heldOriginalLocalPos = part.transform.localPosition;
        _heldOriginalLocalRot = part.transform.localRotation;

        // Detach from donor
        part.MarkDetached("Grafted", FlowerPartRuntime.DetachReason.PlayerRipped, true);

        // Disable physics while held
        _heldRb = part.GetComponent<Rigidbody>();
        if (_heldRb != null)
            _heldRb.isKinematic = true;

        currentState = State.Holding;
        Debug.Log($"[GraftingController] Picked up {part.name}.");
    }

    private void TryPlace()
    {
        if (targetBrain == null || _heldPart == null) return;

        // Find nearest empty GraftableSlot
        var slots = targetBrain.GetComponentsInChildren<GraftableSlot>();
        GraftableSlot bestSlot = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].isOccupied) continue;
            if (slots[i].acceptedKind != _heldPart.kind) continue;

            float dist = Vector3.Distance(_heldPart.transform.position, slots[i].transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSlot = slots[i];
            }
        }

        if (bestSlot != null && bestDist <= placementTolerance)
        {
            PlacePart(bestSlot);
        }
        else
        {
            // Failed placement
            if (AudioManager.Instance != null && failSFX != null)
                AudioManager.Instance.PlaySFX(failSFX);
            Debug.Log("[GraftingController] No valid slot nearby.");
        }
    }

    private void PlacePart(GraftableSlot slot)
    {
        // Snap to slot
        _heldPart.transform.SetParent(slot.transform.parent, true);
        _heldPart.transform.position = slot.transform.position;
        _heldPart.transform.rotation = slot.transform.rotation;

        // Re-enable physics
        if (_heldRb != null)
            _heldRb.isKinematic = false;

        // Mark as attached on the target brain
        _heldPart.isAttached = true;
        _heldPart.condition = FlowerPartCondition.Normal;
        _heldPart.permanentlyDetached = false;

        if (!targetBrain.parts.Contains(_heldPart))
            targetBrain.parts.Add(_heldPart);

        // Fill the slot
        slot.isOccupied = true;
        slot.occupant = _heldPart;
        slot.UpdateVisual();

        if (AudioManager.Instance != null && graftSFX != null)
            AudioManager.Instance.PlaySFX(graftSFX);

        Debug.Log($"[GraftingController] Grafted {_heldPart.name} onto slot {slot.name}.");

        _heldPart = null;
        _heldRb = null;
        currentState = State.Idle;
    }

    private void ReturnPartToDonor()
    {
        if (_heldPart == null) return;

        // Re-parent to original
        _heldPart.transform.SetParent(_heldOriginalParent, true);
        _heldPart.transform.localPosition = _heldOriginalLocalPos;
        _heldPart.transform.localRotation = _heldOriginalLocalRot;

        _heldPart.isAttached = true;
        _heldPart.permanentlyDetached = false;

        if (_heldRb != null)
            _heldRb.isKinematic = false;

        Debug.Log($"[GraftingController] Returned {_heldPart.name} to donor.");

        _heldPart = null;
        _heldRb = null;
        currentState = State.Idle;
    }

    /// <summary>Number of available donor parts (non-crown, attached).</summary>
    public int DonorPartCount
    {
        get
        {
            if (donorBrain == null) return 0;
            int count = 0;
            for (int i = 0; i < donorBrain.parts.Count; i++)
            {
                var p = donorBrain.parts[i];
                if (p != null && p.isAttached && p.kind != FlowerPartKind.Crown) count++;
            }
            return count;
        }
    }

    /// <summary>Number of filled target slots.</summary>
    public int FilledSlotCount
    {
        get
        {
            if (targetBrain == null) return 0;
            var slots = targetBrain.GetComponentsInChildren<GraftableSlot>();
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].isOccupied) count++;
            }
            return count;
        }
    }

    /// <summary>Total target slot count.</summary>
    public int TotalSlotCount
    {
        get
        {
            if (targetBrain == null) return 0;
            return targetBrain.GetComponentsInChildren<GraftableSlot>().Length;
        }
    }
}
