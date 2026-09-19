using System.Collections.Generic;

/// <summary>
/// Checks that a civilian route's edge list is a contiguous chain, and that a loop route's last
/// edge connects back to its first — without walking the whole map, just this one route's edges.
/// </summary>
public static class CivilianRouteValidator {
    /// <summary>Validates edge contiguity, and loop closure when the route is marked as a loop.</summary>
    /// <param name="document">Document that owns the route's edges.</param>
    /// <param name="route">Route to validate.</param>
    /// <param name="issue">Human-readable failure reason, or null when valid.</param>
    /// <returns>True when the route is contiguous (and closed, if it is a loop).</returns>
    public static bool IsValid(MapNavigationDocument document, CivilianRouteRecord route, out string issue) {
        issue = null;
        if (document == null || route == null) {
            issue = "missing document or route";
            return false;
        }
        if (route.edgeIds == null || route.edgeIds.Count == 0) {
            issue = "route has no edges";
            return false;
        }

        var edgeById = new Dictionary<string, RoadEdgeRecord>();
        if (document.edges != null) {
            foreach (var edge in document.edges) {
                if (edge != null && !string.IsNullOrEmpty(edge.edgeId)) edgeById[edge.edgeId] = edge;
            }
        }

        RoadEdgeRecord previous = null;
        RoadEdgeRecord first = null;
        foreach (var edgeId in route.edgeIds) {
            if (string.IsNullOrWhiteSpace(edgeId) || !edgeById.TryGetValue(edgeId, out var edge)) {
                issue = "dangling edge id " + edgeId;
                return false;
            }
            if (first == null) first = edge;
            if (previous != null && previous.toNodeId != edge.fromNodeId) {
                issue = "gap between " + previous.edgeId + " and " + edge.edgeId;
                return false;
            }
            previous = edge;
        }

        if (route.loop && previous != null && first != null && previous.toNodeId != first.fromNodeId) {
            issue = "loop does not connect its last edge back to its first";
            return false;
        }

        return true;
    }
}
