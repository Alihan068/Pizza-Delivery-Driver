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

    /// <summary>Permanent id of the difficulty last selected inside the current map.</summary>
    public string currentDifficultyId;

    /// <summary>Permanent map ids purchased by this profile. Introduced in version 4.</summary>
    public List<string> ownedMapIds;

    /// <summary>Best score records for map difficulty tiers. Introduced in version 6.</summary>
    public List<MapDifficultyProgress> mapDifficultyProgress;

    /// <summary>Per-vehicle upgrade levels and unlock state.</summary>
    public List<VehicleSaveData> vehicleSaveList;

    /// <summary>The driver's own upgrades, carried across every vehicle. Introduced in version 2.</summary>
    public DriverSaveData driverStats;

    /// <summary>Persistent courier rating total. Introduced in version 3 and floored at zero.</summary>
    public int totalReputation;

    /// <summary>Highest visual rank ever reached. It does not unlock content.</summary>
    public int highestRankAchieved = 1;

    /// <summary>Legacy region progress retained for save compatibility; it does not unlock content.</summary>
    public int highestUnlockedRegionTier = 1;

    /// <summary>One-based day counter. Advances when the day's last shift settles.</summary>
    public int currentDay = 1;

    /// <summary>Shifts settled so far on <see cref="currentDay"/>. Resets to zero when the day turns over.</summary>
    public int shiftsCompletedToday;

    /// <summary>False until this career's first rent day, which is charged for free.</summary>
    public bool everPaidRent;

    /// <summary>Rank rent was charged at the last time a day ended. Drives the post-rank-up grace day.</summary>
    public int lastRentChargeRank = 1;

    /// <summary>Legacy completion flag retained for save compatibility.</summary>
    public bool reachedEnding;

    /// <summary>True after the authored final career shift has been completed successfully.</summary>
    public bool careerCompleted;

    /// <summary>Total shifts settled in this career, excluding endless sessions.</summary>
    public int totalShiftsSettled;

    /// <summary>Total customer orders completed across settled career shifts.</summary>
    public int totalOrdersCompleted;

    /// <summary>Total pizzas delivered across settled career shifts.</summary>
    public int totalPizzasDelivered;

    /// <summary>Highest score reached in a settled career shift.</summary>
    public int bestShiftScore;

    /// <summary>Highest number of pizzas delivered in one settled career shift.</summary>
    public int bestShiftDeliveries;

    /// <summary>Highest number of pizzas delivered in one settled endless session.</summary>
    public int bestFreeplayDeliveries;

    /// <summary>
    /// True while a session is running. Set when a shift starts and cleared at settlement, so a
    /// profile loaded with this still set can be recognised as an interrupted shift.
    /// </summary>
    public bool shiftInProgress;

    /// <summary>Legacy version 0 field: the display name of the current vehicle. Migration input only.</summary>
    public string currentVehicleName;
}
