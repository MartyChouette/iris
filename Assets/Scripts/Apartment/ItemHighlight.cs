using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clean highlight system. MPB-only — never touches material arrays.
/// Tints the object's _BaseColor via MaterialPropertyBlock.
/// Layers: Hover, Gaze, Display, PrepLiked, PrepDisliked, Interact.
/// Drop-in replacement for InteractableHighlight.
/// </summary>
public class ItemHighlight : MonoBehaviour
{
    // ── Static registry ───────────────
    private static readonly List<ItemHighlight> s_all = new();
    public static IReadOnlyList<ItemHighlight> All => s_all;

    // ── Layer state ───────────────
    private bool _hover;
    private bool _gaze;
    private bool _display;
    private bool _prepLiked;
    private bool _prepDisliked;
    private bool _interact;

    private Renderer[] _renderers;
    private Color[][] _originalColors; // per-renderer, per-material-slot
    private bool _dirty;

    // ── Colors ───────────────
    private static readonly Color HoverColor = new Color(1f, 0.95f, 0.8f, 1f);
    private static readonly Color GazeColor = new Color(1f, 0.95f, 0.8f, 1f);
    private static readonly Color DisplayColor = new Color(1f, 0.95f, 0.8f, 1f);
    private static readonly Color PrepLikedColor = new Color(1f, 0.95f, 0.8f, 1f);
    private static readonly Color PrepDislikedColor = new Color(1f, 0.35f, 0.5f, 1f);
    private static readonly Color InteractColor = new Color(1f, 0.95f, 0.8f, 1f);

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        CaptureOriginalColors();
    }

    private void OnEnable() => s_all.Add(this);
    private void OnDisable()
    {
        ClearAllMPB();
        s_all.Remove(this);
    }

    private void CaptureOriginalColors()
    {
        if (_renderers == null) return;
        _originalColors = new Color[_renderers.Length][];
        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) { _originalColors[r] = new Color[0]; continue; }
            var mats = _renderers[r].sharedMaterials;
            _originalColors[r] = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] == null) continue;
                _originalColors[r][m] = mats[m].HasProperty("_BaseColor")
                    ? mats[m].GetColor("_BaseColor")
                    : mats[m].color;
            }
        }
    }

    // ── Public API (matches InteractableHighlight) ───────────────

    public void SetHighlighted(bool on) { _hover = on; _dirty = true; }
    public void SetGazeHighlighted(bool on) { _gaze = on; _dirty = true; }
    public void SetDisplayHighlighted(bool on) { _display = on; _dirty = true; }
    public void SetPrepLikedHighlighted(bool on) { _prepLiked = on; _dirty = true; }
    public void SetPrepDislikedHighlighted(bool on) { _prepDisliked = on; _dirty = true; }
    public void SetInteractHighlighted(bool on) { _interact = on; _dirty = true; }

    private bool _anyActive;

    private void Update()
    {
        bool any = _hover || _gaze || _display || _prepLiked || _prepDisliked || _interact;

        if (!any)
        {
            if (_anyActive || _dirty)
            {
                ClearAllMPB();
                _anyActive = false;
                _dirty = false;
            }
            return;
        }

        _anyActive = true;
        _dirty = false;

        // Priority: interact > prepDisliked > prepLiked > display > gaze > hover
        Color tint;
        if (_interact) tint = InteractColor;
        else if (_prepDisliked) tint = PrepDislikedColor;
        else if (_prepLiked) tint = PrepLikedColor;
        else if (_display) tint = DisplayColor;
        else if (_gaze) tint = GazeColor;
        else tint = HoverColor;

        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 3f);
        ApplyTint(tint, pulse);
    }

    private void ApplyTint(Color tint, float intensity)
    {
        if (_renderers == null || _originalColors == null) return;

        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;

            // Per-material-slot MPB so each sub-mesh keeps its own base color
            for (int m = 0; m < _originalColors[r].Length; m++)
            {
                var mpb = new MaterialPropertyBlock();
                Color original = _originalColors[r][m];
                Color highlighted = Color.Lerp(original, tint, 0.4f * intensity);
                mpb.SetColor("_BaseColor", highlighted);
                _renderers[r].SetPropertyBlock(mpb, m);
            }
        }
    }

    private void ClearAllMPB()
    {
        if (_renderers == null) return;
        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] == null) continue;
            int slotCount = _renderers[r].sharedMaterials.Length;
            for (int m = 0; m < slotCount; m++)
                _renderers[r].SetPropertyBlock(null, m);
        }
    }

    /// <summary>Re-cache renderer list and original colors (call after material changes).</summary>
    public void RefreshBaseMaterials()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        CaptureOriginalColors();
    }
}
