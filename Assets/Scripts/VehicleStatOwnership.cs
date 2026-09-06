/// <summary>
/// Which of the two save records a stat's purchased level lives in: the driver's own
/// (<see cref="DriverSaveData"/>) or the specific vehicle's (<see cref="VehicleSaveData"/>).
/// </summary>
/// <remarks>
/// A fixed, binary split rather than per-vehicle data, because it is a rule about the stat itself
/// (Storage and Stabilizer are the driver's packing skill; Speed/Handling/Chassis/Armor are the
/// chassis under them), not something an individual vehicle asset should be able to override.
/// </remarks>
public static class VehicleStatOwnership {

    /// <summary>True when a stat belongs to the driver and carries across every vehicle owned.</summary>
    /// <param name="stat">The stat to check.</param>
    /// <returns>True for Capacity and Protection; false for every vehicle-specific stat.</returns>
    public static bool IsDriverBound(VehicleStatId stat) {
        return stat == VehicleStatId.Capacity || stat == VehicleStatId.Protection;
    }
}
