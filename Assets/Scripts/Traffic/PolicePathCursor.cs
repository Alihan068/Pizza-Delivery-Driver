using System.Collections.Generic;
using UnityEngine;

/// <summary>Open-span police route cursor using bind-time flattened geometry and measured progress.</summary>
public sealed class PolicePathCursor {
    /// <summary>Outcome of a bounded upcoming-turn query.</summary>
    public enum TurnQueryStatus {
        /// <summary>A direction change was found within the requested distance.</summary>
        Found,
        /// <summary>The bounded route portion contains no direction change.</summary>
        None,
        /// <summary>The cursor or maximum distance is invalid.</summary>
        InvalidInput,
        /// <summary>The shared work allowance ended before the query completed.</summary>
        BudgetExceeded,
        /// <summary>The graph changed after binding.</summary>
        StaleGraph
    }

    /// <summary>Outcome of a bounded single-bend corridor certification.</summary>
    public enum CorridorQueryStatus {
        /// <summary>The inspected route is straight through the clamped lookahead.</summary>
        Straight,
        /// <summary>Exactly one direction change and its requested exit corridor were certified.</summary>
        SingleBend,
        /// <summary>A second non-collinear bend lies inside the certified corridor.</summary>
        UnsupportedMultipleBends,
        /// <summary>The selected bend does not have the requested outgoing route length.</summary>
        InsufficientLength,
        /// <summary>A distance or latch argument is invalid.</summary>
        InvalidInput,
        /// <summary>The graph changed after binding.</summary>
        StaleGraph,
        /// <summary>The shared work allowance ended before certification completed.</summary>
        BudgetExceeded
    }

    /// <summary>Value result for a bounded straight or single-bend corridor query.</summary>
    public readonly struct SingleBendCorridor {
        /// <summary>Stable flattened segment token, or -1 for a straight corridor.</summary>
        public readonly int token;
        /// <summary>Absolute route arc of the selected corner.</summary>
        public readonly float cornerArc;
        /// <summary>Actual polyline vertex at the selected corner.</summary>
        public readonly Vector2 vertex;
        /// <summary>Incoming unit direction at the selected corner.</summary>
        public readonly Vector2 incomingDirection;
        /// <summary>Outgoing unit direction at the selected corner.</summary>
        public readonly Vector2 outgoingDirection;
        /// <summary>Clamped lookahead distance from the current cursor position.</summary>
        public readonly float clampedLookahead;
        /// <summary>Point at the requested outgoing exit distance, or the straight lookahead point.</summary>
        public readonly Vector2 exitPoint;
        /// <summary>Point at the clamped lookahead arc from the current cursor position.</summary>
        public readonly Vector2 lookaheadPoint;

        /// <summary>Creates a fully populated corridor certification result.</summary>
        /// <param name="token">Stable flattened segment token, or -1 for a straight corridor.</param>
        /// <param name="cornerArc">Absolute route arc of the selected corner.</param>
        /// <param name="vertex">Actual polyline vertex at the selected corner.</param>
        /// <param name="incomingDirection">Incoming unit direction at the selected corner.</param>
        /// <param name="outgoingDirection">Outgoing unit direction at the selected corner.</param>
        /// <param name="clampedLookahead">Clamped lookahead distance from current progress.</param>
        /// <param name="exitPoint">Point at the requested outgoing exit distance.</param>
        /// <param name="lookaheadPoint">Point at the clamped lookahead arc.</param>
        public SingleBendCorridor(int token, float cornerArc, Vector2 vertex, Vector2 incomingDirection,
            Vector2 outgoingDirection, float clampedLookahead, Vector2 exitPoint, Vector2 lookaheadPoint) {
            this.token = token;
            this.cornerArc = cornerArc;
            this.vertex = vertex;
            this.incomingDirection = incomingDirection;
            this.outgoingDirection = outgoingDirection;
            this.clampedLookahead = clampedLookahead;
            this.exitPoint = exitPoint;
            this.lookaheadPoint = lookaheadPoint;
        }
    }

    /// <summary>Finite bounds for projection, deviation, displacement slack and acquisition.</summary>
    public sealed class TrackingLimits {
        /// <summary>Maximum forward route distance inspected by one measured-position projection.</summary>
        public readonly float projectionWindow;
        /// <summary>Maximum accepted distance from the route.</summary>
        public readonly float maxDeviation;
        /// <summary>Measured displacement multiplier used for forward-jump protection.</summary>
        public readonly float authoredDisplacementSlack;
        /// <summary>Small allowance added after nonzero measured displacement.</summary>
        public readonly float acquisitionTolerance;

        /// <summary>Creates caller-owned tracking limits.</summary>
        /// <param name="projectionWindow">Positive maximum forward projection distance.</param>
        /// <param name="maxDeviation">Nonnegative accepted distance from the clipped route.</param>
        /// <param name="authoredDisplacementSlack">Positive arc-to-measured-displacement allowance.</param>
        /// <param name="acquisitionTolerance">Nonnegative allowance applied only after movement.</param>
        public TrackingLimits(float projectionWindow, float maxDeviation, float authoredDisplacementSlack, float acquisitionTolerance) {
            this.projectionWindow = projectionWindow;
            this.maxDeviation = maxDeviation;
            this.authoredDisplacementSlack = authoredDisplacementSlack;
            this.acquisitionTolerance = acquisitionTolerance;
        }

        /// <summary>Validates all limits without mutating them.</summary>
        /// <param name="reason">Validation failure, or null for usable limits.</param>
        /// <returns>True when values and the squared deviation are representable.</returns>
        public bool IsValid(out string reason) {
            reason = null;
            if (!FinitePositive(projectionWindow) || !FiniteNonnegative(maxDeviation) || !Finite(maxDeviation * maxDeviation) ||
                !FinitePositive(authoredDisplacementSlack) || !FiniteNonnegative(acquisitionTolerance)) {
                reason = "invalid police path cursor tracking limits";
                return false;
            }
            return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool FinitePositive(float value) => Finite(value) && value > 0f;
        static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
    }

    struct Segment {
        public string edgeId;
        public int spanOccurrence;
        public float edgeStart;
        public float edgeEnd;
        public float routeStart;
        public float routeEnd;
        public Vector2 start;
        public Vector2 end;
        public Vector2 direction;
    }

    RoadGraphRuntime graph;
    long graphVersion;
    TrackingLimits limits;
    Segment[] segments;
    RoadPathQuery.PathSpan[] boundSpans;
    Vector2 firstPositiveDirection;
    Vector2 lastPositiveDirection;
    bool hasPositiveDirections;
    RoadPathQuery.EdgeAnchor finalAnchor;
    Vector2 zeroRoutePoint;
    float totalDistance;
    float progressDistance;
    int currentSegment;
    Vector2 lastObservedPosition;
    bool bound;
    bool complete;

    /// <summary>True when the route is bound and its graph version remains current.</summary>
    public bool IsBound => bound && graph != null && graph.Version == graphVersion;

    /// <summary>Current directed edge anchor, or an empty anchor when unbound.</summary>
    public RoadPathQuery.EdgeAnchor CurrentAnchor {
        get {
            if (!IsBound) return default;
            if (complete) return finalAnchor;
            var segment = segments[currentSegment];
            float route = Mathf.Clamp(progressDistance, segment.routeStart, segment.routeEnd);
            float t = (route - segment.routeStart) / (segment.routeEnd - segment.routeStart);
            return new RoadPathQuery.EdgeAnchor(segment.edgeId, Mathf.Lerp(segment.edgeStart, segment.edgeEnd, t));
        }
    }

    /// <summary>Directed distance remaining to the exact final clipped endpoint.</summary>
    public float RemainingDistance => IsBound ? Mathf.Max(0f, totalDistance - progressDistance) : 0f;

    /// <summary>Directed distance consumed from the flattened route.</summary>
    public float ProgressDistance => IsBound ? progressDistance : 0f;

    /// <summary>True only when progress reaches the exact final route distance.</summary>
    public bool IsComplete => IsBound && complete;

    /// <summary>Compares a candidate route with the exact remaining bound-span suffix.
    /// The current directed anchor is compared first, followed by every ordered span occurrence,
    /// including the final clipped endpoint. Each candidate and expected span visit consumes one
    /// work unit; failure never advances route progress and stale graphs invalidate the binding.</summary>
    /// <param name="candidate">Ordered route spans beginning at the measured current anchor.</param>
    /// <param name="workRemaining">Finite caller-owned comparison budget.</param>
    /// <returns>True only when the complete remaining suffix matches this binding exactly.</returns>
    public bool MatchesRemainingSpans(IReadOnlyList<RoadPathQuery.PathSpan> candidate, ref int workRemaining) {
        if (!bound || graph == null || candidate == null || candidate.Count == 0 || workRemaining < 0) return false;
        if (graph.Version != graphVersion) {
            Reset();
            return false;
        }

        int expectedCount = complete ? 1 : 1 + boundSpans.Length - segments[currentSegment].spanOccurrence - 1;
        if (candidate.Count != expectedCount) return false;
        for (int index = 0; index < expectedCount; index++) {
            if (workRemaining < 2) return false;
            workRemaining--;
            workRemaining--;
            RoadPathQuery.PathSpan expected = index == 0
                ? complete
                    ? new RoadPathQuery.PathSpan(finalAnchor.edgeId, finalAnchor.distanceAlongEdge, finalAnchor.distanceAlongEdge)
                    : new RoadPathQuery.PathSpan(CurrentAnchor.edgeId, CurrentAnchor.distanceAlongEdge,
                        boundSpans[segments[currentSegment].spanOccurrence].endDistance)
                : boundSpans[segments[currentSegment].spanOccurrence + index];
            if (!SameSpan(candidate[index], expected)) return false;
        }
        return true;
    }

    /// <summary>Returns the cached positive tangent at both route endpoints.
    /// A route containing no positive-length segment has no tangent and returns false.</summary>
    /// <param name="firstDirection">Unit direction of the first positive route segment.</param>
    /// <param name="lastDirection">Unit direction of the last positive route segment.</param>
    /// <returns>True when both endpoint directions are available on the current binding.</returns>
    public bool TryGetEndpointTangents(out Vector2 firstDirection, out Vector2 lastDirection) {
        firstDirection = Vector2.zero;
        lastDirection = Vector2.zero;
        if (!IsBound || !hasPositiveDirections) return false;
        firstDirection = firstPositiveDirection;
        lastDirection = lastPositiveDirection;
        return true;
    }

    /// <summary>Certifies a bounded straight or single-bend corridor without changing cursor progress.
    /// A latch keeps one previously selected turn valid after its vertex while rejecting skipped turns,
    /// hidden second bends, insufficient exit length, stale bindings and exhausted work.</summary>
    /// <param name="lookaheadDistance">Nonnegative distance from the current progress to inspect.</param>
    /// <param name="exitDistance">Nonnegative outgoing distance required after the selected bend.</param>
    /// <param name="latchedTurnIndex">Existing turn token, or -1 to select the first bend.</param>
    /// <param name="workRemaining">Finite caller-owned segment and sample budget.</param>
    /// <param name="corridor">Certified value result, or default on failure.</param>
    /// <returns>The bounded certification status.</returns>
    public CorridorQueryStatus TryGetSingleBendCorridor(float lookaheadDistance, float exitDistance,
        int latchedTurnIndex, ref int workRemaining, out SingleBendCorridor corridor) {
        corridor = default;
        if (!bound || graph == null || !Finite(lookaheadDistance) || lookaheadDistance < 0f ||
            !Finite(exitDistance) || exitDistance < 0f || latchedTurnIndex < -1 || workRemaining < 0) {
            return CorridorQueryStatus.InvalidInput;
        }
        if (graph.Version != graphVersion) {
            Reset();
            return CorridorQueryStatus.StaleGraph;
        }

        float clampedLookahead = Mathf.Min(lookaheadDistance, RemainingDistance);
        float lookaheadArc = progressDistance + clampedLookahead;
        int selectedToken = latchedTurnIndex;
        if (segments.Length == 0) {
            if (selectedToken >= 0) return CorridorQueryStatus.InvalidInput;
            if (!ConsumeCorridorWork(ref workRemaining)) return CorridorQueryStatus.BudgetExceeded;
            corridor = new SingleBendCorridor(-1, 0f, Vector2.zero, Vector2.zero, Vector2.zero,
                clampedLookahead, zeroRoutePoint, zeroRoutePoint);
            return CorridorQueryStatus.Straight;
        }
        if (selectedToken >= segments.Length - 1) return CorridorQueryStatus.InvalidInput;

        if (selectedToken < 0) {
            for (int i = currentSegment; i < segments.Length - 1; i++) {
                if (segments[i].routeEnd > lookaheadArc) break;
                if (!ConsumeCorridorWork(ref workRemaining)) return CorridorQueryStatus.BudgetExceeded;
                if (IsCorridorBend(i)) {
                    selectedToken = i;
                    break;
                }
            }
            if (selectedToken < 0) {
                if (!TryPointAtRoute(lookaheadArc, currentSegment, ref workRemaining, out Vector2 straightPoint)) {
                    return CorridorQueryStatus.BudgetExceeded;
                }
                corridor = new SingleBendCorridor(-1, 0f, Vector2.zero, Vector2.zero, Vector2.zero,
                    clampedLookahead, straightPoint, straightPoint);
                return CorridorQueryStatus.Straight;
            }
        } else {
            if (!ConsumeCorridorWork(ref workRemaining)) return CorridorQueryStatus.BudgetExceeded;
            if (!IsCorridorBend(selectedToken)) return CorridorQueryStatus.InvalidInput;
            for (int i = currentSegment; i < selectedToken; i++) {
                if (!ConsumeCorridorWork(ref workRemaining)) return CorridorQueryStatus.BudgetExceeded;
                if (IsCorridorBend(i)) return CorridorQueryStatus.InvalidInput;
            }
        }

        Segment incoming = segments[selectedToken];
        Segment outgoing = segments[selectedToken + 1];
        float exitArc = incoming.routeEnd + exitDistance;
        if (!Finite(exitArc) || exitArc > totalDistance) return CorridorQueryStatus.InsufficientLength;
        float inspectionEnd = Mathf.Max(lookaheadArc, exitArc);
        for (int i = selectedToken + 1; i < segments.Length - 1; i++) {
            if (segments[i].routeEnd > inspectionEnd) break;
            if (!ConsumeCorridorWork(ref workRemaining)) return CorridorQueryStatus.BudgetExceeded;
            if (IsCorridorBend(i)) return CorridorQueryStatus.UnsupportedMultipleBends;
        }
        if (!TryPointAtRoute(exitArc, selectedToken + 1, ref workRemaining, out Vector2 exitPoint) ||
            !TryPointAtRoute(lookaheadArc, currentSegment, ref workRemaining, out Vector2 lookaheadPoint)) {
            return CorridorQueryStatus.BudgetExceeded;
        }
        corridor = new SingleBendCorridor(selectedToken, incoming.routeEnd, incoming.end,
            incoming.direction, outgoing.direction, clampedLookahead, exitPoint, lookaheadPoint);
        return CorridorQueryStatus.SingleBend;
    }

    /// <summary>Flattens and validates clipped spans, charging every span and geometry vertex visit.</summary>
    /// <param name="graph">Directed graph owning geometry and authored node identity.</param>
    /// <param name="version">Expected graph version.</param>
    /// <param name="routeSpans">Ordered clipped spans, including repeated occurrences.</param>
    /// <param name="trackingLimits">Finite tracking limits.</param>
    /// <param name="budget">Existing finite bind budget.</param>
    /// <returns>True only when the complete route is bound.</returns>
    public bool TryBind(RoadGraphRuntime graph, long version, IReadOnlyList<RoadPathQuery.PathSpan> routeSpans,
        TrackingLimits trackingLimits, RoadPathQuery.SearchBudget budget) {
        Reset();
        if (graph == null || budget == null || budget.maximumWork <= 0 || version <= 0 || graph.Version != version || routeSpans == null ||
            routeSpans.Count == 0 || routeSpans.Count > budget.RemainingWork || trackingLimits == null || !trackingLimits.IsValid(out _)) return false;
        var geometryByEdge = new Dictionary<string, RoadGraphRuntime.EdgeGeometrySnapshot>();
        var ownedSpans = new RoadPathQuery.PathSpan[routeSpans.Count];
        var flattened = new List<Segment>();
        float route = 0f;
        RoadPathQuery.PathSpan previous = default;
        RoadGraphRuntime.EdgeGeometrySnapshot previousGeometry = null;
        for (int occurrence = 0; occurrence < routeSpans.Count; occurrence++) {
            if (!budget.TryConsume(1)) return false;
            var span = routeSpans[occurrence];
            ownedSpans[occurrence] = span;
            if (string.IsNullOrWhiteSpace(span.edgeId) || !Finite(span.startDistance) || !Finite(span.endDistance) ||
                span.startDistance < 0f || span.endDistance < span.startDistance) return false;
            if (!geometryByEdge.TryGetValue(span.edgeId, out var geometry)) {
                geometry = graph.GetGeometry(span.edgeId);
                if (geometry == null) return false;
                geometryByEdge.Add(span.edgeId, geometry);
            }
            if (geometry.Points == null || geometry.CumulativeLengths == null || geometry.Points.Count < 2 ||
                geometry.Points.Count != geometry.CumulativeLengths.Count || !Finite(geometry.Length) ||
                geometry.Length <= 0f || span.endDistance > geometry.Length) return false;
            for (int vertex = 0; vertex < geometry.Points.Count; vertex++) {
                if (!budget.TryConsume(1) || !Finite(geometry.Points[vertex]) || !Finite(geometry.CumulativeLengths[vertex])) return false;
                if (vertex > 0 && geometry.CumulativeLengths[vertex] < geometry.CumulativeLengths[vertex - 1]) return false;
                if (vertex > 0 && geometry.CumulativeLengths[vertex] >= span.endDistance) break;
            }
            if (occurrence > 0 && !Contiguous(graph, previous, previousGeometry, span, geometry)) return false;
            if (!Flatten(geometry, span, occurrence, ref route, flattened, budget)) return false;
            previous = span;
            previousGeometry = geometry;
        }
        this.graph = graph;
        graphVersion = version;
        limits = trackingLimits;
        segments = flattened.ToArray();
        boundSpans = ownedSpans;
        totalDistance = route;
        progressDistance = 0f;
        currentSegment = 0;
        complete = false;
        var finalSpan = routeSpans[routeSpans.Count - 1];
        finalAnchor = new RoadPathQuery.EdgeAnchor(finalSpan.edgeId, finalSpan.endDistance);
        if (segments.Length == 0) {
            if (!TrySampleGeometry(previousGeometry, finalSpan.endDistance, budget, out zeroRoutePoint)) {
                Reset();
                return false;
            }
            complete = true;
            lastObservedPosition = zeroRoutePoint;
            bound = true;
            return true;
        }
        firstPositiveDirection = segments[0].direction;
        lastPositiveDirection = segments[segments.Length - 1].direction;
        hasPositiveDirections = true;
        lastObservedPosition = segments[0].start;
        bound = true;
        return true;
    }

    /// <summary>Advances from a measured map-local position using shared work units.</summary>
    /// <param name="localPosition">Measured map-local body position.</param>
    /// <param name="workRemaining">Finite caller-owned segment-visit allowance.</param>
    /// <returns>True when accepted, including a stationary measurement. Failure never advances progress; a stale graph clears binding.</returns>
    public bool Advance(Vector2 localPosition, ref int workRemaining) {
        if (!bound || graph == null) return false;
        if (graph.Version != graphVersion) return Invalidate();
        if (!Finite(localPosition) || workRemaining < 0) return false;
        float displacement = Vector2.Distance(lastObservedPosition, localPosition);
        if (!Finite(displacement)) return false;
        if (displacement == 0f) return true;
        if (segments.Length == 0) {
            if (workRemaining < 1 || (localPosition - zeroRoutePoint).sqrMagnitude > limits.maxDeviation * limits.maxDeviation) return false;
            workRemaining--;
            lastObservedPosition = localPosition;
            return true;
        }
        float window = Mathf.Min(limits.projectionWindow, displacement * limits.authoredDisplacementSlack + limits.acquisitionTolerance);
        if (!Finite(window) || window <= 0f) return false;
        bool found = false;
        float bestDeviation = float.PositiveInfinity;
        float bestForward = float.PositiveInfinity;
        float bestRoute = progressDistance;
        int bestSegment = currentSegment;
        float ceiling = progressDistance + Mathf.Min(window, totalDistance - progressDistance);
        for (int i = currentSegment; i < segments.Length; i++) {
            if (segments[i].routeStart > ceiling) break;
            if (workRemaining < 1) return false;
            workRemaining--;
            if (TryProjection(segments[i], localPosition, progressDistance, ceiling, limits.maxDeviation * limits.maxDeviation,
                out float candidateRoute, out float deviation)) {
                float forward = candidateRoute - progressDistance;
                if (!found || deviation < bestDeviation || deviation == bestDeviation && forward < bestForward) {
                    found = true;
                    bestDeviation = deviation;
                    bestForward = forward;
                    bestRoute = candidateRoute;
                    bestSegment = i;
                }
            }
            if (segments[i].routeEnd >= ceiling) break;
        }
        if (!found) return false;
        currentSegment = bestSegment;
        progressDistance = bestRoute;
        complete = progressDistance == totalDistance;
        lastObservedPosition = localPosition;
        return true;
    }

    /// <summary>
    /// Advances one active committed corner from a measured rounded-corner position when the
    /// ordinary polyline projection cannot reach the outgoing segment in one bounded step.
    /// The handoff is limited to the latched token, the profile turning radius, monotonic route
    /// progress, and the outgoing release reserve; ordinary Advance keeps its normal deviation.
    /// </summary>
    /// <param name="localPosition">Measured map-local body position.</param>
    /// <param name="turnToken">Latched flattened incoming-segment token.</param>
    /// <param name="turnAbsoluteArc">Latched absolute route arc at the corner.</param>
    /// <param name="outgoingDirection">Latched outgoing unit tangent.</param>
    /// <param name="turningRadius">Bound police profile minimum turning radius.</param>
    /// <param name="activeTurnTravelLimit">Immutable measured-travel bound retained by the active turn.</param>
    /// <param name="workRemaining">Finite caller-owned work allowance.</param>
    /// <returns>True only when a bounded monotonic corner handoff was accepted.</returns>
    public bool TryAdvanceCommittedCorner(Vector2 localPosition, int turnToken, float turnAbsoluteArc,
        Vector2 outgoingDirection, float turningRadius, float activeTurnTravelLimit, ref int workRemaining) {
        if (!bound || graph == null || graph.Version != graphVersion || !Finite(localPosition) ||
            turnToken < currentSegment || turnToken < 0 || turnToken >= segments.Length - 1 ||
            !FiniteNonnegative(turnAbsoluteArc) || !FinitePositive(turningRadius) ||
            !FinitePositive(activeTurnTravelLimit) || !Finite(outgoingDirection) ||
            !FinitePositive(outgoingDirection.sqrMagnitude) || workRemaining < 1) return false;
        Segment incoming = segments[turnToken];
        Segment outgoing = segments[turnToken + 1];
        Vector2 tangent = outgoingDirection.normalized;
        if (!IsCorridorBend(turnToken) || incoming.routeEnd != turnAbsoluteArc ||
            !FinitePositive(tangent.sqrMagnitude) || Vector2.Angle(tangent, outgoing.direction) > 0.01f) return false;
        Vector2 fromCorner = localPosition - incoming.end;
        float alongOutgoing = Vector2.Dot(fromCorner, tangent);
        float lateral = Mathf.Abs(fromCorner.x * tangent.y - fromCorner.y * tangent.x);
        float cornerDistance = fromCorner.magnitude;
        float radiusLimit = turningRadius + limits.acquisitionTolerance;
        if (!FiniteNonnegative(alongOutgoing) || alongOutgoing <= 0f || !FiniteNonnegative(lateral) ||
            !FiniteNonnegative(cornerDistance) || lateral > radiusLimit || cornerDistance > radiusLimit ||
            alongOutgoing > activeTurnTravelLimit) return false;
        float candidateRoute = turnAbsoluteArc + alongOutgoing;
        if (!Finite(candidateRoute) || candidateRoute < progressDistance || candidateRoute > outgoing.routeEnd) return false;
        workRemaining--;
        currentSegment = turnToken + 1;
        progressDistance = candidateRoute;
        complete = progressDistance == totalDistance;
        lastObservedPosition = localPosition;
        return true;
    }

    /// <summary>Samples a forward point, clamped to the exact endpoint, using shared work units.</summary>
    /// <param name="distanceAhead">Nonnegative distance from the current route position.</param>
    /// <param name="workRemaining">Finite caller-owned segment-visit allowance.</param>
    /// <param name="point">Sampled point, or zero when sampling fails.</param>
    /// <returns>True when a point was sampled.</returns>
    public bool TrySampleAhead(float distanceAhead, ref int workRemaining, out Vector2 point) {
        point = Vector2.zero;
        if (!IsBound || !Finite(distanceAhead) || distanceAhead < 0f) return false;
        if (segments.Length == 0) {
            if (workRemaining < 1) return false;
            workRemaining--;
            point = zeroRoutePoint;
            return true;
        }
        float target = progressDistance + Mathf.Min(distanceAhead, totalDistance - progressDistance);
        for (int i = currentSegment; i < segments.Length; i++) {
            if (workRemaining < 1) return false;
            workRemaining--;
            if (target > segments[i].routeEnd && i < segments.Length - 1) continue;
            float route = Mathf.Clamp(target, segments[i].routeStart, segments[i].routeEnd);
            Vector2 sampled = PointAt(segments[i], route);
            if (!Finite(sampled)) return false;
            point = sampled;
            return true;
        }
        return false;
    }

    /// <summary>Finds the next interior bend or span seam within a caller-selected distance.</summary>
    /// <param name="maximumDistance">Finite nonnegative route distance to inspect.</param>
    /// <param name="workRemaining">Finite caller-owned segment-visit allowance.</param>
    /// <param name="turnPoint">Turn location, or zero when no result is returned.</param>
    /// <param name="incomingDirection">Direction before the turn, or zero on failure.</param>
    /// <param name="outgoingDirection">Direction after the turn, or zero on failure.</param>
    /// <param name="distanceAhead">Distance to the turn, or zero on failure.</param>
    /// <returns>Explicit outcome distinguishing no turn, stale graph and budget exhaustion.</returns>
    public TurnQueryStatus TryGetNextTurn(float maximumDistance, ref int workRemaining, out Vector2 turnPoint,
        out Vector2 incomingDirection, out Vector2 outgoingDirection, out float distanceAhead) {
        return TryGetNextTurn(maximumDistance, ref workRemaining, out turnPoint, out incomingDirection,
            out outgoingDirection, out distanceAhead, out _);
    }

    /// <summary>Finds the next turn and returns its stable flattened-segment occurrence token.
    /// The token is valid only for this cursor binding and remains stable while progress is tracked.</summary>
    /// <param name="maximumDistance">Finite nonnegative route distance to inspect.</param>
    /// <param name="workRemaining">Finite caller-owned segment-visit allowance.</param>
    /// <param name="turnPoint">Turn location, or zero when no result is returned.</param>
    /// <param name="incomingDirection">Direction before the turn, or zero on failure.</param>
    /// <param name="outgoingDirection">Direction after the turn, or zero on failure.</param>
    /// <param name="distanceAhead">Distance to the turn, or zero on failure.</param>
    /// <param name="turnToken">Flattened segment index identifying the turn occurrence, or -1 on failure.</param>
    /// <returns>Explicit outcome distinguishing no turn, stale graph and budget exhaustion.</returns>
    public TurnQueryStatus TryGetNextTurn(float maximumDistance, ref int workRemaining, out Vector2 turnPoint,
        out Vector2 incomingDirection, out Vector2 outgoingDirection, out float distanceAhead, out int turnToken) {
        turnPoint = Vector2.zero;
        incomingDirection = Vector2.zero;
        outgoingDirection = Vector2.zero;
        distanceAhead = 0f;
        turnToken = -1;
        if (!bound || graph == null) return TurnQueryStatus.InvalidInput;
        if (graph.Version != graphVersion) {
            Reset();
            return TurnQueryStatus.StaleGraph;
        }
        if (!Finite(maximumDistance) || maximumDistance < 0f || workRemaining < 0) return TurnQueryStatus.InvalidInput;
        for (int i = currentSegment; i < segments.Length - 1; i++) {
            float distance = segments[i].routeEnd - progressDistance;
            if (distance > maximumDistance) return TurnQueryStatus.None;
            if (workRemaining < 1) return TurnQueryStatus.BudgetExceeded;
            workRemaining--;
            if (Vector2.Dot(segments[i].direction, segments[i + 1].direction) < 0.9999f) {
                turnPoint = segments[i].end;
                incomingDirection = segments[i].direction;
                outgoingDirection = segments[i + 1].direction;
                distanceAhead = distance;
                turnToken = i;
                return TurnQueryStatus.Found;
            }
        }
        return TurnQueryStatus.None;
    }

    /// <summary>Clears route, cached geometry, progress and graph binding.</summary>
    public void Reset() {
        graph = null;
        graphVersion = 0L;
        limits = null;
        segments = null;
        boundSpans = null;
        firstPositiveDirection = Vector2.zero;
        lastPositiveDirection = Vector2.zero;
        hasPositiveDirections = false;
        finalAnchor = default;
        zeroRoutePoint = Vector2.zero;
        totalDistance = 0f;
        progressDistance = 0f;
        currentSegment = 0;
        lastObservedPosition = Vector2.zero;
        bound = false;
        complete = false;
    }

    static bool Flatten(RoadGraphRuntime.EdgeGeometrySnapshot geometry, RoadPathQuery.PathSpan span, int spanOccurrence, ref float route,
        List<Segment> result, RoadPathQuery.SearchBudget budget) {
        for (int i = 1; i < geometry.Points.Count; i++) {
            if (!budget.TryConsume(1)) return false;
            float edgeStart = Mathf.Max(geometry.CumulativeLengths[i - 1], span.startDistance);
            float edgeEnd = Mathf.Min(geometry.CumulativeLengths[i], span.endDistance);
            if (geometry.CumulativeLengths[i - 1] >= span.endDistance) break;
            if (edgeEnd <= edgeStart) continue;
            float fullLength = geometry.CumulativeLengths[i] - geometry.CumulativeLengths[i - 1];
            if (!FinitePositive(fullLength)) return false;
            Vector2 start = SampleEdgeSegment(geometry, i, edgeStart);
            Vector2 end = SampleEdgeSegment(geometry, i, edgeEnd);
            Vector2 direction = (end - start).normalized;
            if (!Finite(start) || !Finite(end) || !Finite(direction) || direction.sqrMagnitude <= 0f) return false;
            float nextRoute = route + (edgeEnd - edgeStart);
            if (!Finite(nextRoute) || nextRoute <= route) return false;
            result.Add(new Segment {
                edgeId = span.edgeId, spanOccurrence = spanOccurrence, edgeStart = edgeStart, edgeEnd = edgeEnd,
                routeStart = route, routeEnd = nextRoute, start = start, end = end, direction = direction
            });
            route = nextRoute;
        }
        return Finite(route);
    }

    static bool TrySampleGeometry(RoadGraphRuntime.EdgeGeometrySnapshot geometry, float distance,
        RoadPathQuery.SearchBudget budget, out Vector2 point) {
        point = Vector2.zero;
        for (int i = 1; i < geometry.Points.Count; i++) {
            if (!budget.TryConsume(1)) return false;
            float start = geometry.CumulativeLengths[i - 1];
            float end = geometry.CumulativeLengths[i];
            if (distance > end) continue;
            float length = end - start;
            if (!FinitePositive(length)) return false;
            point = SampleEdgeSegment(geometry, i, distance);
            return Finite(point);
        }
        return false;
    }

    static Vector2 SampleEdgeSegment(RoadGraphRuntime.EdgeGeometrySnapshot geometry, int endVertex, float distance) {
        float startDistance = geometry.CumulativeLengths[endVertex - 1];
        float endDistance = geometry.CumulativeLengths[endVertex];
        Vector2 start = geometry.Points[endVertex - 1];
        Vector2 end = geometry.Points[endVertex];
        if (distance <= startDistance) return start;
        if (distance >= endDistance) return end;
        // Distance / length followed by multiplication can shift a clipped endpoint by an ULP.
        // Keep full vertices exact and construct interior points from their physical arc offset.
        return start + (end - start).normalized * (distance - startDistance);
    }

    static bool Contiguous(RoadGraphRuntime graph, RoadPathQuery.PathSpan previous, RoadGraphRuntime.EdgeGeometrySnapshot previousGeometry,
        RoadPathQuery.PathSpan next, RoadGraphRuntime.EdgeGeometrySnapshot nextGeometry) {
        if (previous.edgeId == next.edgeId && previous.endDistance == next.startDistance) return true;
        var previousEdge = graph.GetEdge(previous.edgeId);
        var nextEdge = graph.GetEdge(next.edgeId);
        return previous.endDistance == previousGeometry.Length && next.startDistance == 0f && previousEdge != null && nextEdge != null &&
            previousEdge.toNodeId == nextEdge.fromNodeId && Finite(nextGeometry.Length);
    }

    static bool TryProjection(Segment segment, Vector2 position, float current, float ceiling, float maxDeviationSquared,
        out float route, out float deviation) {
        route = 0f;
        deviation = 0f;
        Vector2 delta = segment.end - segment.start;
        float denominator = delta.sqrMagnitude;
        if (!FinitePositive(denominator)) return false;
        float t = Mathf.Clamp01(Vector2.Dot(position - segment.start, delta) / denominator);
        float projected = t >= 1f ? segment.routeEnd :
            t <= 0f ? segment.routeStart : segment.routeStart + (segment.routeEnd - segment.routeStart) * t;
        float min = Mathf.Max(current, segment.routeStart);
        float max = Mathf.Min(segment.routeEnd, ceiling);
        if (max < min) return false;
        route = Mathf.Clamp(projected, min, max);
        Vector2 point = PointAt(segment, route);
        deviation = (position - point).sqrMagnitude;
        return Finite(point) && Finite(route) && Finite(deviation) && deviation <= maxDeviationSquared;
    }

    bool Invalidate() {
        Reset();
        return false;
    }

    static Vector2 PointAt(Segment segment, float route) {
        if (route <= segment.routeStart) return segment.start;
        if (route >= segment.routeEnd) return segment.end;
        float t = (route - segment.routeStart) / (segment.routeEnd - segment.routeStart);
        return Vector2.Lerp(segment.start, segment.end, t);
    }

    bool TryPointAtRoute(float route, int firstSegment, ref int workRemaining, out Vector2 point) {
        point = Vector2.zero;
        if (!Finite(route) || segments == null || segments.Length == 0) {
            if (segments != null && segments.Length == 0 && route == 0f) {
                point = zeroRoutePoint;
                return Finite(point);
            }
            return false;
        }
        int startSegment = Mathf.Clamp(firstSegment, 0, segments.Length - 1);
        for (int i = startSegment; i < segments.Length; i++) {
            if (!ConsumeCorridorWork(ref workRemaining)) return false;
            if (route > segments[i].routeEnd && i < segments.Length - 1) continue;
            point = PointAt(segments[i], Mathf.Clamp(route, segments[i].routeStart, segments[i].routeEnd));
            return Finite(point);
        }
        point = segments[segments.Length - 1].end;
        return Finite(point);
    }

    bool IsTurn(int segmentIndex) {
        return segmentIndex >= 0 && segmentIndex < segments.Length - 1 &&
            Vector2.Dot(segments[segmentIndex].direction, segments[segmentIndex + 1].direction) < 0.9999f;
    }

    static bool IsCorridorBend(Segment incoming, Segment outgoing) {
        float determinant = incoming.direction.x * outgoing.direction.y - incoming.direction.y * outgoing.direction.x;
        return determinant != 0f ||
            Vector2.Dot(incoming.direction, outgoing.direction) <= 0f;
    }

    bool IsCorridorBend(int segmentIndex) {
        return segmentIndex >= 0 && segmentIndex < segments.Length - 1 &&
            IsCorridorBend(segments[segmentIndex], segments[segmentIndex + 1]);
    }

    static bool ConsumeCorridorWork(ref int workRemaining) {
        if (workRemaining < 1) return false;
        workRemaining--;
        return true;
    }

    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;

    static bool SameSpan(RoadPathQuery.PathSpan actual, RoadPathQuery.PathSpan expected) {
        return actual.edgeId == expected.edgeId && actual.startDistance == expected.startDistance && actual.endDistance == expected.endDistance;
    }
}
