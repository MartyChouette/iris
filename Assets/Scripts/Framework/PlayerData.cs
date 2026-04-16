using UnityEngine;

/// <summary>
/// Static session holder for the player's name.
/// Set by NameEntryScreen, read by NewspaperManager for Nema's personal ad.
/// </summary>
public static class PlayerData
{
    public static string PlayerName { get; set; } = "Nema";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PlayerName = "Nema";
    }
}
