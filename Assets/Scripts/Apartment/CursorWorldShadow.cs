using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Projects a shadow onto world surfaces under the mouse cursor.
/// When a CursorContext exists in the scene, the shadow matches the active
/// cursor texture. Otherwise falls back to a procedural disc.
/// </summary>
public class CursorWorldShadow : MonoBehaviour
{
    [Header("Raycast")]
    [Tooltip("Layers the cursor shadow can project onto.")]
    [SerializeField] private LayerMask _surfaceLayers = ~0;

    [Tooltip("Max raycast distance from camera.")]
    [SerializeField] private float _maxDistance = 50f;

    [Header("Shadow Size")]
    [Tooltip("World-space diameter of the shadow quad.")]
    [SerializeField] private float _diameter = 0.3f;

    [Tooltip("Offset from surface along normal to prevent z-fighting.")]
    [SerializeField] private float _surfaceOffset = 0.005f;

    [Header("Cursor Texture")]
    [Tooltip("Cursor texture to project as shadow. If a CursorContext exists in the scene, its active texture is used instead.")]
    [SerializeField] private Texture2D _cursorTexture;

    [Tooltip("Shader for the cursor shadow quad. Drag Iris/CursorShadow here so it's included in builds.")]
    [SerializeField] private Shader _shadowShader;

    [Header("Smoothing")]
    [Tooltip("How quickly the shadow follows the cursor (0 = instant).")]
    [SerializeField] private float _smoothSpeed = 25f;

    private Camera _cam;
    private GameObject _shadowQuad;
    private MeshRenderer _shadowRenderer;
    private Material _shadowMat;
    private Vector3 _currentPos;
    private Quaternion _currentRot;
    private bool _hasTarget;

    private CursorContext _cursorContext;
    private Texture2D _activeTexture;
    private float _baseShadowAlpha;

    private void Awake()
    {
        BuildQuad();
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    private void OnDestroy()
    {
        if (_shadowMat != null) Destroy(_shadowMat);
        if (_shadowQuad != null) Destroy(_shadowQuad);
    }

    private void BuildQuad()
    {
        _shadowQuad = new GameObject("CursorShadowQuad");
        _shadowQuad.transform.SetParent(transform);
        _shadowQuad.hideFlags = HideFlags.HideAndDontSave;

        var mf = _shadowQuad.AddComponent<MeshFilter>();
        _shadowRenderer = _shadowQuad.AddComponent<MeshRenderer>();

        // Simple quad mesh
        var mesh = new Mesh { name = "CursorShadowMesh" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;

        // Use serialized shader reference (survives builds), fall back to Shader.Find
        var shader = _shadowShader;
        if (shader == null) shader = Shader.Find("Iris/CursorShadow");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        _shadowMat = new Material(shader);
        _shadowRenderer.sharedMaterial = _shadowMat;
        _shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _shadowRenderer.receiveShadows = false;

        _shadowQuad.SetActive(false);
        _baseShadowAlpha = _shadowMat.GetColor("_ShadowColor").a;
    }

    private float _cursorContextRetry;

    private void SyncCursorTexture()
    {
        // Try to find CursorContext periodically until found (flower scene fallback)
        if (_cursorContext == null && _cursorContextRetry <= 0f)
        {
            _cursorContext = FindAnyObjectByType<CursorContext>();
            _cursorContextRetry = 2f; // retry every 2s, not every frame
        }
        if (_cursorContextRetry > 0f) _cursorContextRetry -= Time.deltaTime;

        // Pick texture: CursorContext active texture > serialized fallback
        Texture2D desired = null;
        if (_cursorContext != null)
            desired = _cursorContext.ActiveCursorTexture;
        if (desired == null)
            desired = _cursorTexture;

        if (desired != null)
        {
            if (desired != _activeTexture)
            {
                _activeTexture = desired;
                _shadowMat.SetTexture("_MainTex", _activeTexture);
            }
            _shadowMat.SetFloat("_UseTexture", 1f);
        }
        else
        {
            _shadowMat.SetFloat("_UseTexture", 0f);
        }

        // Sync shadow alpha with GlobalCursorManager's smooth fade
        float cursorAlpha = 1f;
        if (GlobalCursorManager.Instance != null && GlobalCursorManager.Instance.IsContextCursor)
            cursorAlpha = GlobalCursorManager.Instance.CurrentAlpha;
        var sc = _shadowMat.GetColor("_ShadowColor");
        sc.a = _baseShadowAlpha * cursorAlpha;
        _shadowMat.SetColor("_ShadowColor", sc);
    }

    private void Update()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        // Don't show when holding an object (ObjectGrabber has its own shadow)
        if (ObjectGrabber.IsHoldingObject)
        {
            _shadowQuad.SetActive(false);
            return;
        }

        SyncCursorTexture();

        Vector2 screenPos = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null
            ? ApartmentManager.Instance.ScreenPointToRay(screenPos)
            : _cam.ScreenPointToRay(screenPos);

        if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _surfaceLayers))
        {
            Vector3 targetPos = hit.point + hit.normal * _surfaceOffset;

            // Orient quad: face the surface normal, align cursor "up" with
            // camera up projected onto the surface so it looks correct on screen.
            Vector3 projUp = Vector3.ProjectOnPlane(_cam.transform.up, hit.normal);
            if (projUp.sqrMagnitude < 0.001f)
                projUp = Vector3.ProjectOnPlane(_cam.transform.forward, hit.normal);
            Quaternion targetRot = Quaternion.LookRotation(projUp.normalized, hit.normal);

            if (!_hasTarget)
            {
                // First frame — snap immediately
                _currentPos = targetPos;
                _currentRot = targetRot;
                _hasTarget = true;
            }
            else
            {
                // Smooth follow
                float dt = Time.unscaledDeltaTime;
                _currentPos = Vector3.Lerp(_currentPos, targetPos, _smoothSpeed * dt);
                _currentRot = Quaternion.Slerp(_currentRot, targetRot, _smoothSpeed * dt);
            }

            _shadowQuad.transform.position = _currentPos;
            _shadowQuad.transform.rotation = _currentRot;

            // Scale shadow so it keeps a consistent screen-space size at any distance/zoom
            float dist = Vector3.Distance(_cam.transform.position, hit.point);
            float d;
            if (_cam.orthographic)
            {
                // Ortho: screen size is purely a function of orthographicSize
                d = _diameter * _cam.orthographicSize * 0.15f;
            }
            else
            {
                // Perspective: world size = screen fraction × distance × tan(fov/2)
                d = _diameter * dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.12f;
            }
            d = Mathf.Max(d, 0.02f);
            _shadowQuad.transform.localScale = new Vector3(d, 1f, d);
            _shadowQuad.SetActive(true);
        }
        else
        {
            _shadowQuad.SetActive(false);
            _hasTarget = false;
        }
    }
}
