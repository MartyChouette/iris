using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A cleanable surface with two procedural texture layers: dirt and wet overlay.
/// Stubborn dirt requires spraying before wiping is effective.
/// Adapted from <see cref="SpillSurface"/> and <see cref="FaceCanvas"/> patterns.
/// </summary>
public class CleanableSurface : MonoBehaviour
{
    [Header("Area")]
    [Tooltip("Which apartment area this surface belongs to.")]
    [SerializeField] private ApartmentArea _area;

    [Header("Definition")]
    [Tooltip("Spill definition controlling appearance and stubbornness.")]
    [SerializeField] private SpillDefinition _definition;

    [Header("Renderers")]
    [Tooltip("Quad renderer for the dirt texture (transparent overlay on surface).")]
    [SerializeField] private Renderer _dirtRenderer;

    [Tooltip("Quad renderer for the wet/spray texture (above dirt layer).")]
    [SerializeField] private Renderer _wetRenderer;

    [Header("Wetness")]
    [Tooltip("Rate at which spray wetness evaporates (alpha/sec).")]
    [SerializeField] private float _evaporationRate = 0.02f;

    [Tooltip("Tint colour for spray wetness.")]
    [SerializeField] private Color _wetColor = new Color(0.5f, 0.7f, 1f, 0.4f);

    [Header("Events")]
    [Tooltip("Fires once when the surface is >= 95% clean.")]
    public UnityEvent OnFullyClean;

    // Dirt texture
    private Texture2D _dirtTex;
    private Material _dirtMat;
    private byte[] _dirtAlpha;
    private int _totalDirtPixels;
    private int _textureSize;

    // Wet overlay texture
    private Texture2D _wetTex;
    private Material _wetMat;
    private byte[] _wetAlpha;
    private Color32[] _wetPixels;
    private bool _wetDirty;

    // Tracking
    private bool _fullyCleanFired;

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Which apartment area this surface belongs to.</summary>
    public ApartmentArea Area => _area;

    /// <summary>Set the apartment area (used by scene builder).</summary>
    public void SetArea(ApartmentArea area) => _area = area;

    /// <summary>The spill definition driving this surface.</summary>
    public SpillDefinition Definition => _definition;

    /// <summary>Assign a new spill definition (used by ApartmentStainSpawner).</summary>
    public void SetDefinition(SpillDefinition def) => _definition = def;

    /// <summary>
    /// Regenerate dirt and wet textures, optionally with a new definition.
    /// Used by ApartmentStainSpawner to reset stains each day.
    /// </summary>
    public void Regenerate(SpillDefinition newDef = null)
    {
        if (newDef != null) _definition = newDef;
        if (_definition == null) return;

        if (_dirtTex != null) Destroy(_dirtTex);
        if (_wetTex != null) Destroy(_wetTex);
        if (_dirtMat != null) Destroy(_dirtMat);
        if (_wetMat != null) Destroy(_wetMat);

        _fullyCleanFired = false;
        _textureSize = _definition.textureSize;
        GenerateDirtTexture();
        GenerateWetTexture();
    }

    /// <summary>Fraction of dirt pixels that have been cleaned (0-1).</summary>
    public float CleanPercent
    {
        get
        {
            if (_totalDirtPixels == 0) return 1f;
            int stillDirty = 0;
            for (int i = 0; i < _dirtAlpha.Length; i++)
                if (_dirtAlpha[i] > 0) stillDirty++;
            return Mathf.Clamp01(1f - (float)stillDirty / _totalDirtPixels);
        }
    }

    /// <summary>True when >= 95% clean.</summary>
    public bool IsFullyClean => CleanPercent >= 0.95f;


    /// <summary>
    /// Wipe dirt at UV position. Effectiveness depends on stubbornness and wetness.
    /// Wiping also absorbs any cleaning fluid at the position.
    /// </summary>
    public void Wipe(Vector2 uv, float radius)
    {
        if (_dirtAlpha == null || _definition == null) return;

        int cx = Mathf.RoundToInt(uv.x * (_textureSize - 1));
        int cy = Mathf.RoundToInt(uv.y * (_textureSize - 1));
        int r = Mathf.Max(1, Mathf.RoundToInt(radius * _textureSize));
        int rSq = r * r;

        int xMin = Mathf.Max(cx - r, 0);
        int xMax = Mathf.Min(cx + r, _textureSize - 1);
        int yMin = Mathf.Max(cy - r, 0);
        int yMax = Mathf.Min(cy + r, _textureSize - 1);

        bool dirty = false;

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > rSq) continue;

                int idx = y * _textureSize + x;
                if (_dirtAlpha[idx] == 0) continue;

                // Sponge is always fully effective
                _dirtAlpha[idx] = 0;
                _wetAlpha[idx] = 0;
                dirty = true;
            }
        }

        if (!dirty) return;

        RebuildDirtTexture();
        RebuildWetTexture();

        if (!_fullyCleanFired && IsFullyClean)
        {
            _fullyCleanFired = true;
            Debug.Log($"[CleanableSurface] {_definition.displayName} is fully clean.");
            OnFullyClean?.Invoke();
        }
    }

    /// <summary>
    /// Spray cleaning fluid at UV position. Adds wetness that boosts wipe effectiveness.
    /// Only sprays where dirt exists.
    /// </summary>
    public void Spray(Vector2 uv, float radius)
    {
        if (_dirtAlpha == null) return;

        int cx = Mathf.RoundToInt(uv.x * (_textureSize - 1));
        int cy = Mathf.RoundToInt(uv.y * (_textureSize - 1));
        int r = Mathf.Max(1, Mathf.RoundToInt(radius * _textureSize));
        int rSq = r * r;

        int xMin = Mathf.Max(cx - r, 0);
        int xMax = Mathf.Min(cx + r, _textureSize - 1);
        int yMin = Mathf.Max(cy - r, 0);
        int yMax = Mathf.Min(cy + r, _textureSize - 1);

        byte addAlpha = (byte)(_wetColor.a * 255f);
        bool dirty = false;

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                int distSq = dx * dx + dy * dy;
                if (distSq > rSq) continue;

                int idx = y * _textureSize + x;
                if (_dirtAlpha[idx] == 0) continue;

                // Soft edge falloff
                float dist = Mathf.Sqrt(distSq);
                float falloff = Mathf.Clamp01(1f - dist / r);
                byte scaledAlpha = (byte)(addAlpha * falloff);

                int newWet = Mathf.Min(255, _wetAlpha[idx] + scaledAlpha);
                _wetAlpha[idx] = (byte)newWet;
                dirty = true;
            }
        }

        if (dirty)
            _wetDirty = true;
    }

    // ── Lifecycle ───────────────────────────────────────────────────

    /// <summary>How much bigger the collider is than the visual quad (forgiving edge).</summary>
    private const float ColliderPadding = 1.4f;

    private bool _colliderExpanded;

    void Awake()
    {
        // Nudge stain slightly above the floor so depth testing works:
        // stain wins against the floor but loses against items placed on top.
        transform.position += transform.up * 0.001f;

        // Expand collider so the sponge doesn't "catch" at the visual edge —
        // cursor can overshoot and still register hits for smooth scrubbing.
        ExpandCollider();

        // Defer texture generation to OnEnable/Regenerate — stain slots start
        // inactive and get activated by AuthoredMessSpawner, so generating
        // textures in Awake wastes time on stains that may never be used.
    }

    void OnEnable()
    {
        ExpandCollider();
        // Generate textures on first activation (lazy init)
        if (_dirtTex == null && _definition != null)
        {
            _textureSize = _definition.textureSize;
            GenerateDirtTexture();
            GenerateWetTexture();
        }
    }

    private void ExpandCollider()
    {
        if (_colliderExpanded) return;
        _colliderExpanded = true;
        var box = GetComponent<BoxCollider>();
        if (box != null)
            box.size = new Vector3(box.size.x * ColliderPadding, box.size.y * ColliderPadding, box.size.z);
    }

    void Update()
    {
        // Evaporate wetness
        if (_wetAlpha != null)
        {
            float decay = _evaporationRate * Time.deltaTime * 255f;
            if (decay > 0f)
            {
                for (int i = 0; i < _wetAlpha.Length; i++)
                {
                    if (_wetAlpha[i] == 0) continue;
                    int newVal = Mathf.Max(0, _wetAlpha[i] - Mathf.RoundToInt(decay));
                    _wetAlpha[i] = (byte)newVal;
                    _wetDirty = true;
                }
            }

            if (_wetDirty)
            {
                RebuildWetTexture();
                _wetDirty = false;
            }
        }

    }

    void OnDestroy()
    {
        if (_dirtTex != null) Destroy(_dirtTex);
        if (_dirtMat != null) Destroy(_dirtMat);
        if (_wetTex != null) Destroy(_wetTex);
        if (_wetMat != null) Destroy(_wetMat);
    }

    // ── Texture generation ──────────────────────────────────────────

    private void GenerateDirtTexture()
    {
        _dirtTex = new Texture2D(_textureSize, _textureSize, TextureFormat.RGBA32, false);
        _dirtTex.filterMode = FilterMode.Bilinear;
        _dirtTex.wrapMode = TextureWrapMode.Clamp;

        _dirtAlpha = new byte[_textureSize * _textureSize];
        var pixels = new Color32[_textureSize * _textureSize];

        float center = _textureSize * 0.5f;
        float maxRadius = _textureSize * 0.5f * _definition.coverage;
        float seed = _definition.seed;

        Color c = _definition.spillColor;
        byte r = (byte)(c.r * 255f);
        byte g = (byte)(c.g * 255f);
        byte b = (byte)(c.b * 255f);

        _totalDirtPixels = 0;

        for (int y = 0; y < _textureSize; y++)
        {
            for (int x = 0; x < _textureSize; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float noiseVal = Mathf.PerlinNoise(
                    seed + x * 0.05f,
                    seed + y * 0.05f);

                float threshold = maxRadius * (0.6f + noiseVal * 0.8f);

                int idx = y * _textureSize + x;
                if (dist < threshold)
                {
                    float edge = Mathf.Clamp01(1f - dist / threshold);
                    byte a = (byte)(edge * 255f);

                    pixels[idx] = new Color32(r, g, b, a);
                    _dirtAlpha[idx] = a;

                    if (a > 0) _totalDirtPixels++;
                }
                else
                {
                    pixels[idx] = new Color32(0, 0, 0, 0);
                    _dirtAlpha[idx] = 0;
                }
            }
        }

        _dirtTex.SetPixels32(pixels);
        _dirtTex.Apply();

        ApplyTransparentMaterial(_dirtRenderer, _dirtTex, out _dirtMat);

        Debug.Log($"[CleanableSurface] {_definition.displayName}: {_totalDirtPixels} dirt pixels.");
    }

    private void GenerateWetTexture()
    {
        _wetTex = new Texture2D(_textureSize, _textureSize, TextureFormat.RGBA32, false);
        _wetTex.filterMode = FilterMode.Bilinear;
        _wetTex.wrapMode = TextureWrapMode.Clamp;

        _wetAlpha = new byte[_textureSize * _textureSize];
        _wetPixels = new Color32[_textureSize * _textureSize];

        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < _wetPixels.Length; i++)
        {
            _wetPixels[i] = clear;
            _wetAlpha[i] = 0;
        }

        _wetTex.SetPixels32(_wetPixels);
        _wetTex.Apply();

        ApplyTransparentMaterial(_wetRenderer, _wetTex, out _wetMat);
    }

    // ── Texture rebuild ─────────────────────────────────────────────

    private void RebuildDirtTexture()
    {
        Color c = _definition.spillColor;
        byte r = (byte)(c.r * 255f);
        byte g = (byte)(c.g * 255f);
        byte b = (byte)(c.b * 255f);

        var pixels = new Color32[_textureSize * _textureSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte a = _dirtAlpha[i];
            pixels[i] = a > 0 ? new Color32(r, g, b, a) : new Color32(0, 0, 0, 0);
        }

        _dirtTex.SetPixels32(pixels);
        _dirtTex.Apply();
    }

    private void RebuildWetTexture()
    {
        byte wr = (byte)(_wetColor.r * 255f);
        byte wg = (byte)(_wetColor.g * 255f);
        byte wb = (byte)(_wetColor.b * 255f);

        for (int i = 0; i < _wetPixels.Length; i++)
        {
            byte a = _wetAlpha[i];
            _wetPixels[i] = a > 0 ? new Color32(wr, wg, wb, a) : new Color32(0, 0, 0, 0);
        }

        _wetTex.SetPixels32(_wetPixels);
        _wetTex.Apply();
    }

    // ── Material setup ──────────────────────────────────────────────

    private void ApplyTransparentMaterial(Renderer rend, Texture2D tex, out Material mat)
    {
        mat = null;
        if (rend == null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        mat = new Material(shader);
        mat.SetTexture("_BaseMap", tex);
        mat.color = Color.white;

        // Transparent setup (same as SpillSurface)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

}
