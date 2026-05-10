using UnityEngine;

/// <summary>
/// Drop this on any object that's turning white. Logs the exact frame
/// and call stack when materials or MPB colors change to white.
/// Remove after debugging.
/// </summary>
public class WhiteOutDetector : MonoBehaviour
{
    private Renderer _renderer;
    private Color[] _lastColors;
    private Material[] _lastMats;
    private int _lastMatCount;
    private bool _caught;

    private void Start()
    {
        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer == null) { enabled = false; return; }
        CaptureState();
        // Disable self — remove this component from scene when done debugging
        enabled = false;
    }

    private void Update()
    {
        if (_renderer == null || _caught) return;

        var mats = _renderer.sharedMaterials;

        // Check if material count changed
        if (mats.Length != _lastMatCount)
        {
            Debug.LogWarning($"[WhiteOutDetector] '{name}' material count changed {_lastMatCount} → {mats.Length}", this);
            Debug.LogWarning($"[WhiteOutDetector] Stack: {System.Environment.StackTrace}");
            CaptureState();
        }

        // Check if any material was swapped to null or different
        for (int i = 0; i < Mathf.Min(mats.Length, _lastMats.Length); i++)
        {
            if (mats[i] != _lastMats[i])
            {
                string oldName = _lastMats[i] != null ? _lastMats[i].name : "NULL";
                string newName = mats[i] != null ? mats[i].name : "NULL";
                Debug.LogWarning($"[WhiteOutDetector] '{name}' slot {i} material changed: {oldName} → {newName}", this);
            }
        }

        // Check MPB color
        var mpb = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(mpb);
        if (mpb.HasColor("_BaseColor"))
        {
            Color c = mpb.GetColor("_BaseColor");
            if (c.r > 0.95f && c.g > 0.95f && c.b > 0.95f && c != Color.clear)
            {
                Debug.LogError($"[WhiteOutDetector] '{name}' MPB _BaseColor is WHITE ({c})! Catching culprit.", this);
                Debug.LogError($"[WhiteOutDetector] Stack: {System.Environment.StackTrace}");
                _caught = true;
            }
        }

        // Check actual material colors
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            Color bc = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color;
            if (i < _lastColors.Length && _lastColors[i] != bc)
            {
                Debug.LogWarning($"[WhiteOutDetector] '{name}' slot {i} _BaseColor changed: {_lastColors[i]} → {bc}", this);
                if (bc.r > 0.95f && bc.g > 0.95f && bc.b > 0.95f)
                {
                    Debug.LogError($"[WhiteOutDetector] '{name}' slot {i} turned WHITE! Stack: {System.Environment.StackTrace}", this);
                    _caught = true;
                }
            }
        }

        CaptureState();
    }

    private void CaptureState()
    {
        var mats = _renderer.sharedMaterials;
        _lastMatCount = mats.Length;
        _lastMats = new Material[mats.Length];
        _lastColors = new Color[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            _lastMats[i] = mats[i];
            _lastColors[i] = mats[i] != null
                ? (mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color)
                : Color.clear;
        }
    }
}
