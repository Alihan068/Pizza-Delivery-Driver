/// <summary>
/// One vehicle's persistent state within a career profile: whether it is owned, how far each of
/// its stats has been upgraded, and how much has been sunk into it.
/// </summary>
/// <remarks>
/// Keyed by <see cref="vehicleId"/>, never by display name. Renaming a vehicle used to orphan this
/// record: the lookup missed, a fresh locked entry was appended, and the player silently lost a
/// purchased vehicle along with every upgrade bought for it.
/// </remarks>
[System.Serializable]
public class VehicleSaveData {

    /// <summary>Permanent id of the vehicle this record belongs to.</summary>
    public string vehicleId;

    /// <summary>Whether the player owns this vehicle.</summary>
    public bool isUnlocked;

    /// <summary>Purchased Speed levels.</summary>
    public int speedLevel;

    /// <summary>Purchased Turn levels.</summary>
    public int turnLevel;

    /// <summary>Purchased Health levels.</summary>
    public int healthLevel;

    /// <summary>Purchased Armor levels.</summary>
    public int armorLevel;

    /// <summary>Purchased Capacity levels.</summary>
    public int capacityLevel;

    /// <summary>Purchased Protection levels.</summary>
    public int protectionLevel;

    /// <summary>Total money spent on this vehicle. Feeds the repair cost through vehicle value.</summary>
    public int moneySpent;

    /// <summary>Legacy version 0 field: the display name this record was keyed by. Migration input only.</summary>
    public string vehicleName;

    /// <summary>Creates an empty record for a vehicle.</summary>
    /// <param name="vehicleId">Permanent id of the vehicle.</param>
    /// <param name="unlocked">Whether the player already owns it.</param>
    public VehicleSaveData(string vehicleId, bool unlocked) {
        this.vehicleId = vehicleId;
        isUnlocked = unlocked;
        speedLevel = 0;
        turnLevel = 0;
        healthLevel = 0;
        armorLevel = 0;
        capacityLevel = 0;
        protectionLevel = 0;
        moneySpent = 0;
    }

    /// <summary>Reads the purchased level of one stat.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The number of levels bought, zero when none.</returns>
    public int GetLevel(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return speedLevel;
            case VehicleStatId.Turn: return turnLevel;
            case VehicleStatId.Health: return healthLevel;
            case VehicleStatId.Armor: return armorLevel;
            case VehicleStatId.Capacity: return capacityLevel;
            case VehicleStatId.Protection: return protectionLevel;
            default: return 0;
        }
    }

    /// <summary>Adds one purchased level to a stat.</summary>
    /// <param name="stat">Which stat was upgraded.</param>
    public void AddLevel(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: speedLevel++; break;
            case VehicleStatId.Turn: turnLevel++; break;
            case VehicleStatId.Health: healthLevel++; break;
            case VehicleStatId.Armor: armorLevel++; break;
            case VehicleStatId.Capacity: capacityLevel++; break;
            case VehicleStatId.Protection: protectionLevel++; break;
        }
    }
}
