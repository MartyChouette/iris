using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ScissorsCutController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The newspaper surface for stamping cuts.")]
    [SerializeField] private NewspaperSurface surface;

    [Tooltip("Camera used for raycasting.")]
    [SerializeField] private Camera cam;

    [Tooltip("Layer mask for the newspaper surface.")]
    [SerializeField] private LayerMask newspaperLayer;

    [Tooltip("3D scissors visual that trails behind the cursor.")]
    [SerializeField] private Transform scissorsVisual;

    [Header("Cut Line")]
    [Tooltip("Minimum world distance between path points.")]
    [SerializeField] private float minPointDistance = 0.005f;

    [Tooltip("Max total path length before auto-close.")]
    [SerializeField] private float maxPathLength = 5f;

    [Tooltip("Color of the dotted cut line.")]
    [SerializeField] private Color cutLineColor = Color.black;

    [Tooltip("Width of the dotted cut line.")]
    [SerializeField] private float lineWidth = 0.003f;

    [Tooltip("Dash density — higher values produce more dashes per world unit.")]
    [SerializeField] private float dashesPerUnit = 40f;

    [Header("Scissors")]
    [Tooltip("Speed of the scissors trailing the cursor (world units/sec).")]
    [SerializeField] private float scissorsSpeed = 0.5f;

    [Tooltip("Offset from surface along hit normal for scissors/line visuals.")]
    [SerializeField] private float surfaceOffset = 0.001f;

    [Header("Close Tolerance")]
    [Tooltip("Max UV distance from end to start to close the polygon.")]
    [SerializeField] private float closeTolerance = 0.05f;

    [Header("Audio")]
    [Tooltip("Looping cut sound while scissors move.")]
    [SerializeField] private AudioClip cutLoopSFX;

    [Tooltip("Played when loop closes.")]
    [SerializeField] private AudioClip cutCompleteSFX;

    [Header("Events")]
    [Tooltip("Fires with UV polygon points when cut loop closes.")]
    public UnityEvent<List<Vector2>> OnCutComplete;

    // Inline input actions
    private InputAction _mousePosition;
    private InputAction _mouseClick;

    // State
    private bool _isEnabled;
    private bool _isDrawing;
    private List<Vector3> _pathWorldPoints = new List<Vector3>();
    private List<Vector2> _pathUVPoints = new List<Vector2>();
    private float _drawnArcLength;
    private float _scissorsArcLength;
    private int _scissorsLastCutIndex;
    private LineRenderer _lineRenderer;
    private Texture2D _dashTexture;
    private Material _lineMaterial;

    // Pre-computed cumulative arc lengths for each point
    private List<float> _cumulativeArcLengths = new List<float>();

    // Cached surface normal (computed once when enabled)
    private Vector3 _surfaceNormal;

    // ─── Lifecycle ────────────────────────────────────────────────

    private void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        _mousePosition = new InputAction("MousePos", InputActionType.Value, "<Mouse>/position");
        _mouseClick = new InputAction("MouseClick", InputActionType.Button, "<Mouse>/leftButton");

        // Procedural dash texture (half opaque, half transparent)
        _dashTexture = new Texture2D(8, 2, TextureFormat.RGBA32, false);
        _dashTexture.filterMode = FilterMode.Point;
        _dashTexture.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color32[16];
        for (int i = 0; i < 16; i++)
        {
            int x = i % 8;
            pixels[i] = x < 4
                ? new Color32(255, 255, 255, 255)
                : new Color32(0, 0, 0, 0);
        }
        _dashTexture.SetPixels32(pixels);
        _dashTexture.Apply();

        // Dotted line renderer
        _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.positionCount = 0;
        _lineRenderer.startWidth = lineWidth;
        _lineRenderer.endWidth = lineWidth;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices = 2;
        _lineRenderer.textureMode = LineTextureMode.Tile;

        _lineMaterial = new Material(Shader.Find("Sprites/Default"));
        _lineMaterial.mainTexture = _dashTexture;
        _lineMaterial.color = cutLineColor;
        _lineMaterial.mainTextureScale = new Vector2(dashesPerUnit, 1f);
        _lineRenderer.sharedMaterial = _lineMaterial;

        if (scissorsVisual != null)
            scissorsVisual.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _mousePosition.Enable();
        _mouseClick.Enable();
    }

    private void OnDisable()
    {
        _mousePosition.Disable();
        _mouseClick.Disable();
    }

    private void OnDestroy()
    {
        if (_dashTexture != null) Destroy(_dashTexture);
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }

    private void Update()
    {
        if (!_isEnabled) return;

        if (_isDrawing)
        {
            UpdateDrawing();
            UpdateScissorsFollow();

            if (_mouseClick.WasReleasedThisFrame())
                FinishDrawing();
        }
        else
        {
            if (_mouseClick.WasPressedThisFrame())
                TryStartDrawing();
        }
    }

    // ─── Public API ───────────────────────────────────────────────

    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;

        if (enabled && surface != null)
            _surfaceNormal = surface.transform.forward;

        if (!enabled)
        {
            if (_isDrawing)
                CancelDrawing();

            if (scissorsVisual != null)
                scissorsVisual.gameObject.SetActive(false);
        }
    }

    // ─── Ray-Plane Intersection ──────────────────────────────────
    // Bypasses Physics.Raycast entirely — no collider needed.
    // Computes UV from the quad's local-space hit position.

    private bool RaycastSurface(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal, out Vector2 hitUV)
    {
        hitPoint = Vector3.zero;
        hitNormal = _surfaceNormal;
        hitUV = Vector2.zero;

        if (surface == null) return false;

        Transform surfT = surface.transform;
        Vector3 planeNormal = surfT.forward;
        Vector3 planePoint = surfT.position;

        float denom = Vector3.Dot(planeNormal, ray.direction);
        if (Mathf.Abs(denom) < 1e-6f) return false; // ray parallel to plane

        float t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
        if (t < 0f) return false; // plane behind ray

        hitPoint = ray.origin + ray.direction * t;

        // Convert to local space — quad mesh spans -0.5..0.5 in XY
        Vector3 local = surfT.InverseTransformPoint(hitPoint);

        if (local.x < -0.5f || local.x > 0.5f || local.y < -0.5f || local.y > 0.5f)
            return false; // outside quad bounds

        hitUV = new Vector2(local.x + 0.5f, local.y + 0.5f);
        hitNormal = planeNormal;
        return true;
    }

    // ─── Drawing ──────────────────────────────────────────────────

    private void TryStartDrawing()
    {
        Vector2 screenPos = _mousePosition.ReadValue<Vector2>();
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(screenPos) : cam.ScreenPointToRay(screenPos);

        if (!RaycastSurface(ray, out Vector3 hitPoint, out Vector3 hitNormal, out Vector2 hitUV))
            return;

        _isDrawing = true;
        _pathWorldPoints.Clear();
        _pathUVPoints.Clear();
        _cumulativeArcLengths.Clear();
        _drawnArcLength = 0f;
        _scissorsArcLength = 0f;
        _scissorsLastCutIndex = 0;

        Vector3 worldPoint = hitPoint + hitNormal * surfaceOffset;
        _pathWorldPoints.Add(worldPoint);
        _pathUVPoints.Add(hitUV);
        _cumulativeArcLengths.Add(0f);

        _lineRenderer.positionCount = 1;
        _lineRenderer.SetPosition(0, worldPoint);

        if (scissorsVisual != null)
        {
            scissorsVisual.position = worldPoint;
            scissorsVisual.gameObject.SetActive(true);
        }

        if (cutLoopSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(cutLoopSFX);
    }

    private void UpdateDrawing()
    {
        Vector2 screenPos = _mousePosition.ReadValue<Vector2>();
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(screenPos) : cam.ScreenPointToRay(screenPos);

        if (!RaycastSurface(ray, out Vector3 hitPoint, out Vector3 hitNormal, out Vector2 hitUV))
            return;

        Vector3 worldPoint = hitPoint + hitNormal * surfaceOffset;
        Vector3 lastPoint = _pathWorldPoints[_pathWorldPoints.Count - 1];
        float dist = Vector3.Distance(worldPoint, lastPoint);

        if (dist < minPointDistance)
            return;

        _drawnArcLength += dist;
        _pathWorldPoints.Add(worldPoint);
        _pathUVPoints.Add(hitUV);
        _cumulativeArcLengths.Add(_drawnArcLength);

        _lineRenderer.positionCount = _pathWorldPoints.Count;
        _lineRenderer.SetPosition(_pathWorldPoints.Count - 1, worldPoint);

        // Auto-close if path too long
        if (_drawnArcLength >= maxPathLength)
            FinishDrawing();
    }

    private void UpdateScissorsFollow()
    {
        if (_pathWorldPoints.Count < 2) return;

        // Advance scissors at configurable fixed speed
        _scissorsArcLength += scissorsSpeed * Time.deltaTime;
        _scissorsArcLength = Mathf.Min(_scissorsArcLength, _drawnArcLength);

        // Interpolate position along path
        Vector3 scissorsPos = InterpolateAlongPath(_scissorsArcLength,
            out int segmentIndex, out Vector3 direction);

        if (scissorsVisual != null)
        {
            scissorsVisual.position = scissorsPos;
            if (direction.sqrMagnitude > 0.0001f)
                scissorsVisual.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        // Stamp cut on surface up to scissors position
        if (surface != null && segmentIndex > _scissorsLastCutIndex)
        {
            surface.CutAlongPath(_pathUVPoints, _scissorsLastCutIndex, segmentIndex);
            _scissorsLastCutIndex = segmentIndex;
        }
    }

    private void FinishDrawing()
    {
        _isDrawing = false;

        if (_pathUVPoints.Count < 3)
        {
            ClearVisuals();
            return;
        }

        // Check if end is close enough to start to form a closed polygon
        Vector2 start = _pathUVPoints[0];
        Vector2 end = _pathUVPoints[_pathUVPoints.Count - 1];
        float closeDist = Vector2.Distance(start, end);

        if (closeDist <= closeTolerance)
        {
            // Close the polygon — add start point to end
            _pathUVPoints.Add(start);

            // Scissors finish remaining path
            if (surface != null)
                surface.CutAlongPath(_pathUVPoints, _scissorsLastCutIndex, _pathUVPoints.Count - 1);

            // Fill the cut area
            if (surface != null)
            {
                surface.FillCutPolygon(_pathUVPoints);
                surface.SpawnCutPiece(_pathUVPoints);
            }

            if (cutCompleteSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(cutCompleteSFX);

            Debug.Log($"[ScissorsCutController] Closed polygon with {_pathUVPoints.Count} points.");
            OnCutComplete?.Invoke(new List<Vector2>(_pathUVPoints));
        }
        else
        {
            // Open cut — cosmetic damage only, stamp remaining path
            if (surface != null)
                surface.CutAlongPath(_pathUVPoints, _scissorsLastCutIndex, _pathUVPoints.Count - 1);

            Debug.Log("[ScissorsCutController] Open cut — no polygon formed.");
        }

        ClearVisuals();
    }

    private void CancelDrawing()
    {
        _isDrawing = false;
        _pathWorldPoints.Clear();
        _pathUVPoints.Clear();
        _cumulativeArcLengths.Clear();
        ClearVisuals();
    }

    private void ClearVisuals()
    {
        _lineRenderer.positionCount = 0;

        if (scissorsVisual != null)
            scissorsVisual.gameObject.SetActive(false);
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private Vector3 InterpolateAlongPath(float arcLength, out int segmentIndex, out Vector3 direction)
    {
        segmentIndex = 0;
        direction = Vector3.forward;

        if (_pathWorldPoints.Count < 2)
            return _pathWorldPoints.Count > 0 ? _pathWorldPoints[0] : Vector3.zero;

        for (int i = 1; i < _cumulativeArcLengths.Count; i++)
        {
            if (arcLength <= _cumulativeArcLengths[i])
            {
                segmentIndex = i - 1;
                float segStart = _cumulativeArcLengths[i - 1];
                float segEnd = _cumulativeArcLengths[i];
                float segLen = segEnd - segStart;
                float t = segLen > 0f ? (arcLength - segStart) / segLen : 0f;

                direction = (_pathWorldPoints[i] - _pathWorldPoints[i - 1]).normalized;
                return Vector3.Lerp(_pathWorldPoints[i - 1], _pathWorldPoints[i], t);
            }
        }

        segmentIndex = _pathWorldPoints.Count - 2;
        if (_pathWorldPoints.Count >= 2)
            direction = (_pathWorldPoints[_pathWorldPoints.Count - 1]
                       - _pathWorldPoints[_pathWorldPoints.Count - 2]).normalized;

        return _pathWorldPoints[_pathWorldPoints.Count - 1];
    }
}
