using UnityEngine;

/// <summary>Pure helper turning a <see cref="VehicleSpawnRecord"/>'s edge attachment into a map-local pose (position on the polyline, heading along it).</summary>
public static class SpawnPoseLocator {
    /// <summary>Locates a spawn record on its edge's full polyline.</summary>
    /// <param name="graph">Indexed graph the edge belongs to.</param>
    /// <param name="spawn">Spawn record to locate.</param>
    /// <param name="position">Map-local position at the spawn's arc distance (clamped to the edge).</param>
    /// <param name="headingDegrees">Heading (CCW from +Y) of the polyline segment at that point.</param>
    /// <returns>False when the edge, its nodes or the distance are invalid.</returns>
    public static bool TryLocate(RoadGraphRuntime graph, VehicleSpawnRecord spawn, out Vector2 position, out float headingDegrees) {
        position = Vector2.zero;
        headingDegrees = 0f;
        if (graph == null || spawn == null || float.IsNaN(spawn.distanceAlongEdge) || float.IsInfinity(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f) return false;
        var edge = graph.GetEdge(spawn.edgeId);
        if (edge == null) return false;
        var from = graph.GetNode(edge.fromNodeId);
        var to = graph.GetNode(edge.toNodeId);
        if (from == null || to == null) return false;

        Vector2 previous = new Vector2(from.x, from.y);
        float remaining = spawn.distanceAlongEdge;
        int interior = edge.orderedPoints != null ? edge.orderedPoints.Count : 0;
        Vector2 lastDelta = Vector2.zero;
        for (int i = 0; i <= interior; i++) {
            Vector2 next = i < interior ? edge.orderedPoints[i] : new Vector2(to.x, to.y);
            Vector2 delta = next - previous;
            float segmentLength = delta.magnitude;
            if (segmentLength > 0f) {
                lastDelta = delta;
                if (remaining <= segmentLength) {
                    position = previous + delta * (remaining / segmentLength);
                    headingDegrees = MapNavigationCoordinates.DirectionToHeadingDegrees(delta);
                    return true;
                }
                remaining -= segmentLength;
            }
            previous = next;
        }
        if (lastDelta.sqrMagnitude <= 0f) return false;
        position = previous; // past the end: clamp to the to-node
        headingDegrees = MapNavigationCoordinates.DirectionToHeadingDegrees(lastDelta);
        return true;
    }
}
