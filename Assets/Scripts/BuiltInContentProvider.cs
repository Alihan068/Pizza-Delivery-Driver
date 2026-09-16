using System.Collections.Generic;

/// <summary>
/// Supplies the content that ships inside the build, taken from the arrays authored on the
/// GameManager prefab.
/// </summary>
/// <remarks>
/// Registered first, so built-in content keeps its authored order and occupies the leading
/// positions. That matters because the first registered vehicle is treated as the free starter.
/// </remarks>
public class BuiltInContentProvider : IContentProvider {

    /// <summary>Stable provider id used to distinguish shipped content from external content.</summary>
    public const string SourceId = "built-in";

    readonly VehicleData[] vehicles;
    readonly MapData[] maps;

    /// <summary>Creates a provider over the content authored in the project.</summary>
    /// <param name="vehicles">Built-in vehicles, in presentation order. May be null.</param>
    /// <param name="maps">Built-in maps, in presentation order. May be null.</param>
    public BuiltInContentProvider(VehicleData[] vehicles, MapData[] maps) {
        this.vehicles = vehicles;
        this.maps = maps;
    }

    /// <summary>Identifies this source as the shipped content.</summary>
    public string ProviderId => SourceId;

    /// <summary>Returns the built-in vehicles, skipping empty array entries.</summary>
    /// <returns>The vehicles in authored order.</returns>
    public IEnumerable<VehicleData> GetVehicles() {
        if (vehicles == null) yield break;
        foreach (var vehicle in vehicles) {
            if (vehicle != null) yield return vehicle;
        }
    }

    /// <summary>Returns the built-in maps, skipping empty array entries.</summary>
    /// <returns>The maps in authored order.</returns>
    public IEnumerable<MapData> GetMaps() {
        if (maps == null) yield break;
        foreach (var map in maps) {
            if (map != null) yield return map;
        }
    }
}
