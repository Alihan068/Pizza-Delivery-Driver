using System.Collections.Generic;

/// <summary>
/// A police-usable entry point into the road graph. Unlike a civilian spawn, a police entry is not
/// bound to a fixed route — police pathfinds freely across the graph using the roles/access it is
/// authored with.
/// </summary>
[System.Serializable]
public class PoliceEntryRecord {
    public string entryId;
    public string spawnId;
    public List<VehicleRole> allowedRoles = new List<VehicleRole>();
}
