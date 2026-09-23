using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporal cache around <see cref="PoliceRoadTargetQuery"/> for one police unit. The planner
/// owns cadence and session state only: it never moves a vehicle or reads a clock. Intercept
/// prediction is opt-in and uses the same caller-supplied cadence and finite query budget.
/// </summary>
public sealed class PolicePursuitTargetPlanner {
    readonly RoadGraphRuntime graph;
    readonly MapNavigationDocument navigation;
    readonly Rect localBounds;
    readonly PoliceNavigationSettings settings;
    readonly NpcVehicleProfile boundProfile;
    readonly Vector2 boundColliderSize;
    readonly float boundTurningRadius;
    readonly bool boundAllowsPolice;
    readonly Vector2 localColliderOffset;
    readonly IAreaClearanceQuery staticClearance;
    readonly PoliceInterceptPlanner interceptPlanner;
    readonly bool interceptEnabled;
    readonly PoliceTacticalRole tacticalRole;
    readonly int boundLifeId;
    readonly float planningDistance;
    readonly List<RoadPathQuery.EdgeAnchor> interceptAnchors = new List<RoadPathQuery.EdgeAnchor>();
    long boundGraphVersion;
    PoliceRoadTargetQuery.Result currentResult;
    Vector2 lastAttemptedTarget;
    float lastAttemptSeconds;
    float lastObservedSeconds;
    bool hasAttempt;
    bool hasObservedTime;
    bool interceptHandoff;
    bool hasRetainedIntercept;
    RoadPathQuery.EdgeAnchor retainedInterceptAnchor;
    long retainedInterceptGraphVersion;
    int retainedInterceptPoliceLifeId;
    int retainedInterceptTargetLifeId;
    float retainedInterceptSeconds;
    Vector2 retainedInterceptVelocity;
    int attemptCount;
    int lastWorkConsumed;
    bool finalRefreshFailed;

    /// <summary>Number of expensive stateless target-query attempts made in this session.</summary>
    public int AttemptCount => attemptCount;

    /// <summary>Work charged by the most recent attempt; zero when no attempt has run.</summary>
    public int LastWorkConsumed => lastWorkConsumed;

    /// <summary>Most recent result, or a safe-wait result before the first valid attempt.</summary>
    public PoliceRoadTargetQuery.Result CurrentResult {
        get {
            RefreshBindingState();
            return currentResult;
        }
    }

    /// <summary>
    /// Binds one planner to a graph, detached map document, copied bounds, settings snapshot and
    /// profile facts. Profile and settings objects remain caller-owned; later mutations cannot
    /// silently alter this session's constraints.
    /// </summary>
    /// <param name="graph">Directed runtime graph that owns the route version.</param>
    /// <param name="navigation">Detached navigation document used by the stateless query.</param>
    /// <param name="localBounds">Map-local bounds copied for this planner session.</param>
    /// <param name="profile">Police vehicle profile bound for this session.</param>
    /// <param name="localColliderOffset">Profile-local collider pivot offset.</param>
    /// <param name="staticClearance">Optional static clearance adapter for displaced connectors.</param>
    /// <param name="settings">Authorable settings copied into a private snapshot.</param>
    public PolicePursuitTargetPlanner(RoadGraphRuntime graph, MapNavigationDocument navigation, Rect localBounds,
        NpcVehicleProfile profile, Vector2 localColliderOffset, IAreaClearanceQuery staticClearance,
        PoliceNavigationSettings settings, PoliceInterceptSettings interceptSettings = null,
        PoliceTacticalRole tacticalRole = PoliceTacticalRole.Pursue, int lifeId = 0, float planningDistance = 0f) {
        this.graph = graph;
        this.navigation = navigation == null ? null : new MapNavigationDocument { localBounds = localBounds };
        this.localBounds = localBounds;
        this.settings = settings == null ? null : settings.Clone();
        boundProfile = profile;
        boundColliderSize = profile == null ? Vector2.zero : profile.colliderSize;
        boundTurningRadius = profile == null || profile.motorSettings == null ? float.NaN : profile.motorSettings.minimumTurningRadius;
        boundAllowsPolice = profile != null && profile.allowedRoles != null && profile.allowedRoles.Contains(VehicleRole.Police);
        this.localColliderOffset = localColliderOffset;
        this.staticClearance = staticClearance;
        this.tacticalRole = tacticalRole;
        boundLifeId = lifeId;
        this.planningDistance = planningDistance > 0f ? planningDistance : (settings == null ? 0f : settings.projectionSearchRadius);
        interceptEnabled = tacticalRole == PoliceTacticalRole.Intercept && interceptSettings != null && interceptSettings.IsValid(out _);
        interceptPlanner = interceptEnabled ? new PoliceInterceptPlanner(graph, navigation, interceptSettings, this.planningDistance) : null;
        boundGraphVersion = graph == null ? 0L : graph.Version;
        currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.InvalidInput);
    }

    /// <summary>
    /// Evaluates a map-local police pose and target at caller-supplied session time. An attempt is
    /// made only after the hard minimum and one refresh/displacement/invalid-route condition hold.
    /// Invalid time or changed bound profile fails closed without returning a stale route.
    /// </summary>
    /// <param name="policePosition">Current map-local police position.</param>
    /// <param name="policeDirection">Current map-local travel direction.</param>
    /// <param name="targetPosition">Current map-local player target position.</param>
    /// <param name="activeSeconds">Monotonic session seconds supplied by the caller.</param>
    /// <param name="hasKnownAnchor">Whether the caller has a directed current edge anchor.</param>
    /// <param name="knownAnchor">Directed current edge anchor when supplied.</param>
    /// <returns>The cached result or the fresh result from this call.</returns>
    public PoliceRoadTargetQuery.Result Update(Vector2 policePosition, Vector2 policeDirection, Vector2 targetPosition,
        float activeSeconds, bool hasKnownAnchor, RoadPathQuery.EdgeAnchor knownAnchor) {
        return Update(policePosition, policeDirection, targetPosition, Vector2.zero, boundLifeId, activeSeconds, hasKnownAnchor, knownAnchor);
    }

    /// <summary>
    /// Evaluates one cadence attempt with actual target velocity for Intercept; Pursue callers retain
    /// the original overload and never enter prediction code.
    /// </summary>
    public PoliceRoadTargetQuery.Result Update(Vector2 policePosition, Vector2 policeDirection, Vector2 targetPosition,
        Vector2 targetVelocity, int targetLifeId, float activeSeconds, bool hasKnownAnchor, RoadPathQuery.EdgeAnchor knownAnchor) {
        return Update(policePosition, policeDirection, targetPosition, targetVelocity, Vector2.zero, targetLifeId,
            activeSeconds, hasKnownAnchor, knownAnchor);
    }

    /// <summary>
    /// Evaluates Intercept using the target's measured forward axis for reverse detection; prediction
    /// remains derived from the target's actual Rigidbody velocity.
    /// </summary>
    /// <param name="targetForward">Measured target body forward axis, or zero for legacy callers without that observation.</param>
    public PoliceRoadTargetQuery.Result Update(Vector2 policePosition, Vector2 policeDirection, Vector2 targetPosition,
        Vector2 targetVelocity, Vector2 targetForward, int targetLifeId, float activeSeconds, bool hasKnownAnchor,
        RoadPathQuery.EdgeAnchor knownAnchor) {
        if (!Finite(activeSeconds) || activeSeconds < 0f || hasObservedTime && activeSeconds < lastObservedSeconds) return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);
        lastObservedSeconds = activeSeconds;
        hasObservedTime = true;

        RefreshBindingState();
        if (!InputsAreCheaplyValid(policePosition, policeDirection, targetPosition, hasKnownAnchor, knownAnchor))
            return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);
        if (!ProfileStillBound()) return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);
        if (!settingsValid()) return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);

        if (interceptEnabled && !Finite(targetVelocity)) {
            return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);
        }
        if (interceptEnabled && !Finite(targetForward)) return FailClosed(PoliceRoadTargetQuery.WaitReason.InvalidInput);
        bool reverseTravel = targetVelocity.sqrMagnitude > 0f && targetForward.sqrMagnitude > 0f &&
            Vector2.Dot(targetVelocity.normalized, targetForward.normalized) < 0f;
        if (hasAttempt && !Eligible(activeSeconds, targetPosition)) return currentResult;
        if (interceptHandoff && HasPassedIntercept(targetPosition)) {
            interceptHandoff = false;
            hasRetainedIntercept = false;
        }
        lastAttemptSeconds = activeSeconds;
        lastAttemptedTarget = targetPosition;
        hasAttempt = true;
        attemptCount++;
        var budget = new RoadPathQuery.SearchBudget(settings.workBudget);
        if (interceptEnabled && !interceptHandoff && !reverseTravel) {
            interceptAnchors.Clear();
            if (RetainedInterceptIsCurrent(targetLifeId, targetVelocity, activeSeconds) && !HasPassedIntercept(targetPosition) &&
                TryIntercept(retainedInterceptAnchor, policePosition, policeDirection, targetPosition,
                    hasKnownAnchor, knownAnchor, budget)) return currentResult;
            if (budget.IsExhausted) return BudgetWait(budget);
            bool collected = interceptPlanner.TryCollectCandidates(targetPosition, targetVelocity, boundLifeId, budget, interceptAnchors);
            if (!collected && budget.IsExhausted) return BudgetWait(budget);
            for (int candidateIndex = 0; candidateIndex < interceptAnchors.Count; candidateIndex++) {
                if (TryIntercept(interceptAnchors[candidateIndex], policePosition, policeDirection, targetPosition,
                    hasKnownAnchor, knownAnchor, budget)) {
                    retainedInterceptAnchor = interceptAnchors[candidateIndex];
                    retainedInterceptGraphVersion = graph.Version;
                    retainedInterceptPoliceLifeId = boundLifeId;
                    retainedInterceptTargetLifeId = targetLifeId;
                    retainedInterceptSeconds = activeSeconds;
                    retainedInterceptVelocity = targetVelocity;
                    hasRetainedIntercept = true;
                    lastWorkConsumed = budget.ConsumedWork;
                    return currentResult;
                }
                if (budget.IsExhausted) return BudgetWait(budget);
            }
            hasRetainedIntercept = false;
        }
        var input = CreateInput(policePosition, policeDirection, targetPosition, hasKnownAnchor, knownAnchor, budget);
        PoliceRoadTargetQuery.TryQuery(input, out currentResult);
        lastWorkConsumed = budget.ConsumedWork;
        return currentResult;
    }

    /// <summary>Clears the cached route immediately while preserving the last-attempt timestamp.</summary>
    public void InvalidateRoute() {
        currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.NoReachableCandidate);
        finalRefreshFailed = false;
    }

    /// <summary>
    /// Replans only the future final path from a measured off-road pose on the same hard cadence.
    /// Failure brakes until another eligible attempt; it never asks for a connector back to the
    /// completed road anchor or changes the controller's original maneuver time/travel bounds.
    /// </summary>
    public bool TryRefreshFinalApproach(Vector2 position, Vector2 forward, Vector2 targetPosition, float seconds,
        float remainingTravel, PoliceFinalApproachGeometry.Plan current, out PoliceFinalApproachGeometry.Plan replacement,
        out bool attempted) {
        replacement = current; attempted = false;
        if (!Finite(seconds) || seconds < 0f || hasObservedTime && seconds < lastObservedSeconds) return false;
        lastObservedSeconds = seconds; hasObservedTime = true;
        if (graph.Version != boundGraphVersion || !ProfileStillBound() || !settingsValid() ||
            !InputsAreCheaplyValid(position, forward, targetPosition, false, default)) return false;
        if (hasAttempt && !Eligible(seconds, targetPosition)) return !finalRefreshFailed;
        attempted = true; hasAttempt = true; attemptCount++;
        lastAttemptSeconds = seconds; lastAttemptedTarget = targetPosition; lastWorkConsumed = 0;
        if (current.target.x == targetPosition.x && current.target.y == targetPosition.y && !finalRefreshFailed) return true;
        int work = settings.workBudget;
        bool clear = PoliceFinalApproachGeometry.TrySolve(position, forward, targetPosition,
            PoliceRoadTargetQuery.ApproachRadius(boundProfile, settings), Mathf.Min(remainingTravel, settings.projectionSearchRadius), out replacement) &&
            PoliceFinalApproachGeometry.IsRemainingClear(replacement, 0f, boundColliderSize + Vector2.one * settings.clearanceMargin,
                localColliderOffset, localBounds, staticClearance, ref work);
        lastWorkConsumed = settings.workBudget - work;
        finalRefreshFailed = !clear;
        return clear;
    }

    /// <summary>Requests the next eligible Intercept update to hand back to the actual player route.</summary>
    public void RequestPursueHandoff() {
        if (!interceptEnabled) return;
        interceptHandoff = true;
    }

    /// <summary>Clears target-life-specific Intercept state while retaining the planner cadence.</summary>
    /// <param name="targetLifeId">New player life identity supplied by the bound controller.</param>
    public void ResetForTargetLife(int targetLifeId) {
        retainedInterceptTargetLifeId = targetLifeId;
        hasRetainedIntercept = false;
        retainedInterceptAnchor = default;
        retainedInterceptSeconds = 0f;
        retainedInterceptVelocity = Vector2.zero;
        interceptHandoff = false;
        InvalidateRoute();
    }

    /// <summary>
    /// Starts a new session timeline and clears result and diagnostics. The shared graph index and
    /// its version remain untouched so other planners can continue using it.
    /// </summary>
    public void Reset() {
        boundGraphVersion = graph == null ? 0L : graph.Version;
        currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.NoReachableCandidate);
        lastAttemptedTarget = Vector2.zero;
        lastAttemptSeconds = 0f;
        lastObservedSeconds = 0f;
        hasAttempt = false;
        hasObservedTime = false;
        interceptHandoff = false;
        hasRetainedIntercept = false;
        retainedInterceptAnchor = default;
        retainedInterceptGraphVersion = boundGraphVersion;
        retainedInterceptPoliceLifeId = boundLifeId;
        retainedInterceptTargetLifeId = 0;
        retainedInterceptSeconds = 0f;
        retainedInterceptVelocity = Vector2.zero;
        attemptCount = 0;
        lastWorkConsumed = 0;
    }

    bool Eligible(float activeSeconds, Vector2 targetPosition) {
        float elapsed = activeSeconds - lastAttemptSeconds;
        if (elapsed < settings.hardMinimumInterval) return false;
        bool moved = (targetPosition - lastAttemptedTarget).sqrMagnitude >= settings.targetDisplacementThreshold * settings.targetDisplacementThreshold;
        bool refresh = elapsed >= settings.refreshInterval;
        bool invalid = currentResult == null || currentResult.status != PoliceRoadTargetQuery.Status.Route;
        return refresh || moved || invalid;
    }

    bool ProfileStillBound() {
        if (boundProfile == null || boundProfile.colliderSize.x != boundColliderSize.x || boundProfile.colliderSize.y != boundColliderSize.y || boundProfile.motorSettings == null ||
            boundProfile.motorSettings.minimumTurningRadius != boundTurningRadius) return false;
        bool allowsPolice = boundProfile.allowedRoles != null && boundProfile.allowedRoles.Contains(VehicleRole.Police);
        return allowsPolice == boundAllowsPolice && boundAllowsPolice;
    }

    bool settingsValid() => settings != null && settings.IsValid(out _);

    PoliceRoadTargetQuery.Input CreateInput(Vector2 policePosition, Vector2 policeDirection, Vector2 targetPosition,
        bool hasKnownAnchor, RoadPathQuery.EdgeAnchor knownAnchor, RoadPathQuery.SearchBudget budget) => new PoliceRoadTargetQuery.Input {
            graph = graph, navigation = navigation, profile = boundProfile,
            policePosition = policePosition, policeDirection = policeDirection, targetPosition = targetPosition,
            localColliderOffset = localColliderOffset, hasKnownAnchor = hasKnownAnchor, knownAnchor = knownAnchor,
            staticClearance = staticClearance, settings = settings, budget = budget,
            targetKind = PoliceRoadTargetQuery.TargetKind.ActualPlayer
        };

    void RefreshBindingState() {
        long graphVersion = graph == null ? 0L : graph.Version;
        if (graphVersion != boundGraphVersion) {
            boundGraphVersion = graphVersion;
            currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.NoReachableCandidate);
            hasRetainedIntercept = false;
            interceptHandoff = false;
        }
        if (!ProfileStillBound()) currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.InvalidInput);
    }

    bool TryIntercept(RoadPathQuery.EdgeAnchor choice, Vector2 policePosition, Vector2 policeDirection,
        Vector2 targetPosition, bool hasKnownAnchor, RoadPathQuery.EdgeAnchor knownAnchor, RoadPathQuery.SearchBudget budget) {
        if (!budget.TryConsume(1)) return false;
        var input = CreateInput(policePosition, policeDirection, targetPosition, hasKnownAnchor, knownAnchor, budget);
        input.targetKind = PoliceRoadTargetQuery.TargetKind.InterceptJunction;
        input.hasExactTargetAnchor = true; input.exactTargetAnchor = choice;
        bool found = PoliceRoadTargetQuery.TryQuery(input, out currentResult);
        lastWorkConsumed = budget.ConsumedWork;
        return found;
    }

    bool HasPassedIntercept(Vector2 targetPosition) {
        if (!hasRetainedIntercept) return false;
        var geometry = graph.GetGeometry(retainedInterceptAnchor.edgeId);
        return geometry != null && geometry.Points.Count > 0 &&
            Vector2.Dot(targetPosition - geometry.Points[geometry.Points.Count - 1], retainedInterceptVelocity) > 0f;
    }

    bool InputsAreCheaplyValid(Vector2 policePosition, Vector2 policeDirection, Vector2 targetPosition,
        bool hasKnownAnchor, RoadPathQuery.EdgeAnchor knownAnchor) {
        if (navigation == null || graph == null || !IsValidRect(localBounds) || !Inside(policePosition, localBounds) ||
            !Inside(targetPosition, localBounds) || !Finite(policePosition) || !Finite(targetPosition) ||
            !Finite(policeDirection) || policeDirection.sqrMagnitude <= 0f) return false;
        if (!hasKnownAnchor) return true;
        if (string.IsNullOrWhiteSpace(knownAnchor.edgeId) || !Finite(knownAnchor.distanceAlongEdge) || knownAnchor.distanceAlongEdge < 0f)
            return false;
        float edgeLength = graph.GetArcLength(knownAnchor.edgeId);
        return edgeLength > 0f && knownAnchor.distanceAlongEdge <= edgeLength;
    }

    PoliceRoadTargetQuery.Result FailClosed(PoliceRoadTargetQuery.WaitReason reason) {
        currentResult = MakeSafeWait(reason);
        return currentResult;
    }

    PoliceRoadTargetQuery.Result BudgetWait(RoadPathQuery.SearchBudget budget) {
        currentResult = MakeSafeWait(PoliceRoadTargetQuery.WaitReason.BudgetExceeded);
        lastWorkConsumed = budget.ConsumedWork;
        return currentResult;
    }

    bool RetainedInterceptIsCurrent(int targetLifeId, Vector2 targetVelocity, float activeSeconds) {
        if (!hasRetainedIntercept || retainedInterceptGraphVersion != graph.Version ||
            retainedInterceptPoliceLifeId != boundLifeId || retainedInterceptTargetLifeId != targetLifeId ||
            !Finite(targetVelocity) || targetVelocity.magnitude < interceptPlanner.MinimumSpeed ||
            activeSeconds < retainedInterceptSeconds || activeSeconds - retainedInterceptSeconds >
            interceptPlanner.PredictionSeconds) return false;
        return Vector2.Dot(targetVelocity.normalized, retainedInterceptVelocity.normalized) >= 0f;
    }

    PoliceRoadTargetQuery.Result MakeSafeWait(PoliceRoadTargetQuery.WaitReason reason) => new PoliceRoadTargetQuery.Result {
        status = PoliceRoadTargetQuery.Status.SafeWait, waitReason = reason, graphVersion = boundGraphVersion,
        spans = new List<RoadPathQuery.PathSpan>()
    };

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool IsValidRect(Rect value) => Finite(value.x) && Finite(value.y) && Finite(value.width) && Finite(value.height) &&
        Finite(value.xMax) && Finite(value.yMax) && value.width > 0f && value.height > 0f;
    static bool Inside(Vector2 point, Rect bounds) => point.x >= bounds.xMin && point.x <= bounds.xMax && point.y >= bounds.yMin && point.y <= bounds.yMax;
}
