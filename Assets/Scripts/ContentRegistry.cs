using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single place the game asks "what vehicles and maps exist?". Aggregates every registered
/// <see cref="IContentProvider"/> and indexes the result by permanent id.
/// </summary>
/// <remarks>
/// <para>
/// Nothing outside this class holds a fixed array of content. That is what makes externally
/// supplied content possible later: a new provider is added, <see cref="Rebuild"/> runs, and every
/// consumer sees the new entries without changing.
/// </para>
/// <para>
/// Ids must be unique across all sources. When two entries claim the same id the first one wins
/// and the collision is recorded in <see cref="Conflicts"/> rather than throwing, because a broken
/// external package must never stop the game from starting.
/// </para>
/// </remarks>
public class ContentRegistry {

    readonly List<IContentProvider> providers = new List<IContentProvider>();

    readonly List<VehicleData> vehicles = new List<VehicleData>();
    readonly Dictionary<string, VehicleData> vehiclesById = new Dictionary<string, VehicleData>();
    readonly Dictionary<VehicleData, string> vehicleProviderIds = new Dictionary<VehicleData, string>();

    readonly List<MapData> maps = new List<MapData>();
    readonly Dictionary<string, MapData> mapsById = new Dictionary<string, MapData>();
    readonly Dictionary<MapData, string> mapProviderIds = new Dictionary<MapData, string>();

    readonly List<string> conflicts = new List<string>();

    /// <summary>Every known vehicle, in registration order. The first entry is the free starter.</summary>
    public IReadOnlyList<VehicleData> Vehicles => vehicles;

    /// <summary>Every known map, in registration order.</summary>
    public IReadOnlyList<MapData> Maps => maps;

    /// <summary>
    /// Problems found while building the index: duplicate or missing ids, and which source caused them.
    /// Empty when everything is well formed.
    /// </summary>
    public IReadOnlyList<string> Conflicts => conflicts;

    /// <summary>Adds a content source. Call <see cref="Rebuild"/> afterwards to index it.</summary>
    /// <param name="provider">The source to add. Ignored when null or already registered.</param>
    public void AddProvider(IContentProvider provider) {
        if (provider == null || providers.Contains(provider)) return;
        providers.Add(provider);
    }

    /// <summary>
    /// Rebuilds the index from every registered provider. Safe to call again whenever the set of
    /// sources changes.
    /// </summary>
    public void Rebuild() {
        vehicles.Clear();
        vehiclesById.Clear();
        vehicleProviderIds.Clear();
        maps.Clear();
        mapsById.Clear();
        mapProviderIds.Clear();
        conflicts.Clear();

        foreach (var provider in providers) {
            foreach (var vehicle in provider.GetVehicles()) {
                if (!TryClaimId(vehicle.vehicleId, provider.ProviderId, vehicle.name, "vehicle", vehiclesById.ContainsKey)) continue;
                vehiclesById.Add(vehicle.vehicleId, vehicle);
                vehicleProviderIds.Add(vehicle, provider.ProviderId);
                vehicles.Add(vehicle);
            }
            foreach (var map in provider.GetMaps()) {
                if (!TryClaimId(map.mapId, provider.ProviderId, map.name, "map", mapsById.ContainsKey)) continue;
                mapsById.Add(map.mapId, map);
                mapProviderIds.Add(map, provider.ProviderId);
                maps.Add(map);
            }
        }
    }

    /// <summary>Which source supplied a vehicle, for display in a content or mods listing.</summary>
    /// <param name="vehicle">A vehicle returned by <see cref="Vehicles"/>.</param>
    /// <returns>The supplying <see cref="IContentProvider.ProviderId"/>, or empty if unknown.</returns>
    public string GetVehicleProviderId(VehicleData vehicle) {
        return vehicle != null && vehicleProviderIds.TryGetValue(vehicle, out var id) ? id : string.Empty;
    }

    /// <summary>Which source supplied a map, for display in a content or mods listing.</summary>
    /// <param name="map">A map returned by <see cref="Maps"/>.</param>
    /// <returns>The supplying <see cref="IContentProvider.ProviderId"/>, or empty if unknown.</returns>
    public string GetMapProviderId(MapData map) {
        return map != null && mapProviderIds.TryGetValue(map, out var id) ? id : string.Empty;
    }

    bool TryClaimId(string id, string providerId, string assetName, string kind, System.Func<string, bool> alreadyTaken) {
        if (string.IsNullOrWhiteSpace(id)) {
            conflicts.Add(providerId + ": " + kind + " '" + assetName + "' has no id and was skipped.");
            return false;
        }
        if (alreadyTaken(id)) {
            conflicts.Add(providerId + ": " + kind + " '" + assetName + "' claims id '" + id + "', which is already taken. It was skipped.");
            return false;
        }
        return true;
    }

    /// <summary>Finds a vehicle by its permanent id.</summary>
    /// <param name="id">The id stored in a save file.</param>
    /// <returns>The vehicle, or null when no source supplies it — for example an uninstalled package.</returns>
    public VehicleData GetVehicle(string id) {
        if (string.IsNullOrEmpty(id)) return null;
        return vehiclesById.TryGetValue(id, out var vehicle) ? vehicle : null;
    }

    /// <summary>Finds a map by its permanent id.</summary>
    /// <param name="id">The id stored in a save file.</param>
    /// <returns>The map, or null when no source supplies it.</returns>
    public MapData GetMap(string id) {
        if (string.IsNullOrEmpty(id)) return null;
        return mapsById.TryGetValue(id, out var map) ? map : null;
    }

    /// <summary>
    /// The vehicle a new career starts with: the configured one when it is registered, otherwise
    /// the first registered vehicle.
    /// </summary>
    /// <param name="preferred">Vehicle named by <see cref="GameConfig"/>. May be null.</param>
    /// <returns>The starting vehicle, or null when no vehicle is registered at all.</returns>
    public VehicleData ResolveStartingVehicle(VehicleData preferred) {
        if (preferred != null && GetVehicle(preferred.vehicleId) != null) return preferred;
        return vehicles.Count > 0 ? vehicles[0] : null;
    }

    /// <summary>
    /// The map a new career starts on: the configured one when it is registered, otherwise the
    /// first registered map.
    /// </summary>
    /// <param name="preferred">Map named by <see cref="GameConfig"/>. May be null.</param>
    /// <returns>The starting map, or null when no map is registered at all.</returns>
    public MapData ResolveStartingMap(MapData preferred) {
        if (preferred != null && GetMap(preferred.mapId) != null) return preferred;
        return maps.Count > 0 ? maps[0] : null;
    }

    /// <summary>Writes any id conflicts to the console so a broken package is visible to the player.</summary>
    public void LogConflicts() {
        foreach (var conflict in conflicts) {
            Debug.LogWarning("[Content] " + conflict);
        }
    }
}
