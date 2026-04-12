using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// Sets Watering and Drink HUD elements to large absolute sizes.
/// Idempotent — safe to run multiple times, always produces the same result.
/// </summary>
public static class HUDScaleUp
{
    // ── Target values (original values × 2.5) ──
    // Pour bar: original was 200h × 40w
    private const float BarHeight = 500f;
    private const float BarWidth = 100f;
    // Pour bar container RT: original was 60 × 220
    private const float BarRTWidth = 150f;
    private const float BarRTHeight = 550f;

    // Font sizes: originals were 22 (plant name), 24 (drink name), 20 (score), 18 (buttons)
    private const float PlantNameFontSize = 55f;
    private const float DrinkNameFontSize = 60f;
    private const float ScoreFontSize = 50f;
    private const float RecipeTitleFontSize = 60f;
    private const float RecipeButtonFontSize = 45f;

    // Recipe button sizes: original was 300 × 40, spacing 50
    private const float ButtonWidth = 750f;
    private const float ButtonHeight = 100f;
    private const float ButtonSpacing = 125f;

    [MenuItem("Window/Iris/Set Pour HUDs to Large Size")]
    public static void SetPourHUDsLarge()
    {
        int changed = 0;

        // ── Watering HUD ──
        var wateringHUDs = Object.FindObjectsByType<WateringHUD>(FindObjectsSortMode.None);
        foreach (var hud in wateringHUDs)
        {
            if (hud.plantNameLabel != null)
                SetFont(hud.plantNameLabel, PlantNameFontSize, ref changed);
        }

        // ── Drink HUD ──
        var drinkHUDs = Object.FindObjectsByType<SimpleDrinkHUD>(FindObjectsSortMode.None);
        foreach (var hud in drinkHUDs)
        {
            if (hud.drinkNameLabel != null)
                SetFont(hud.drinkNameLabel, DrinkNameFontSize, ref changed);

            if (hud.scoreLabel != null)
                SetFont(hud.scoreLabel, ScoreFontSize, ref changed);

            if (hud.pourBar != null)
                SetPourBar(hud.pourBar, ref changed);

            // Recipe panel
            if (hud.recipePanel != null)
            {
                // Title label
                var titleTF = hud.recipePanel.transform.Find("RecipePanelTitle");
                if (titleTF != null)
                {
                    var titleTMP = titleTF.GetComponent<TMP_Text>();
                    if (titleTMP != null)
                        SetFont(titleTMP, RecipeTitleFontSize, ref changed);
                }

                // Buttons — find by Btn_ prefix
                int btnIndex = 0;
                foreach (Transform child in hud.recipePanel.transform)
                {
                    if (!child.name.StartsWith("Btn_")) continue;
                    var btnRT = child.GetComponent<RectTransform>();
                    if (btnRT != null)
                    {
                        float yPos = (160f - btnIndex * ButtonSpacing);
                        Undo.RecordObject(btnRT, "Set button size");
                        btnRT.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
                        btnRT.anchoredPosition = new Vector2(0f, yPos);
                        EditorUtility.SetDirty(btnRT);
                        changed++;
                    }

                    // Button text
                    var btnTMP = child.GetComponentInChildren<TMP_Text>(true);
                    if (btnTMP != null)
                        SetFont(btnTMP, RecipeButtonFontSize, ref changed);

                    btnIndex++;
                }
            }
        }

        Debug.Log($"[HUDScaleUp] Set {changed} elements to large size. Ctrl+Z to undo.");
    }

    private static void SetPourBar(PourBarUI bar, ref int changed)
    {
        Undo.RecordObject(bar, "Set PourBarUI size");
        bar.barHeight = BarHeight;
        bar.barWidth = BarWidth;
        EditorUtility.SetDirty(bar);
        changed++;

        var rt = bar.GetComponent<RectTransform>();
        if (rt != null)
        {
            Undo.RecordObject(rt, "Set PourBarUI RT size");
            rt.sizeDelta = new Vector2(BarRTWidth, BarRTHeight);
            // Keep position where it is — don't move it
            EditorUtility.SetDirty(rt);
            changed++;
        }
    }

    private static void SetFont(TMP_Text label, float size, ref int changed)
    {
        Undo.RecordObject(label, "Set font size");
        label.fontSize = size;
        EditorUtility.SetDirty(label);
        changed++;
    }
}
