using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared directed road-path search for legacy node queries and anchored police queries.</summary>
public static class RoadPathQuery {
    /// <summary>Terminal state of an anchored query.</summary>
    public enum QueryStatus {
        /// <summary>A complete validated route was found.</summary>
        Found,
        /// <summary>No route satisfies the directed graph constraints.</summary>
        NoPath,
        /// <summary>Input, graph, geometry, or constraint data is invalid.</summary>
        InvalidInput,
        /// <summary>The finite caller-owned work budget was exhausted.</summary>
        BudgetExceeded
    }

    /// <summary>Directed location on one authored edge.</summary>
    public readonly struct EdgeAnchor {
        /// <summary>Authored edge identifier.</summary>
        public readonly string edgeId;
        /// <summary>Finite distance from the edge's authored start.</summary>
        public readonly float distanceAlongEdge;

        /// <summary>Creates an edge anchor.</summary>
        public EdgeAnchor(string edgeId, float distanceAlongEdge) {
            this.edgeId = edgeId;
            this.distanceAlongEdge = distanceAlongEdge;
        }
    }

    /// <summary>One directed, possibly partial, edge span in a returned route.</summary>
    public readonly struct PathSpan {
        /// <summary>Authored edge identifier.</summary>
        public readonly string edgeId;
        /// <summary>Distance where traversal enters the edge.</summary>
        public readonly float startDistance;
        /// <summary>Distance where traversal leaves the edge.</summary>
        public readonly float endDistance;

        /// <summary>Creates an ordered edge span.</summary>
        public PathSpan(string edgeId, float startDistance, float endDistance) {
            this.edgeId = edgeId;
            this.startDistance = startDistance;
            this.endDistance = endDistance;
        }
    }

    /// <summary>Mutable finite work allowance shared by one or more candidate queries.</summary>
    public sealed class SearchBudget {
        /// <summary>Maximum number of charged work units.</summary>
        public readonly int maximumWork;
        /// <summary>Work units consumed so far.</summary>
        public int ConsumedWork { get; private set; }
        /// <summary>Whether a charge was refused because this budget is exhausted.</summary>
        public bool IsExhausted { get; private set; }
        /// <summary>Remaining finite work units available to the caller.</summary>
        public int RemainingWork => maximumWork - ConsumedWork;

        /// <summary>Creates a finite positive budget.</summary>
        public SearchBudget(int maximumWork) {
            this.maximumWork = maximumWork;
        }

        /// <summary>Consumes work, returning false without partial consumption when exhausted.</summary>
        public bool TryConsume(int work) {
            if (work <= 0 || maximumWork <= 0 || ConsumedWork > maximumWork - work) {
                IsExhausted = true;
                return false;
            }
            ConsumedWork += work;
            return true;
        }
    }

    /// <summary>Complete anchored query result; failed results never expose a partial route.</summary>
    public sealed class AnchoredPathResult {
        /// <summary>Query terminal status.</summary>
        public QueryStatus status;
        /// <summary>Ordered validated spans, empty for non-found results.</summary>
        public IReadOnlyList<PathSpan> spans;
        /// <summary>Total directed distance represented by the spans.</summary>
        public float totalDistance;
    }

    struct SearchState : IEquatable<SearchState> {
        public string nodeId;
        public string incomingEdgeId;
        public bool initialPartial;

        public bool Equals(SearchState other) {
            return nodeId == other.nodeId && incomingEdgeId == other.incomingEdgeId && initialPartial == other.initialPartial;
        }

        public override bool Equals(object obj) {
            return obj is SearchState other && Equals(other);
        }

        public override int GetHashCode() {
            int first = nodeId == null ? 0 : nodeId.GetHashCode();
            int second = incomingEdgeId == null ? 0 : incomingEdgeId.GetHashCode();
            return (first * 397 ^ second) * 397 ^ (initialPartial ? 1 : 0);
        }
    }

    sealed class SearchNode {
        public SearchState state;
        public float distance;
        public List<PathSpan> spans;
    }

    /// <summary>Finds a shortest width-filtered node path with the existing legacy contract.</summary>
    /// <param name="graph">Indexed graph to search.</param>
    /// <param name="startNodeId">Starting node.</param>
    /// <param name="targetNodeId">Destination node.</param>
    /// <param name="role">Vehicle role used by authored edge permissions.</param>
    /// <param name="vehicleWidth">Positive finite vehicle width.</param>
    /// <param name="edgePath">Found edge identifiers, or null when no path exists.</param>
    /// <returns>True when a path was found.</returns>
    public static bool TryFindPath(RoadGraphRuntime graph, string startNodeId, string targetNodeId,
        VehicleRole role, float vehicleWidth, out List<string> edgePath) {
        edgePath = null;
        if (graph == null || string.IsNullOrEmpty(startNodeId) || string.IsNullOrEmpty(targetNodeId) ||
            !IsFiniteNonnegative(vehicleWidth) || graph.GetNode(startNodeId) == null || graph.GetNode(targetNodeId) == null) return false;
        if (startNodeId == targetNodeId) {
            edgePath = new List<string>();
            return true;
        }

        var request = new SearchRequest {
            graph = graph,
            role = role,
            legacy = true,
            startNodeId = startNodeId,
            targetNodeId = targetNodeId,
            width = vehicleWidth,
            budget = new SearchBudget(int.MaxValue)
        };
        if (!RunSearch(request, out var result)) return false;
        edgePath = new List<string>();
        foreach (var span in result.spans) edgePath.Add(span.edgeId);
        return edgePath.Count > 0;
    }

    /// <summary>Finds a finite-budget directed route between two edge anchors.</summary>
    /// <param name="graph">Indexed graph with cached normalized edge geometry.</param>
    /// <param name="start">Directed starting edge and distance.</param>
    /// <param name="target">Directed destination edge and distance.</param>
    /// <param name="role">Vehicle role used by authored edge permissions.</param>
    /// <param name="constraints">Width and turn constraints applied during expansion.</param>
    /// <param name="budget">Caller-owned budget shared by candidate queries.</param>
    /// <param name="result">Complete result with no partial spans on failure.</param>
    /// <returns>True only when <paramref name="result"/> has status <see cref="QueryStatus.Found"/>.</returns>
    public static bool TryFindAnchoredPath(RoadGraphRuntime graph, EdgeAnchor start, EdgeAnchor target,
        VehicleRole role, RouteTransitionFilter.VehicleConstraints constraints, SearchBudget budget,
        out AnchoredPathResult result) {
        result = NewResult(QueryStatus.InvalidInput);
        if (graph == null || budget == null || budget.maximumWork <= 0 ||
            !IsFinitePositive(constraints.width) || !IsFiniteNonnegative(constraints.widthSafetyMargin) ||
            !IsFiniteNonnegative(constraints.minimumTurningRadius) || !IsFinite(constraints.maxTurnAngleDegrees) ||
            constraints.maxTurnAngleDegrees <= 0f || constraints.maxTurnAngleDegrees > 180f) return false;

        var startEdge = graph.GetEdge(start.edgeId);
        var targetEdge = graph.GetEdge(target.edgeId);
        float startLength = graph.GetArcLength(start.edgeId);
        float targetLength = graph.GetArcLength(target.edgeId);
        if (startEdge == null || targetEdge == null || !IsFinite(startLength) || !IsFinite(targetLength) ||
            startLength <= 0f || targetLength <= 0f || !IsFinite(start.distanceAlongEdge) ||
            !IsFinite(target.distanceAlongEdge) || start.distanceAlongEdge < 0f || start.distanceAlongEdge > startLength ||
            target.distanceAlongEdge < 0f || target.distanceAlongEdge > targetLength) return false;
        if (!IsEdgeRoleAndWidthUsable(graph, startEdge, role, constraints.width + constraints.widthSafetyMargin) ||
            !IsEdgeRoleAndWidthUsable(graph, targetEdge, role, constraints.width + constraints.widthSafetyMargin)) return false;

        if (start.edgeId == target.edgeId && target.distanceAlongEdge >= start.distanceAlongEdge) {
            if (!TryValidateSpan(graph, startEdge, start.distanceAlongEdge, target.distanceAlongEdge, constraints, budget)) {
                result = NewResult(budget.IsExhausted ? QueryStatus.BudgetExceeded : QueryStatus.NoPath);
                return false;
            }
            result = FoundResult(new List<PathSpan> { new PathSpan(start.edgeId, start.distanceAlongEdge, target.distanceAlongEdge) });
            return result.status == QueryStatus.Found;
        }

        var request = new SearchRequest {
            graph = graph,
            role = role,
            legacy = false,
            startEdgeId = start.edgeId,
            targetEdgeId = target.edgeId,
            startDistance = start.distanceAlongEdge,
            targetDistance = target.distanceAlongEdge,
            constraints = constraints,
            budget = budget
        };
        return RunSearch(request, out result);
    }

    struct SearchRequest {
        public RoadGraphRuntime graph;
        public VehicleRole role;
        public bool legacy;
        public string startNodeId;
        public string targetNodeId;
        public string startEdgeId;
        public string targetEdgeId;
        public float startDistance;
        public float targetDistance;
        public float width;
        public RouteTransitionFilter.VehicleConstraints constraints;
        public SearchBudget budget;
    }

    static bool RunSearch(SearchRequest request, out AnchoredPathResult result) {
        result = NewResult(QueryStatus.NoPath);
        var graph = request.graph;
        var frontier = new Dictionary<SearchState, SearchNode>();
        var visited = new HashSet<SearchState>();
        if (request.legacy) {
            var seed = new SearchState { nodeId = request.startNodeId, incomingEdgeId = null, initialPartial = false };
            frontier[seed] = new SearchNode { state = seed, distance = 0f, spans = new List<PathSpan>() };
        } else {
            var startEdge = graph.GetEdge(request.startEdgeId);
            float length = graph.GetArcLength(request.startEdgeId);
            if (!TryValidateSpan(graph, startEdge, request.startDistance, length, request.constraints, request.budget)) {
                if (request.budget.IsExhausted) result = NewResult(QueryStatus.BudgetExceeded);
                return false;
            }
            var seed = new SearchState { nodeId = startEdge.toNodeId, incomingEdgeId = startEdge.edgeId, initialPartial = true };
            frontier[seed] = new SearchNode {
                state = seed,
                distance = length - request.startDistance,
                spans = new List<PathSpan> { new PathSpan(startEdge.edgeId, request.startDistance, length) }
            };
        }

        while (frontier.Count > 0) {
            if (!request.budget.TryConsume(1)) {
                result = NewResult(QueryStatus.BudgetExceeded);
                return false;
            }
            SearchNode current = SelectBest(frontier, visited, request.budget);
            if (current == null) {
                if (request.budget.IsExhausted) result = NewResult(QueryStatus.BudgetExceeded);
                return false;
            }
            frontier.Remove(current.state);
            visited.Add(current.state);

            if (request.legacy && current.state.nodeId == request.targetNodeId) {
                result = FoundResult(current.spans);
                return result.status == QueryStatus.Found;
            }

            foreach (var candidate in graph.GetOutgoingEdges(current.state.nodeId)) {
                if (!request.budget.TryConsume(1)) {
                    result = NewResult(QueryStatus.BudgetExceeded);
                    return false;
                }
                float candidateLength = graph.GetArcLength(candidate == null ? null : candidate.edgeId);
                if (candidate == null || !IsEdgeRoleAndWidthUsable(graph, candidate, request.role,
                    request.legacy ? request.width : request.constraints.width + request.constraints.widthSafetyMargin) || !IsFinite(candidateLength)) continue;
                if (!IsTurnAllowed(graph, current.state.incomingEdgeId, candidate, request.budget)) {
                    if (request.budget.IsExhausted) { result = NewResult(QueryStatus.BudgetExceeded); return false; }
                    continue;
                }

                if (request.legacy) {
                    var next = new SearchState { nodeId = candidate.toNodeId, incomingEdgeId = candidate.edgeId, initialPartial = false };
                    if (visited.Contains(next)) continue;
                    float distance = current.distance + candidateLength;
                    var spans = new List<PathSpan>(current.spans) { new PathSpan(candidate.edgeId, 0f, candidateLength) };
                    if (!frontier.TryGetValue(next, out var existing) || distance < existing.distance) {
                        frontier[next] = new SearchNode { state = next, distance = distance, spans = spans };
                    }
                    continue;
                }

                bool isTarget = candidate.edgeId == request.targetEdgeId;
                float candidateEnd = isTarget ? request.targetDistance : candidateLength;
                bool spanValid = TryValidateSpan(graph, candidate, 0f, candidateEnd, request.constraints, request.budget);
                bool transitionValid = spanValid && TryValidateTransition(graph, current.spans[current.spans.Count - 1], candidate.edgeId, candidateEnd, request.constraints, request.budget);
                if (spanValid && transitionValid && isTarget) {
                    result = FoundResult(new List<PathSpan>(current.spans) { new PathSpan(candidate.edgeId, 0f, candidateEnd) });
                    if (result.status == QueryStatus.Found) return true;
                    return false;
                }
                if (request.budget.IsExhausted) { result = NewResult(QueryStatus.BudgetExceeded); return false; }
                if (isTarget) {
                    candidateEnd = candidateLength;
                    spanValid = TryValidateSpan(graph, candidate, 0f, candidateEnd, request.constraints, request.budget);
                    transitionValid = spanValid && TryValidateTransition(graph, current.spans[current.spans.Count - 1], candidate.edgeId, candidateEnd, request.constraints, request.budget);
                    if (request.budget.IsExhausted) { result = NewResult(QueryStatus.BudgetExceeded); return false; }
                    if (!spanValid || !transitionValid) continue;
                } else if (!spanValid || !transitionValid) continue;
                var candidateSpans = new List<PathSpan>(current.spans) { new PathSpan(candidate.edgeId, 0f, candidateEnd) };
                var nextState = new SearchState { nodeId = candidate.toNodeId, incomingEdgeId = candidate.edgeId, initialPartial = false };
                if (visited.Contains(nextState)) continue;
                float nextDistance = current.distance + candidateLength;
                if (!frontier.TryGetValue(nextState, out var nextExisting) || nextDistance < nextExisting.distance) {
                    frontier[nextState] = new SearchNode { state = nextState, distance = nextDistance, spans = candidateSpans };
                }
            }
        }
        return false;
    }

    static bool TryValidateSpan(RoadGraphRuntime graph, RoadEdgeRecord edge, float from, float to,
        RouteTransitionFilter.VehicleConstraints constraints, SearchBudget budget) {
        if (edge == null || !budget.TryConsume(1)) return false;
        var geometry = graph.GetGeometry(edge.edgeId);
        if (geometry == null || geometry.Points == null || geometry.CumulativeLengths == null || geometry.Points.Count < 2 ||
            geometry.CumulativeLengths.Count != geometry.Points.Count || !IsFinite(geometry.Length) || geometry.Length <= 0f ||
            !IsFinite(from) || !IsFinite(to) || from < 0f || to < from || to > geometry.Length) return false;
        if (from == to) return true;
        for (int segment = 1; segment < geometry.CumulativeLengths.Count; segment++) {
            if (!budget.TryConsume(1)) return false;
            float segmentStart = geometry.CumulativeLengths[segment - 1];
            float segmentEnd = geometry.CumulativeLengths[segment];
            float availableStart = Mathf.Max(segmentStart, from);
            float availableEnd = Mathf.Min(segmentEnd, to);
            if (availableEnd > availableStart && !IsFinite(availableEnd - availableStart)) return false;
        }
        for (int i = 1; i < geometry.CumulativeLengths.Count - 1; i++) {
            float vertexDistance = geometry.CumulativeLengths[i];
            if (vertexDistance <= from || vertexDistance >= to) continue;
            if (!budget.TryConsume(1)) return false;
            float incomingAvailable = Mathf.Min(vertexDistance, to) - Mathf.Max(geometry.CumulativeLengths[i - 1], from);
            float outgoingAvailable = Mathf.Min(geometry.CumulativeLengths[i + 1], to) - Mathf.Max(vertexDistance, from);
            Vector2 incomingDirection = geometry.Points[i] - geometry.Points[i - 1];
            Vector2 outgoingDirection = geometry.Points[i + 1] - geometry.Points[i];
            if (!RouteTransitionFilter.TransitionFits(incomingDirection, incomingAvailable, outgoingDirection, outgoingAvailable, constraints, out _, out _, out _)) return false;
        }
        return true;
    }

    static bool TryValidateTransition(RoadGraphRuntime graph, PathSpan incomingSpan, string outgoingEdgeId, float outgoingEnd,
        RouteTransitionFilter.VehicleConstraints constraints, SearchBudget budget) {
        if (!budget.TryConsume(1)) return false;
        var incomingGeometry = graph.GetGeometry(incomingSpan.edgeId);
        var outgoingGeometry = graph.GetGeometry(outgoingEdgeId);
        if (incomingGeometry == null || outgoingGeometry == null) return false;
        if (incomingGeometry.Points.Count < 2 || outgoingGeometry.Points.Count < 2 ||
            !IsFinite(incomingSpan.startDistance) || !IsFinite(incomingSpan.endDistance) || !IsFinite(outgoingEnd) ||
            incomingSpan.startDistance < 0f || incomingSpan.endDistance > incomingGeometry.Length ||
            outgoingEnd < 0f || outgoingEnd > outgoingGeometry.Length) return false;
        Vector2 incomingDirection = incomingGeometry.Points[incomingGeometry.Points.Count - 1] - incomingGeometry.Points[incomingGeometry.Points.Count - 2];
        Vector2 outgoingDirection = outgoingGeometry.Points[1] - outgoingGeometry.Points[0];
        float incomingAvailable = Mathf.Min(incomingSpan.endDistance - incomingSpan.startDistance,
            incomingGeometry.CumulativeLengths[incomingGeometry.CumulativeLengths.Count - 1] - incomingGeometry.CumulativeLengths[incomingGeometry.CumulativeLengths.Count - 2]);
        float outgoingAvailable = Mathf.Min(outgoingEnd, outgoingGeometry.CumulativeLengths[1]);
        return RouteTransitionFilter.TransitionFits(incomingDirection, incomingAvailable, outgoingDirection, outgoingAvailable,
            constraints, out _, out _, out _);
    }

    static bool IsEdgeRoleAndWidthUsable(RoadGraphRuntime graph, RoadEdgeRecord edge, VehicleRole role, float width) {
        if (edge == null || !IsFiniteNonnegative(width) || !IsFinite(edge.usableWidth) || width > edge.usableWidth) return false;
        if ((!string.IsNullOrEmpty(edge.startJunctionId) && graph.GetJunction(edge.startJunctionId) == null) ||
            (!string.IsNullOrEmpty(edge.endJunctionId) && graph.GetJunction(edge.endJunctionId) == null)) return false;
        return edge.allowedRoles == null || edge.allowedRoles.Count == 0 || edge.allowedRoles.Contains(role);
    }

    static bool IsTurnAllowed(RoadGraphRuntime graph, string incomingEdgeId, RoadEdgeRecord outgoingEdge, SearchBudget budget) {
        if (outgoingEdge == null) return false;
        if (string.IsNullOrEmpty(incomingEdgeId)) return true;
        var incomingEdge = graph.GetEdge(incomingEdgeId);
        if (incomingEdge == null || incomingEdge.toNodeId != outgoingEdge.fromNodeId) return false;
        string junctionId = outgoingEdge.startJunctionId;
        if (!string.IsNullOrEmpty(incomingEdge.endJunctionId)) {
            if (!string.IsNullOrEmpty(junctionId) && junctionId != incomingEdge.endJunctionId) return false;
            junctionId = incomingEdge.endJunctionId;
        }
        if (string.IsNullOrEmpty(junctionId)) return true;
        var junction = graph.GetJunction(junctionId);
        if (junction == null || junction.allowedTransitions == null) return false;
        bool allowed = false;
        foreach (var transition in junction.allowedTransitions) {
            if (!budget.TryConsume(1)) return false;
            if (transition == null || string.IsNullOrWhiteSpace(transition.fromEdgeId) || string.IsNullOrWhiteSpace(transition.toEdgeId)) return false;
            var from = graph.GetEdge(transition.fromEdgeId);
            var to = graph.GetEdge(transition.toEdgeId);
            if (from == null || to == null || string.IsNullOrWhiteSpace(from.toNodeId) || string.IsNullOrWhiteSpace(to.fromNodeId) ||
                from.toNodeId != to.fromNodeId || (!string.IsNullOrEmpty(from.endJunctionId) && from.endJunctionId != junctionId) ||
                (!string.IsNullOrEmpty(to.startJunctionId) && to.startJunctionId != junctionId)) return false;
            if (transition.fromEdgeId == incomingEdgeId && transition.toEdgeId == outgoingEdge.edgeId) allowed = true;
        }
        return allowed;
    }

    static SearchNode SelectBest(Dictionary<SearchState, SearchNode> frontier, HashSet<SearchState> visited, SearchBudget budget) {
        SearchNode best = null;
        foreach (var pair in frontier) {
            if (!budget.TryConsume(1)) return null;
            if (visited.Contains(pair.Key)) continue;
            var node = pair.Value;
            if (!IsFinite(node.distance)) continue;
            if (best == null || node.distance < best.distance ||
                (node.distance == best.distance && CompareState(node.state, best.state) < 0)) best = node;
        }
        return best;
    }

    static AnchoredPathResult FoundResult(List<PathSpan> spans) {
        float total = 0f;
        foreach (var span in spans) {
            float distance = span.endDistance - span.startDistance;
            if (!IsFinite(distance) || distance < 0f || !IsFinite(total + distance)) return NewResult(QueryStatus.InvalidInput);
            total += distance;
        }
        return new AnchoredPathResult { status = QueryStatus.Found, spans = spans, totalDistance = total };
    }

    static AnchoredPathResult NewResult(QueryStatus status) {
        return new AnchoredPathResult { status = status, spans = new List<PathSpan>(), totalDistance = 0f };
    }

    static int CompareState(SearchState first, SearchState second) {
        int node = string.CompareOrdinal(first.nodeId, second.nodeId);
        if (node != 0) return node;
        int edge = string.CompareOrdinal(first.incomingEdgeId, second.incomingEdgeId);
        return edge != 0 ? edge : first.initialPartial.CompareTo(second.initialPartial);
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
    static bool IsFiniteNonnegative(float value) => IsFinite(value) && value >= 0f;
}
