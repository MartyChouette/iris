using UnityEngine;

/// <summary>
/// Drop zone for dirty dishes. Pulses a highlight color — brighter when
/// the player is holding a plate. Counts deposited plates and disables them.
/// </summary>
public class DishDropZone : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("Renderer for the zone highlight quad.")]
    [SerializeField] private Renderer _zoneRenderer;

    [Tooltip("Idle pulse color (no plate held).")]
    [SerializeField] private Color _idleColor = new Color(0.3f, 0.8f, 0.4f, 0.35f);

    [Tooltip("Active pulse color (plate held).")]
    [SerializeField] private Color _activeColor = new Color(0.4f, 1.0f, 0.5f, 0.6f);

    [Tooltip("Pulse speed (oscillations per second).")]
    [SerializeField] private float _pulseSpeed = 2f;

    [Header("Stacking")]
    [Tooltip("Y offset per deposited plate.")]
    [SerializeField] private float _stackOffset = 0.03f;

    public int DepositCount { get; private set; }

    private Material _instanceMat;
    private bool _playerHoldingPlate;

    private void Start()
    {
        if (_zoneRenderer == null)
            _zoneRenderer = GetComponent<Renderer>();

        if (_zoneRenderer != null && _zoneRenderer.sharedMaterial != null)
        {
            _instanceMat = new Material(_zoneRenderer.sharedMaterial);
            _zoneRenderer.material = _instanceMat;
        }
    }

    private void Update()
    {
        // Check if the held object is a plate (uses static accessor — no scene scan)
        var held = ObjectGrabber.HeldObject;
        _playerHoldingPlate = held != null && held.GetComponent<StackablePlate>() != null;

        // Pulse color
        if (_instanceMat != null)
        {
            Color baseColor = _playerHoldingPlate ? _activeColor : _idleColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * _pulseSpeed * Mathf.PI * 2f);
            _instanceMat.color = Color.Lerp(baseColor * 0.6f, baseColor, pulse);
        }
    }

    /// <summary>
    /// Register a plate deposit. Disables the plate's PlaceableObject and collider.
    /// </summary>
    public void RegisterDeposit(StackablePlate plate)
    {
        if (plate == null) return;

        DepositCount++;

        // Mark as at home and clear smell
        var placeable = plate.GetComponent<PlaceableObject>();
        if (placeable != null)
        {
            placeable.IsAtHome = true;
            placeable.enabled = false;
        }

        var tag = plate.GetComponent<ReactableTag>();
        if (tag != null)
            tag.SmellAmount = 0f;

        // Dismiss flies after a short delay — hide plate renderers but keep
        // the transform alive so flies have a valid orbit target while exiting.
        StartCoroutine(DismissFliesDelayed(plate.gameObject, 2f));

        // Hide plate visually (renderers off, not GameObject — transform stays for flies)
        foreach (var r in plate.GetComponentsInChildren<Renderer>())
            r.enabled = false;

        var col = plate.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        var rb = plate.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
        }

        // Snap plate to stacked position on the zone
        plate.transform.SetParent(transform);
        plate.transform.localPosition = Vector3.up * ((DepositCount - 1) * _stackOffset + 0.03f);
        plate.transform.localRotation = Quaternion.identity;

        // Play SFX
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(null); // placeholder — wire actual clip later

        Debug.Log($"[DishDropZone] Plate deposited. Total: {DepositCount}");
    }

    private System.Collections.IEnumerator DismissFliesDelayed(GameObject plateGO, float delay)
    {
        yield return new WaitForSeconds(delay);
        FlyController.DismissFliesFor(plateGO.transform);
        // Give flies time to animate out, then fully deactivate the plate
        yield return new WaitForSeconds(1f);
        if (plateGO != null) plateGO.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_instanceMat != null)
            Destroy(_instanceMat);
    }
}
