using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fly that buzzes around smelly items. Click to swat.
/// Spawns near items with SmellAmount >= threshold. Nema dislikes seeing them.
/// Each fly has a ReactableTag with "pest" so the date system reacts negatively.
/// </summary>
public class FlyController : MonoBehaviour
{
    // ── Static registry ──
    private static readonly List<FlyController> s_all = new();
    public static IReadOnlyList<FlyController> All => s_all;

    [Header("Movement")]
    [Tooltip("Radius of the lazy orbit around the target item.")]
    [SerializeField] private float _orbitRadius = 0.3f;

    [Tooltip("Orbit speed (radians/sec).")]
    [SerializeField] private float _orbitSpeed = 2.5f;

    [Tooltip("Vertical bob amplitude.")]
    [SerializeField] private float _bobAmplitude = 0.08f;

    [Tooltip("Vertical bob speed.")]
    [SerializeField] private float _bobSpeed = 4f;

    [Tooltip("Random jitter amplitude (makes path feel erratic).")]
    [SerializeField] private float _jitterAmount = 0.05f;

    [Header("Audio")]
    [Tooltip("Buzz volume at closest approach (max after fade-in).")]
    [SerializeField, Range(0f, 1f)] private float _buzzVolume = 0.015f;

    [Tooltip("Seconds for buzz to fade in from silence.")]
    [SerializeField] private float _buzzFadeIn = 3f;

    [Tooltip("Buzz frequency (Hz).")]
    [SerializeField] private float _buzzFrequency = 180f;

    [Tooltip("Max audible distance.")]
    [SerializeField] private float _buzzRange = 3f;

    // ── Runtime ──
    private Transform _target;
    private Vector3 _targetCenter;
    private float _orbitAngle;
    private float _bobPhase;
    private float _jitterTimer;
    private Vector3 _jitterOffset;
    private AudioSource _buzzSource;
    private MeshRenderer _renderer;
    private MeshFilter _meshFilter;
    private bool _isDead;
    private Camera _cam;
    private float _camTimer;

    // Splat
    private float _splatTimer = -1f;
    private const float SplatDuration = 0.4f;
    private float _buzzAge;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    /// <summary>
    /// Initialize the fly with a target to orbit around.
    /// </summary>
    public void Init(Transform target)
    {
        _target = target;
        _targetCenter = target != null ? target.position + Vector3.up * 0.25f : transform.position;
        _orbitAngle = Random.Range(0f, Mathf.PI * 2f);
        _bobPhase = Random.Range(0f, Mathf.PI * 2f);

        // Build visual — tiny black sphere
        _meshFilter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _meshFilter.sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        Destroy(sphere);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"));
        mat.color = new Color(0.1f, 0.1f, 0.12f);
        _renderer.material = mat;

        // Tiny scale — fly is about 1.5cm
        transform.localScale = Vector3.one * 0.015f;

        // Trigger collider for click detection only — no physics collisions
        var col = gameObject.AddComponent<SphereCollider>();
        col.radius = 3f; // expanded radius in local space (0.015 * 3 = ~0.045m hit zone)
        col.isTrigger = true;

        // ReactableTag — Nema dislikes pests
        var tag = gameObject.AddComponent<ReactableTag>();
        tag.Setup(new[] { "pest" }, "Fly");
        tag.IsActive = true;

        // Buzz audio
        _buzzSource = gameObject.AddComponent<AudioSource>();
        _buzzSource.loop = true;
        _buzzSource.spatialBlend = 1f; // 3D
        _buzzSource.minDistance = 0.3f;
        _buzzSource.maxDistance = _buzzRange;
        _buzzSource.rolloffMode = AudioRolloffMode.Linear;
        _buzzSource.volume = 0f;
        _buzzAge = 0f;
        CreateBuzzClip();
        _buzzSource.Play();
    }

    private void Update()
    {
        if (_isDead)
        {
            if (_splatTimer >= 0f)
            {
                _splatTimer -= Time.deltaTime;
                if (_splatTimer <= 0f)
                    Destroy(gameObject);
            }
            return;
        }

        // Update target center if target moves
        if (_target != null)
            _targetCenter = _target.position + Vector3.up * 0.25f;

        // Orbit
        _orbitAngle += _orbitSpeed * Time.deltaTime;
        float x = Mathf.Cos(_orbitAngle) * _orbitRadius;
        float z = Mathf.Sin(_orbitAngle) * _orbitRadius;

        // Bob
        _bobPhase += _bobSpeed * Time.deltaTime;
        float y = Mathf.Sin(_bobPhase) * _bobAmplitude;

        // Jitter — random direction changes
        _jitterTimer -= Time.deltaTime;
        if (_jitterTimer <= 0f)
        {
            _jitterOffset = Random.insideUnitSphere * _jitterAmount;
            _jitterTimer = Random.Range(0.1f, 0.3f);
        }

        transform.position = _targetCenter + new Vector3(x, y, z) + _jitterOffset;

        // Buzz fades in from silence, respects global settings
        _buzzAge += Time.deltaTime;
        float fadeT = _buzzFadeIn > 0f ? Mathf.Clamp01(_buzzAge / _buzzFadeIn) : 1f;
        float globalVol = AccessibilitySettings.MasterVolume * AccessibilitySettings.SFXVolume;
        _buzzSource.volume = _buzzVolume * fadeT * globalVol;
    }

    /// <summary>The transform this fly orbits around.</summary>
    public Transform Target => _target;

    /// <summary>Dismiss all flies targeting this transform or any of its children.</summary>
    public static void DismissFliesFor(Transform root)
    {
        if (root == null) return;
        for (int i = s_all.Count - 1; i >= 0; i--)
        {
            if (s_all[i] == null || s_all[i]._target == null) continue;
            if (s_all[i]._target == root || s_all[i]._target.IsChildOf(root))
                s_all[i].FlyAway();
        }
    }

    /// <summary>Fly away and self-destruct. Called when the source item is cleaned up.</summary>
    public void FlyAway()
    {
        if (_isDead) return;
        _isDead = true;
        if (_buzzSource != null) _buzzSource.Stop();
        var tag = GetComponent<ReactableTag>();
        if (tag != null) tag.IsActive = false;
        StartCoroutine(FlyAwayRoutine());
    }

    private System.Collections.IEnumerator FlyAwayRoutine()
    {
        // Fly high into the sky and shrink — long enough to feel like it's leaving
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * 6f + Random.insideUnitSphere * 1.5f;
        float elapsed = 0f;
        float duration = 2f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Ease-in so it accelerates upward
            float curved = t * t;
            transform.position = Vector3.Lerp(startPos, endPos, curved);
            // Only start shrinking in the second half
            float shrink = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
            transform.localScale = Vector3.one * 0.015f * shrink;
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>Swat this fly. Called by FlySpawner on click detection.</summary>
    public void Swat()
    {
        if (_isDead) return;
        _isDead = true;

        // Stop buzzing
        _buzzSource.Stop();

        // Play splat SFX
        AudioManager.Instance?.PlaySFX(null); // no clip yet — silent for now

        // Visual: flatten and darken
        transform.localScale = new Vector3(0.03f, 0.002f, 0.03f);
        if (_renderer != null)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            mpb.SetColor("_Color", new Color(0.05f, 0.05f, 0.05f));
            _renderer.SetPropertyBlock(mpb);
        }

        // Disable reactable tag so Nema stops reacting
        var tag = GetComponent<ReactableTag>();
        if (tag != null) tag.IsActive = false;

        // Fade out after brief splat display
        _splatTimer = SplatDuration;

        Debug.Log("[FlyController] Fly swatted!");
    }

    private void CreateBuzzClip()
    {
        int sampleRate = 44100;
        int sampleCount = sampleRate / 2; // 0.5 second loop
        var clip = AudioClip.Create("FlyBuzz", sampleCount, 1, sampleRate, false);
        float[] data = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            // Harsh buzz: fundamental + odd harmonics + noise
            float sample = 0.4f * Mathf.Sin(2f * Mathf.PI * _buzzFrequency * t)
                         + 0.3f * Mathf.Sin(2f * Mathf.PI * _buzzFrequency * 3f * t)
                         + 0.15f * Mathf.Sin(2f * Mathf.PI * _buzzFrequency * 5f * t)
                         + 0.15f * (Random.value * 2f - 1f); // noise
            data[i] = sample * 0.3f;
        }

        clip.SetData(data, 0);
        _buzzSource.clip = clip;
    }

    private void OnDestroy()
    {
        if (_buzzSource != null && _buzzSource.clip != null)
            Destroy(_buzzSource.clip);
        if (_renderer != null && _renderer.material != null)
            Destroy(_renderer.material);
    }
}
