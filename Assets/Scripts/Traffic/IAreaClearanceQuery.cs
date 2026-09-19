using UnityEngine;

/// <summary>
/// Collider-footprint clearance check used by <see cref="VehicleSpawnPolicy"/>. Kept as an adapter
/// interface rather than a direct Physics2D call so the pure candidate filter stays EditMode-testable
/// with a fake; a real Unity adapter wraps Physics2D.OverlapBox for actual gameplay.
/// </summary>
public interface IAreaClearanceQuery {
    /// <summary>Returns true when the given footprint at the given position/heading has no blocking overlap.</summary>
    bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees);
}
