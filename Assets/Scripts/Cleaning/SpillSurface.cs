using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A runtime procedural spill texture on a Quad. Players wipe it clean by
/// clearing pixels. No fluid simulation — just a Texture2D with Perlin noise
/// splatter and per-pixel alpha erasure.
/// </summary>
public class SpillSurface : MonoBehaviour
{
    [Header("Spill Appearance")]
    [Tooltip("Base colour of the spill (coffee brown by default).")]
    [SerializeField] private Color spillColor = new Color(0.35f, 0.20f, 0.08f, 0.9f);

    [Tooltip("Resolution of the spill texture (square). 256 = ~256 KB.")]
    [SerializeField] private int textureSize = 256;

    [Tooltip("Fraction of the texture radius covered by the splatter (0-1).")]
    [SerializeField] private float spillCoverage = 0.35f;

    [Tooltip("Seed for Perlin noise. Change for different splatter shapes.")]
    [SerializeField] private int splatterSeed = 42;

    [Header("Events")]
    [Tooltip("Fires once when the surface is >= 95 % clean.")]
    public UnityEvent OnFullyClean;

    private Texture2D _tex;
    private Material _matInstance;
    private Color32[] _pixels;
    private int _totalSpillPixels;
    private int _cleanedPixels;
    private bool _fullyCleanFired;

    // ─── Public API ──────────────────────────────────────────────────

    /// <summary>Ratio of cleaned pixels to total spill pixels (0-1).</summary>
    public float CleanPercent
    {
        get
        {
            if (_totalSpillPixels == 0) return 1f;
            return Mathf.Clamp01((float)_cleanedPixels / _totalSpillPixels);
        }
    }

    /// <summary>
    /// Clear pixels within <paramref name="radius"/> (in UV space, 0-1) around
    /// the given UV coordinate. Call once per frame while the player drags.
    /// </summary>
    public void Wipe(Vector2 uv, float radius)
    {
        int cx = Mathf.RoundToInt(uv.x * (textureSize - 1));
        int cy = Mathf.RoundToInt(uv.y * (textureSize - 1));
        int r = Mathf.RoundToInt(radius * textureSize);
        int rSq = r * r;

        bool dirty = false;

        int xMin = Mathf.Max(cx - r, 0);
        int xMax = Mathf.Min(cx + r, textureSize - 1);
        int yMin = Mathf.Max(cy - r, 0);
        int yMax = Mathf.Min(cy + r, textureSize - 1);

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > rSq) continue;

                int idx = y * textureSize + x;
                if (_pixels[idx].a == 0) continue;

                _pixels[idx] = new Color32(0, 0, 0, 0);
                _cleanedPixels++;
                dirty = true;
            }
        }

        if (!dirty) return;

        _tex.SetPixels32(_pixels);
        _tex.Apply();

        if (!_fullyCleanFired && CleanPercent >= 0.95f)
        {
            _fullyCleanFired = true;
            Debug.Log("[SpillSurface] Surface is fully clean.");
            OnFullyClean?.Invoke();
        }
    }

    // ─── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        // Nudge stain slightly above the floor so depth testing works:
        // stain wins against the floor but loses against items placed on top.
        transform.position += transform.up * 0.001f;

        GenerateSpillTexture();
        ApplyToMaterial();
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_matInstance != null) Destroy(_matInstance);
    }

    // ─── Internals ───────────────────────────────────────────────────

    private void GenerateSpillTexture()
    {
        _tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Bilinear;
        _tex.wrapMode = TextureWrapMode.Clamp;

        _pixels = new Color32[textureSize * textureSize];

        float center = textureSize * 0.5f;
        float maxRadius = textureSize * 0.5f * spillCoverage;
        float seed = splatterSeed;

        _totalSpillPixels = 0;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Perlin noise modulates the edge of the splatter
                float noiseVal = Mathf.PerlinNoise(
                    seed + x * 0.05f,
                    seed + y * 0.05f);

                float threshold = maxRadius * (0.6f + noiseVal * 0.8f);

                if (dist < threshold)
                {
                    // Soft edge falloff
                    float edge = Mathf.Clamp01(1f - dist / threshold);
                    byte a = (byte)(spillColor.a * edge * 255f);

                    _pixels[y * textureSize + x] = new Color32(
                        (byte)(spillColor.r * 255f),
                        (byte)(spillColor.g * 255f),
                        (byte)(spillColor.b * 255f),
                        a);

                    if (a > 0) _totalSpillPixels++;
                }
                else
                {
                    _pixels[y * textureSize + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        _tex.SetPixels32(_pixels);
        _tex.Apply();

        _cleanedPixels = 0;
        _fullyCleanFired = false;

        Debug.Log($"[SpillSurface] Generated spill: {_totalSpillPixels} pixels.");
    }

    private void ApplyToMaterial()
    {
        var rend = GetComponent<Renderer>();
        if (rend == null)
        {
            Debug.LogError("[SpillSurface] No Renderer found on this GameObject.");
            return;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        _matInstance = new Material(shader);
        _matInstance.SetTexture("_BaseMap", _tex);
        _matInstance.color = Color.white;

        // Enable transparent rendering
        _matInstance.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
        _matInstance.SetFloat("_Blend", 0f);   // 0 = Alpha
        _matInstance.SetFloat("_AlphaClip", 0f);
        _matInstance.SetOverrideTag("RenderType", "Transparent");
        _matInstance.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _matInstance.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _matInstance.SetInt("_ZWrite", 0);
        // Transparent pass with standard depth testing. The 0.001m Y offset
        // (applied in Awake) prevents z-fighting with the floor, and ZTest LEqual
        // ensures items placed on top correctly occlude the stain.
        _matInstance.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        _matInstance.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        rend.sharedMaterial = _matInstance;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
    }
}
