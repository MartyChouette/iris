using UnityEngine;

[CreateAssetMenu(fileName = "JuiceSettings", menuName = "Game/Juice Settings")]
public class JuiceSettings : ScriptableObject
{
    [Header("Global Toggles")]
    public bool enableUIJuice = true;     // rope cut UI label, splash, etc.
    public bool enableTimeJuice = true;   // freeze/slomo on cut
    public bool enableSapFX = true;       // sap bursts/splats from leaves/cuts
}