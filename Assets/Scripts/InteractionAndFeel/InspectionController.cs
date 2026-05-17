/**
 * @file InspectionController.cs
 * @brief InspectionController script.
 * @details
 * - Auto-generated Doxygen header. Expand @details with intent, invariants, and perf notes as needed.
*
 * @ingroup tools
 */

using UnityEngine;
using TMPro;
/**
 * @class InspectionController
 * @brief InspectionController component.
 * @details
 * Responsibilities:
 * - (Documented) See fields and methods below.
 *
 * Unity lifecycle:
 * - Awake(): cache references / validate setup.
 * - OnEnable()/OnDisable(): hook/unhook events.
 * - Update(): per-frame behavior (if any).
 *
 * Gotchas:
 * - Keep hot paths allocation-free (Update/cuts/spawns).
 * - Prefer event-driven UI updates over per-frame string building.
 *
 * @ingroup tools
 */

public class InspectionController : MonoBehaviour
{
    [Header("World-space Preview")]
    [Tooltip("Empty transform in front of the camera where the preview model will be spawned. " +
             "Best: make this a child of the main camera at (0,0,previewDistance).")]
    public Transform previewRoot;

    [Tooltip("Default distance in front of the camera for the previewRoot if we auto-create one.")]
    public float previewDistance = 2.0f;

    [Header("UI Overlay")]
    [Tooltip("Canvas for the inspection UI (screen space overlay).")]
    public Canvas inspectionCanvas;

    [Tooltip("Optional dark panel behind the text/model (can be a full-screen Image).")]
    public GameObject dimBackground;

    [Tooltip("TMP text that shows the description under the preview.")]
    public TMP_Text descriptionText;

    [Header("Controls")]
    public float rotationSpeed = 150f;
    public float zoomSpeed = 2.0f;
    public float minZoom = 0.5f;
    public float maxZoom = 2.5f;

    [Tooltip("Key to close inspection besides right mouse button.")]
    public KeyCode closeKey = KeyCode.Escape;

    [Header("Debug")]
    public bool debugLogs = true;

    private Camera cam;
    private GameObject spawnedModel;
    private Vector3 defaultLocalPosition;
    private Vector3 defaultLocalScale;
    private float currentZoom = 1f;
    private bool inspecting = false;

    void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[InspectionController] No Camera.main found!", this);
        }

        // Auto-create previewRoot if not assigned
        if (previewRoot == null && cam != null)
        {
            GameObject go = new GameObject("PreviewRoot_Auto");
            previewRoot = go.transform;
            previewRoot.SetParent(cam.transform, false);
            previewRoot.localPosition = new Vector3(0f, 0f, previewDistance);
            previewRoot.localRotation = Quaternion.identity;
            if (debugLogs) Debug.Log("[InspectionController] Auto-created previewRoot under main camera.", this);
        }

        if (inspectionCanvas != null)
            inspectionCanvas.enabled = false;
    }

    void Update()
    {
        if (!inspecting)
        {
            TryStartInspection();
        }
        else
        {
            HandleInspectControls();
            HandleCloseInput();
        }
    }

    // ─────────────────────────────────────────────
    // Start inspection by clicking an object
    // ─────────────────────────────────────────────

    void TryStartInspection()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // Prefer ApartmentManager's raycast (accounts for Cinemachine brain)
        var am = ApartmentManager.Instance;
        Ray ray;
        if (am != null)
            ray = am.ScreenPointToRay(Input.mousePosition);
        else if (cam != null)
            ray = cam.ScreenPointToRay(Input.mousePosition);
        else
            return;
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            if (debugLogs) Debug.Log("[InspectionController] Raycast hit nothing.", this);
            return;
        }

        if (debugLogs) Debug.Log("[InspectionController] Raycast hit " + hit.collider.name, this);

        InspectableObject inspectable = hit.collider.GetComponentInParent<InspectableObject>();
        if (inspectable == null)
        {
            if (debugLogs) Debug.Log("[InspectionController] Hit object has no InspectableObject.", this);
            return;
        }

        BeginInspection(inspectable);
    }

    void BeginInspection(InspectableObject inspectable)
    {
        if (previewRoot == null)
        {
            Debug.LogError("[InspectionController] No previewRoot assigned/created.", this);
            return;
        }

        if (inspectionCanvas == null || descriptionText == null)
        {
            Debug.LogError("[InspectionController] UI references not set (canvas/descriptionText).", this);
            return;
        }

        // Spawn preview model
        GameObject prefab = inspectable.modelOverride != null
            ? inspectable.modelOverride
            : inspectable.gameObject;

        spawnedModel = Instantiate(prefab, previewRoot);
        spawnedModel.transform.localPosition = Vector3.zero;
        spawnedModel.transform.localRotation = Quaternion.identity;
        spawnedModel.transform.localScale = Vector3.one;

        defaultLocalPosition = spawnedModel.transform.localPosition;
        defaultLocalScale = spawnedModel.transform.localScale;
        currentZoom = 1f;

        // Optional: remove physics on the preview copy
        foreach (var rb in spawnedModel.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;
        foreach (var col in spawnedModel.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // UI on
        inspectionCanvas.enabled = true;
        if (dimBackground != null) dimBackground.SetActive(true);

        descriptionText.text = inspectable.description;
        inspecting = true;

        if (debugLogs)
            Debug.Log("[InspectionController] BeginInspection on " + inspectable.gameObject.name, this);
    }

    // ─────────────────────────────────────────────
    // Rotate + zoom
    // ─────────────────────────────────────────────

    void HandleInspectControls()
    {
        if (spawnedModel == null) return;

        // Rotate with MMB
        if (Input.GetMouseButton(2))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            spawnedModel.transform.Rotate(Vector3.up, -dx * rotationSpeed * Time.unscaledDeltaTime, Space.World);
            spawnedModel.transform.Rotate(Vector3.right, dy * rotationSpeed * Time.unscaledDeltaTime, Space.World);
        }

        // Zoom with scroll
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            currentZoom = Mathf.Clamp(currentZoom + scroll * zoomSpeed * Time.unscaledDeltaTime, minZoom, maxZoom);
            spawnedModel.transform.localScale = defaultLocalScale * currentZoom;
        }
    }

    // ─────────────────────────────────────────────
    // Close inspection
    // ─────────────────────────────────────────────

    void HandleCloseInput()
    {
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(closeKey))
        {
            EndInspection();
        }
    }

    void EndInspection()
    {
        if (spawnedModel != null)
            Destroy(spawnedModel);

        if (inspectionCanvas != null)
            inspectionCanvas.enabled = false;

        if (dimBackground != null)
            dimBackground.SetActive(false);

        inspecting = false;

        if (debugLogs)
            Debug.Log("[InspectionController] EndInspection.", this);
    }
}
