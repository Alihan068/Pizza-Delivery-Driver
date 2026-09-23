using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Stateless bounded composition of projection, directed reachability, and clearance.</summary>
public static class PoliceRoadTargetQuery {
    /// <summary>Identifies whether a route result serves the live player or an authored intercept anchor.</summary>
    public enum TargetKind {
        /// <summary>Route ends at the current player's projected or actual target point.</summary>
        ActualPlayer,
        /// <summary>Route ends at an exact incoming-edge endpoint at an authored junction.</summary>
        InterceptJunction
    }

    /// <summary>Terminal state of one stateless target query.</summary>
    public enum Status {
        /// <summary>A complete target route and optional approach were proven.</summary>
        Route,
        /// <summary>The query must wait without exposing a partial route.</summary>
        SafeWait,
        /// <summary>Input or graph data was invalid.</summary>
        InvalidInput,
        /// <summary>The shared finite work budget was exhausted.</summary>
        BudgetExceeded
    }

    /// <summary>Safe-wait reason exposed for later cadence/controller layers.</summary>
    public enum WaitReason {
        /// <summary>No wait reason applies.</summary>
        None,
        /// <summary>No candidate in the resolved distance groups was reachable.</summary>
        NoReachableCandidate,
        /// <summary>A required static clearance query was not supplied.</summary>
        MissingStaticQuery,
        /// <summary>A supplied connector or approach was blocked.</summary>
        BlockedConnector,
        /// <summary>Input or graph data was invalid.</summary>
        InvalidInput,
        /// <summary>The shared finite work budget was exhausted.</summary>
        BudgetExceeded
    }

    /// <summary>Explicit input bundle for one map-local query.</summary>
    public sealed class Input {
        /// <summary>Indexed directed road graph.</summary>
        public RoadGraphRuntime graph;
        /// <summary>Navigation document supplying bounds and spawn exclusions.</summary>
        public MapNavigationDocument navigation;
        /// <summary>Validated vehicle profile constraints.</summary>
        public NpcVehicleProfile profile;
        /// <summary>Police map-local pivot position.</summary>
        public Vector2 policePosition;
        /// <summary>Police map-local travel direction.</summary>
        public Vector2 policeDirection;
        /// <summary>Player map-local target position.</summary>
        public Vector2 targetPosition;
        /// <summary>Profile-local collider pivot offset in map-local vehicle orientation.</summary>
        public Vector2 localColliderOffset;
        /// <summary>Whether the caller supplied a directed current anchor.</summary>
        public bool hasKnownAnchor;
        /// <summary>Caller-owned directed current anchor.</summary>
        public RoadPathQuery.EdgeAnchor knownAnchor;
        /// <summary>Static geometry adapter; required for displaced connectors.</summary>
        public IAreaClearanceQuery staticClearance;
        /// <summary>Validated immutable settings snapshot.</summary>
        public PoliceNavigationSettings settings;
        /// <summary>Aggregate caller-owned finite budget.</summary>
        public RoadPathQuery.SearchBudget budget;
        /// <summary>Target interpretation; omitted callers retain the actual-player behavior.</summary>
        public TargetKind targetKind = TargetKind.ActualPlayer;
        /// <summary>Whether <see cref="exactTargetAnchor"/> is supplied for Intercept.</summary>
        public bool hasExactTargetAnchor;
        /// <summary>Exact incoming-edge endpoint required by an Intercept query.</summary>
        public RoadPathQuery.EdgeAnchor exactTargetAnchor;
    }

    /// <summary>Complete route/target result with no partial route on failure.</summary>
    public sealed class Result {
        /// <summary>Query terminal status.</summary>
        public Status status;
        /// <summary>Detailed wait reason.</summary>
        public WaitReason waitReason;
        /// <summary>Selected graph version.</summary>
        public long graphVersion;
        /// <summary>Selected road target point.</summary>
        public Vector2 roadTarget;
        /// <summary>Ordered directed route spans.</summary>
        public IReadOnlyList<RoadPathQuery.PathSpan> spans;
        /// <summary>Selected directed start anchor.</summary>
        public RoadPathQuery.EdgeAnchor startAnchor;
        /// <summary>Target interpretation used to produce this route.</summary>
        public TargetKind targetKind;
        /// <summary>Selected start-road point.</summary>
        public Vector2 startRoadPoint;
        /// <summary>True when a displaced connector must be driven first.</summary>
        public bool requiresConnector;
        /// <summary>True when an optional static-clear final approach was proven.</summary>
        public bool hasFinalApproach;
        /// <summary>Final approach start point.</summary>
        public Vector2 finalApproachStart;
        /// <summary>Final approach end point.</summary>
        public Vector2 finalApproachEnd;
        /// <summary>Whether the final approach is an arc followed by a tangent connector.</summary>
        public bool finalApproachCurved;
        /// <summary>Immutable curved final-approach geometry when <see cref="finalApproachCurved"/> is true.</summary>
        public PoliceFinalApproachGeometry.Plan finalApproachPlan;
    }

    sealed class CandidatePath {
        public RoadSegmentSpatialIndex.ProjectionCandidate target;
        public RoadPathQuery.AnchoredPathResult path;
    }

    /// <summary>Executes one bounded target query; no temporal state or movement is retained.</summary>
    public static bool TryQuery(Input input, out Result result) {
        result = NewResult(Status.InvalidInput, WaitReason.InvalidInput);
        if (!ValidateInput(input, out string reason)) return false;
        if (!RoadSegmentSpatialIndex.TryGetOrBuild(input.graph, input.settings.indexCellSize, input.budget, out var index, out var buildStatus)) {
            result = NewResult(buildStatus == RoadSegmentSpatialIndex.BuildStatus.BudgetExceeded ? Status.BudgetExceeded : Status.SafeWait,
                buildStatus == RoadSegmentSpatialIndex.BuildStatus.BudgetExceeded ? WaitReason.BudgetExceeded : WaitReason.NoReachableCandidate);
            return false;
        }
        if (input.graph.Version != index.GraphVersion) return SetInvalidFailure(out result);

        if (!TryResolveStart(input, index, out var startAnchor, out Vector2 startPoint, out Vector2 startDirection, out bool requiresConnector, out WaitReason startReason)) {
            result = NewResult(startReason == WaitReason.BudgetExceeded ? Status.BudgetExceeded : Status.SafeWait, startReason);
            return false;
        }

        if (input.targetKind == TargetKind.InterceptJunction) {
            if (!input.hasExactTargetAnchor || !TryQueryExactAnchor(input, startAnchor, startPoint, requiresConnector, out result))
                return input.budget.IsExhausted ? SetBudgetFailure(out result) : SetSafeWait(out result, WaitReason.NoReachableCandidate);
            return true;
        }
        if (input.targetKind != TargetKind.ActualPlayer) return SetInvalidFailure(out result);

        var targetCandidates = new List<RoadSegmentSpatialIndex.ProjectionCandidate>();
        var projections = index.Query(input.targetPosition, input.settings.projectionSearchRadius, input.budget);
        if (projections.status == RoadSegmentSpatialIndex.QueryStatus.BudgetExceeded) return SetBudgetFailure(out result);
        if (projections.status == RoadSegmentSpatialIndex.QueryStatus.InvalidInput || projections.status == RoadSegmentSpatialIndex.QueryStatus.StaleGraph) return SetInvalidFailure(out result);
        if (projections.status == RoadSegmentSpatialIndex.QueryStatus.Found) {
            targetCandidates.AddRange(projections.candidates);
        } else if (projections.status == RoadSegmentSpatialIndex.QueryStatus.NoCandidates) {
            projections = index.QueryAll(input.targetPosition, input.budget);
            if (projections.status == RoadSegmentSpatialIndex.QueryStatus.BudgetExceeded) return SetBudgetFailure(out result);
            if (projections.status == RoadSegmentSpatialIndex.QueryStatus.InvalidInput || projections.status == RoadSegmentSpatialIndex.QueryStatus.StaleGraph)
                return SetInvalidFailure(out result);
            if (projections.status == RoadSegmentSpatialIndex.QueryStatus.Found) targetCandidates.AddRange(projections.candidates);
        }
        foreach (var candidate in targetCandidates) if (!Inside(candidate.point, input.navigation.localBounds)) return SetInvalidFailure(out result);
        if (!AddUpstreamCandidates(input, targetCandidates)) return SetBudgetFailure(out result);
        if (!AddCurrentSuffixCandidate(input, startAnchor, startPoint, targetCandidates)) return SetBudgetFailure(out result);
        targetCandidates = DeduplicateCandidates(targetCandidates);
        if (targetCandidates.Count == 0) {
            result = NewResult(Status.SafeWait, WaitReason.NoReachableCandidate);
            return false;
        }

        var groups = GroupCandidates(targetCandidates);
        Result roadFallback = null;
        foreach (var group in groups) {
            var successful = new List<CandidatePath>();
            foreach (var candidate in group) {
                var constraints = new RouteTransitionFilter.VehicleConstraints(input.profile.colliderSize.x, input.settings.clearanceMargin,
                    input.profile.motorSettings.minimumTurningRadius, input.settings.maxTurnAngle);
                if (!RoadPathQuery.TryFindAnchoredPath(input.graph, startAnchor,
                    new RoadPathQuery.EdgeAnchor(candidate.edgeId, candidate.distanceAlongEdge),
                    VehicleRole.Police, constraints, input.budget, out var path)) {
                    if (path.status == RoadPathQuery.QueryStatus.BudgetExceeded) return SetBudgetFailure(out result);
                    continue;
                }
                successful.Add(new CandidatePath { target = candidate, path = path });
            }
            if (successful.Count == 0) continue;
            successful.Sort(ComparePaths);
            foreach (var chosen in successful) {
                var candidateResult = NewResult(Status.Route, WaitReason.None);
                candidateResult.graphVersion = input.graph.Version;
                candidateResult.targetKind = TargetKind.ActualPlayer;
                candidateResult.roadTarget = chosen.target.point;
                candidateResult.spans = chosen.path.spans;
                candidateResult.startAnchor = startAnchor;
                candidateResult.startRoadPoint = startPoint;
                candidateResult.requiresConnector = requiresConnector;
                if (!TryAddFinalApproach(input, chosen.target.point, chosen.target.direction, ref candidateResult)) return SetBudgetFailure(out result);
                if (candidateResult.hasFinalApproach) { result = candidateResult; return true; }
                roadFallback ??= candidateResult;
            }
            // Keep a road-only fallback, but continue searching farther projection groups for a
            // proven final approach. The nearest road projection is not automatically the safest
            // terminal candidate.
        }
        if (roadFallback != null) { result = roadFallback; return true; }
        result = NewResult(Status.SafeWait, WaitReason.NoReachableCandidate);
        return false;
    }

    static bool TryQueryExactAnchor(Input input, RoadPathQuery.EdgeAnchor startAnchor, Vector2 startPoint,
        bool requiresConnector, out Result result) {
        result = NewResult(Status.InvalidInput, WaitReason.InvalidInput);
        float targetLength = input.graph.GetArcLength(input.exactTargetAnchor.edgeId);
        if (!FinitePositive(targetLength) || input.exactTargetAnchor.distanceAlongEdge != targetLength ||
            !RoadAnchorGeometry.TryGetPoint(input.graph, input.exactTargetAnchor.edgeId, targetLength, input.budget,
                out Vector2 targetPoint, out _)) return false;
        var constraints = new RouteTransitionFilter.VehicleConstraints(input.profile.colliderSize.x, input.settings.clearanceMargin,
            input.profile.motorSettings.minimumTurningRadius, input.settings.maxTurnAngle);
        if (!RoadPathQuery.TryFindAnchoredPath(input.graph, startAnchor, input.exactTargetAnchor, VehicleRole.Police,
            constraints, input.budget, out var path)) {
            result = NewResult(path.status == RoadPathQuery.QueryStatus.BudgetExceeded ? Status.BudgetExceeded : Status.SafeWait,
                path.status == RoadPathQuery.QueryStatus.BudgetExceeded ? WaitReason.BudgetExceeded : WaitReason.NoReachableCandidate);
            return false;
        }
        result = NewResult(Status.Route, WaitReason.None);
        result.graphVersion = input.graph.Version;
        result.targetKind = TargetKind.InterceptJunction;
        result.roadTarget = targetPoint;
        result.spans = path.spans;
        result.startAnchor = startAnchor;
        result.startRoadPoint = startPoint;
        result.requiresConnector = requiresConnector;
        return true;
    }

    static bool TryResolveStart(Input input, RoadSegmentSpatialIndex index, out RoadPathQuery.EdgeAnchor anchor,
        out Vector2 point, out Vector2 direction, out bool requiresConnector, out WaitReason reason) {
        anchor = default; point = Vector2.zero; direction = Vector2.zero; requiresConnector = false; reason = WaitReason.NoReachableCandidate;
        if (input.hasKnownAnchor) {
            anchor = input.knownAnchor;
            if (!RoadAnchorGeometry.TryGetPoint(input.graph, anchor.edgeId, anchor.distanceAlongEdge, input.budget, out point, out direction)) {
                reason = input.budget.IsExhausted ? WaitReason.BudgetExceeded : WaitReason.NoReachableCandidate;
                return false;
            }
            float displacement = Vector2.Distance(input.policePosition, point);
            if (!Finite(displacement)) { reason = WaitReason.InvalidInput; return false; }
            if (!Inside(point, input.navigation.localBounds)) { reason = WaitReason.InvalidInput; return false; }
            if (displacement > input.settings.onRoadTolerance) {
                if (input.staticClearance == null) { reason = WaitReason.BlockedConnector; return false; }
                if (!input.budget.TryConsume(1)) { reason = WaitReason.BudgetExceeded; return false; }
                if (!RoadFootprintClearance.IsSweepClear(input.policePosition, point, PaddedFootprint(input.profile, input.settings), input.localColliderOffset,
                    Heading(input.policeDirection), Heading(direction), input.navigation.localBounds, null, input.staticClearance)) {
                    reason = WaitReason.BlockedConnector; return false;
                }
                requiresConnector = true;
            }
            return true;
        }
        var candidates = index.Query(input.policePosition, input.settings.projectionSearchRadius, input.budget);
        if (candidates.status == RoadSegmentSpatialIndex.QueryStatus.BudgetExceeded) { reason = WaitReason.BudgetExceeded; return false; }
        if (candidates.status == RoadSegmentSpatialIndex.QueryStatus.StaleGraph) { reason = WaitReason.InvalidInput; return false; }
        if (candidates.status != RoadSegmentSpatialIndex.QueryStatus.Found) return false;
        float bestDistanceSquared = float.PositiveInfinity;
        float bestAlignment = float.NegativeInfinity;
        string bestEdgeId = null;
        int bestSegmentIndex = int.MaxValue;
        foreach (var candidate in candidates.candidates) {
            if (!Inside(candidate.point, input.navigation.localBounds)) { reason = WaitReason.InvalidInput; return false; }
            float alignment = Vector2.Dot(input.policeDirection.normalized, candidate.direction);
            if (!Finite(alignment) || alignment < 0f) continue;
            if (candidate.distanceSquared > bestDistanceSquared) break;
            if (!input.budget.TryConsume(1)) { reason = WaitReason.BudgetExceeded; return false; }
            bool clear = candidate.distanceSquared <= input.settings.onRoadTolerance * input.settings.onRoadTolerance ||
                input.staticClearance != null && RoadFootprintClearance.IsSweepClear(input.policePosition, candidate.point,
                    PaddedFootprint(input.profile, input.settings), input.localColliderOffset, Heading(input.policeDirection), Heading(candidate.direction),
                    input.navigation.localBounds, null, input.staticClearance);
            if (!clear) continue;
            if (candidate.distanceSquared < bestDistanceSquared ||
                candidate.distanceSquared == bestDistanceSquared &&
                (alignment > bestAlignment || alignment == bestAlignment &&
                (string.CompareOrdinal(candidate.edgeId, bestEdgeId) < 0 ||
                string.Equals(candidate.edgeId, bestEdgeId, StringComparison.Ordinal) && candidate.segmentIndex < bestSegmentIndex))) {
                bestDistanceSquared = candidate.distanceSquared;
                bestAlignment = alignment;
                bestEdgeId = candidate.edgeId;
                bestSegmentIndex = candidate.segmentIndex;
                anchor = new RoadPathQuery.EdgeAnchor(candidate.edgeId, candidate.distanceAlongEdge);
                point = candidate.point;
                direction = candidate.direction;
                requiresConnector = candidate.distanceSquared > input.settings.onRoadTolerance * input.settings.onRoadTolerance;
            }
        }
        if (string.IsNullOrWhiteSpace(anchor.edgeId)) { reason = input.staticClearance == null ? WaitReason.MissingStaticQuery : WaitReason.BlockedConnector; return false; }
        return true;
    }

    static bool AddCurrentSuffixCandidate(Input input, RoadPathQuery.EdgeAnchor anchor, Vector2 point,
        List<RoadSegmentSpatialIndex.ProjectionCandidate> candidates) {
        if (!input.budget.TryConsume(1)) return false;
        float distanceSquared = (input.targetPosition - point).sqrMagnitude;
        if (!Finite(distanceSquared) || distanceSquared > input.settings.projectionSearchRadius * input.settings.projectionSearchRadius) return true;
        if (!RoadAnchorGeometry.TryGetPoint(input.graph, anchor.edgeId, anchor.distanceAlongEdge, input.budget, out _, out var direction)) return false;
        candidates.Add(new RoadSegmentSpatialIndex.ProjectionCandidate(anchor.edgeId, -1, anchor.distanceAlongEdge, point, direction, distanceSquared));
        return true;
    }

    static bool AddUpstreamCandidates(Input input, List<RoadSegmentSpatialIndex.ProjectionCandidate> candidates) {
        int originalCount = candidates.Count;
        float radius = ApproachRadius(input.profile, input.settings);
        for (int i = 0; i < originalCount; i++) {
            var candidate = candidates[i];
            Vector2 offset = input.targetPosition - candidate.point;
            if (Mathf.Abs(candidate.direction.x * offset.y - candidate.direction.y * offset.x) <= input.settings.onRoadTolerance) continue;
            var geometry = input.graph.GetGeometry(candidate.edgeId);
            if (geometry == null || candidate.segmentIndex < 0) continue;
            float segmentStart = geometry.CumulativeLengths[candidate.segmentIndex];
            // Both exits remain inside this existing directed segment. Reachability/width/radius
            // are still certified by the normal anchored path query below.
            for (int step = 1; step <= 2; step++) {
                if (!input.budget.TryConsume(1)) return false;
                float distance = candidate.distanceAlongEdge - radius * step;
                if (distance < segmentStart) continue;
                Vector2 point = candidate.point - candidate.direction * (radius * step);
                float squared = (point - input.targetPosition).sqrMagnitude;
                if (squared > input.settings.projectionSearchRadius * input.settings.projectionSearchRadius) continue;
                candidates.Add(new RoadSegmentSpatialIndex.ProjectionCandidate(candidate.edgeId, candidate.segmentIndex,
                    distance, point, candidate.direction, squared));
            }
        }
        return true;
    }

    /// <summary>Retains the authored minimum radius and adds local footprint room for discrete tracking correction.</summary>
    public static float ApproachRadius(NpcVehicleProfile profile, PoliceNavigationSettings settings) =>
        profile.motorSettings.minimumTurningRadius + PaddedFootprint(profile, settings).magnitude * 0.5f;

    static List<RoadSegmentSpatialIndex.ProjectionCandidate> DeduplicateCandidates(List<RoadSegmentSpatialIndex.ProjectionCandidate> source) {
        var result = new List<RoadSegmentSpatialIndex.ProjectionCandidate>();
        var seen = new HashSet<string>();
        foreach (var candidate in source) if (seen.Add(candidate.edgeId + "\n" + candidate.distanceAlongEdge.ToString("R"))) result.Add(candidate);
        result.Sort((a, b) => a.distanceSquared != b.distanceSquared ? a.distanceSquared.CompareTo(b.distanceSquared) : string.CompareOrdinal(a.edgeId, b.edgeId));
        return result;
    }

    static List<List<RoadSegmentSpatialIndex.ProjectionCandidate>> GroupCandidates(List<RoadSegmentSpatialIndex.ProjectionCandidate> candidates) {
        var groups = new List<List<RoadSegmentSpatialIndex.ProjectionCandidate>>();
        foreach (var candidate in candidates) {
            if (groups.Count == 0 || groups[groups.Count - 1][0].distanceSquared != candidate.distanceSquared) groups.Add(new List<RoadSegmentSpatialIndex.ProjectionCandidate>());
            groups[groups.Count - 1].Add(candidate);
        }
        return groups;
    }

    static int ComparePaths(CandidatePath first, CandidatePath second) {
        int distance = first.path.totalDistance.CompareTo(second.path.totalDistance);
        return distance != 0 ? distance : string.CompareOrdinal(first.target.edgeId, second.target.edgeId);
    }

    static bool TryAddFinalApproach(Input input, Vector2 roadTarget, Vector2 startDirection, ref Result result) {
        if (Vector2.Distance(input.targetPosition, roadTarget) <= input.settings.onRoadTolerance) return true;
        if (input.staticClearance == null) return true;
        float radius = ApproachRadius(input.profile, input.settings);
        if (!PoliceFinalApproachGeometry.TrySolve(roadTarget, startDirection, input.targetPosition,
            radius, input.settings.projectionSearchRadius, out var plan)) return true;
        // A lateral target must use an upstream exit, not a U-turn past its road projection.
        if (plan.kind == PoliceFinalApproachGeometry.Kind.ArcThenStraight && plan.arcLength / plan.radius > Mathf.PI * 0.5f) return true;
        Vector2 padded = PaddedFootprint(input.profile, input.settings);
        int work = input.budget.RemainingWork;
        int before = work;
        bool clear = PoliceFinalApproachGeometry.IsRemainingClear(plan, 0f, padded, input.localColliderOffset,
            input.navigation.localBounds, input.staticClearance, ref work);
        if (!input.budget.TryConsume(before - work) || !clear && work <= 0) return false;
        if (!clear) return true;
        result.hasFinalApproach = true;
        result.finalApproachStart = roadTarget;
        result.finalApproachEnd = input.targetPosition;
        result.finalApproachCurved = plan.kind == PoliceFinalApproachGeometry.Kind.ArcThenStraight;
        result.finalApproachPlan = plan;
        return true;
    }

    static Vector2 PaddedFootprint(NpcVehicleProfile profile, PoliceNavigationSettings settings) => profile.colliderSize + Vector2.one * settings.clearanceMargin;
    static float Heading(Vector2 direction) => MapNavigationCoordinates.DirectionToHeadingDegrees(direction.normalized);
    static bool ValidateInput(Input input, out string reason) {
        reason = null;
        Vector2 paddedFootprint = input == null || input.profile == null || input.settings == null ? Vector2.zero : PaddedFootprint(input.profile, input.settings);
        if (input == null || input.graph == null || input.navigation == null || input.profile == null || input.settings == null || input.budget == null ||
            !input.settings.IsValid(out reason) || input.profile.allowedRoles == null || !input.profile.allowedRoles.Contains(VehicleRole.Police) ||
            input.profile.motorSettings == null || !Finite(input.policePosition) || !Finite(input.targetPosition) || !Finite(input.policeDirection) ||
            input.policeDirection.sqrMagnitude <= 0f || !IsValidRect(input.navigation.localBounds) || !Inside(input.policePosition, input.navigation.localBounds) ||
            !Inside(input.targetPosition, input.navigation.localBounds) || !Finite(input.localColliderOffset) || !FinitePositive(input.profile.colliderSize.x) ||
            !FinitePositive(input.profile.colliderSize.y) || !FinitePositive(paddedFootprint.x) || !FinitePositive(paddedFootprint.y) ||
            input.budget.maximumWork <= 0 || !FiniteNonnegative(input.profile.motorSettings.minimumTurningRadius)) {
            reason = reason ?? "invalid police target query input";
            return false;
        }
        return true;
    }

    static Result NewResult(Status status, WaitReason reason) => new Result { status = status, waitReason = reason, targetKind = TargetKind.ActualPlayer, spans = new List<RoadPathQuery.PathSpan>() };
    static bool SetSafeWait(out Result result, WaitReason reason) { result = NewResult(Status.SafeWait, reason); return false; }
    static bool SetBudgetFailure(out Result result) { result = NewResult(Status.BudgetExceeded, WaitReason.BudgetExceeded); return false; }
    static bool SetInvalidFailure(out Result result) { result = NewResult(Status.InvalidInput, WaitReason.InvalidInput); return false; }
    static bool IsValidRect(Rect value) => Finite(value.position) && Finite(value.size) && Finite(value.xMax) && Finite(value.yMax) && value.width > 0f && value.height > 0f;
    static bool Inside(Vector2 point, Rect bounds) => point.x >= bounds.xMin && point.x <= bounds.xMax && point.y >= bounds.yMin && point.y <= bounds.yMax;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
