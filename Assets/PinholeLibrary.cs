using System.Collections.Generic;
using UnityEngine;

/// Put this on the stem root. Fill `pinholePoints` with empty transforms
/// placed where leaves attach. Optionally add a Rigidbody on the stem so
/// the counterforce can visibly tug it.
[DisallowMultipleComponent]
public class PinholeLibrary : MonoBehaviour
{
    public List<Transform> pinholePoints = new();

    public Transform GetNearest(Vector3 worldPos)
    {
        if (pinholePoints == null || pinholePoints.Count == 0) return null;

        Transform best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (var t in pinholePoints)
        {
            if (!t) continue;
            float sq = (t.position - worldPos).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = t; }
        }
        return best;
    }
}