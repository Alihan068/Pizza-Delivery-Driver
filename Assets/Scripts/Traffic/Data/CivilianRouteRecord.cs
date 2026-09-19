using System.Collections.Generic;

/// <summary>
/// A closed loop a civilian vehicle repeatedly follows: an ordered list of edge ids, the vehicle
/// pool it draws from, and how many vehicles this route tries to keep populated. A loop route's
/// edge list must be contiguous and its last edge must connect back to its first (checked by the
/// S02.4 validator, not by this plain record).
/// </summary>
[System.Serializable]
public class CivilianRouteRecord {
    public string routeId;
    public List<string> edgeIds = new List<string>();
    public bool loop;
    public string vehiclePoolId;
    public int targetCount;
    public string respawnProfileId;
}
