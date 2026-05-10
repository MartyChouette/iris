using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Raycasts from the camera toward key targets (date NPC, held object, area focus).
/// Renderers hit that use the PSXLitDissolvable shader get their _DissolveAmount
/// driven up via MaterialPropertyBlock. Respects AlwaysFadedWall base dissolve floor.
/// </summary>
public class WallOcclusionFader : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────
    public static WallOcclusionFader Instance { get; private set; }

    [Header("Raycast Settings")]
    [Tooltip("Layer mask for walls that can be faded.")]
    [SerializeField] private LayerMask _wallLayer = ~0;

    [Tooltip("Max raycast distance from camera to targets.")]
    [SerializeField] private float _maxRayDistance = 50f;

    [Tooltip("Radius of the sphere cast (0 = line cast).")]
    [SerializeField] private float _rayRadius = 0.15f;

    [Header("Fade Timing")]
    [Tooltip("Seconds to fully dissolve a wall (fade in).")]
    [SerializeField] private float _fadeInDuration = 0.2f;

    [Tooltip("Seconds for a wall to reappear (fade out).")]
    [SerializeField] private float _fadeOutDuration = 0.4f;

    [Tooltip("Target dissolve amount when occluding (0-1).")]
    [SerializeField] private float _targetDissolve = 0.85f;

    // ── Internals ────────────────────────────────────────────────────
    private Camera _cam;
    private readonly Dictionary<Renderer, FadeState> _trackedRenderers = new();
    private readonly List<Renderer> _removeList = new();
    private readonly List<Renderer> _keyBuffer = new();
    private readonly HashSet<Renderer> _hitThisFrame = new();
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

    private readonly HashSet<Renderer> _exemptThisFrame = new();
    private readonly Dictionary<Renderer, Collider[]> _colliderCache = new();

    private static readonly int DissolveID = Shader.PropertyToID("_DissolveAmount");
    private int _lastAreaIndex = -1;
    private AreaWallFade[] _cachedAreaWalls;

    /// <summary>Dissolve threshold above which a wall's collider is disabled (raycasts pass through).</summary>
    private const float ColliderDisableThreshold = 0.3f;

    private struct FadeState
    {
        public float current;
        public float floor;     // AlwaysFadedWall / AreaWallFade dissolve or 0
        public MaterialPropertyBlock mpb;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _cachedAreaWalls = FindObjectsByType<AreaWallFade>(FindObjectsSortMode.None);
    }

    private void OnDestroy()
    {
        // Clean up any driven dissolve values and re-enable colliders
        foreach (var kvp in _trackedRenderers)
        {
            if (kvp.Key != null)
            {
                kvp.Value.mpb.SetFloat(DissolveID, kvp.Value.floor);
                kvp.Key.SetPropertyBlock(kvp.Value.mpb);
                SetWallCollider(kvp.Key, true);
            }
        }
        _trackedRenderers.Clear();
        if (Instance == this) Instance = null;
    }

    private float _camRefreshTimer;

    private void LateUpdate()
    {
        _camRefreshTimer -= Time.deltaTime;
        if (_cam == null || _camRefreshTimer <= 0f)
        {
            _cam = Camera.main;
            _camRefreshTimer = 0.5f;
            if (_cam == null) return;
        }

        // ── Refresh dissolve floors when area changes ────────────────
        int currentArea = ApartmentManager.Instance != null ? ApartmentManager.Instance.CurrentAreaIndex : 0;
        if (currentArea != _lastAreaIndex)
        {
            _lastAreaIndex = currentArea;
            RefreshAreaFloors();
        }

        _hitThisFrame.Clear();
        _exemptThisFrame.Clear();
        Vector3 camPos = _cam.transform.position;

        // ── Exempt wall the held object is being placed on ───────────
        var held = ObjectGrabber.HeldObject;
        if (held != null)
        {
            var surface = ObjectGrabber.CurrentSurface;
            if (surface != null)
            {
                var wallRend = surface.GetComponentInParent<Renderer>();
                if (wallRend != null)
                    _exemptThisFrame.Add(wallRend);
            }
        }

        // ── Gather targets ──────────────────────────────────────────
        // 1. Date NPC
        var dateMgr = DateSessionManager.Instance;
        if (dateMgr != null && dateMgr.DateCharacter != null)
            CastToTarget(camPos, dateMgr.DateCharacter.transform.position);

        // 2. Held object
        if (held != null)
            CastToTarget(camPos, held.transform.position);

        // 3. Current area focus point (camera target position)
        var apt = ApartmentManager.Instance;
        if (apt != null)
        {
            int idx = apt.CurrentAreaIndex;
            Vector3 focusPoint = camPos + _cam.transform.forward * 5f;
            CastToTarget(camPos, focusPoint);
        }

        // Remove exempt renderers from hit set so they won't be faded
        _hitThisFrame.ExceptWith(_exemptThisFrame);

        // ── Drive dissolve on hit renderers ─────────────────────────
        float dt = Time.deltaTime;

        foreach (var rend in _hitThisFrame)
        {
            if (!_trackedRenderers.TryGetValue(rend, out var state))
            {
                state = new FadeState
                {
                    current = GetBaseDissolve(rend),
                    floor = GetBaseDissolve(rend),
                    // Fresh MPB — never GetPropertyBlock, it copies ALL material
                    // properties (including _BaseColor) which contaminates multi-mat objects.
                    mpb = new MaterialPropertyBlock()
                };
            }

            float target = Mathf.Max(_targetDissolve, state.floor);
            float speed = _fadeInDuration > 0f ? 1f / _fadeInDuration : 100f;
            state.current = Mathf.MoveTowards(state.current, target, speed * dt);
            state.mpb.SetFloat(DissolveID, state.current);
            rend.SetPropertyBlock(state.mpb);
            _trackedRenderers[rend] = state;

            // Disable wall collider so raycasts pass through faded walls
            SetWallCollider(rend, state.current < ColliderDisableThreshold);
        }

        // ── Fade out renderers that are no longer hit ───────────────
        _removeList.Clear();
        _keyBuffer.Clear();
        foreach (var kvp in _trackedRenderers)
            _keyBuffer.Add(kvp.Key);

        for (int i = 0; i < _keyBuffer.Count; i++)
        {
            var rend = _keyBuffer[i];
            if (_hitThisFrame.Contains(rend)) continue;

            if (rend == null) { _removeList.Add(rend); continue; }

            var state = _trackedRenderers[rend];
            float speed = _fadeOutDuration > 0f ? 1f / _fadeOutDuration : 100f;
            state.current = Mathf.MoveTowards(state.current, state.floor, speed * dt);
            state.mpb.SetFloat(DissolveID, state.current);
            rend.SetPropertyBlock(state.mpb);
            _trackedRenderers[rend] = state;

            // Re-enable wall collider as it fades back in
            SetWallCollider(rend, state.current < ColliderDisableThreshold);

            if (Mathf.Approximately(state.current, state.floor))
                _removeList.Add(rend);
        }

        foreach (var rend in _removeList)
            _trackedRenderers.Remove(rend);
    }

    // ── Area change: refresh dissolve floors ────────────────────────

    private void RefreshAreaFloors()
    {
        // Update floors for already-tracked renderers
        _keyBuffer.Clear();
        foreach (var kvp in _trackedRenderers)
            _keyBuffer.Add(kvp.Key);

        for (int i = 0; i < _keyBuffer.Count; i++)
        {
            var rend = _keyBuffer[i];
            if (rend == null) continue;
            var state = _trackedRenderers[rend];
            state.floor = GetBaseDissolve(rend);
            // If current dissolve is below the new floor, snap up
            if (state.current < state.floor)
                state.current = state.floor;
            state.mpb.SetFloat(DissolveID, state.current);
            rend.SetPropertyBlock(state.mpb);
            _trackedRenderers[rend] = state;
        }

        // Find AreaWallFade walls not yet tracked and apply their floor immediately
        if (_cachedAreaWalls == null)
            _cachedAreaWalls = FindObjectsByType<AreaWallFade>(FindObjectsSortMode.None);
        foreach (var aw in _cachedAreaWalls)
        {
            var rend = aw.GetComponent<Renderer>();
            if (rend == null) continue;
            if (_trackedRenderers.ContainsKey(rend)) continue;

            float floor = GetBaseDissolve(rend);
            if (floor <= 0f) continue;

            var state = new FadeState
            {
                current = floor,
                floor = floor,
                mpb = new MaterialPropertyBlock()
            };
            // Fresh MPB — only set dissolve, never GetPropertyBlock.
            state.mpb.SetFloat(DissolveID, floor);
            rend.SetPropertyBlock(state.mpb);
            _trackedRenderers[rend] = state;
        }
    }

    // ── Raycast helper ──────────────────────────────────────────────

    private void CastToTarget(Vector3 camPos, Vector3 targetPos)
    {
        Vector3 dir = targetPos - camPos;
        float dist = dir.magnitude;
        if (dist < 0.01f || dist > _maxRayDistance) return;

        int hitCount;
        if (_rayRadius > 0f)
            hitCount = Physics.SphereCastNonAlloc(camPos, _rayRadius, dir.normalized, _hitBuffer, dist, _wallLayer, QueryTriggerInteraction.Ignore);
        else
            hitCount = Physics.RaycastNonAlloc(camPos, dir.normalized, _hitBuffer, dist, _wallLayer, QueryTriggerInteraction.Ignore);

        // Find the farthest hit wall — that's the wall the target sits on/against.
        // We exempt it so wall-mounted items don't dissolve their own wall.
        float farthestDist = -1f;
        Renderer farthestRend = null;
        for (int i = 0; i < hitCount; i++)
        {
            var rend = _hitBuffer[i].collider.GetComponentInParent<Renderer>();
            if (rend == null) continue;
            if (!UsesDissolvableShader(rend)) continue;
            if (_hitBuffer[i].distance > farthestDist)
            {
                farthestDist = _hitBuffer[i].distance;
                farthestRend = rend;
            }
        }

        for (int i = 0; i < hitCount; i++)
        {
            var rend = _hitBuffer[i].collider.GetComponentInParent<Renderer>();
            if (rend == null) continue;
            if (!UsesDissolvableShader(rend)) continue;
            // Skip the wall closest to (behind) the target — it's the wall the target is on
            if (rend == farthestRend) continue;
            _hitThisFrame.Add(rend);
        }
    }

    private static bool UsesDissolvableShader(Renderer rend)
    {
        // Check shared material to avoid instantiation
        var mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && mats[i].shader.name == "Iris/PSXLitDissolvable")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Enable or disable all colliders on a wall renderer's GameObject.
    /// When a wall is sufficiently dissolved, its colliders are disabled
    /// so pickup/cleaning raycasts pass through to items behind it.
    /// </summary>
    private void SetWallCollider(Renderer rend, bool enabled)
    {
        if (rend == null) return;
        if (!_colliderCache.TryGetValue(rend, out var colliders))
        {
            colliders = rend.GetComponentsInParent<Collider>();
            _colliderCache[rend] = colliders;
        }
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }
    }

    private static float GetBaseDissolve(Renderer rend)
    {
        // Per-area dissolve takes priority
        var areaFade = rend.GetComponentInParent<AreaWallFade>();
        if (areaFade != null)
        {
            int area = ApartmentManager.Instance != null ? ApartmentManager.Instance.CurrentAreaIndex : 0;
            float areaDissolve = areaFade.GetDissolveForArea(area);
            if (areaDissolve > 0f) return areaDissolve;
        }

        // Fall back to always-faded marker
        var marker = rend.GetComponentInParent<AlwaysFadedWall>();
        return marker != null ? marker.BaseDissolve : 0f;
    }
}
