using UnityEngine;

/// <summary>
/// Per-object PSX rendering overrides. Drop on any object to customize
/// vertex snapping and affine texture warping independently from the
/// global PSXRenderController settings.
///
/// Set SnapResolution to 0 to disable snapping on this object.
/// Set to -1 to use the global value (default behavior).
/// </summary>
public class PSXObjectSettings : MonoBehaviour
{
    [Header("Vertex Snapping")]
    [Tooltip("-1 = use global. 0 = disabled. >0 = custom snap resolution (80=PS1, 160=PS2, 500=smooth).")]
    [SerializeField] private float _snapResolution = -1f;

    [Header("Affine Texture Mapping")]
    [Tooltip("-1 = use global. 0 = normal UVs (no warp). 1 = full PS1 affine warping.")]
    [Range(-1f, 1f)]
    [SerializeField] private float _affineIntensity = -1f;

    [Header("Shadow Dither")]
    [Tooltip("-1 = use global. 0 = smooth shadows. 1 = full stipple dither.")]
    [Range(-1f, 1f)]
    [SerializeField] private float _shadowDither = -1f;

    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private bool _applied;

    private static readonly int SnapResID = Shader.PropertyToID("_VertexSnapResolution");
    private static readonly int AffineID = Shader.PropertyToID("_AffineIntensity");
    private static readonly int ShadowDitherID = Shader.PropertyToID("_ShadowDitherIntensity");

    public float SnapResolution
    {
        get => _snapResolution;
        set { _snapResolution = value; Apply(); }
    }

    public float AffineIntensity
    {
        get => _affineIntensity;
        set { _affineIntensity = value; Apply(); }
    }

    public float ShadowDither
    {
        get => _shadowDither;
        set { _shadowDither = value; Apply(); }
    }

    private void Start()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _mpb = new MaterialPropertyBlock();
        Apply();
    }

    private void OnValidate()
    {
        if (_renderers != null) Apply();
    }

    /// <summary>Apply overrides to all renderers via MaterialPropertyBlock.</summary>
    public void Apply()
    {
        if (_renderers == null) return;

        bool hasAny = (_snapResolution >= 0f) || (_affineIntensity >= 0f) || (_shadowDither >= 0f);
        if (!hasAny) return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;

            // Use a fresh MPB — never GetPropertyBlock first, because that
            // copies ALL material properties into the MPB (including _BaseColor),
            // which then overrides the material's authored color when set back.
            var mpb = new MaterialPropertyBlock();

            if (_snapResolution >= 0f)
                mpb.SetVector(SnapResID, new Vector4(_snapResolution, _snapResolution * 0.75f, 0f, 0f));

            if (_affineIntensity >= 0f)
                mpb.SetFloat(AffineID, _affineIntensity);

            if (_shadowDither >= 0f)
                mpb.SetFloat(ShadowDitherID, _shadowDither);

            r.SetPropertyBlock(mpb);
        }

        _applied = true;
    }

    /// <summary>Remove overrides — revert to global PSX settings.</summary>
    public void Clear()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(null);
        }
        _applied = false;
    }

    private void OnDisable()
    {
        if (_applied) Clear();
    }
}
