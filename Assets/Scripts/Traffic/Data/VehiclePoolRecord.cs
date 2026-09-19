using System.Collections.Generic;

/// <summary>
/// A named weighted set of vehicle profiles a route or spawn can draw from. Entries reference
/// catalog vehicleProfileIds only — never a prefab or asset reference — so this document stays a
/// plain, portable data record.
/// </summary>
[System.Serializable]
public class VehiclePoolRecord {
    public string poolId;
    public List<VehiclePoolEntry> entries = new List<VehiclePoolEntry>();
}
