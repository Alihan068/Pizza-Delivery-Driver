using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picks a weighted-random vehicle profile id from a <see cref="VehiclePoolRecord"/>. Entries with
/// a zero/negative/NaN/Infinity weight or an id the catalog cannot resolve are excluded and
/// reported, never silently included in the weighted draw.
/// </summary>
public static class VehiclePoolResolver {
    /// <summary>Attempts a weighted pick from a pool's valid entries.</summary>
    /// <param name="pool">Pool to pick from.</param>
    /// <param name="catalog">Catalog used to reject entries whose id does not resolve. May be null to skip that check.</param>
    /// <param name="randomValue01">Caller-supplied random value in [0, 1); deterministic and testable, never rolled internally.</param>
    /// <param name="vehicleProfileId">The picked id, or null when no valid entry exists.</param>
    /// <param name="issues">Every rejected entry's reason, in encounter order. Never null.</param>
    /// <returns>True when a valid pick was made.</returns>
    public static bool TryPickWeighted(VehiclePoolRecord pool, ITrafficProfileCatalog catalog, float randomValue01,
        out string vehicleProfileId, out List<string> issues) {
        issues = new List<string>();
        vehicleProfileId = null;

        if (pool == null || pool.entries == null || pool.entries.Count == 0) {
            issues.Add("pool has no entries");
            return false;
        }

        float totalWeight = 0f;
        var validEntries = new List<VehiclePoolEntry>();
        foreach (var entry in pool.entries) {
            if (entry == null || string.IsNullOrEmpty(entry.vehicleProfileId)) {
                issues.Add("entry missing vehicleProfileId");
                continue;
            }
            if (float.IsNaN(entry.weight) || float.IsInfinity(entry.weight) || entry.weight <= 0f) {
                issues.Add(entry.vehicleProfileId + ": invalid weight " + entry.weight);
                continue;
            }
            if (catalog != null && catalog.ResolveVehicleProfile(entry.vehicleProfileId) == null) {
                issues.Add(entry.vehicleProfileId + ": not found in catalog");
                continue;
            }
            validEntries.Add(entry);
            totalWeight += entry.weight;
        }

        if (validEntries.Count == 0 || totalWeight <= 0f) return false;

        float target = Mathf.Clamp01(randomValue01) * totalWeight;
        float cursor = 0f;
        foreach (var entry in validEntries) {
            cursor += entry.weight;
            if (target <= cursor) {
                vehicleProfileId = entry.vehicleProfileId;
                return true;
            }
        }

        // Floating point edge case at randomValue01 very close to 1: fall back to the last valid entry.
        vehicleProfileId = validEntries[validEntries.Count - 1].vehicleProfileId;
        return true;
    }
}
