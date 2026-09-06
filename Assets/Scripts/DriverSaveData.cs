/// <summary>
/// The driver's own permanent upgrades: purchased once, kept forever, regardless of which vehicle
/// is currently owned or being driven.
/// </summary>
/// <remarks>
/// Storage and Stabilizer represent the driver's own packing skill and cargo rigging rather than
/// anything built into a specific chassis, so the owner's decision is that they carry across every
/// vehicle bought — unlike <see cref="VehicleSaveData"/>'s stats, which reset per vehicle. See
/// <see cref="VehicleStatOwnership"/> for which stat lives where.
/// </remarks>
[System.Serializable]
public class DriverSaveData {

    /// <summary>Purchased Capacity (Storage) levels.</summary>
    public int capacityLevel;

    /// <summary>Purchased Protection (Stabilizer) levels.</summary>
    public int protectionLevel;

    /// <summary>Reads the purchased level of one driver-bound stat.</summary>
    /// <param name="stat">Which stat to read. Anything not driver-bound returns zero.</param>
    /// <returns>The number of levels bought, zero when none.</returns>
    public int GetLevel(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Capacity: return capacityLevel;
            case VehicleStatId.Protection: return protectionLevel;
            default: return 0;
        }
    }

    /// <summary>Adds one purchased level to a driver-bound stat.</summary>
    /// <param name="stat">Which stat was upgraded. Ignored when not driver-bound.</param>
    public void AddLevel(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Capacity: capacityLevel++; break;
            case VehicleStatId.Protection: protectionLevel++; break;
        }
    }
}
