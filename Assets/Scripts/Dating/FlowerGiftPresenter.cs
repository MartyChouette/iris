using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Zelda-style "You got an item!" presentation for the flower gift after a successful date.
/// Scene-scoped singleton. Spawns flower prefab in front of camera, bobs and spins it,
/// shows dark overlay with announcement text, plays SFX, then cleans up.
///
/// The flower prefab may come from the flower trimming scene (large scale). This presenter
/// normalizes it to _presentationScale (default 0.1) and parents it to the camera so it
/// stays in view regardless of which Cinemachine camera is active.
/// </summary>
public class FlowerGiftPresenter : MonoBehaviour
{
    public static FlowerGiftPresenter Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Root GameObject for the dark overlay (starts hidden).")]
    [SerializeField] private GameObject _overlayRoot;

    [Tooltip("CanvasGroup on the overlay for fade in/out.")]
    [SerializeField] private CanvasGroup _canvasGroup;

    [Tooltip("Text displaying the gift announcement.")]
    [SerializeField] private TMP_Text _itemNameText;

    [Header("Audio")]
    [Tooltip("Jingle SFX played when the flower is presented.")]
    [SerializeField] private AudioClip _presentSFX;

    [Header("Presentation")]
    [Tooltip("Offset from camera (local space: right, up, forward).")]
    [SerializeField] private Vector3 _spawnOffset = new Vector3(0f, 0.3f, 1.5f);

    [Tooltip("Scale applied to the flower clone (flower prefabs are often at trimming-scene scale).")]
    [SerializeField] private float _presentationScale = 0.1f;

    [Tooltip("Vertical bob amplitude in local units.")]
    [SerializeField] private float _bobAmplitude = 0.08f;

    [Tooltip("Bob oscillation speed.")]
    [SerializeField] private float _bobSpeed = 2.0f;

    [Tooltip("Spin speed in degrees per second (tilted on local axis like Zelda).")]
    [SerializeField] private float _spinSpeed = 60.0f;

    [Tooltip("Forward tilt angle for the spin (degrees).")]
    [SerializeField] private float _tiltAngle = 20f;

    [Tooltip("How long the flower is held on screen (seconds).")]
    [SerializeField] private float _holdDuration = 2.5f;

    [Tooltip("Fade in/out duration (seconds).")]
    [SerializeField] private float _fadeDuration = 0.3f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FlowerGiftPresenter] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_overlayRoot != null)
            _overlayRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Present a flower gift. Yields until the full animation + fade sequence is complete.
    /// </summary>
    public Coroutine Present(GameObject flowerPrefab, string characterName)
    {
        return StartCoroutine(PresentSequence(flowerPrefab, characterName));
    }

    private IEnumerator PresentSequence(GameObject flowerPrefab, string characterName)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[FlowerGiftPresenter] No main camera found, skipping presentation.");
            yield break;
        }

        // Create a pivot wrapper so the flower rotates around its visual center.
        var pivot = new GameObject("FlowerPivot");
        pivot.transform.SetParent(cam.transform);
        pivot.transform.localPosition = _spawnOffset;
        pivot.transform.localRotation = Quaternion.identity;
        pivot.transform.localScale = Vector3.one;

        // Spawn flower as child of the pivot.
        var flowerClone = Instantiate(flowerPrefab, pivot.transform);
        flowerClone.transform.localPosition = Vector3.zero;
        flowerClone.transform.localRotation = Quaternion.identity;
        flowerClone.transform.localScale = Vector3.one * _presentationScale;

        // Strip physics so the clone doesn't interact with the world
        foreach (var rb in flowerClone.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;
        foreach (var col in flowerClone.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // Center the flower visuals at the pivot origin so rotation is around the visual center
        CenterFlowerAtPivot(flowerClone);

        // Render flower on top of scene geometry (but behind UI overlay text)
        SetRenderOnTop(flowerClone);

        // Apply Zelda-style tilt (lean forward so spin looks dynamic)
        pivot.transform.localRotation = Quaternion.Euler(_tiltAngle, 0f, 0f);

        // Spawn sparkle particle ring around the flower
        var sparkleGO = SpawnSparkleRing(pivot.transform);

        float baseLocalY = _spawnOffset.y;

        // Set up overlay and block input
        if (_overlayRoot != null)
            _overlayRoot.SetActive(true);

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
        }

        if (_itemNameText != null)
            _itemNameText.text = $"{characterName} gave you a flower!";

        // Play SFX
        if (_presentSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_presentSFX);

        // Fade in
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (_canvasGroup != null)
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
            yield return null;
        }
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        // Hold: bob + spin + scroll zoom (in local space since flower is parented to camera)
        elapsed = 0f;
        float zoomScale = 1f;
        const float zoomMin = 0.5f;
        const float zoomMax = 2.0f;
        const float zoomStep = 0.08f;

        while (elapsed < _holdDuration)
        {
            elapsed += Time.deltaTime;

            // Scroll-wheel zoom
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                float scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    zoomScale += Mathf.Sign(scroll) * zoomStep;
                    zoomScale = Mathf.Clamp(zoomScale, zoomMin, zoomMax);
                    flowerClone.transform.localScale = Vector3.one * _presentationScale * zoomScale;
                }
            }

            if (pivot != null)
            {
                // Bob in local Y
                var localPos = pivot.transform.localPosition;
                localPos.y = baseLocalY + Mathf.Sin(elapsed * _bobSpeed * Mathf.PI * 2f) * _bobAmplitude;
                pivot.transform.localPosition = localPos;

                // Spin around tilted Y axis (Zelda-style) — pivot rotates, flower stays centered
                pivot.transform.localRotation =
                    Quaternion.Euler(_tiltAngle, 0f, 0f) *
                    Quaternion.Euler(0f, _spinSpeed * elapsed, 0f);
            }

            yield return null;
        }

        // Fade out
        elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / _fadeDuration);
            yield return null;
        }
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }

        // Cleanup — destroying pivot also destroys the flower clone and sparkle
        if (pivot != null)
            Destroy(pivot);

        if (_overlayRoot != null)
            _overlayRoot.SetActive(false);
    }

    /// <summary>
    /// Swap all renderers to the Iris/HeldOnTop shader (ZTest Always) so the
    /// flower draws on top of scene geometry. URP/Lit hard-codes ZTest LEqual,
    /// so only a shader swap reliably defeats depth testing.
    /// </summary>
    private static void SetRenderOnTop(GameObject flower)
    {
        var onTopShader = Shader.Find("Iris/HeldOnTop");
        if (onTopShader == null)
        {
            Debug.LogWarning("[FlowerGiftPresenter] Iris/HeldOnTop shader not found — flower may be occluded.");
            // Fallback: at least bump render queue (helps with shaders that do respect it)
            foreach (var rend in flower.GetComponentsInChildren<Renderer>())
                foreach (var mat in rend.materials)
                    if (mat != null) mat.renderQueue = 4000;
            return;
        }

        foreach (var rend in flower.GetComponentsInChildren<Renderer>())
        {
            // Instance materials so we don't mutate shared assets
            // (clone is destroyed after presentation, so no restore needed)
            var mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                mats[i].shader = onTopShader;
                mats[i].renderQueue = 4000;
            }
            rend.materials = mats;
        }
    }

    /// <summary>
    /// Offset all children so the visual center of the flower sits at the local origin.
    /// This ensures rotation around the visual center instead of an arbitrary pivot.
    /// </summary>
    private static void CenterFlowerAtPivot(GameObject flower)
    {
        var renderers = flower.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        // Convert world-space center to local space of the flower and shift all children
        Vector3 localCenter = flower.transform.InverseTransformPoint(bounds.center);
        foreach (Transform child in flower.transform)
            child.localPosition -= localCenter;
    }

    /// <summary>
    /// Creates a sparkle particle ring orbiting the flower (child of flower transform).
    /// </summary>
    private GameObject SpawnSparkleRing(Transform parent)
    {
        var go = new GameObject("GiftSparkle");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration = _holdDuration + _fadeDuration * 2f;
        main.loop = true;
        main.startLifetime = 0.8f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
        main.startColor = new Color(1f, 0.95f, 0.75f, 0.9f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 20f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.3f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = gradient;

        // Material
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 1f); // Additive
            mat.color = new Color(1f, 0.95f, 0.75f, 1f);
            rend.material = mat;
        }

        return go;
    }
}

