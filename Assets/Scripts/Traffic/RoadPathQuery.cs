using System.Collections.Generic;

/// <summary>
/// Weighted shortest-path search over a <see cref="RoadGraphRuntime"/>, filtered by vehicle role,
/// width, and authored junction turn restrictions. Search state is (node, incoming edge) rather
/// than just node, so a junction's <see cref="JunctionRecord.allowedTransitions"/> can correctly
/// forbid a specific turn without forbidding every other way through the same node. Bounded by
/// construction: the visited set only grows, so a cyclic graph can never loop forever, and no path
/// found is a safe empty result, never an exception. This is meant to be called sparingly (e.g.
/// once when a police unit needs a new route) — a route cursor should not call this every frame.
/// </summary>
public static class RoadPathQuery {
    struct SearchState : System.IEquatable<SearchState> {
        public string nodeId;
        public string incomingEdgeId;

        public bool Equals(SearchState other) => nodeId == other.nodeId && incomingEdgeId == other.incomingEdgeId;
        public override bool Equals(object obj) => obj is SearchState other && Equals(other);
        public override int GetHashCode() {
            int h1 = nodeId != null ? nodeId.GetHashCode() : 0;
            int h2 = incomingEdgeId != null ? incomingEdgeId.GetHashCode() : 0;
            return h1 * 397 ^ h2;
        }
    }

    /// <summary>Finds a shortest edge sequence from one node to another.</summary>
    /// <param name="graph">Indexed graph to search.</param>
    /// <param name="startNodeId">Starting node.</param>
    /// <param name="targetNodeId">Destination node.</param>
    /// <param name="role">Vehicle role; edges with authored allowedRoles excluding it are skipped.</param>
    /// <param name="vehicleWidth">Vehicle footprint width; edges narrower than this are skipped.</param>
    /// <param name="edgePath">The found edge id sequence, or null when no path exists.</param>
    /// <returns>True when a path was found (empty list when start already equals target).</returns>
    public static bool TryFindPath(RoadGraphRuntime graph, string startNodeId, string targetNodeId,
        VehicleRole role, float vehicleWidth, out List<string> edgePath) {
        edgePath = null;
        if (graph == null || string.IsNullOrEmpty(startNodeId) || string.IsNullOrEmpty(targetNodeId)) return false;
        if (graph.GetNode(startNodeId) == null || graph.GetNode(targetNodeId) == null) return false;
        if (startNodeId == targetNodeId) {
            edgePath = new List<string>();
            return true;
        }

        var startState = new SearchState { nodeId = startNodeId, incomingEdgeId = null };
        var distance = new Dictionary<SearchState, float> { [startState] = 0f };
        var cameFromEdge = new Dictionary<SearchState, string>();
        var cameFromState = new Dictionary<SearchState, SearchState>();
        var visited = new HashSet<SearchState>();
        SearchState goalState = default;
        bool goalFound = false;

        while (true) {
            SearchState current = default;
            bool haveCurrent = false;
            float bestDistance = float.PositiveInfinity;
            foreach (var pair in distance) {
                if (visited.Contains(pair.Key)) continue;
                if (pair.Value < bestDistance) {
                    bestDistance = pair.Value;
                    current = pair.Key;
                    haveCurrent = true;
                }
            }
            if (!haveCurrent) break; // nothing left reachable: disconnected component or fully explored

            if (current.nodeId == targetNodeId) {
                goalState = current;
                goalFound = true;
                break;
            }
            visited.Add(current);

            foreach (var edge in graph.GetOutgoingEdges(current.nodeId)) {
                if (!IsEdgeUsable(graph, edge, role, vehicleWidth)) continue;
                if (!IsTurnAllowed(graph, current.incomingEdgeId, edge)) continue;

                var nextState = new SearchState { nodeId = edge.toNodeId, incomingEdgeId = edge.edgeId };
                float candidateDistance = distance[current] + graph.GetArcLength(edge.edgeId);
                if (!distance.TryGetValue(nextState, out float existingDistance) || candidateDistance < existingDistance) {
                    distance[nextState] = candidateDistance;
                    cameFromEdge[nextState] = edge.edgeId;
                    cameFromState[nextState] = current;
                }
            }
        }

        if (!goalFound) return false;

        var path = new List<string>();
        var cursor = goalState;
        while (cameFromEdge.TryGetValue(cursor, out var edgeId)) {
            path.Insert(0, edgeId);
            cursor = cameFromState[cursor];
        }
        edgePath = path;
        return path.Count > 0;
    }

    static bool IsEdgeUsable(RoadGraphRuntime graph, RoadEdgeRecord edge, VehicleRole role, float vehicleWidth) {
        if (edge == null) return false;
        if (float.IsNaN(vehicleWidth) || float.IsInfinity(vehicleWidth) || vehicleWidth < 0f ||
            float.IsNaN(edge.usableWidth) || float.IsInfinity(edge.usableWidth)) return false;
        if ((!string.IsNullOrEmpty(edge.startJunctionId) && graph.GetJunction(edge.startJunctionId) == null) ||
            (!string.IsNullOrEmpty(edge.endJunctionId) && graph.GetJunction(edge.endJunctionId) == null)) return false;
        if (vehicleWidth > edge.usableWidth) return false;
        if (edge.allowedRoles != null && edge.allowedRoles.Count > 0 && !edge.allowedRoles.Contains(role)) return false;
        return true;
    }

    static bool IsTurnAllowed(RoadGraphRuntime graph, string incomingEdgeId, RoadEdgeRecord outgoingEdge) {
        if (outgoingEdge == null) return false;
        if (string.IsNullOrEmpty(incomingEdgeId)) return true; // Edge references were validated before this call.
        var incomingEdge = graph.GetEdge(incomingEdgeId);
        if (incomingEdge == null || incomingEdge.toNodeId != outgoingEdge.fromNodeId) return false;
        string junctionId = outgoingEdge.startJunctionId;
        if (!string.IsNullOrEmpty(incomingEdge.endJunctionId)) {
            if (!string.IsNullOrEmpty(junctionId) && junctionId != incomingEdge.endJunctionId) return false;
            junctionId = incomingEdge.endJunctionId;
        }
        if (string.IsNullOrEmpty(junctionId)) return true; // Plain node without authored turn restrictions.
        var junction = graph.GetJunction(junctionId);
        if (junction == null || junction.allowedTransitions == null) return false;
        bool allowed = false;
        foreach (var transition in junction.allowedTransitions) {
            if (transition == null || string.IsNullOrWhiteSpace(transition.fromEdgeId) || string.IsNullOrWhiteSpace(transition.toEdgeId)) return false;
            var from = graph.GetEdge(transition.fromEdgeId);
            var to = graph.GetEdge(transition.toEdgeId);
            if (from == null || to == null || string.IsNullOrWhiteSpace(from.toNodeId) || from.toNodeId != to.fromNodeId ||
                (!string.IsNullOrEmpty(from.endJunctionId) && from.endJunctionId != junctionId) ||
                (!string.IsNullOrEmpty(to.startJunctionId) && to.startJunctionId != junctionId)) return false;
            if (transition.fromEdgeId == incomingEdgeId && transition.toEdgeId == outgoingEdge.edgeId) allowed = true;
        }
        return allowed; // An empty authored list permits no movement; corrupt data never removes restrictions.
    }
}
