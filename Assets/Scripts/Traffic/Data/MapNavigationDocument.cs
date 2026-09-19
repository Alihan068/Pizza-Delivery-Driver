using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The complete portable traffic/police navigation data for one map: primitives, strings and lists
/// only, no <see cref="Object"/>/Transform/prefab/instanceId reference anywhere in the graph, so it
/// serializes as plain JSON (via <see cref="JsonUtility"/>) and round-trips through a Unity import
/// or a future Workshop package unchanged. IDs are assigned once by whichever authoring tool wrote
/// this document; deserializing this class never generates or rewrites an id.
/// </summary>
[System.Serializable]
public class MapNavigationDocument {
    public int schemaVersion = 1;
    public string mapId;
    public string documentId;

    /// <summary>Human-readable tag for the coordinate contract this document was authored under. See <see cref="MapNavigationCoordinates"/>.</summary>
    public string coordinateConvention = "MapLocalUnits_XRight_YUp_YForward_HeadingCCWFromY";

    public Rect localBounds;
    public List<NoSpawnRegion> noSpawnRegions = new List<NoSpawnRegion>();

    public List<RoadNodeRecord> nodes = new List<RoadNodeRecord>();
    public List<RoadEdgeRecord> edges = new List<RoadEdgeRecord>();
    public List<JunctionRecord> junctions = new List<JunctionRecord>();

    public List<VehiclePoolRecord> vehiclePools = new List<VehiclePoolRecord>();
    public List<CivilianRouteRecord> civilianRoutes = new List<CivilianRouteRecord>();
    public List<VehicleSpawnRecord> spawnPoints = new List<VehicleSpawnRecord>();
    public List<PoliceEntryRecord> policeEntries = new List<PoliceEntryRecord>();
    public List<DifficultyTrafficBinding> difficultyProfileBindings = new List<DifficultyTrafficBinding>();

    /// <summary>Ids of external profile/catalog records this document references. Validated as resolvable before this map's traffic is activated.</summary>
    public List<string> profileDependencies = new List<string>();
}
