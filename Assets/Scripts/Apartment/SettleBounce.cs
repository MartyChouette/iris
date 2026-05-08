using System.Collections;
using UnityEngine;

/// <summary>
/// Self-removing settle-bounce animation component.
/// Scale: 1 → 0.92 → 1.05 → 1.0 over 0.2s.
/// Add via AddComponent, call Play(), and it removes itself when done.
/// </summary>
public class SettleBounce : MonoBehaviour
{
    private Vector3 _baseScale;

    /// <summary>The true base scale this bounce animates around.</summary>
    public Vector3 BaseScale => _baseScale;

    public void Play(Vector3 baseScale)
    {
        // Kill any existing bounce on this object so they don't stack
        foreach (var existing in GetComponents<SettleBounce>())
        {
            if (existing != this)
            {
                existing.StopAllCoroutines();
                Destroy(existing);
            }
        }
        _baseScale = baseScale;
        transform.localScale = baseScale;
        StartCoroutine(Bounce(baseScale));
    }

    private IEnumerator Bounce(Vector3 baseScale)
    {
        float[] times  = { 0.067f, 0.067f, 0.067f };
        float[] scales = { 0.92f,  1.05f,  1.00f  };
        for (int i = 0; i < scales.Length; i++)
        {
            float elapsed = 0f;
            Vector3 fromScale = transform.localScale;
            Vector3 toScale   = baseScale * scales[i];
            while (elapsed < times[i])
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(fromScale, toScale, elapsed / times[i]);
                yield return null;
            }
            transform.localScale = toScale;
        }
        Destroy(this);
    }
}
