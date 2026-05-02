using UnityEngine;

/// <summary>
/// Defines hint text for a specific game phase. Drop these into
/// HintDisplayManager's list to configure all hints from the Inspector.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Hint Definition")]
public class HintDefinition : ScriptableObject
{
    public enum GamePhase
    {
        RoomArrangement,
        DrinkMaking,
        WarmUp,
        FlowerArrangement
    }

    [Tooltip("Which game phase this hint applies to.")]
    public GamePhase phase;

    [Header("Display Text")]
    [TextArea(3, 8)]
    [Tooltip("Control hints (LMB, RMB, Tab, etc.)")]
    public string hintContent = "";

    [TextArea(2, 4)]
    [Tooltip("Contextual prompt or flavor text below the controls.")]
    public string hintPrompt = "";
}
