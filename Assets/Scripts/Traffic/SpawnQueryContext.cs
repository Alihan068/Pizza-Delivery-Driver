using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything <see cref="VehicleSpawnPolicy"/> checks a candidate against: camera visibility (bounds
/// + margin), player position/velocity for near-future-path rejection, the road graph (node access),
/// authored no-spawn service regions, and an area-clearance adapter (collider footprint). Passed in
/// whole rather than read from live Camera/Physics2D so the filter itself stays pure.
/// </summary>
public sealed class SpawnQueryContext {
    public SpawnPlacementMode mode;

    public Rect cameraBounds;
    public float cameraMargin;

    public Vector2 playerPosition;
    public Vector2 playerVelocity;
    public float minApproachTimeSeconds;

    public RoadGraphRuntime graph;
    public IReadOnlyList<NoSpawnRegion> noSpawnRegions;
    public IAreaClearanceQuery clearanceQuery;
}
