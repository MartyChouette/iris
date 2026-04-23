/**
 * @file SapParticleController.cs
 * @brief Lightweight ParticleSystem-based sap spray - replaces expensive Obi Fluid.
 *
 * @details
 * This system uses Unity's built-in ParticleSystem (GPU-accelerated) instead of
 * Obi Fluid's CPU-based SPH simulation. Performance improvement is 10-50x.
 *
 * Features:
 * - Object pooling for ParticleSystems
 * - Configurable burst profiles per part type (stem/leaf/petal)
 * - Particle collision detection for surface staining
 * - Works with SapDecalSpawner for persistent stains
 *
 * @ingroup fluids
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SapParticleController : MonoBehaviour
{
    public static SapParticleController Instance { get; private set; }

    private static readonly WaitForSeconds s_pollWait = new WaitForSeconds(0.1f);
    private static readonly WaitForSeconds s_wait015 = new WaitForSeconds(0.15f);

    [System.Serializable]
    public class SapBurstProfile
    {
        [Tooltip("Particle emission speed (units/sec)")]
        public float speed = 10f;

        [Tooltip("How long the burst lasts")]
        public float duration = 0.2f;

        [Tooltip("Random angle variation in degrees")]
        public float angleJitter = 5f;

        [Tooltip("Number of particles per burst")]
        public int particleCount = 30;

        [Tooltip("Particle size")]
        public float particleSize = 0.08f;

        [Tooltip("Particle lifetime")]
        public float lifetime = 1.5f;
    }

    [Header("Prefab")]
    [Tooltip("ParticleSystem prefab for sap spray. Should have collision enabled.")]
    public ParticleSystem sapParticlePrefab;

    [Header("Pooling")]
    public int initialPoolSize = 8;
    public int maxPoolSize = 16;

    [Header("Stem Cut Settings")]
    public float stemEndOffset = 0.1f;
    public SapBurstProfile stemTopBurst = new SapBurstProfile
    {
        speed = 50f, duration = 1f, angleJitter = 15f,
        particleCount = 80, particleSize = 1.2f, lifetime = 8f
    };
    public SapBurstProfile stemBottomBurst = new SapBurstProfile
    {
        speed = 35f, duration = 0.8f, angleJitter = 10f,
        particleCount = 50, particleSize = 1f, lifetime = 6f
    };

    [Header("Leaf / Petal Tear Settings")]
    public SapBurstProfile leafTearBurst = new SapBurstProfile
    {
        speed = 40f, duration = 5f, angleJitter = 20f,
        particleCount = 60, particleSize = 1.2f, lifetime = 8f
    };
    public SapBurstProfile petalTearBurst = new SapBurstProfile
    {
        speed = 35f, duration = 4f, angleJitter = 25f,
        particleCount = 50, particleSize = 1f, lifetime = 6f
    };

    [Header("Follow Drip")]
    [Tooltip("How long the follow-drip lasts after a leaf/petal tears off (seconds)")]
    public float followDripDuration = 2f;

    [Header("Appearance")]
    public Color sapColor = new Color(0.2f, 0.8f, 0.1f, 1f);
    public Gradient sapColorOverLifetime;

    [Header("Global Intensity")]
    [Tooltip("Master sap control — 1 = little squirt, 10 = complete gusher")]
    [Range(0f, 10f)]
    public float sapIntensity = 1f;

    [Header("Physics")]
    [Tooltip("Layer mask for particle collision detection")]
    public LayerMask collisionLayers = ~0;

    [Tooltip("Gravity multiplier for particles")]
    public float gravityModifier = 1f;

    // Pool
    private List<ParticleSystem> _pool;
    private Queue<ParticleSystem> _freeQueue;
    private Transform _poolRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset()
    {
        Instance = null;
    }

    private void Awake()
    {
        Instance = this;
        InitializePool();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // No test burst — particles only fire from actual bleed points on leaves/petals/stems.

    private void InitializePool()
    {
        _pool = new List<ParticleSystem>(maxPoolSize);
        _freeQueue = new Queue<ParticleSystem>(maxPoolSize);

        // Create pool root for organization
        var poolObj = new GameObject("SapParticlePool");
        poolObj.transform.SetParent(transform);
        _poolRoot = poolObj.transform;

        if (sapParticlePrefab == null)
        {
            Debug.LogWarning("[SapParticleController] No particle prefab assigned. Creating default.");
            CreateDefaultPrefab();
        }

        for (int i = 0; i < initialPoolSize; i++)
        {
            CreatePooledParticleSystem();
        }
    }

    private void CreateDefaultPrefab()
    {
        // Create a basic particle system if none provided
        var go = new GameObject("DefaultSapParticle");
        go.transform.SetParent(_poolRoot);
        var ps = go.AddComponent<ParticleSystem>();

        // Configure main module
        var main = ps.main;
        main.startLifetime = 1f;
        main.startSpeed = 10f;
        main.startSize = 0.05f;
        main.startColor = sapColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravityModifier;
        main.maxParticles = 1000;

        // Configure emission (we'll burst manually)
        var emission = ps.emission;
        emission.enabled = false;

        // Configure shape
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.01f;

        // Configure collision
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.dampen = 0.2f;
        collision.bounce = 0.1f;
        collision.lifetimeLoss = 0.3f;
        collision.sendCollisionMessages = true;
        collision.collidesWith = collisionLayers;

        // Configure renderer
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Use default particle material - user should assign proper material

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.SetActive(false);

        sapParticlePrefab = ps;
    }

    private ParticleSystem CreatePooledParticleSystem()
    {
        ParticleSystem ps = Instantiate(sapParticlePrefab, _poolRoot);
        ps.gameObject.name = $"SapParticle_{_pool.Count}";

        // Minimal overrides for pool compatibility — keep all visual settings from prefab
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);

        // Ensure collision is fully configured for surface splatting
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.sendCollisionMessages = true;
        collision.collidesWith = collisionLayers;
        collision.bounce = 0.1f;
        collision.dampen = 0.3f;
        collision.lifetimeLoss = 0.2f;

        // Add decal spawner if not present
        if (ps.GetComponent<SapDecalSpawner>() == null)
        {
            ps.gameObject.AddComponent<SapDecalSpawner>();
        }

        _pool.Add(ps);
        _freeQueue.Enqueue(ps);

        return ps;
    }

    private ParticleSystem GetFreeParticleSystem()
    {
        // Try to get from free queue
        while (_freeQueue.Count > 0)
        {
            var ps = _freeQueue.Dequeue();
            if (ps != null && !ps.isPlaying)
                return ps;
        }

        // Expand pool if possible
        if (_pool.Count < maxPoolSize)
        {
            var newPs = CreatePooledParticleSystem();
            return newPs;
        }

        // Fallback: find any stopped system
        foreach (var ps in _pool)
        {
            if (!ps.isPlaying) return ps;
        }

        Debug.LogWarning("[SapParticleController] Pool exhausted!");
        return null;
    }

    private void ReturnToPool(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(false);
        _freeQueue.Enqueue(ps);
    }

    // ─────────────────────────────────────────────────────────────
    // Public API - matches FlowerSapController interface
    // ─────────────────────────────────────────────────────────────

    public void EmitStemCut(Vector3 planePoint, Vector3 planeNormal, FlowerStemRuntime stem)
    {
        if (sapIntensity <= 0f) return;

        Vector3 exactPos = planePoint;
        if (stem != null)
        {
            exactPos = stem.GetClosestPointOnStem(planePoint);
        }

        Vector3 dir = planeNormal.normalized;

        // Spray from both ends of the cut
        EmitBurst(exactPos + dir * stemEndOffset, dir, stemTopBurst);
        EmitBurst(exactPos - dir * stemEndOffset, -dir, stemBottomBurst);
    }

    public void EmitLeafTear(Vector3 pos, Vector3 normal, Transform followTarget = null)
    {
        if (sapIntensity <= 0f) return;
        EmitBurst(pos, normal.normalized, leafTearBurst);
    }

    public void EmitPetalTear(Vector3 pos, Vector3 normal, Transform followTarget = null)
    {
        if (sapIntensity <= 0f) return;
        EmitBurst(pos, normal.normalized, petalTearBurst);
    }

    /// <summary>
    /// Called directly from FlowerPartRuntime.MarkDetached — guaranteed to fire for ALL parts.
    /// Does an initial burst + follow-drip that tracks the falling part.
    /// </summary>
    public void EmitTearWithFollow(Transform part, bool isPetal)
    {
        if (sapIntensity <= 0f) return;
        if (part == null) return;

        var profile = isPetal ? petalTearBurst : leafTearBurst;
        Vector3 dir = Vector3.down;

        // Initial burst at the break point
        EmitBurst(part.position, dir, profile);

        // Follow-drip: one PS emits periodic small bursts at the part's position as it falls
        ParticleSystem ps = GetFreeParticleSystem();
        if (ps == null) return;

        float sizeScale = Mathf.Pow(sapIntensity, 0.5f);
        float speedScale = Mathf.Pow(sapIntensity, 0.3f);
        float lifeScale = Mathf.Pow(sapIntensity, 0.3f);

        ps.transform.SetParent(_poolRoot);
        ps.transform.position = part.position;
        ps.transform.rotation = Quaternion.LookRotation(dir);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = profile.speed * speedScale * 0.5f;
        main.startSize = profile.particleSize * sizeScale;
        main.startLifetime = profile.lifetime * lifeScale;
        main.startColor = sapColor;
        main.gravityModifier = gravityModifier;

        ps.gameObject.SetActive(true);
        ps.Clear();
        ps.Play();

        StartCoroutine(FollowAndDrip(ps, part, profile, dir));
    }

    private IEnumerator FollowAndDrip(ParticleSystem ps, Transform target, SapBurstProfile profile, Vector3 direction)
    {
        float elapsed = 0f;
        float duration = followDripDuration;
        float interval = 0.15f;
        int dripCount = Mathf.Max(2, Mathf.RoundToInt(profile.particleCount * sapIntensity * 0.2f));

        while (elapsed < duration && ps != null && target != null)
        {
            ps.transform.position = target.position;
            ps.transform.rotation = Quaternion.LookRotation(direction);
            ps.Emit(dripCount);
            elapsed += interval;
            yield return s_wait015;
        }

        // Let remaining particles finish
        float remainLife = profile.lifetime * Mathf.Pow(sapIntensity, 0.3f) + 0.5f;
        yield return new WaitForSeconds(remainLife);
        while (ps != null && ps.particleCount > 0)
            yield return s_pollWait;

        ReturnToPool(ps);
    }

    private void EmitBurst(Vector3 position, Vector3 direction, SapBurstProfile profile)
    {
        ParticleSystem ps = GetFreeParticleSystem();
        if (ps == null) return;

        // Intensity-driven scaling: 1 = squirt, 10 = gusher
        float countScale = sapIntensity;
        float sizeScale = Mathf.Pow(sapIntensity, 0.5f);   // 1x → 3.16x
        float speedScale = Mathf.Pow(sapIntensity, 0.3f);  // 1x → 2x
        float lifeScale = Mathf.Pow(sapIntensity, 0.3f);   // 1x → 2x

        // Apply jitter to direction
        if (profile.angleJitter > 0f)
        {
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-profile.angleJitter, profile.angleJitter),
                Random.Range(-profile.angleJitter, profile.angleJitter),
                0f
            );
            direction = jitter * direction;
        }

        // Position and orient
        ps.transform.SetParent(_poolRoot);
        ps.transform.position = position;
        ps.transform.rotation = Quaternion.LookRotation(direction);

        // Apply burst profile tuning — scaled by master intensity
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = profile.speed * speedScale;
        main.startSize = profile.particleSize * sizeScale;
        main.startLifetime = profile.lifetime * lifeScale;
        main.startColor = sapColor;
        main.gravityModifier = gravityModifier;

        // Activate and emit
        ps.gameObject.SetActive(true);
        ps.Clear();
        ps.Play();
        ps.Emit(Mathf.Max(1, Mathf.RoundToInt(profile.particleCount * countScale)));

        // Schedule return to pool
        StartCoroutine(ReturnToPoolAfterDelay(ps, profile.lifetime * lifeScale + 0.5f));
    }

    private IEnumerator ReturnToPoolAfterDelay(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);

        // Wait until all particles are gone, but cap at 3 extra seconds
        // to prevent infinite loops from collision-bouncing particles.
        float timeout = 3f;
        float waited = 0f;
        while (ps != null && ps.particleCount > 0 && waited < timeout)
        {
            yield return s_pollWait;
            waited += 0.1f;
        }

        // Force-clear any lingering particles
        if (ps != null && ps.particleCount > 0)
        {
            ps.Clear();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        ReturnToPool(ps);
    }

}
