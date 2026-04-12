using UnityEngine;

/// <summary>
/// Bridge between a DatePersonalDefinition (ScriptableObject) and scene-placed
/// per-phase character models. Place one of these in the scene for each date
/// character, drag the SO into <see cref="definition"/>, and wire the three
/// phase models. DateSessionManager auto-finds the matching set at runtime.
/// </summary>
public class DateSceneModels : MonoBehaviour
{
    [Tooltip("Which date character these models belong to.")]
    public DatePersonalDefinition definition;

    [Header("Per-Phase Models")]
    [Tooltip("Phase 1 — Arrival (e.g. standing greeting pose).")]
    public GameObject arrivalModel;

    [Tooltip("Phase 2 — Kitchen / Drinks (e.g. standing idle pose).")]
    public GameObject kitchenModel;

    [Tooltip("Phase 3 — Couch / Reveal (e.g. sitting pose).")]
    public GameObject couchModel;

    // ── Static registry (avoids FindObjectsByType) ──
    private static readonly System.Collections.Generic.List<DateSceneModels> s_all = new();
    public static System.Collections.Generic.IReadOnlyList<DateSceneModels> All => s_all;

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    private void Awake()
    {
        // Ensure all models start hidden
        if (arrivalModel != null) arrivalModel.SetActive(false);
        if (kitchenModel != null) kitchenModel.SetActive(false);
        if (couchModel   != null) couchModel.SetActive(false);
    }

    /// <summary>Find the scene models for a given date definition. Returns null if not found.</summary>
    public static DateSceneModels FindForDate(DatePersonalDefinition date)
    {
        if (date == null) return null;
        for (int i = 0; i < s_all.Count; i++)
        {
            if (s_all[i].definition == date)
                return s_all[i];
        }
        return null;
    }

    /// <summary>Deactivate all models on this set and activate the target. Pass null to hide all.</summary>
    public void ShowOnly(GameObject target)
    {
        if (arrivalModel != null && arrivalModel != target) arrivalModel.SetActive(false);
        if (kitchenModel != null && kitchenModel != target) kitchenModel.SetActive(false);
        if (couchModel   != null && couchModel   != target) couchModel.SetActive(false);

        if (target != null)
        {
            target.SetActive(true);

            // Models inactive during PSXRenderController's startup scan still
            // have URP/Standard shaders — swap to PSXLit to avoid stripped-shader
            // invisibility in builds.
            if (PSXRenderController.Instance != null)
                PSXRenderController.Instance.EnsureSwapped(target);
        }
    }

    /// <summary>Hide all models in this set.</summary>
    public void HideAll()
    {
        ShowOnly(null);
    }
}
