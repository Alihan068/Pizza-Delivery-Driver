/// <summary>
/// A spawn point attached to a road edge at a specific arc distance along it. Civilian spawns
/// reference a route id; police spawns do not need one (police is not bound to a fixed loop).
/// The clearance footprint is authored here so S02.5 can reject a spawn no real vehicle would fit
/// through, independent of the vehicle profile eventually placed there.
/// </summary>
[System.Serializable]
public class VehicleSpawnRecord {
    public string spawnId;
    public string edgeId;
    public float distanceAlongEdge;

    /// <summary>Owning civilian route id, or empty for a non-civilian (e.g. police) spawn.</summary>
    public string routeId = string.Empty;

    public VehicleRole role;
    public float clearanceWidth;
    public float clearanceLength;
}
