using System;
using System.Collections.Generic;

/// <summary>
/// Unified save data schema for one Iris game slot.
/// Serialized to iris_save_{slot}.json via SaveManager.
/// </summary>
[Serializable]
public class IrisSaveData
{
    // Version 2: DateHistoryEntry gains succeeded, learnedLikes, learnedDislikes
    public int saveVersion = 2;

    // ── Player ────────────────────────────────────────────────────
    public string playerName;

    // ── Game Mode ──────────────────────────────────────────────────
    public string gameModeName;

    // ── Calendar ──────────────────────────────────────────────────
    public int currentDay;
    public float currentHour;
    public int dayPhase; // maps to DayPhaseManager.DayPhase

    // ── Date History ──────────────────────────────────────────────
    public List<DateHistory.DateHistoryEntry> dateHistory = new List<DateHistory.DateHistoryEntry>();

    // ── Item Display States ──────────────────────────────────────
    public List<ItemDisplayRecord> itemDisplayStates = new List<ItemDisplayRecord>();

    // ── Object Positions ──────────────────────────────────────────
    public List<PlaceablePositionRecord> objectPositions = new List<PlaceablePositionRecord>();

    // ── Living Plants (flower trimming results → apartment decorations) ──
    public List<LivingPlantRecord> livingPlants = new List<LivingPlantRecord>();

    // ── Weather ─────────────────────────────────────────────────────
    public int weatherState;
}

/// <summary>Serializable record of an item's display state.</summary>
[Serializable]
public class ItemDisplayRecord
{
    public string itemId;
    public int displayState; // maps to ItemStateRegistry.ItemDisplayState
}

/// <summary>Serializable record of a placeable object's world position and rotation.</summary>
[Serializable]
public class PlaceablePositionRecord
{
    public string objectName;
    public float px, py, pz;
    public float rx, ry, rz, rw;
}

/// <summary>Serializable record of a living plant (trimmed flower) in the apartment.</summary>
[Serializable]
public class LivingPlantRecord
{
    public string characterName;
    public int spawnDay;
    public int totalDaysAlive;
    public float currentHealth;
    public float currentWaterLevel;
    public float px, py, pz;
}
