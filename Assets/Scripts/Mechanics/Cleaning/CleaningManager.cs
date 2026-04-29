using System.Collections;
using UnityEngine;

/// <summary>
/// Scene-scoped singleton for ambient cleaning. Sponge only — click+drag
/// stains to wipe them clean. Raycasts onto <see cref="CleanableSurface"/>.
/// Skips already-clean stains, plays sparkle VFX + SFX on completion,
/// and deactivates stain quads after cleaning.
/// </summary>
[DisallowMultipleComponent]
public class CleaningManager : MonoBehaviour
{
    public static CleaningManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Main camera for raycasts.")]
    [SerializeField] private Camera _mainCamera;

    [Tooltip("All cleanable surfaces in the scene.")]
    [SerializeField] private CleanableSurface[] _surfaces;

    [Tooltip("HUD display.")]
    [SerializeField] private CleaningHUD _hud;

    [Header("Visuals")]
    [Tooltip("Sponge visual that follows the mouse on surfaces.")]
    [SerializeField] private Transform _spongeVisual;

    [Header("Settings")]
    [Tooltip("UV-space radius for wipe brush.")]
    [SerializeField] private float _wipeRadius = 0.06f;

    [Tooltip("Layer mask for raycasting onto cleanable surfaces.")]
    [SerializeField] private LayerMask _cleanableLayer;

    [Tooltip("Offset above surface for tool visual placement.")]
    [SerializeField] private float _surfaceOffset = 0.005f;

    [Header("Audio")]
    public AudioClip wipeSFX;
    public AudioClip allCleanSFX;

    [Tooltip("SFX played when an individual stain is fully cleaned.")]
    [SerializeField] private AudioClip _stainCompleteSFX;

    // Input managed by IrisInput singleton

    /// <summary>Fired once on the rising edge of a wipe (mouse press on a stain).</summary>
    public static event System.Action OnWipeStarted;

    // State
    private CleanableSurface _hoveredSurface;
    private float _sfxCooldown;
    private bool _allCleanFired;
    private static bool s_hasCleanedFirstStain;
    private bool _wasPressingLastFrame;
    private const float SFX_COOLDOWN = 0.15f;

    // ── Sponge squish (velocity-based deformation) ──────────────────
    private static readonly Vector3 SpongeBaseScale = new(0.14f, 0.07f, 0.16f);
    private Vector3 _lastSpongePos;
    private Vector3 _smoothVelocity;
    private float _squishSpring;      // current spring displacement
    private float _squishSpringVel;   // spring velocity (for overshoot)
    private float _clickBounce;       // extra compression on wipe click
    private float _clickBounceVel;    // spring velocity for click bounce
    private float _spongeLingerTimer; // grace period before hiding sponge
    private const float SpongeLingerTime = 0.25f; // seconds to keep sponge visible after leaving stain
    private const float SquishMaxSpeed = 2f;    // world-space speed for full squish
    private const float SquishSpringK = 55f;    // softer spring = more wobbly
    private const float SquishDamping = 3.5f;   // less damping = more oscillation
    private const float SquishStretch = 0.5f;   // generous stretch along movement
    private const float SquishCompress = 0.4f;  // generous Y compression
    private const float VelocitySmoothing = 8f; // more reactive/sloppy
    private const float IdleWobbleSpeed = 3.5f; // idle breathing frequency
    private const float IdleWobbleAmount = 0.06f; // idle breathing amplitude
    private const float ClickBounceK = 200f;    // click bounce spring stiffness
    private const float ClickBounceDamp = 10f;  // click bounce damping
    private const float ClickBounceStrength = 0.6f; // initial click bounce impulse

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>The surface currently under the cursor, or null.</summary>
    public CleanableSurface HoveredSurface => _hoveredSurface;

    /// <summary>True while the player is click-holding to scrub a stain (3D sponge visible).</summary>
    public bool IsScrubbing => _hoveredSurface != null && _spongeVisual != null && _spongeVisual.gameObject.activeSelf;

    /// <summary>All surfaces in the scene.</summary>
    public CleanableSurface[] Surfaces => _surfaces;

    /// <summary>Replace the surfaces array (used by ApartmentStainSpawner).</summary>
    public void SetSurfaces(CleanableSurface[] surfaces)
    {
        _surfaces = surfaces;
        ReconcileLayers();
    }

    /// <summary>Average clean percent for surfaces in a specific area.</summary>
    public float GetAreaCleanPercent(ApartmentArea area)
    {
        if (_surfaces == null || _surfaces.Length == 0) return 1f;
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < _surfaces.Length; i++)
        {
            if (_surfaces[i].Area == area)
            {
                sum += _surfaces[i].CleanPercent;
                count++;
            }
        }
        return count == 0 ? 1f : sum / count;
    }

    /// <summary>Average clean percent across all surfaces.</summary>
    public float OverallCleanPercent
    {
        get
        {
            if (_surfaces == null || _surfaces.Length == 0) return 1f;
            float sum = 0f;
            for (int i = 0; i < _surfaces.Length; i++)
                sum += _surfaces[i].CleanPercent;
            return sum / _surfaces.Length;
        }
    }

    /// <summary>True when all surfaces are >= 95% clean.</summary>
    public bool AllClean
    {
        get
        {
            if (_surfaces == null) return true;
            for (int i = 0; i < _surfaces.Length; i++)
                if (!_surfaces[i].IsFullyClean) return false;
            return true;
        }
    }

    /// <summary>
    /// Ensure the layer mask includes whatever layers the actual surface GOs are on.
    /// Fixes mismatches from scene rebuilds, layer renumbering, or missing layer assignment.
    /// Also assigns surfaces to the expected layer if possible.
    /// </summary>
    private void ReconcileLayers()
    {
        if (_surfaces == null || _surfaces.Length == 0) return;

        int expectedLayer = -1;
        if (_cleanableLayer.value != 0)
        {
            // Find the single layer number from the mask
            for (int i = 0; i < 32; i++)
            {
                if ((_cleanableLayer.value & (1 << i)) != 0)
                {
                    expectedLayer = i;
                    break;
                }
            }
        }

        for (int i = 0; i < _surfaces.Length; i++)
        {
            if (_surfaces[i] == null) continue;
            var go = _surfaces[i].gameObject;
            int surfaceLayer = go.layer;

            if (expectedLayer >= 0 && surfaceLayer != expectedLayer)
            {
                // Surface is on wrong layer — fix it (and its colliders' GO)
                Debug.LogWarning($"[CleaningManager] Surface '{go.name}' on layer {surfaceLayer}, " +
                                 $"expected {expectedLayer}. Reassigning.");
                go.layer = expectedLayer;
                // Also fix child layers
                foreach (Transform child in go.transform)
                    child.gameObject.layer = expectedLayer;
            }
            else if (expectedLayer < 0 && surfaceLayer != 0)
            {
                // No expected layer but surface is on a specific layer — adopt it
                _cleanableLayer |= 1 << surfaceLayer;
                expectedLayer = surfaceLayer;
                Debug.LogWarning($"[CleaningManager] Adopted layer {surfaceLayer} from surface '{go.name}' (mask now={_cleanableLayer.value}).");
            }
        }

        // Final fallback: if mask is still 0 but we have surfaces, use "Everything"
        if (_cleanableLayer.value == 0 && _surfaces.Length > 0)
        {
            _cleanableLayer = ~0; // all layers
            Debug.LogWarning("[CleaningManager] No layer detected — using all-layers mask as last resort.");
        }
    }

    // ── Singleton lifecycle ─────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        // Auto-detect Cleanable layer if mask wasn't wired (e.g. scene built before layer existed)
        if (_cleanableLayer.value == 0)
        {
            int layer = LayerMask.NameToLayer("Cleanable");
            if (layer >= 0)
            {
                _cleanableLayer = 1 << layer;
                Debug.LogWarning($"[CleaningManager] _cleanableLayer was 0 — auto-detected 'Cleanable' layer {layer} (mask={_cleanableLayer.value}).");
            }
            else
            {
                Debug.LogError("[CleaningManager] _cleanableLayer is 0 and no 'Cleanable' layer found in Project Settings! Stain raycasts will fail.");
            }
        }

        ReconcileLayers();

        Debug.Log($"[CleaningManager] Awake — camera={(_mainCamera != null ? _mainCamera.name : "NULL")}, " +
                  $"surfaces={(_surfaces != null ? _surfaces.Length : 0)}, " +
                  $"sponge={(_spongeVisual != null ? "OK" : "NULL")}, " +
                  $"layer={_cleanableLayer.value}");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Input managed by IrisInput singleton — no local enable/disable needed.

    // ── Update ──────────────────────────────────────────────────────

    void Update()
    {
        // Retry Camera.main every frame if reference was lost (e.g. scene rebuild)
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
            Debug.Log("[CleaningManager] Re-acquired main camera.");
        }

        // Block cleaning outside interaction phases (name entry, newspaper, date end)
        // and during all date phases (arrival, drink making, reveal)
        if (DayPhaseManager.Instance != null && !DayPhaseManager.Instance.IsInteractionPhase)
        {
            SetSpongeVisual(Vector3.zero, false);
            _hoveredSurface = null;
            return;
        }
        if (DateSessionManager.Instance != null && DateSessionManager.Instance.IsDateActive)
        {
            SetSpongeVisual(Vector3.zero, false);
            _hoveredSurface = null;
            return;
        }

        // Only allow cleaning interaction while Browsing the apartment
        if (ApartmentManager.Instance != null
            && ApartmentManager.Instance.CurrentState != ApartmentManager.State.Browsing)
        {
            SetSpongeVisual(Vector3.zero, false);
            _hoveredSurface = null;
            return;
        }

        if (ObjectGrabber.IsHoldingObject || ObjectGrabber.ClickConsumedThisFrame)
        {
            SetSpongeVisual(Vector3.zero, false);
            _hoveredSurface = null;
            return;
        }

        _sfxCooldown -= Time.deltaTime;

        Vector2 pointer = IrisInput.CursorPosition;
        Ray ray = ApartmentManager.Instance != null ? ApartmentManager.Instance.ScreenPointToRay(pointer) : _mainCamera.ScreenPointToRay(pointer);

        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 100f, _cleanableLayer);

        // Fallback: if layer-masked raycast missed, directly test each active surface's collider
        if (!hit && _surfaces != null)
        {
            for (int i = 0; i < _surfaces.Length; i++)
            {
                if (_surfaces[i] == null || !_surfaces[i].gameObject.activeInHierarchy) continue;
                var col = _surfaces[i].GetComponent<Collider>();
                if (col != null && col.Raycast(ray, out hitInfo, 100f))
                {
                    hit = true;
                    _surfaces[i].gameObject.layer = LayerMaskToLayer(_cleanableLayer);
                    break;
                }
            }
        }

        if (hit)
        {
            _hoveredSurface = hitInfo.collider.GetComponentInParent<CleanableSurface>();

            // Skip already-clean stains — no sponge, no wipe
            if (_hoveredSurface != null && _hoveredSurface.IsFullyClean)
            {
                _hoveredSurface = null;
                RequestSpongeHide();
            }
            else
            {
                Vector3 toolPos = hitInfo.point + hitInfo.normal * _surfaceOffset;
                bool isPressed = IrisInput.Instance != null && IrisInput.Instance.Click.IsPressed();

                // 3D sponge only appears while actively scrubbing (click+hold)
                // Context cursor handles the hover state
                if (isPressed && _hoveredSurface != null)
                {
                    _spongeLingerTimer = SpongeLingerTime;
                    SetSpongeVisual(toolPos, true);

                    if (!_wasPressingLastFrame)
                    {
                        OnWipeStarted?.Invoke();
                        ObjectGrabber.ConsumeClickExternal();
                        _clickBounce = ClickBounceStrength;
                        _clickBounceVel = 0f;
                    }

                    bool wasClean = _hoveredSurface.IsFullyClean;
                    Vector2 uv = HitToUV(hitInfo, _hoveredSurface.transform);
                    _hoveredSurface.Wipe(uv, _wipeRadius);
                    PlaySFX(wipeSFX);

                    if (!wasClean && _hoveredSurface.IsFullyClean)
                    {
                        OnStainCompleted(_hoveredSurface);
                    }
                }
                else
                {
                    // Just hovering — hide 3D sponge (context cursor shows instead)
                    RequestSpongeHide();
                }
            }
        }
        else
        {
            _hoveredSurface = null;
            RequestSpongeHide();
        }

        _wasPressingLastFrame = IrisInput.Instance != null && IrisInput.Instance.Click.IsPressed();

        // Check all-clean
        if (!_allCleanFired && AllClean)
        {
            _allCleanFired = true;
            Debug.Log("[CleaningManager] All surfaces clean!");
            if (AudioManager.Instance != null && allCleanSFX != null)
                AudioManager.Instance.PlaySFX(allCleanSFX);
            DialoguePortraitBox.Instance?.Say("Look at this place shine!", 2.5f);
        }
    }

    // ── Stain completion ────────────────────────────────────────────

    private void OnStainCompleted(CleanableSurface surface)
    {
        Vector3 pos = surface.transform.position;

        // SFX
        if (AudioManager.Instance != null && _stainCompleteSFX != null)
            AudioManager.Instance.PlaySFX(_stainCompleteSFX);

        // First stain voice line
        if (!s_hasCleanedFirstStain)
        {
            s_hasCleanedFirstStain = true;
            DialoguePortraitBox.Instance?.Say("There, much better!", 2f);
        }

        // Sparkle VFX
        SpawnSparkle(pos);

        // Deactivate stain quad after sparkle finishes
        StartCoroutine(DeactivateAfterDelay(surface.gameObject, 0.5f));
    }

    private void SpawnSparkle(Vector3 position)
    {
        var go = new GameObject("StainSparkle");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.4f;
        main.startSpeed = 0.3f;
        main.startSize = 0.02f;
        main.startColor = new Color(1f, 0.95f, 0.8f, 1f);
        main.gravityModifier = -0.3f;
        main.maxParticles = 12;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 1f); // Additive
            mat.color = new Color(1f, 0.95f, 0.8f, 1f);
            renderer.material = mat;
        }

        ps.Play();
    }

    private IEnumerator DeactivateAfterDelay(GameObject stainGO, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (stainGO != null)
            stainGO.SetActive(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Convert world-space raycast hit to UV on the stain surface.
    /// Used instead of hitInfo.textureCoord because BoxColliders don't provide UVs.
    /// The stain parent is rotated Euler(90,0,0) with local XY spanning -0.3..0.3 (scale 0.6).
    /// NOT clamped — the collider is larger than the visual so the brush center can
    /// overshoot the edge. Wipe() already bounds-checks pixel coordinates.
    /// </summary>
    private static Vector2 HitToUV(RaycastHit hit, Transform surfaceTransform)
    {
        Vector3 local = surfaceTransform.InverseTransformPoint(hit.point);
        // DirtQuad is 0.6 x 0.6 in local XY, centered at origin
        return new Vector2(
            (local.x / 0.6f) + 0.5f,
            (local.y / 0.6f) + 0.5f
        );
    }

    private void EnsureSpongeVisual()
    {
        if (_spongeVisual != null) return;

        // Auto-create a simple sponge cube at runtime
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SpongeVisual_Auto";
        go.transform.localScale = SpongeBaseScale;
        // Remove collider — sponge is visual only, must not block raycasts
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = new Color(0.9f, 0.85f, 0.3f);
        go.SetActive(false);
        _spongeVisual = go.transform;
        Debug.Log("[CleaningManager] Auto-created sponge visual at runtime.");
    }

    /// <summary>
    /// Start the linger countdown instead of hiding immediately.
    /// Keeps the sponge visible for a grace period so scrubbing doesn't flicker.
    /// </summary>
    private void RequestSpongeHide()
    {
        if (_spongeLingerTimer > 0f)
        {
            _spongeLingerTimer -= Time.deltaTime;
            // Keep sponge visible at its last position, still running squish
            if (_spongeVisual != null && _spongeVisual.gameObject.activeSelf)
                ApplySpongeSquish(_spongeVisual.position);
            return;
        }
        SetSpongeVisual(Vector3.zero, false);
    }

    /// <summary>Strip any collider from the sponge so it can't intercept physics raycasts.</summary>
    private bool _spongeColliderStripped;

    private void SetSpongeVisual(Vector3 position, bool visible)
    {
        EnsureSpongeVisual();
        if (_spongeVisual == null) return;

        // One-time: strip collider from scene-builder sponge (CreateBox adds BoxCollider)
        if (!_spongeColliderStripped)
        {
            _spongeColliderStripped = true;
            var col = _spongeVisual.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
                Debug.Log("[CleaningManager] Stripped collider from sponge visual.");
            }
            // Apply bigger base scale to scene-builder sponge too
            _spongeVisual.localScale = SpongeBaseScale;
        }

        // Always sync with actual GO state to prevent desync
        bool currentlyActive = _spongeVisual.gameObject.activeSelf;
        if (visible != currentlyActive)
            _spongeVisual.gameObject.SetActive(visible);

        if (!visible)
        {
            // Reset spring state when hidden so it doesn't pop on reappear
            _squishSpring = 0f;
            _squishSpringVel = 0f;
            _clickBounce = 0f;
            _clickBounceVel = 0f;
            _smoothVelocity = Vector3.zero;
            return;
        }

        _spongeVisual.position = position;
        ApplySpongeSquish(position);
    }

    /// <summary>
    /// Velocity-based squash/stretch with idle wobble and click bounce.
    /// Orientation stays fixed — only scale deforms to convey velocity.
    /// Soft spring creates sloppy overshoot and wobble.
    /// </summary>
    private void ApplySpongeSquish(Vector3 position)
    {
        float dt = Time.deltaTime;
        if (dt < 1e-6f) return;

        // Compute instantaneous velocity, smooth it
        Vector3 instantVel = (position - _lastSpongePos) / dt;
        _smoothVelocity = Vector3.Lerp(_smoothVelocity, instantVel, VelocitySmoothing * dt);
        _lastSpongePos = position;

        float speed = _smoothVelocity.magnitude;
        float targetSquish = Mathf.Clamp01(speed / SquishMaxSpeed);

        // Damped spring toward target (soft = lots of wobble)
        float springForce = (targetSquish - _squishSpring) * SquishSpringK;
        _squishSpringVel += springForce * dt;
        _squishSpringVel -= _squishSpringVel * SquishDamping * dt;
        _squishSpring += _squishSpringVel * dt;
        _squishSpring = Mathf.Clamp(_squishSpring, -0.4f, 1.3f);

        // Click bounce spring (decays independently)
        float bounceForce = (0f - _clickBounce) * ClickBounceK;
        _clickBounceVel += bounceForce * dt;
        _clickBounceVel -= _clickBounceVel * ClickBounceDamp * dt;
        _clickBounce += _clickBounceVel * dt;
        _clickBounce = Mathf.Clamp(_clickBounce, -0.5f, 1f);

        float s = Mathf.Clamp01(_squishSpring);

        // Idle breathing wobble — gentle sine pulse when not moving much
        float idleFactor = 1f - Mathf.Clamp01(speed / (SquishMaxSpeed * 0.3f));
        float wobble = Mathf.Sin(Time.time * IdleWobbleSpeed) * IdleWobbleAmount * idleFactor;
        float wobbleX = Mathf.Sin(Time.time * IdleWobbleSpeed * 1.3f) * IdleWobbleAmount * 0.5f * idleFactor;

        // Combined compression: movement squish + click bounce + idle wobble
        float yScale = 1f - s * SquishCompress - Mathf.Abs(_clickBounce) * 0.3f + wobble;
        float stretchScale = 1f + _squishSpring * SquishStretch;
        stretchScale = Mathf.Max(stretchScale, 0.6f);

        // Lateral wobble from idle breathing + click bounce bulge
        float xScale = 1f + wobbleX + Mathf.Abs(_clickBounce) * 0.15f;

        // Scale only — no rotation. Orientation stays fixed.
        _spongeVisual.localScale = new Vector3(
            SpongeBaseScale.x * xScale,
            SpongeBaseScale.y * yScale,
            SpongeBaseScale.z * stretchScale
        );
    }

    private void PlaySFX(AudioClip clip)
    {
        if (_sfxCooldown > 0f) return;
        if (AudioManager.Instance != null && clip != null)
        {
            AudioManager.Instance.PlaySFX(clip);
            _sfxCooldown = SFX_COOLDOWN;
        }
    }

    /// <summary>Extract the first set bit from a LayerMask value.</summary>
    private static int LayerMaskToLayer(LayerMask mask)
    {
        int val = mask.value;
        for (int i = 0; i < 32; i++)
            if ((val & (1 << i)) != 0) return i;
        return 0;
    }
}
