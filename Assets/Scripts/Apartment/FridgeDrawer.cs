using System.Collections;
using UnityEngine;

/// <summary>
/// Click-to-slide fridge drawer on the local Z axis. Works alongside
/// FridgeController — the drawer only operates when the fridge doors are open.
/// </summary>
public class FridgeDrawer : MonoBehaviour
{
    [Tooltip("How far the drawer slides out on its local Z axis (world units).")]
    [SerializeField] private float _slideDistance = 0.3f;

    [Tooltip("Seconds for the open / close tween.")]
    [SerializeField] private float _tweenDuration = 0.4f;

    [Tooltip("Layer mask for click detection on this drawer.")]
    [SerializeField] private LayerMask _clickLayer = ~0;

    [Tooltip("Main camera (auto-found if null).")]
    [SerializeField] private Camera _mainCamera;

    [Tooltip("If true, this drawer only opens when the fridge doors are open. Uncheck for standalone drawers outside the fridge.")]
    [SerializeField] private bool _requireFridgeOpen = true;

    [Header("Audio")]
    [SerializeField] private AudioClip _openSFX;
    [SerializeField] private AudioClip _closeSFX;

    private enum State { Closed, Tweening, Open }
    private State _state = State.Closed;
    private Vector3 _closedLocalPos;

    private void Awake()
    {
        _closedLocalPos = transform.localPosition;
        if (_mainCamera == null) _mainCamera = Camera.main;
    }

    private void Update()
    {
        if (_state == State.Tweening) return;

        // Only operate when fridge is open (if this drawer is inside the fridge)
        if (_requireFridgeOpen && FridgeController.Instance != null && !FridgeController.Instance.IsOpen) return;

        if (IrisInput.Instance == null || !IrisInput.Instance.Click.WasPressedThisFrame()) return;
        if (ObjectGrabber.ClickConsumedThisFrame) return;
        if (ObjectGrabber.IsHoldingObject) return;
        if (_mainCamera == null) return;

        Vector2 mousePos = IrisInput.CursorPosition;
        var ray = _mainCamera.ScreenPointToRay(mousePos);

        if (!Physics.Raycast(ray, out var hit, 20f, _clickLayer)) return;

        // Check if the click hit this drawer or a child of it
        if (!hit.collider.transform.IsChildOf(transform) && hit.collider.transform != transform)
            return;

        Toggle();
    }

    private void Toggle()
    {
        if (_state == State.Closed)
            StartCoroutine(Slide(true));
        else if (_state == State.Open)
            StartCoroutine(Slide(false));
    }

    /// <summary>Snap shut immediately (called on phase transitions).</summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        transform.localPosition = _closedLocalPos;
        _state = State.Closed;
    }

    private IEnumerator Slide(bool opening)
    {
        _state = State.Tweening;

        if (opening && _openSFX != null)
            AudioManager.Instance?.PlaySFX(_openSFX);
        else if (!opening && _closeSFX != null)
            AudioManager.Instance?.PlaySFX(_closeSFX);

        Vector3 from = transform.localPosition;
        Vector3 to = opening
            ? _closedLocalPos + transform.parent.InverseTransformDirection(transform.forward) * _slideDistance
            : _closedLocalPos;

        // Use local Z direction for the slide
        if (opening)
            to = _closedLocalPos + Vector3.forward * _slideDistance;

        float elapsed = 0f;
        while (elapsed < _tweenDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _tweenDuration;
            t = t * t * (3f - 2f * t); // smoothstep
            transform.localPosition = Vector3.Lerp(from, to, t);
            yield return null;
        }

        transform.localPosition = to;
        _state = opening ? State.Open : State.Closed;
    }
}
