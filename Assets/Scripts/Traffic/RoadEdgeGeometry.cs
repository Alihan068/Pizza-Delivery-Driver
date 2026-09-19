using UnityEngine;

/// <summary>Pure geometry helper shared by <see cref="RoadGraphRuntime"/> and <see cref="MapNavigationEditCommands"/> so edge-length math is written exactly once.</summary>
public static class RoadEdgeGeometry {
    /// <summary>Computes an edge's arc length across its full polyline: from-node, interior points, to-node.</summary>
    public static float ComputeLength(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode) {
        if (fromNode == null || edge == null || toNode == null) return 0f;

        Vector2 previous = new Vector2(fromNode.x, fromNode.y);
        float total = 0f;
        if (edge.orderedPoints != null) {
            foreach (var point in edge.orderedPoints) {
                total += Vector2.Distance(previous, point);
                previous = point;
            }
        }
        total += Vector2.Distance(previous, new Vector2(toNode.x, toNode.y));
        return total;
    }
}
