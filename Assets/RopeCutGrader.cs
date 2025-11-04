using System.Collections.Generic;
using UnityEngine;

public class RopeCutGrader : MonoBehaviour
{
    public enum Grade { Bad, Good, Perfect }

    [Header("Vase Opening Sizes (world units, e.g. meters)")]
    [Tooltip("Opening sizes used as the denominator for proportional length scoring.")]
    public List<float> vaseOpenings = new() { 0.08f, 0.12f, 0.16f };  // example diameters
    [Tooltip("Which vase opening to use right now.")]
    public int activeVaseIndex = 0;

    [Header("Reference Points")]
    [Tooltip("Where the stem is considered to start (base in world space).")]
    public Transform stemBase;   // optional; pass base point manually if null

    [Header("Ratio Bands (stemLength / vaseOpening)")]
    [Tooltip("Inclusive min/max for PERFECT ratio band.")]
    public Vector2 perfectRange = new Vector2(1.5f, 1.8f);
    [Tooltip("Inclusive min/max for GOOD ratio band (evaluated if not PERFECT).")]
    public Vector2 goodRange = new Vector2(1.2f, 2.0f);
    [Tooltip("Everything outside PERFECT/GOOD is BAD.")]

    [Header("Debug")]
    public bool logDecisions = false;

    /// <summary>
    /// Grade by world points (base + cut point).
    /// Returns the grade and outputs measured stem length and ratio = length/opening.
    /// </summary>
    public Grade GradeByWorldPoints(Vector3 basePoint, Vector3 cutPoint, out float stemLength, out float ratio)
    {
        float opening = CurrentOpening();
        stemLength = Vector3.Distance(basePoint, cutPoint);
        ratio = (opening > 1e-6f) ? stemLength / opening : 0f;

        Grade g = EvaluateRatio(ratio);
        if (logDecisions)
            Debug.Log($"[IkebanaGrade] opening={opening:F3}, length={stemLength:F3}, ratio={ratio:F3} => {g}");
        return g;
    }

    /// <summary>
    /// Grade using the configured stemBase transform and a world cut point.
    /// </summary>
    public Grade GradeCutFromWorldPoint(Vector3 cutPoint, out float stemLength, out float ratio)
    {
        Vector3 basePoint = stemBase ? stemBase.position : Vector3.zero;
        return GradeByWorldPoints(basePoint, cutPoint, out stemLength, out ratio);
    }

    /// <summary>
    /// Evaluate ratio against bands.
    /// </summary>
    public Grade EvaluateRatio(float ratio)
    {
        if (ratio >= perfectRange.x && ratio <= perfectRange.y) return Grade.Perfect;
        if (ratio >= goodRange.x && ratio <= goodRange.y) return Grade.Good;
        return Grade.Bad;
    }

    /// <summary>
    /// Change the active vase (safe clamp).
    /// </summary>
    public void SetActiveVase(int index)
    {
        if (vaseOpenings == null || vaseOpenings.Count == 0) { activeVaseIndex = 0; return; }
        activeVaseIndex = Mathf.Clamp(index, 0, vaseOpenings.Count - 1);
    }

    public float CurrentOpening()
    {
        if (vaseOpenings == null || vaseOpenings.Count == 0) return 1f;
        int i = Mathf.Clamp(activeVaseIndex, 0, vaseOpenings.Count - 1);
        return Mathf.Max(1e-6f, vaseOpenings[i]);
    }

    // Optional gizmo to visualize the base point
    void OnDrawGizmosSelected()
    {
        if (stemBase)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(stemBase.position, 0.01f);
        }
    }
}
