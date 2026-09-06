using System.Collections.Generic;

/// <summary>
/// Everything one career profile persists.
/// </summary>
/// <remarks>
/// Serialized with <c>JsonUtility</c>, which fills any field missing from the file with its C#
/// default and reports nothing. That silence is why <see cref="saveVersion"/> exists: without it
/// an old save and a save whose meaning has changed look identical.
/// <para>
/// The legacy name fields are only read, never written. They exist so a version 0 file — which
/// keyed everything by display name — can still be migrated.
/// </para>
/// </remarks>
[System.Serializable]
public class GameSaveData {

    /// <summary>Schema version this file was written with. Version 0 means "written before versioning existed".</summary>
    public int saveVersion;

    /// <summary>Banked money.</summary>
    public int totalMoney;

    /// <summary>Permanent id of the vehicle the player is currently driving.</summary>
    public string currentVehicleId;

    /// <summary>Permanent id of the map the player last selected.</summary>
    public string currentMapId;

    /// <summary>Per-vehicle upgrade levels and unlock state.</summary>
    public List<VehicleSaveData> vehicleSaveList;

    /// <summary>The driver's own upgrades, carried across every vehicle. Introduced in version 2.</summary>
    public DriverSaveData driverStats;

    /// <summary>Live, penalty-adjustable reputation total. Introduced in version 3.</summary>
    public int totalReputation;

    /// <summary>Highest rank ever reached. A high-water mark: never lowered by a later reputation penalty.</summary>
    public int highestRankAchieved = 1;

    /// <summary>Highest region tier ever unlocked. A high-water mark, same as <see cref="highestRankAchieved"/>.</summary>
    public int highestUnlockedRegionTier = 1;

    /// <summary>One-based day counter. Advances when the day's last shift settles.</summary>
    public int currentDay = 1;

    /// <summary>Shifts settled so far on <see cref="currentDay"/>. Resets to zero when the day turns over.</summary>
    public int shiftsCompletedToday;

    /// <summary>False until this career's first rent day, which is charged for free.</summary>
    public bool everPaidRent;

    /// <summary>Rank rent was charged at the last time a day ended. Drives the post-rank-up grace day.</summary>
    public int lastRentChargeRank = 1;

    /// <summary>True once the career's reputation ending threshold has been reached.</summary>
    public bool reachedEnding;

    /// <summary>
    /// True while a session is running. Set when a shift starts and cleared at settlement, so a
    /// profile loaded with this still set can be recognised as an interrupted shift.
    /// </summary>
    public bool shiftInProgress;

    /// <summary>Legacy version 0 field: the display name of the current vehicle. Migration input only.</summary>
    public string currentVehicleName;
}
