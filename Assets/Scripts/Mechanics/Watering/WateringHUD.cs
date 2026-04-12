using TMPro;
using UnityEngine;

/// <summary>
/// Legacy HUD bridge for the watering system. The new PotCrossSectionUI handles
/// the main pour overlay. This component now just manages the optional plant
/// name label and panel visibility based on WateringManager state.
/// </summary>
[DisallowMultipleComponent]
public class WateringHUD : MonoBehaviour
{
    [Header("References")]
    public WateringManager manager;

    [Header("UI")]
    public TMP_Text plantNameLabel;

    [Header("Panels")]
    [Tooltip("Root panel — hidden when Idle.")]
    public GameObject hudPanel;

    void Update()
    {
        if (manager == null) return;

        bool showHUD = manager.CurrentState == WateringManager.State.Pouring;

        if (hudPanel != null)
            hudPanel.SetActive(showHUD);

        if (!showHUD) return;

        if (plantNameLabel != null)
        {
            var plant = manager.ActivePlant;
            plantNameLabel.text = plant != null && plant.definition != null
                ? plant.definition.plantName
                : "";
        }
    }
}
