using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Brief screen-edge vignette pulse for impact moments (flower cuts, heavy impacts).
/// Finds the scene's Volume with a Vignette override and bumps it momentarily.
/// Self-spawns a runner GameObject on first call.
/// </summary>
public static class VignettePulse
{
    private static VignettePulseRunner s_runner;

    /// <summary>Flash the vignette briefly. Safe to call even if no Volume exists.</summary>
    public static void Pulse(float intensity = 0.45f, float duration = 0.25f)
    {
        if (s_runner == null)
        {
            var go = new GameObject("VignettePulseRunner");
            Object.DontDestroyOnLoad(go);
            s_runner = go.AddComponent<VignettePulseRunner>();
        }
        s_runner.DoPulse(intensity, duration);
    }
}

public class VignettePulseRunner : MonoBehaviour
{
    private Vignette _vignette;
    private float _baseIntensity;
    private bool _cached;
    private Coroutine _activeRoutine;

    private void CacheVignette()
    {
        if (_cached) return;
        _cached = true;

        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i].profile != null && volumes[i].profile.TryGet(out Vignette v))
            {
                _vignette = v;
                _baseIntensity = v.intensity.value;
                return;
            }
        }
    }

    public void DoPulse(float intensity, float duration)
    {
        CacheVignette();
        if (_vignette == null) return;

        if (_activeRoutine != null) StopCoroutine(_activeRoutine);
        _activeRoutine = StartCoroutine(PulseRoutine(intensity, duration));
    }

    private IEnumerator PulseRoutine(float peakIntensity, float duration)
    {
        float half = duration * 0.5f;

        // Ramp up
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            float blend = t / half;
            _vignette.intensity.value = Mathf.Lerp(_baseIntensity, peakIntensity, blend);
            yield return null;
        }

        // Ramp down
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            float blend = t / half;
            _vignette.intensity.value = Mathf.Lerp(peakIntensity, _baseIntensity, blend);
            yield return null;
        }

        _vignette.intensity.value = _baseIntensity;
        _activeRoutine = null;
    }
}
