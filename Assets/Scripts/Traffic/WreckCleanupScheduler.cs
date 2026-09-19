using System.Collections.Generic;

/// <summary>
/// Tracks each wreck's own spawn time on a timer fully separate from
/// <see cref="TrafficRespawnScheduler"/>'s moving-vehicle deficits. When the wreck budget is full,
/// <see cref="TryFindOldestEligibleWreck"/> finds the oldest wreck that has already reached its
/// authored minimum visible lifetime so it can be cleaned up before a new spawn proceeds; if none
/// qualifies yet, the caller must defer the new spawn rather than remove a too-young wreck.
/// </summary>
public sealed class WreckCleanupScheduler {
    readonly Dictionary<string, float> spawnTimeByWreckId = new Dictionary<string, float>();

    public int Count => spawnTimeByWreckId.Count;

    /// <summary>Starts tracking a wreck's lifetime from the moment it was created.</summary>
    public void RegisterWreck(string wreckId, float sessionTime) {
        if (!string.IsNullOrEmpty(wreckId)) spawnTimeByWreckId[wreckId] = sessionTime;
    }

    /// <summary>Finds the oldest wreck whose minimum visible lifetime has elapsed. False when every tracked wreck is still too young.</summary>
    public bool TryFindOldestEligibleWreck(float sessionTime, float minimumLifetimeSeconds, out string wreckId) {
        wreckId = null;
        float oldestSpawnTime = float.PositiveInfinity;
        foreach (var pair in spawnTimeByWreckId) {
            if (sessionTime - pair.Value < minimumLifetimeSeconds) continue;
            if (pair.Value < oldestSpawnTime) {
                oldestSpawnTime = pair.Value;
                wreckId = pair.Key;
            }
        }
        return wreckId != null;
    }

    /// <summary>
    /// Stops tracking a wreck. Callers must only call this once the wreck's collider and visuals have
    /// actually both been removed/reset (a single Release/reset-hook call, per NpcVehiclePool) —
    /// this scheduler holds no visual/collider state itself to remove in a second step.
    /// </summary>
    public bool MarkCleaned(string wreckId) {
        return !string.IsNullOrEmpty(wreckId) && spawnTimeByWreckId.Remove(wreckId);
    }
}
