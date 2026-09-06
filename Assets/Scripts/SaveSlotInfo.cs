using System;

/// <summary>
/// Summary of one save slot, enough to draw its card on the profile screen without loading the
/// whole profile into the running game.
/// </summary>
public class SaveSlotInfo {

    /// <summary>Zero-based slot index.</summary>
    public int slotIndex;

    /// <summary>How reading this slot turned out.</summary>
    public SaveLoadStatus status;

    /// <summary>True when the slot holds a readable career.</summary>
    public bool HasCareer => status == SaveLoadStatus.Loaded || status == SaveLoadStatus.RecoveredFromBackup;

    /// <summary>Banked money, when the slot is readable.</summary>
    public int totalMoney;

    /// <summary>Display name of the current vehicle, or empty when it could not be resolved.</summary>
    public string vehicleDisplayName = string.Empty;

    /// <summary>
    /// True when the profile references content that is not installed — for example a vehicle from
    /// a package that has since been removed.
    /// </summary>
    public bool hasMissingContent;

    /// <summary>When the slot was last written, in UTC. Default when the slot is empty.</summary>
    public DateTime lastPlayedUtc;
}
