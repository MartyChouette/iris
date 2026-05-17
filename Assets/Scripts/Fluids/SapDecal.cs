/**
 * @file SapDecal.cs
 * @brief Individual sap decal/stain that fades out over time.
 *
 * @details
 * Pooled decal component that:
 * - Fades in quickly when activated
 * - Persists for a configurable duration
 * - Fades out smoothly
 * - Returns to pool when done
 *
 * Can use either a SpriteRenderer, MeshRenderer with transparency,
 * or a DecalProjector (URP).
 *
 * @ingroup fluids
 */

using UnityEngine;

public class SapDecal : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("How long the decal stays fully visible")]
    public float visibleDuration = 5f;

    [Tooltip("How long it takes to fade out")]
    public float fadeOutDuration = 2f;

    [Tooltip("How long it takes to fade in")]
    public float fadeInDuration = 0.1f;

    [Header("Appearance")]
    public Color decalColor = new Color(0.2f, 0.7f, 0.1f, 0.8f);

    // Internal state
    private float _timer;
    private float _alpha;
    private bool _isActive;

    // Cached components (set whichever is available)
    private SpriteRenderer _spriteRenderer;
    private MeshRenderer _meshRenderer;
    private Material _material;
    private Color _baseColor;

    private void Awake()
    {
        // Cache renderer references — defer material instance to Activate()
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (_spriteRenderer != null)
            _baseColor = _spriteRenderer.color;
        else if (_meshRenderer != null && _meshRenderer.sharedMaterial != null)
            _baseColor = _meshRenderer.sharedMaterial.color;

        gameObject.SetActive(false);
    }

    public void Activate()
    {
        _timer = 0f;
        _alpha = 0f;
        _isActive = true;

        // Create material instance on first activation (not on pool init)
        if (_material == null && _meshRenderer != null)
            _material = _meshRenderer.material;

        gameObject.SetActive(true);
        UpdateAlpha(0f);
    }

    public void Deactivate()
    {
        _isActive = false;
        gameObject.SetActive(false);
        SapDecalPool.Instance?.Return(this);
    }

    private void Update()
    {
        if (!_isActive) return;

        _timer += Time.deltaTime;

        float totalDuration = fadeInDuration + visibleDuration + fadeOutDuration;

        if (_timer < fadeInDuration)
        {
            // Fade in
            _alpha = _timer / fadeInDuration;
        }
        else if (_timer < fadeInDuration + visibleDuration)
        {
            // Fully visible
            _alpha = 1f;
        }
        else if (_timer < totalDuration)
        {
            // Fade out
            float fadeProgress = (_timer - fadeInDuration - visibleDuration) / fadeOutDuration;
            _alpha = 1f - fadeProgress;
        }
        else
        {
            // Done - return to pool
            Deactivate();
            return;
        }

        UpdateAlpha(_alpha);
    }

    private void UpdateAlpha(float alpha)
    {
        Color c = decalColor;
        c.a = decalColor.a * alpha;

        if (_spriteRenderer != null)
        {
            _spriteRenderer.color = c;
        }
        else if (_material != null)
        {
            _material.color = c;
        }
    }

    private void OnDestroy()
    {
        // Clean up instanced material
        if (_material != null)
        {
            Destroy(_material);
        }
    }
}
