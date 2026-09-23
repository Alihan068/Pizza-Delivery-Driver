using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Selects a bounded exact authored junction anchor from the player's actual travel velocity.
/// This planner owns no route cursor, timer, physics body, or predicted final connector; callers
/// provide the existing cadence and aggregate search budget for every attempt.
/// </summary>
public sealed class PoliceInterceptPlanner {
    readonly RoadGraphRuntime graph;
    readonly MapNavigationDocument navigation;
    readonly PoliceInterceptSettings settings;
    readonly float planningDistance;
    readonly List<Candidate> candidates = new List<Candidate>();

    /// <summary>Minimum actual player speed required before authored interception is considered.</summary>
    public float MinimumSpeed => settings == null ? float.PositiveInfinity : settings.minimumSpeed;
    /// <summary>Bounded actual-velocity horizon used to retain one selected junction choice.</summary>
    public float PredictionSeconds => settings == null ? 0f : settings.predictionSeconds;

    sealed class Candidate {
        public string edgeId;
        public float distanceAlongEdge;
        public float distanceFromPlayer;
    }

    /// <summary>Creates a bounded planner over one directed graph and copied Intercept tuning.</summary>
    /// <param name="graph">Indexed directed graph used for authored junction traversal.</param>
    /// <param name="navigation">Map-local document supplying finite bounds.</param>
    /// <param name="settings">Provisional Intercept tuning copied into this planner.</param>
    /// <param name="planningDistance">Existing driving planning-distance cap.</param>
    public PoliceInterceptPlanner(RoadGraphRuntime graph, MapNavigationDocument navigation,
        PoliceInterceptSettings settings, float planningDistance) {
        this.graph = graph;
        this.navigation = navigation;
        this.settings = settings == null ? null : settings.Clone();
        this.planningDistance = planningDistance;
    }

    /// <summary>
    /// Selects one exact incoming-edge endpoint at an authored junction. Low-speed, reverse,
    /// invalid, unreachable, or exhausted inputs fail closed without exposing a partial choice.
    /// </summary>
    /// <param name="playerPosition">Measured map-local player position.</param>
    /// <param name="playerVelocity">Measured world/map-local player Rigidbody velocity.</param>
    /// <param name="lifeId">Stable police life identity used for deterministic candidate preference.</param>
    /// <param name="budget">Existing aggregate budget shared with projection and route validation.</param>
    /// <param name="anchor">Exact endpoint anchor when a candidate is selected.</param>
    /// <returns>True only when a bounded, authored and velocity-aligned candidate is selected.</returns>
    public bool TrySelectAnchor(Vector2 playerPosition, Vector2 playerVelocity, int lifeId,
        RoadPathQuery.SearchBudget budget, out RoadPathQuery.EdgeAnchor anchor) {
        anchor = default;
        var choices = new List<RoadPathQuery.EdgeAnchor>();
        if (!TryCollectCandidates(playerPosition, playerVelocity, lifeId, budget, choices) || choices.Count == 0) return false;
        anchor = choices[0];
        return true;
    }

    /// <summary>Collects all bounded reachable junction choices in deterministic life-specific order.</summary>
    /// <param name="playerPosition">Measured map-local player position.</param>
    /// <param name="playerVelocity">Measured player Rigidbody velocity.</param>
    /// <param name="lifeId">Stable police-life identity used for diversification.</param>
    /// <param name="budget">Shared finite search budget.</param>
    /// <param name="anchors">Cleared output list of exact incoming-edge junction anchors.</param>
    /// <returns>True only when the complete bounded scan finished without budget exhaustion.</returns>
    public bool TryCollectCandidates(Vector2 playerPosition, Vector2 playerVelocity, int lifeId,
        RoadPathQuery.SearchBudget budget, List<RoadPathQuery.EdgeAnchor> anchors) {
        if (anchors == null) return false;
        anchors.Clear();
        if (!ValidateInput(playerPosition, playerVelocity, budget, out float speed)) return false;
        if (!RoadSegmentSpatialIndex.TryGetOrBuild(graph, planningDistance, budget, out var index, out var buildStatus) ||
            buildStatus != RoadSegmentSpatialIndex.BuildStatus.Built) return false;
        var projections = index.Query(playerPosition, planningDistance, budget);
        if (projections.status != RoadSegmentSpatialIndex.QueryStatus.Found) return false;

        Vector2 travelDirection = playerVelocity / speed;
        ProjectionSeed seed = FindAlignedSeed(projections.candidates, travelDirection, budget);
        if (seed == null) return false;
        candidates.Clear();
        float maximumDistance = Mathf.Min(speed * settings.predictionSeconds, planningDistance);
        if (!FiniteNonnegative(maximumDistance) || !ScanForward(seed.edgeId, seed.distanceAlongEdge,
            seed.distanceFromPlayer, travelDirection, maximumDistance, budget)) {
            candidates.Clear();
            return false;
        }
        if (candidates.Count == 0) return false;
        candidates.Sort(CompareCandidates);
        int offset = StableIndex(lifeId, candidates.Count);
        for (int indexValue = 0; indexValue < candidates.Count; indexValue++) {
            Candidate choice = candidates[(offset + indexValue) % candidates.Count];
            if (!FinitePositive(choice.distanceAlongEdge) || !Finite(choice.distanceFromPlayer)) {
                anchors.Clear();
                return false;
            }
            anchors.Add(new RoadPathQuery.EdgeAnchor(choice.edgeId, choice.distanceAlongEdge));
        }
        return anchors.Count > 0;
    }

    /// <summary>Clears per-attempt candidate state without changing shared graph indexes.</summary>
    public void Reset() => candidates.Clear();

    sealed class ProjectionSeed {
        public string edgeId;
        public float distanceAlongEdge;
        public float distanceFromPlayer;
        public Vector2 direction;
    }

    ProjectionSeed FindAlignedSeed(IReadOnlyList<RoadSegmentSpatialIndex.ProjectionCandidate> projections,
        Vector2 travelDirection, RoadPathQuery.SearchBudget budget) {
        ProjectionSeed best = null;
        for (int index = 0; index < projections.Count; index++) {
            if (!budget.TryConsume(1)) return null;
            var projection = projections[index];
            float alignment = Vector2.Dot(travelDirection, projection.direction);
            if (!Finite(alignment) || alignment <= 0f) continue;
            var candidate = new ProjectionSeed {
                edgeId = projection.edgeId,
                distanceAlongEdge = projection.distanceAlongEdge,
                distanceFromPlayer = Mathf.Sqrt(Mathf.Max(0f, projection.distanceSquared)),
                direction = projection.direction
            };
            if (best == null || candidate.distanceFromPlayer < best.distanceFromPlayer ||
                candidate.distanceFromPlayer == best.distanceFromPlayer && string.CompareOrdinal(candidate.edgeId, best.edgeId) < 0)
                best = candidate;
        }
        return best;
    }

    bool ScanForward(string edgeId, float distanceAlongEdge, float distanceFromPlayer, Vector2 travelDirection,
        float maximumDistance, RoadPathQuery.SearchBudget budget) {
        var queue = new List<ScanState> { new ScanState { edgeId = edgeId, distanceFromPlayer = distanceFromPlayer, entryDistance = distanceAlongEdge } };
        var visited = new HashSet<string>();
        for (int cursor = 0; cursor < queue.Count && candidates.Count < settings.maxCandidates; cursor++) {
            if (!budget.TryConsume(1)) return false;
            var state = queue[cursor];
            if (!visited.Add(state.edgeId)) continue;
            var edge = graph.GetEdge(state.edgeId);
            if (edge == null) continue;
            if (!budget.TryConsume(1)) return false;
            float edgeLength = graph.GetArcLength(edge.edgeId);
            float remaining = Mathf.Max(0f, edgeLength - state.entryDistance);
            float junctionDistance = state.distanceFromPlayer + remaining;
            if (FiniteNonnegative(junctionDistance) && junctionDistance <= maximumDistance && !string.IsNullOrEmpty(edge.endJunctionId)) {
                var junction = graph.GetJunction(edge.endJunctionId);
                if (junction != null && HasForwardTransition(junction, edge.edgeId, edge.toNodeId, travelDirection, budget)) {
                    candidates.Add(new Candidate { edgeId = edge.edgeId, distanceAlongEdge = edgeLength, distanceFromPlayer = junctionDistance });
                    // A side approach reaches the same junction through a different incoming edge.
                    // The final route query, not this prediction, proves police reachability and width.
                    foreach (var transition in junction.allowedTransitions) {
                        if (!budget.TryConsume(1)) return false;
                        if (candidates.Count >= settings.maxCandidates) break;
                        if (transition == null || transition.fromEdgeId == edge.edgeId) continue;
                        var alternative = graph.GetEdge(transition.fromEdgeId);
                        if (alternative == null || alternative.toNodeId != edge.toNodeId) continue;
                        bool duplicate = false;
                        foreach (var existing in candidates) {
                            if (!budget.TryConsume(1)) return false;
                            if (existing.edgeId == alternative.edgeId) { duplicate = true; break; }
                        }
                        if (!duplicate && HasForwardTransition(junction, alternative.edgeId, edge.toNodeId, travelDirection, budget))
                            candidates.Add(new Candidate { edgeId = alternative.edgeId,
                                distanceAlongEdge = graph.GetArcLength(alternative.edgeId), distanceFromPlayer = junctionDistance });
                    }
                }
            }
            if (junctionDistance > maximumDistance) continue;
            foreach (var outgoing in graph.GetOutgoingEdges(edge.toNodeId)) {
                if (!budget.TryConsume(1) || outgoing == null) return false;
                var junctionForOutgoing = graph.GetJunction(edge.endJunctionId);
                bool allowed = false;
                if (junctionForOutgoing != null && junctionForOutgoing.allowedTransitions != null) {
                    foreach (var transition in junctionForOutgoing.allowedTransitions) {
                        if (!budget.TryConsume(1)) return false;
                        if (transition != null && transition.fromEdgeId == edge.edgeId && transition.toEdgeId == outgoing.edgeId) {
                            allowed = true;
                            break;
                        }
                    }
                }
                if (!allowed) continue;
                float outgoingLength = graph.GetArcLength(outgoing.edgeId);
                if (!FinitePositive(outgoingLength)) continue;
                queue.Add(new ScanState { edgeId = outgoing.edgeId, distanceFromPlayer = junctionDistance, entryDistance = 0f });
            }
        }
        return true;
    }

    bool HasForwardTransition(JunctionRecord junction, string incomingEdgeId, string nodeId, Vector2 travelDirection,
        RoadPathQuery.SearchBudget budget) {
        if (junction == null || junction.allowedTransitions == null) return false;
        foreach (var transition in junction.allowedTransitions) {
            if (!budget.TryConsume(1)) return false;
            if (transition == null || transition.fromEdgeId != incomingEdgeId) continue;
            var outgoing = graph.GetEdge(transition.toEdgeId);
            if (outgoing == null || outgoing.fromNodeId != nodeId) continue;
            var geometry = graph.GetGeometry(outgoing.edgeId);
            if (geometry == null || geometry.Points.Count < 2) continue;
            Vector2 direction = (geometry.Points[1] - geometry.Points[0]).normalized;
            if (Vector2.Dot(travelDirection, direction) > 0f || Vector2.Dot(direction, travelDirection) >= 0f) return true;
        }
        return false;
    }

    struct ScanState {
        public string edgeId;
        public float distanceFromPlayer;
        public float entryDistance;
    }

    static int CompareCandidates(Candidate first, Candidate second) {
        int distance = first.distanceFromPlayer.CompareTo(second.distanceFromPlayer);
        return distance != 0 ? distance : string.CompareOrdinal(first.edgeId, second.edgeId);
    }

    static int StableIndex(int lifeId, int count) {
        long value = lifeId;
        if (value < 0L) value = -value;
        return (int)(value % count);
    }

    bool ValidateInput(Vector2 position, Vector2 velocity, RoadPathQuery.SearchBudget budget, out float speed) {
        speed = velocity.magnitude;
        return graph != null && navigation != null && settings != null && settings.IsValid(out _) && budget != null &&
            budget.maximumWork > 0 && Finite(position) && Inside(position, navigation.localBounds) && Finite(velocity) &&
            FinitePositive(speed) && speed >= settings.minimumSpeed && FinitePositive(planningDistance);
    }

    static bool Inside(Vector2 point, Rect bounds) => point.x >= bounds.xMin && point.x <= bounds.xMax && point.y >= bounds.yMin && point.y <= bounds.yMax;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
