using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Free-drive policy facade for one live police life. It owns bounded planning intent and motor
/// command production while the population owner remains responsible for cadence and result adoption.
/// </summary>
internal sealed class PoliceFreeAgent : PolicePursuitController.IFreeDrive {
    const int RequestWorkLimit = 32768;
    const float EscapeCommitSeconds = 0.8f;

    readonly PoliceVehicleBody body;
    readonly TrafficDamageWorld world;
    readonly VehicleDamageReceiver target;
    readonly Rigidbody2D targetBody;
    readonly PoliceVehicleProfile vehicle;
    readonly PoliceBehaviorProfile behavior;
    readonly PolicePrefabGeometry geometry;
    readonly Vector2 origin;
    readonly PoliceFreeNavigationQuery query;
    readonly PoliceNavigationScheduler scheduler;
    readonly PoliceFreeChaseSettings chase;
    readonly PoliceNavigationSettings navigation;
    readonly PoliceDriverTraits traits;
    readonly PoliceTacticsCoordinator tactics;
    readonly float playerSurfaceRadius;
    readonly PoliceFreePathPlanner planner;
    readonly PoliceFreePursuitDriver driver;
    readonly PoliceFreeRecovery recovery;
    readonly PoliceRamDecision ramDecision = new PoliceRamDecision();
    readonly PoliceFreePursuitDriver.IAvoidanceQuery avoidance;
    readonly Collider2D targetCollider;
    readonly int lifeId;
    readonly int targetLifeId;
    readonly PoliceDrivingSettings driving;
    readonly NpcMotorSettings motor;
    PoliceTacticsCoordinator.ApproachClaim claim;
    PoliceTacticalRole role;
    Vector2 lastTargetPosition;
    Vector2 lastPosition;
    float nextPlanAt;
    float clock;
    int targetRevision;
    int latestRequestId;
    int appliedRequestId;
    int revisionRequestFloor = 1;
    float nextRoleAt;
    float nextDecisionAt;
    float reactionReadyAt;
    NpcDriveCommand lastCommand = NpcDriveCommand.Stopped;
    bool hasClaim;
    bool hasPosition;
    bool hasCommand;
    bool holding;
    bool interceptFallback;
    bool reset;
    bool strategicDirty = true;
    bool terminalBlocked;
    int observedTerminalId;
    float lastDecisionClock;
    bool emptyGoal;
    Vector2 acceptedGoal;
    readonly PoliceJamTracker jam = new PoliceJamTracker();
    PolicePinPressure pin;
    Vector2 lastJamPosition;
    float lastJamAt = float.NegativeInfinity;
    int consecutiveJams;
    float forceThroughSuppressedUntil = float.NegativeInfinity;
    readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[8];
    Vector2 escapeDirection;
    bool hasEscapeDirection;
    float escapeCommitUntil = float.NegativeInfinity;
    float nextEscapeCheckAt = float.NegativeInfinity;
    static readonly float[] EscapeProbeDistances = { 1f, 2f, 3.5f };
    const int EscapeCandidateCount = 16;
    float corridorBlockedSince = float.NegativeInfinity;
    bool corridorClear = true;
    float nextCorridorCheckAt = float.NegativeInfinity;
    float replanningSince = float.NegativeInfinity;
    IReadOnlyList<PoliceFreePathPlanner.Primitive> adoptedPath;
    readonly PoliceShadowTactics.Settings shadowSettings;
    PoliceShadowTactics.Goal shadowGoal;
    bool hasShadowGoal;
    float nextShadowGoalAt = float.NegativeInfinity;
    const float ShadowGoalInterval = 0.2f;
    const float ShadowOpeningRadius = 3f;
    const float ShadowOpeningClaimSeconds = 0.6f;
    const float ShadowStopDistance = 1.2f;
    const float ShadowPassClearance = 2.2f;

    /// <summary>Creates and registers one free-drive policy for an already-bound police life.</summary>
    internal PoliceFreeAgent(PoliceVehicleBody body, TrafficDamageWorld world, VehicleDamageReceiver target,
        Rigidbody2D targetBody, PoliceVehicleProfile vehicle, PoliceBehaviorProfile behavior,
        PolicePrefabGeometry geometry, Vector2 origin, PoliceFreeNavigationQuery query,
        PoliceNavigationScheduler scheduler, PoliceFreeChaseSettings chase,
        PoliceNavigationSettings navigation, PoliceDriverTraits traits,
        PoliceTacticsCoordinator tactics, float playerSurfaceRadius) {
        this.body = body;
        this.world = world;
        this.target = target;
        this.targetBody = targetBody;
        this.vehicle = vehicle;
        this.behavior = behavior;
        this.geometry = geometry;
        this.origin = origin;
        this.query = query;
        this.scheduler = scheduler;
        this.chase = chase != null ? chase.Clone() : new PoliceFreeChaseSettings();
        this.navigation = navigation != null ? navigation.Clone() : new PoliceNavigationSettings();
        this.traits = traits ?? PoliceDriverTraits.Reset();
        this.tactics = tactics;
        this.playerSurfaceRadius = playerSurfaceRadius;
        lifeId = body != null && body.DamageReceiver != null ? body.DamageReceiver.Identity.lifeId : 0;
        targetLifeId = target != null ? target.Identity.lifeId : 0;
        targetCollider = targetBody != null ? targetBody.GetComponent<Collider2D>() : null;
        driving = CreateDrivingSettings(behavior != null ? behavior.driving : null, this.chase);
        driving.stopGap *= this.traits.followGapMultiplier;
        driving.ramMaximumSurfaceGap *= 1f + this.traits.riskBias;
        driving.ramMaximumSurfaceGap = Mathf.Min(driving.ramMaximumSurfaceGap, driving.maxSweepDistance);
        motor = CreateMotorSettings(vehicle != null && vehicle.sharedNpc != null ? vehicle.sharedNpc.motorSettings : null,
            this.traits);
        planner = new PoliceFreePathPlanner();
        pin = new PolicePinPressure(new PolicePinPressure.Settings(this.chase.pinEngageGap, this.chase.pinReleaseGap,
            this.chase.pinReleaseGraceSeconds, this.chase.pinMaximumSeconds, this.chase.pinCooldownSeconds, 3f, 35f));
        driver = new PoliceFreePursuitDriver(motor, driving, body != null && body.Body != null ? body.Body.mass : 0f);
        recovery = new PoliceFreeRecovery(driving);
        avoidance = body != null && geometry != null && motor != null && driving != null && query != null
            ? new PoliceFreeAvoidanceQuery(body, geometry, motor, driving, query.LocalBounds, origin, targetBody) : null;
        role = behavior != null ? behavior.tacticalRole : PoliceTacticalRole.Pursue;
        shadowSettings = behavior != null && behavior.shadow != null ? behavior.shadow.Clone() : new PoliceShadowTactics.Settings();
        lastTargetPosition = targetBody != null ? targetBody.position : Vector2.positiveInfinity;
        nextPlanAt = float.NegativeInfinity;
        nextRoleAt = float.NegativeInfinity;
        nextDecisionAt = float.NegativeInfinity;
        reactionReadyAt = float.NegativeInfinity;
        if (body != null && body.Motor != null && motor != null) body.Motor.Configure(motor);
        if (body != null && body.Sensor != null && motor != null) body.Sensor.Configure(motor, driving.sensorBuffer);
        if (driving != null) ramDecision.TryConfigure(driving);
        if (this.chase.recklessPursuit && body != null && body.Motor != null)
            body.Motor.crashModeForceMultiplier = 1f;
        if (lifeId > 0 && scheduler != null) scheduler.Register(lifeId, planner);
        if (lifeId > 0 && targetLifeId > 0 && tactics != null) {
            tactics.BeginLife(lifeId, new PoliceTacticsCoordinator.TargetFacts(targetLifeId, targetRevision), this.traits);
            UpdateRole(0f);
        }
        if (body != null && body.DamageReceiver != null) body.DamageReceiver.ImpactObserved += HandleImpact;
    }

    /// <summary>True while the bounded exact-player ram command owns the motor.</summary>
    bool PolicePursuitController.IFreeDrive.IsRamming => !reset && ramDecision.IsActive;

    /// <summary>True while crash recovery or bounded replanning owns the motor.</summary>
    bool PolicePursuitController.IFreeDrive.IsRecovering => !reset &&
        (recovery.CurrentState == PoliceFreeRecovery.State.CrashWait ||
            recovery.CurrentState == PoliceFreeRecovery.State.Braking ||
            recovery.CurrentState == PoliceFreeRecovery.State.Reversing ||
            recovery.CurrentState == PoliceFreeRecovery.State.Replanning);

    /// <summary>True only at a measured low-speed surface standoff from the exact player.</summary>
    bool PolicePursuitController.IFreeDrive.IsHolding => !reset && holding;

    /// <summary>Queues one cadence-gated bounded plan from the current physical pose.</summary>
    internal void PreparePlan(float activeClock, int requestId) {
        if (reset || !Finite(activeClock) || !IsBound() || requestId <= 0 ||
            !IsClockCurrent(activeClock)) return;
        clock = activeClock;
        if (activeClock >= nextRoleAt) {
            UpdateRole(activeClock);
            nextRoleAt = activeClock + Mathf.Max(chase.roleCadenceSeconds, .01f);
        }
        if (activeClock < nextPlanAt) return;
        Vector2 targetPosition = targetBody.position;
        if (!Finite(targetPosition) || !Finite(targetBody.linearVelocity)) return;
        float displacement = Vector2.Distance(lastTargetPosition, targetPosition);
        bool targetMoved = !Finite(lastTargetPosition) || displacement >= Mathf.Max(.1f, navigation.targetDisplacementThreshold);
        if (!targetMoved && !driver.NeedsReplan && !recovery.NeedsReplan && !terminalBlocked && !strategicDirty && appliedRequestId > 0) return;
        if (targetMoved) {
            targetRevision++;
            interceptFallback = false;
            tactics?.TryAcceptTargetFacts(lifeId, new PoliceTacticsCoordinator.TargetFacts(targetLifeId, targetRevision));
            ReleaseClaim();
            UpdateRole(activeClock);
            reactionReadyAt = activeClock + Mathf.Max(0f, chase.reactionSeconds * traits.reactionMultiplier);
        }
        Vector2 planningTarget = ApplyApproachOffset(targetPosition);
        if (role == PoliceTacticalRole.Intercept) {
            var facts = new PoliceFreePursuitDriver.InterceptTarget(targetPosition, targetBody.linearVelocity);
            if (!PoliceFreePursuitDriver.TryPredictIntercept(facts,
                chase.predictionSeconds * traits.predictionMultiplier, chase.maximumLeadDistance, out planningTarget))
                planningTarget = ApplyApproachOffset(targetPosition);
            else planningTarget = ApplyApproachOffset(planningTarget);
        }
        // A Shadow unit plans to its own lead/blocking point, never to the player.
        if (role == PoliceTacticalRole.Shadow && hasShadowGoal) planningTarget = shadowGoal.point;
        Vector2 start = MapNavigationCoordinates.WorldToLocal(body.Body.position, origin);
        Vector2 targetLocal = MapNavigationCoordinates.WorldToLocal(planningTarget, origin);
        float radius = motor.minimumTurningRadius;
        if (!FinitePositive(radius)) return;
        var request = new PoliceFreePathPlanner.Request(requestId, lifeId, targetLifeId, query.GeometryRevision,
            start, body.Body.rotation, targetLocal,
            new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset), query,
            chase.goalRadius, radius, 1f, 10f, .25f, chase.turnPenalty, .75f, 64, 8192,
            RequestWorkLimit, navigation.clearanceMargin);
        scheduler.Submit(request);
        strategicDirty = false;
        latestRequestId = requestId;
        lastTargetPosition = targetPosition;
        nextPlanAt = activeClock + Mathf.Max(navigation.hardMinimumInterval,
            Mathf.Max(navigation.refreshInterval, chase.decisionIntervalSeconds));
    }

    /// <summary>Adopts a current Found result only after reconciling it with the actual body pose.</summary>
    internal void AdoptResult() {
        ObserveTerminalResult();
        if (reset || scheduler == null || !IsBound() ||
            !scheduler.TryGetResult(lifeId, out PoliceFreePathPlanner.Result result) ||
            result.status != PoliceFreePathPlanner.Status.Found || result.unitLifeId != lifeId ||
            result.targetLifeId != targetLifeId || result.geometryRevision != query.GeometryRevision ||
            result.requestId < revisionRequestFloor || result.requestId == appliedRequestId) return;
        IReadOnlyList<PoliceFreePathPlanner.Primitive> primitives = result.Primitives;
        if (!ReconcilesWithActualPose(primitives, out int firstPrimitive)) return;
        var worldPath = new List<PoliceFreePathPlanner.Primitive>(primitives.Count);
        for (int index = firstPrimitive; index < primitives.Count; index++) {
            PoliceFreePathPlanner.Primitive primitive = primitives[index];
            worldPath.Add(new PoliceFreePathPlanner.Primitive(primitive.kind, primitive.startPivot + origin,
                primitive.startHeadingDegrees, primitive.endPivot + origin, primitive.endHeadingDegrees,
                primitive.turnRadius, primitive.deltaHeadingDegrees, primitive.length));
        }
        driver.SetPath(worldPath);
        adoptedPath = worldPath;
        emptyGoal = worldPath.Count == 0;
        acceptedGoal = emptyGoal ? body.Body.position : worldPath[worldPath.Count - 1].endPivot;
        terminalBlocked = false;
        appliedRequestId = result.requestId;
        recovery.ResumePursuit();
    }

    /// <summary>Produces one motor command from the actual body pose without moving the body directly.</summary>
    public NpcDriveCommand Tick(float deltaTime, bool paused) {
        if (reset || !IsBound() || !FinitePositive(deltaTime) || paused || Time.timeScale <= 0f) return NpcDriveCommand.Stopped;
        float activeClock = world != null ? world.SessionTime : float.NaN;
        if (!Finite(activeClock) || !IsClockCurrent(activeClock)) return NpcDriveCommand.Stopped;
        clock = activeClock;
        ObserveTerminalResult();
        Vector2 position = body.Body.position;
        Vector2 forward = body.transform.up;
        Vector2 velocity = body.Body.linearVelocity;
        if (!Finite(position) || !Finite(forward) || !Finite(velocity) || !FinitePositive(forward.sqrMagnitude)) return NpcDriveCommand.Stopped;
        float speed = Mathf.Abs(Vector2.Dot(velocity, forward.normalized));
        float displacement = hasPosition ? Vector2.Distance(position, lastPosition) : 0f;
        lastPosition = position;
        hasPosition = true;
        if (avoidance is PoliceFreeAvoidanceQuery localAvoidance)
            localAvoidance.SetTargetDistance(Vector2.Distance(position, targetBody.position));
        holding = IsAtHoldingStandoff(speed);
        if (chase.recklessPursuit) {
            query?.JamMemory?.SetClock(activeClock);
            bool shadow = role == PoliceTacticalRole.Shadow;
            float playerGap = PlayerSurfaceGap(out bool playerOverlap);
            bool playerContact = ramDecision.IsActive || playerOverlap || playerGap <= 0.03f;
            bool playerClose = playerGap <= Mathf.Min(driving.ramMaximumSurfaceGap, Mathf.Max(2f, driving.stopGap + 1.5f));
            // Contact pressure outranks every other decision: after the first hit the unit keeps
            // shoving the player instead of disengaging to line up a fresh ram.
            // A Shadow unit never shoves the player; contact with it is incidental.
            if (!shadow && pin.Observe(deltaTime, playerContact, playerGap, !body.Motor.IsStoppedPermanently)) {
                if (recovery.CurrentState != PoliceFreeRecovery.State.Pursuing) recovery.Reset();
                ResetJamTracking();
                replanningSince = float.NegativeInfinity;
                return CacheCommand(PinCommand(forward, position));
            }
            // Only genuine contact (or an active ram) suppresses escape. Mere proximity must not:
            // a player who slipped through a gap this car cannot fit is "close" while the car is
            // jammed on the wall between them, which is exactly when escape has to be available.
            if (playerContact) {
                if (recovery.CurrentState != PoliceFreeRecovery.State.Pursuing) recovery.Reset();
                ResetJamTracking();
                replanningSince = float.NegativeInfinity;
                escapeCommitUntil = float.NegativeInfinity;
            }
            if (!playerContact && recovery.CurrentState == PoliceFreeRecovery.State.Pursuing &&
                TrackObstacleJam(deltaTime, position, playerClose && !shadow)) {
                RecordJamSpot(position, forward);
                escapeDirection = ComputeEscapeDirection(position, forward);
                hasEscapeDirection = true;
                recovery.BeginImmediateEscape();
            }
            if (!playerContact && recovery.CurrentState != PoliceFreeRecovery.State.Pursuing) {
                NpcDriveCommand escape = recovery.Step(deltaTime, speed, displacement,
                    RearIsClear(), true, false);
                if (recovery.CurrentState != PoliceFreeRecovery.State.Replanning) {
                    replanningSince = float.NegativeInfinity;
                    // Reverse with the nose swinging toward the escape heading. A straight reverse only
                    // backs off along the same line and the unit drives back into the same wall.
                    if (recovery.CurrentState == PoliceFreeRecovery.State.Reversing && hasEscapeDirection)
                        escape = new NpcDriveCommand(escape.throttle, escape.brake,
                            ProportionalSteering(Vector2.SignedAngle(forward, escapeDirection)), escape.targetSpeed, escape.reverseAllowed);
                    return CacheCommand(escape);
                }
                // Bounded replanning may never own the unit forever: a car wedged into geometry can
                // keep failing to produce an adoptable path, which used to trap it here for good.
                if (!Finite(replanningSince)) {
                    replanningSince = activeClock;
                    escapeCommitUntil = activeClock + EscapeCommitSeconds;
                }
                // While the fresh plan is pending, drive out along the escape heading instead of
                // aiming straight back at the player, which was pointing the unit into the same wall.
                if (activeClock - replanningSince < Mathf.Max(0.1f, chase.replanGiveUpSeconds))
                    return CacheCommand(hasEscapeDirection && activeClock < escapeCommitUntil
                        ? EscapeCommitCommand(forward)
                        : shadow ? ShadowCommand(deltaTime, forward, position, velocity) : RecklessPursuitCommand(forward, position));
                recovery.Reset();
                ResetJamTracking();
                replanningSince = float.NegativeInfinity;
            }
            if (hasEscapeDirection && activeClock < escapeCommitUntil && !playerContact) {
                // The committed heading is re-checked while driving; a new obstacle ends it early
                // and hands back to the planner-aware pursuit instead of ploughing into it.
                if (EscapeHeadingStillClear(position)) return CacheCommand(EscapeCommitCommand(forward));
                escapeCommitUntil = float.NegativeInfinity;
            }
            if (shadow) return CacheCommand(ShadowCommand(deltaTime, forward, position, velocity));
            if (TryRamCommand(deltaTime, forward, speed, out NpcDriveCommand recklessRam)) return CacheCommand(recklessRam);
            // Close range: stop being clever. Full speed straight at where the player is going, no
            // planner, no detour, no cornering slowdown, so a late dodge sends the unit into whatever
            // is behind the player. Far units stay on the planner-aware pursuit below.
            if (ShouldDive(playerGap, position)) return CacheCommand(DiveCommand(forward, position));
            bool corridorBlockedNow = !IsDirectCorridorClear(position, targetBody.position - position);
            NpcDriveCommand guided = driver.Tick(new PoliceFreePursuitDriver.Pose(position, forward, velocity),
                deltaTime, avoidance);
            lastDecisionClock = activeClock;
            // With an open way to the player the planner's output is ignored outright: its avoidance
            // would steer around the very target this unit must hit.
            if (!corridorBlockedNow) return CacheCommand(RecklessPursuitCommand(forward, position));
            // Blocked by static geometry: keep the planner's steering, never its stop/brake output, so
            // a detour that needs slowing down is driven instead of discarded for a straight ram.
            if (guided.throttle > 0.5f && guided.brake <= 0.01f)
                return CacheCommand(guided);
            if (Mathf.Abs(guided.steering) > 0.01f)
                return CacheCommand(new NpcDriveCommand(1f, 0f, guided.steering,
                    Mathf.Max(guided.targetSpeed, motor.cruiseSpeed), false));
            return CacheCommand(RecklessPursuitCommand(forward, position));
        }
        bool atAcceptedGoal = emptyGoal && Vector2.Distance(position, acceptedGoal) <= chase.goalRadius && !strategicDirty;
        bool pending = latestRequestId > 0 && planner.Current.status == PoliceFreePathPlanner.Status.Pending;
        bool recoveryBlocked = !chase.recklessPursuit && !holding && !atAcceptedGoal &&
            (driver.NeedsReplan || terminalBlocked);
        NpcDriveCommand recoveryCommand = recovery.Step(deltaTime, speed, displacement,
            RearIsClear(), recoveryBlocked, pending && !chase.recklessPursuit);
        if (((PolicePursuitController.IFreeDrive)this).IsRecovering) return CacheCommand(recoveryCommand);
        if (TryRamCommand(deltaTime, forward, speed, out NpcDriveCommand ramCommand)) return CacheCommand(ramCommand);
        if (atAcceptedGoal && !chase.recklessPursuit) return CacheCommand(NpcDriveCommand.Stopped);
        if (activeClock < reactionReadyAt || activeClock < nextDecisionAt)
            return hasCommand ? lastCommand : NpcDriveCommand.Stopped;
        NpcDriveCommand command = driver.Tick(new PoliceFreePursuitDriver.Pose(position, forward, velocity),
            Mathf.Max(deltaTime, activeClock - lastDecisionClock), avoidance);
        lastDecisionClock = activeClock;
        if (driver.NeedsReplan) recovery.BeginAvoiding();
        nextDecisionAt = activeClock + Mathf.Max(chase.decisionIntervalSeconds, .01f);
        return CacheCommand(command);
    }

    /// <summary>Prepares the free policy for an owner-controlled relocation without changing life facts.</summary>
    public bool PrepareRelocation() {
        if (reset || ramDecision.IsActive) return false;
        planner.Cancel();
        scheduler.Unregister(lifeId);
        scheduler.Register(lifeId, planner);
        driver.Reset();
        recovery.Reset();
        ramDecision.ResetForNewLife();
        latestRequestId = 0;
        appliedRequestId = 0;
        revisionRequestFloor = 1;
        nextPlanAt = float.NegativeInfinity;
        nextRoleAt = float.NegativeInfinity;
        nextDecisionAt = float.NegativeInfinity;
        reactionReadyAt = float.NegativeInfinity;
        lastTargetPosition = Vector2.positiveInfinity;
        holding = false;
        hasPosition = false;
        hasCommand = false;
        lastCommand = NpcDriveCommand.Stopped;
        interceptFallback = false;
        terminalBlocked = false;
        strategicDirty = true;
        observedTerminalId = 0;
        emptyGoal = false;
        adoptedPath = null;
        corridorBlockedSince = float.NegativeInfinity;
        corridorClear = true;
        nextCorridorCheckAt = float.NegativeInfinity;
        replanningSince = float.NegativeInfinity;
        escapeCommitUntil = float.NegativeInfinity;
        hasEscapeDirection = false;
        ResetJamTracking();
        pin?.Reset();
        consecutiveJams = 0;
        lastJamAt = float.NegativeInfinity;
        forceThroughSuppressedUntil = float.NegativeInfinity;
        ReleaseClaim();
        return true;
    }

    /// <summary>Resets all subscriptions and scheduler/tactics ownership exactly once.</summary>
    public void Reset() {
        if (reset) return;
        reset = true;
        if (body != null && body.DamageReceiver != null) body.DamageReceiver.ImpactObserved -= HandleImpact;
        ramDecision.ResetForNewLife();
        planner.Cancel();
        scheduler?.Unregister(lifeId);
        ReleaseClaim();
        tactics?.EndLife(lifeId);
        driver.Reset();
        recovery.Reset();
    }

    bool PolicePursuitController.IFreeDrive.TrySetTarget(VehicleDamageReceiver receiver) {
        return !reset && receiver != null && receiver == target && receiver.Identity.lifeId == targetLifeId &&
            targetBody != null && receiver.GetComponent<Rigidbody2D>() == targetBody;
    }

    void UpdateRole(float activeClock) {
        if (tactics == null || vehicle == null || !Finite(activeClock)) return;
        PoliceTacticsCoordinator.RoleDecision decision = tactics.AssignRole(lifeId, vehicle, activeClock);
        PoliceTacticalRole previousRole = role;
        if (decision.accepted) role = interceptFallback && decision.role == PoliceTacticalRole.Intercept
            ? PoliceTacticalRole.Pursue : decision.role;
        if (previousRole != role) { strategicDirty = true; if (ramDecision.IsActive) ramDecision.Cancel(clock); }
        if (decision.changed) ReleaseClaim();
        if (hasClaim && !tactics.IsClaimCurrent(claim, activeClock)) {
            hasClaim = false;
            claim = default;
        }
        if (!hasClaim) ClaimApproach(activeClock);
    }

    void ClaimApproach(float activeClock) {
        if (tactics == null || !Finite(activeClock)) return;
        PoliceTacticsCoordinator.ApproachSlot slot = role == PoliceTacticalRole.Intercept || role == PoliceTacticalRole.Shadow
            ? PoliceTacticsCoordinator.ApproachSlot.Front
            : role == PoliceTacticalRole.Ram ? PoliceTacticsCoordinator.ApproachSlot.Left : PoliceTacticsCoordinator.ApproachSlot.Rear;
        for (int index = 0; index < 4 && !hasClaim; index++) {
            var choice = (PoliceTacticsCoordinator.ApproachSlot)(((int)slot + index) % 4);
            hasClaim = tactics.TryClaimApproach(lifeId, new PoliceTacticsCoordinator.TargetFacts(targetLifeId, targetRevision),
                choice, activeClock, Mathf.Max(chase.minimumRoleHoldSeconds, chase.roleCadenceSeconds), out claim);
        }
    }

    void ReleaseClaim() {
        if (hasClaim) tactics?.ReleaseApproach(claim);
        hasClaim = false;
        claim = default;
    }

    bool TryRamCommand(float deltaTime, Vector2 forward, float speed, out NpcDriveCommand command) {
        command = NpcDriveCommand.Stopped;
        if (chase == null || !chase.recklessPursuit || target == null || targetBody == null || targetCollider == null ||
            body.MainCollider == null || body.Sensor == null || body.Motor == null || !target.gameObject.activeInHierarchy) return false;
        if (body.Motor.IsCrashMode) { ramDecision.Cancel(clock); return false; }
        Vector2 delta = targetBody.position - body.Body.position;
        var separation = body.MainCollider.Distance(targetCollider);
        if (!separation.isValid || separation.isOverlapped || !Finite(separation.distance)) {
            if (ramDecision.IsActive) ramDecision.Cancel(clock);
            return false;
        }
        float gap = separation.distance;
        bool wasActive = ramDecision.IsActive;
        if (!wasActive) {
            if (!ramDecision.IsEligible(forward, delta, gap, driving.stopGap) ||
                !ramDecision.CanAttempt(clock, speed, driving.stoppedSpeedThreshold, ((PolicePursuitController.IFreeDrive)this).IsRecovering)) return false;
            VehicleObstacleSensor.SweepStatus acquire = body.Sensor.QuerySweep(forward, driving.ramMaximumSurfaceGap,
                out _, out Collider2D obstacle);
            if (acquire != VehicleObstacleSensor.SweepStatus.Blocked || obstacle == null ||
                obstacle.attachedRigidbody != targetBody || !ramDecision.TryBegin(clock, deltaTime, speed,
                    driving.stoppedSpeedThreshold, true, false)) return false;
        }
        if (wasActive && !ramDecision.Advance(clock, deltaTime)) return false;
        float acceleration = Mathf.Min(motor.acceleration, motor.maxEngineForce / body.Body.mass);
        float braking = Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / body.Body.mass);
        if (!PoliceRamDecision.TryReserve(speed, acceleration, braking, ramDecision.RemainingActiveSeconds,
            motor.reactionTime, deltaTime, driving.stopGap, gap, driving.maxSweepDistance,
            out float reservedDistance, out float peakSpeed)) {
            ramDecision.Cancel(clock);
            return false;
        }
        VehicleObstacleSensor.SweepStatus sweep = body.Sensor.QuerySweep(forward, reservedDistance,
            targetBody, out _, out Collider2D blockingObstacle);
        if (sweep != VehicleObstacleSensor.SweepStatus.Clear || blockingObstacle != null) {
            ramDecision.Cancel(clock);
            return false;
        }
        command = new NpcDriveCommand(1f, 0f, 0f,
            Mathf.Min(motor.maxSpeed, Mathf.Max(0f, peakSpeed)), false);
        return true;
    }

    NpcDriveCommand RecklessPursuitCommand(Vector2 forward, Vector2 position) {
        Vector2 delta = targetBody.position - position;
        if (!Finite(delta) || delta.sqrMagnitude <= 0.0001f) return new NpcDriveCommand(1f, 0f, 0f, motor.maxSpeed, false);
        Vector2 aim = delta.normalized;
        bool corridorBlocked = !IsDirectCorridorClear(position, delta);
        if (!corridorBlocked) corridorBlockedSince = float.NegativeInfinity;
        else if (!Finite(corridorBlockedSince)) corridorBlockedSince = clock;
        // "Force it first, then go around": the straight ram is kept for the authored window even
        // when the way is blocked, and only afterwards does the car steer along its detour.
        bool forcingThrough = !corridorBlocked || (clock >= forceThroughSuppressedUntil &&
            clock - corridorBlockedSince < Mathf.Max(0f, chase.forceThroughSeconds));
        // Every lateral bias below exists only to get around static geometry. None of them may run
        // while the way to the player is open, or the unit visibly swerves past the player it is
        // supposed to hit.
        if (corridorBlocked && !forcingThrough) {
            if (TryGetDetourAim(position, out Vector2 detourAim)) aim = detourAim;
            else if (driver.SelectedAvoidanceIndex >= 0 && driver.SelectedAvoidanceIndex != 2) {
                Vector2 right = new Vector2(forward.y, -forward.x);
                float side = driver.SelectedAvoidanceIndex < 2 ? -1f : 1f;
                aim = (aim + right * (0.55f * side)).normalized;
            } else if (Vector2.Dot(forward, aim) > 0.8f) {
                // No usable path and the way is blocked: bias laterally so a wall cannot turn
                // pursuit into a stall.
                Vector2 right = new Vector2(forward.y, -forward.x);
                float side = Mathf.Sin(clock * 2.1f) >= 0f ? 1f : -1f;
                aim = (aim + right * (0.45f * side)).normalized;
            }
        }
        float angle = Vector2.SignedAngle(forward, aim);
        return new NpcDriveCommand(1f, 0f, ProportionalSteering(angle), TurnLimitedSpeed(angle), false);
    }

    /// <summary>
    /// True inside the close dive range, measured in this unit's own body lengths so it means the
    /// same thing whatever the map scale. Right after a jam the dive is not retried through
    /// geometry that still blocks the way; the unit goes around first.
    /// </summary>
    bool ShouldDive(float playerGap, Vector2 position) {
        if (geometry == null || targetBody == null || !Finite(playerGap)) return false;
        float bodyLength = Mathf.Max(geometry.colliderFootprint.x, geometry.colliderFootprint.y);
        if (playerGap > Mathf.Max(0f, chase.diveRangeBodyLengths) * bodyLength) return false;
        if (Finite(lastJamAt) && clock - lastJamAt < chase.diveRetryAfterJamSeconds &&
            !IsDirectCorridorClear(position, targetBody.position - position)) return false;
        return true;
    }

    /// <summary>Full-throttle dive at the player's near-future position. Only a unit facing away slows enough to turn around.</summary>
    NpcDriveCommand DiveCommand(Vector2 forward, Vector2 position) {
        Vector2 aimPoint = targetBody.position + targetBody.linearVelocity * Mathf.Max(0f, chase.diveLeadSeconds);
        Vector2 delta = aimPoint - position;
        if (!Finite(delta) || delta.sqrMagnitude <= 0.0001f) return new NpcDriveCommand(1f, 0f, 0f, motor.maxSpeed, false);
        float angle = Vector2.SignedAngle(forward, delta);
        float speed = Mathf.Abs(angle) > 100f ? TurnLimitedSpeed(angle) : motor.maxSpeed;
        return new NpcDriveCommand(1f, 0f, ProportionalSteering(angle), speed, false);
    }

    /// <summary>Damped steering with a small deadzone; bang-bang full lock made a fast unit orbit its target instead of lining up.</summary>
    static float ProportionalSteering(float angleDegrees) {
        if (!Finite(angleDegrees)) return 0f;
        float magnitude = Mathf.Abs(angleDegrees);
        if (magnitude <= 3f) return 0f;
        return Mathf.Clamp01((magnitude - 3f) / 27f) * Mathf.Sign(angleDegrees);
    }

    /// <summary>
    /// Speed target for a turn. At full throttle the motor turns on an arc of roughly speed/turnRate,
    /// so a unit that has to swing more than a quarter turn orbits a target it can never reach — the
    /// observed "circles next to the street it wants to enter". Slowing into the turn shrinks that arc
    /// without ever braking to a stop.
    /// </summary>
    float TurnLimitedSpeed(float angleDegrees) {
        float magnitude = Mathf.Abs(angleDegrees);
        if (!Finite(magnitude) || magnitude <= 25f) return motor.maxSpeed;
        float corner = Mathf.Sqrt(Mathf.Max(0.1f, chase.lateralAcceleration) * Mathf.Max(0.5f, motor.minimumTurningRadius));
        return Mathf.Lerp(motor.maxSpeed, Mathf.Min(corner, motor.maxSpeed), Mathf.InverseLerp(25f, 90f, magnitude));
    }

    bool ReconcilesWithActualPose(IReadOnlyList<PoliceFreePathPlanner.Primitive> primitives, out int firstPrimitive) {
        firstPrimitive = 0;
        if (primitives == null || primitives.Count == 0) return true;
        Vector2 actualLocal = MapNavigationCoordinates.WorldToLocal(body.Body.position, origin);
        float nearest = float.PositiveInfinity;
        Vector2 projected = actualLocal;
        float heading = body.Body.rotation;
        int count = Mathf.Min(primitives.Count, driving.cursorWork);
        for (int index = 0; index < count; index++) {
            var primitive = primitives[index];
            float t;
            Vector2 point;
            if (primitive.kind == PoliceFreePathPlanner.PrimitiveKind.Straight) {
                Vector2 line = primitive.endPivot - primitive.startPivot;
                t = Mathf.Clamp01(Vector2.Dot(actualLocal - primitive.startPivot, line) / Mathf.Max(line.sqrMagnitude, 0.0001f));
                point = Vector2.Lerp(primitive.startPivot, primitive.endPivot, t);
            } else {
                Vector2 forward = PoliceFreeNavigationGeometry.Rotate(Vector2.up, primitive.startHeadingDegrees);
                Vector2 center = primitive.startPivot + new Vector2(-forward.y, forward.x) *
                    Mathf.Sign(primitive.deltaHeadingDegrees) * primitive.turnRadius;
                float angle = Vector2.SignedAngle(primitive.startPivot - center, actualLocal - center);
                t = Mathf.Clamp01(angle / primitive.deltaHeadingDegrees);
                point = center + PoliceFreeNavigationGeometry.Rotate(primitive.startPivot - center, primitive.deltaHeadingDegrees * t);
            }
            float distance = Vector2.Distance(actualLocal, point);
            if (distance >= nearest) continue;
            nearest = distance;
            projected = point;
            heading = primitive.startHeadingDegrees + primitive.deltaHeadingDegrees * t;
            firstPrimitive = index;
        }
        float difference = Mathf.Abs(Mathf.DeltaAngle(body.Body.rotation, heading));
        if (!Finite(actualLocal) || nearest > driving.maxDeviation || difference > driving.turnExitAlignmentDegrees) return false;
        float rotationPad = 2f * geometry.ColliderSurfaceRadius * Mathf.Sin(difference * Mathf.Deg2Rad * 0.5f);
        return query.CheckLine(actualLocal, projected,
            new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset),
            body.Body.rotation, navigation.clearanceMargin + rotationPad,
            new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear;
    }

    void ObserveTerminalResult() {
        if (reset || !scheduler.TryGetResult(lifeId, out var result) || result.requestId == observedTerminalId ||
            result.unitLifeId != lifeId || result.targetLifeId != targetLifeId || result.geometryRevision != query.GeometryRevision) return;
        observedTerminalId = result.requestId;
        terminalBlocked = result.status == PoliceFreePathPlanner.Status.NoPath;
        if (terminalBlocked && role == PoliceTacticalRole.Intercept) {
            interceptFallback = true;
            role = PoliceTacticalRole.Pursue;
            ReleaseClaim();
            strategicDirty = true;
        }
    }

    NpcDriveCommand CacheCommand(NpcDriveCommand command) {
        lastCommand = command;
        hasCommand = true;
        return command;
    }

    /// <summary>
    /// Detects a physical jam from net world displacement over an authored window instead of a
    /// single-frame speed/displacement test. A car grinding along a wall keeps producing small
    /// per-frame motion and speed jitter — especially under the reckless lateral wiggle — which used
    /// to reset the old counter every frame and made the unit stay pinned indefinitely.
    /// </summary>
    bool TrackObstacleJam(float deltaTime, Vector2 position, bool playerClose) {
        float window = Mathf.Max(0.1f, chase.jamWindowSeconds);
        if (terminalBlocked) window *= 0.5f; // the planner already proved this pose has no way out
        if (playerClose) window += Mathf.Max(0f, chase.forceThroughSeconds); // keep ramming a close player first
        bool throttling = !hasCommand || lastCommand.throttle > 0.1f;
        return jam.Observe(deltaTime, position, throttling, window, chase.jamMinimumDisplacement);
    }

    void ResetJamTracking() => jam.Reset();

    /// <summary>
    /// Heading that gets a jammed car off the wall it is pressed against: along the wall toward the
    /// player (or the adopted detour), angled slightly away from it. Contact normals come from the
    /// body's current contacts, oriented from the contact point toward the car so their sign never
    /// depends on Unity's pair ordering. Without a wall contact the player direction is used.
    /// </summary>
    Vector2 ComputeEscapeDirection(Vector2 position, Vector2 forward) {
        Vector2 preferred = targetBody.position - position;
        preferred = Finite(preferred) && preferred.sqrMagnitude > 0.0001f ? preferred.normalized : forward;
        if (TryGetDetourAim(position, out Vector2 detour)) preferred = detour;
        if (TryGetWallContact(position, out _, out Vector2 normal)) {
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            if (Vector2.Dot(tangent, preferred) < 0f) tangent = -tangent;
            preferred = (tangent + normal * 0.5f).normalized;
        }
        return ChooseClearEscapeHeading(position, forward, preferred);
    }

    /// <summary>
    /// Picks the escape heading by actually measuring free space around the car instead of trusting
    /// only the wall it touches, which used to send it straight into the next obvious obstacle.
    /// Sixteen headings are swept once per jam (static geometry plus remembered jam spots); the
    /// score favours long clear runs, then agreement with the preferred escape, then small turns.
    /// </summary>
    Vector2 ChooseClearEscapeHeading(Vector2 position, Vector2 forward, Vector2 preferred) {
        if (query == null || geometry == null) return preferred;
        Vector2 local = MapNavigationCoordinates.WorldToLocal(position, origin);
        float bestScore = float.NegativeInfinity;
        Vector2 best = preferred;
        for (int i = 0; i < EscapeCandidateCount; i++) {
            Vector2 direction = MapNavigationCoordinates.HeadingDegreesToDirection(i * (360f / EscapeCandidateCount));
            float clear = ClearRunAlong(local, direction);
            if (clear <= 0f) continue;
            float score = clear + 1.5f * Vector2.Dot(direction, preferred) + 0.3f * Vector2.Dot(direction, forward);
            if (score > bestScore) {
                bestScore = score;
                best = direction;
            }
        }
        return best;
    }

    /// <summary>
    /// Longest probe distance a car-width corridor stays clear along a direction. The corridor starts
    /// half a metre out so the wall the car is pressed against does not block every heading.
    /// </summary>
    float ClearRunAlong(Vector2 localStart, Vector2 direction) {
        var corridor = new PoliceFreeNavigationGeometry.Footprint(new Vector2(geometry.colliderFootprint.x, 0.5f), Vector2.zero);
        float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(direction);
        Vector2 start = localStart + direction * 0.5f;
        float clear = 0f;
        for (int i = 0; i < EscapeProbeDistances.Length; i++) {
            float distance = EscapeProbeDistances[i];
            if (!query.CheckLine(start, localStart + direction * distance, corridor, heading, navigation.clearanceMargin,
                new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear) break;
            clear = distance;
        }
        return clear;
    }

    /// <summary>True while the committed escape heading still has at least a metre of clear road ahead.</summary>
    bool EscapeHeadingStillClear(Vector2 position) {
        if (query == null || geometry == null) return true;
        if (clock < nextEscapeCheckAt) return true;
        nextEscapeCheckAt = clock + 0.1f;
        return ClearRunAlong(MapNavigationCoordinates.WorldToLocal(position, origin), escapeDirection) >= 1f;
    }

    /// <summary>Full-throttle drive along the escape heading for a short committed window after reversing off a wall.</summary>
    NpcDriveCommand EscapeCommitCommand(Vector2 forward) {
        float angle = Vector2.SignedAngle(forward, escapeDirection);
        return new NpcDriveCommand(1f, 0f, ProportionalSteering(angle), TurnLimitedSpeed(angle), false);
    }

    /// <summary>Remembers the pocket in front of a jammed car so every unit's later path search avoids it.</summary>
    /// <summary>
    /// Averages the body's current non-player contacts into one wall point and one outward normal
    /// (pointing from the wall toward the car, independent of Unity's pair ordering).
    /// </summary>
    bool TryGetWallContact(Vector2 position, out Vector2 point, out Vector2 normal) {
        point = Vector2.zero;
        normal = Vector2.zero;
        if (body == null || body.Body == null) return false;
        int used = 0;
        int count = body.Body.GetContacts(contactBuffer);
        for (int i = 0; i < count; i++) {
            ContactPoint2D contact = contactBuffer[i];
            if (contact.collider == null || contact.collider.isTrigger || contact.collider == targetCollider) continue;
            Vector2 n = contact.normal;
            if (!Finite(n) || n.sqrMagnitude <= 0.0001f || !Finite(contact.point)) continue;
            normal += Vector2.Dot(n, position - contact.point) < 0f ? -n : n;
            point += contact.point;
            used++;
        }
        if (used == 0 || normal.sqrMagnitude <= 0.0001f) return false;
        point /= used;
        normal.Normalize();
        return true;
    }

    void RecordJamSpot(Vector2 position, Vector2 forward) {
        // A second jam in the same pocket means forcing it again is pointless: commit to the detour
        // for a while and remember a wider spot so no unit re-enters it.
        bool repeat = Finite(lastJamAt) && clock - lastJamAt <= 8f && Vector2.Distance(position, lastJamPosition) <= 6f;
        consecutiveJams = repeat ? consecutiveJams + 1 : 1;
        lastJamPosition = position;
        lastJamAt = clock;
        if (consecutiveJams >= 2) forceThroughSuppressedUntil = clock + 4f;
        PoliceJamMemory memory = query != null ? query.JamMemory : null;
        if (memory == null || geometry == null || !Finite(position)) return;
        // Remember the obstacle side of the jam (the wall or corner that was hit), not the lane in
        // front of the car: at an alley mouth "in front" is the alley itself, and blocking it made
        // every unit stop following the player into narrow streets. No wall contact, nothing to remember.
        if (!TryGetWallContact(position, out Vector2 wallPoint, out Vector2 wallNormal)) return;
        float surface = Mathf.Max(0.25f, geometry.ColliderSurfaceRadius);
        float radius = surface * 0.6f * Mathf.Min(1.5f, 1f + 0.25f * (consecutiveJams - 1));
        Vector2 spot = wallPoint - wallNormal * (radius * 0.6f);
        // Never a spot that would fence the player in.
        if (targetBody != null && Vector2.Distance(spot, targetBody.position) <= radius + 1f) return;
        memory.SetClock(clock);
        memory.Record(MapNavigationCoordinates.WorldToLocal(spot, origin), radius);
    }

    /// <summary>True while a straight sweep of this car's own footprint toward the player is provably clear.</summary>
    bool IsDirectCorridorClear(Vector2 position, Vector2 delta) {
        if (query == null || geometry == null) return true;
        if (clock < nextCorridorCheckAt) return corridorClear;
        nextCorridorCheckAt = clock + 0.1f;
        float distance = Mathf.Min(delta.magnitude, 8f);
        if (distance <= 0.05f) {
            corridorClear = true;
            return corridorClear;
        }
        Vector2 direction = delta / delta.magnitude;
        Vector2 startLocal = MapNavigationCoordinates.WorldToLocal(position, origin);
        corridorClear = query.CheckLine(startLocal, startLocal + direction * distance,
            new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset),
            MapNavigationCoordinates.DirectionToHeadingDegrees(direction), navigation.clearanceMargin,
            new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear;
        return corridorClear;
    }

    /// <summary>Picks the first adopted-path waypoint far enough ahead to steer at while keeping full throttle.</summary>
    bool TryGetDetourAim(Vector2 position, out Vector2 aim) {
        aim = Vector2.zero;
        if (adoptedPath == null || adoptedPath.Count == 0) return false;
        for (int index = 0; index < adoptedPath.Count; index++) {
            Vector2 offset = adoptedPath[index].endPivot - position;
            if (!Finite(offset) || offset.sqrMagnitude < 2.25f) continue;
            aim = offset.normalized;
            return true;
        }
        Vector2 last = adoptedPath[adoptedPath.Count - 1].endPivot - position;
        if (!Finite(last) || last.sqrMagnitude <= 0.25f) return false;
        aim = last.normalized;
        return true;
    }

    /// <summary>Measured surface gap to the player: 0 while overlapping, +infinity when it cannot be measured.</summary>
    float PlayerSurfaceGap(out bool overlapped) {
        overlapped = false;
        if (body == null || body.MainCollider == null || targetCollider == null) return float.PositiveInfinity;
        ColliderDistance2D separation = body.MainCollider.Distance(targetCollider);
        if (!separation.isValid) return float.PositiveInfinity;
        overlapped = separation.isOverlapped;
        if (overlapped) return 0f;
        return Finite(separation.distance) ? separation.distance : float.PositiveInfinity;
    }

    /// <summary>Full-throttle shove toward the player with damped steering, used while contact pressure owns the motor.</summary>
    NpcDriveCommand PinCommand(Vector2 forward, Vector2 position) {
        Vector2 delta = targetBody.position - position;
        if (!Finite(delta) || delta.sqrMagnitude <= 0.0001f)
            return new NpcDriveCommand(1f, 0f, 0f, motor.maxSpeed, false);
        float bearing = Vector2.SignedAngle(forward, delta.normalized);
        return new NpcDriveCommand(1f, 0f, pin.Steering(bearing), motor.maxSpeed, false);
    }

    /// <summary>
    /// Shadow role: drive to a point slightly ahead of the player, or park across a side street it could
    /// turn into, matching the player's speed instead of ramming it. The planner still owns detours
    /// whenever static geometry blocks the direct way to that point.
    /// </summary>
    NpcDriveCommand ShadowCommand(float deltaTime, Vector2 forward, Vector2 position, Vector2 velocity) {
        if (!hasShadowGoal || clock >= nextShadowGoalAt) {
            SelectShadowGoal(position);
            nextShadowGoalAt = clock + ShadowGoalInterval;
        }
        Vector2 player = targetBody.position;
        Vector2 goal = shadowGoal.point;
        Vector2 aim = PassBesidePlayer(position, goal, player);
        Vector2 delta = aim - position;
        float distance = Vector2.Distance(position, goal);
        if (!Finite(delta) || delta.sqrMagnitude <= 0.0001f) return new NpcDriveCommand(0f, 1f, 0f, 0f, false);

        // The planner cursor advances every tick, as for the other roles, even when its output is unused.
        NpcDriveCommand guided = driver.Tick(new PoliceFreePursuitDriver.Pose(position, forward, velocity), deltaTime, avoidance);
        lastDecisionClock = clock;
        if (!IsDirectCorridorClear(position, delta)) {
            if (TryGetDetourAim(position, out Vector2 detourAim)) delta = detourAim * Mathf.Max(1f, delta.magnitude);
            else if (Mathf.Abs(guided.steering) > 0.01f || guided.throttle > 0.01f) return guided;
        }

        float angle = Vector2.SignedAngle(forward, delta.normalized);
        float steering = ProportionalSteering(angle);
        if (shadowGoal.blocking && distance <= ShadowStopDistance)
            return new NpcDriveCommand(0f, 1f, 0f, 0f, false);

        float playerSpeed = targetBody.linearVelocity.magnitude;
        // Behind the goal: catch up faster than the player; at it: hold the player's pace; blocking:
        // slow down smoothly into the mouth so the unit stops across it instead of overshooting.
        float desired = shadowGoal.blocking
            ? Mathf.Lerp(1.5f, motor.maxSpeed, Mathf.InverseLerp(ShadowStopDistance, 8f, distance))
            : playerSpeed + Mathf.Clamp(distance - 1f, -2f, 4f) * 1.2f;
        desired = Mathf.Clamp(desired, 0f, Mathf.Min(motor.maxSpeed, TurnLimitedSpeed(angle)));
        float current = Vector2.Dot(velocity, forward);
        if (current > desired + 0.75f)
            return new NpcDriveCommand(0f, Mathf.Clamp01((current - desired) / 3f), steering, desired, false);
        return new NpcDriveCommand(1f, 0f, steering, desired, false);
    }

    void SelectShadowGoal(Vector2 position) {
        var footprint = new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset);
        bool Clear(Vector2 from, Vector2 to) {
            if (query == null || geometry == null) return true;
            Vector2 segment = to - from;
            float length = segment.magnitude;
            if (length <= 0.01f) return true;
            Vector2 startLocal = MapNavigationCoordinates.WorldToLocal(from, origin);
            return query.CheckLine(startLocal, startLocal + segment, footprint,
                MapNavigationCoordinates.DirectionToHeadingDegrees(segment / length), navigation.clearanceMargin,
                new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear;
        }
        bool Available(Vector2 mouth) => tactics == null ||
            tactics.IsOpeningFree(lifeId, mouth, ShadowOpeningRadius, clock);

        PoliceShadowTactics.Goal next = PoliceShadowTactics.Select(targetBody.position, targetBody.linearVelocity,
            position, shadowSettings, Clear, Available);
        if (next.blocking) tactics?.TryClaimOpening(lifeId, next.point, ShadowOpeningRadius, clock, ShadowOpeningClaimSeconds);
        else tactics?.ReleaseOpening(lifeId);
        if (!hasShadowGoal || Vector2.Distance(next.point, shadowGoal.point) > 0.5f) strategicDirty = true;
        shadowGoal = next;
        hasShadowGoal = true;
    }

    // A goal ahead of the player often lies straight through the player's car; aim beside it instead.
    static Vector2 PassBesidePlayer(Vector2 position, Vector2 goal, Vector2 player) {
        Vector2 line = goal - position;
        float length = line.magnitude;
        if (length <= 0.01f) return goal;
        Vector2 direction = line / length;
        float along = Vector2.Dot(player - position, direction);
        if (along <= 0f || along >= length) return goal;
        Vector2 normal = new Vector2(-direction.y, direction.x);
        float offset = Vector2.Dot(player - position, normal);
        if (Mathf.Abs(offset) >= ShadowPassClearance) return goal;
        float side = offset >= 0f ? -1f : 1f;
        return player + normal * (side * ShadowPassClearance);
    }

    Vector2 ApplyApproachOffset(Vector2 position) {
        if (interceptFallback || !hasClaim || !tactics.IsClaimCurrent(claim, clock)) return position;
        Vector2 forward = targetBody.linearVelocity.sqrMagnitude > 0.0001f ? targetBody.linearVelocity.normalized : (Vector2)targetBody.transform.up;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 direction = claim.slot == PoliceTacticsCoordinator.ApproachSlot.Front ? forward :
            claim.slot == PoliceTacticsCoordinator.ApproachSlot.Rear ? -forward :
            claim.slot == PoliceTacticsCoordinator.ApproachSlot.Left ? -right : right;
        float gap = geometry.ColliderSurfaceRadius + playerSurfaceRadius + driving.stopGap;
        Vector2 point = position + direction * gap + right * (traits.sidePreference * driving.stopGap);
        var footprint = new PoliceFreeNavigationGeometry.Footprint(geometry.colliderFootprint, geometry.colliderOffset);
        return query.CheckPose(point - origin, body.Body.rotation, footprint, navigation.clearanceMargin,
            new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear ? point : position;
    }

    bool IsAtHoldingStandoff(float speed) {
        if (!Finite(speed) || speed > driving.stoppedSpeedThreshold || targetBody == null ||
            targetBody.linearVelocity.magnitude > driving.stoppedSpeedThreshold ||
            targetCollider == null || body.MainCollider == null) return false;
        var separation = body.MainCollider.Distance(targetCollider);
        return separation.isValid && !separation.isOverlapped && Finite(separation.distance) &&
            Mathf.Abs(separation.distance - driving.stopGap) <= chase.holdingTolerance;
    }

    bool RearIsClear() {
        if (body.Sensor == null || !FinitePositive(driving.reverseMaxDistance)) return false;
        VehicleObstacleSensor.SweepStatus status = body.Sensor.QuerySweep(-body.transform.up,
            driving.reverseMaxDistance, out _, out _);
        return status == VehicleObstacleSensor.SweepStatus.Clear;
    }

    bool IsBound() => body != null && body.Body != null && body.Motor != null && body.Sensor != null &&
        world != null && world.IsActive && target != null && targetBody != null &&
        target.Identity.lifeId == targetLifeId && body.DamageReceiver != null &&
        body.DamageReceiver.Identity.lifeId == lifeId;

    bool IsClockCurrent(float value) => Finite(value) && value >= clock;

    void HandleImpact(float impact) {
        if (!reset && Finite(impact) && impact >= driving.recovery.lightImpactSpeed) {
            if (chase == null || !chase.recklessPursuit) recovery.ReportImpact();
            ramDecision.Cancel(clock);
        }
    }

    static PoliceDrivingSettings CreateDrivingSettings(PoliceDrivingSettings authored, PoliceFreeChaseSettings chase) {
        PoliceDrivingSettings result = authored != null ? authored.Clone() : new PoliceDrivingSettings();
        if (chase == null) return result;
        result.lateralAcceleration = chase.lateralAcceleration;
        result.avoidanceHorizon = chase.avoidanceHorizonSeconds;
        result.avoidanceCommitment = chase.avoidanceCommitmentSeconds;
        result.avoidanceProgressWeight = chase.progressWeight;
        result.avoidanceClearanceWeight = chase.clearanceWeight;
        result.avoidanceHeadingWeight = chase.headingWeight;
        result.avoidanceSwitchWeight = chase.switchWeight;
        result.stuckDuration = chase.stuckDurationSeconds;
        result.reverseMaxDistance = chase.reverseDistance;
        result.reverseMaxDuration = chase.reverseDurationSeconds;
        result.reverseMaxAttempts = chase.maximumReverseAttempts;
        result.reverseCooldown = chase.reverseCooldownSeconds;
        if (chase.recklessPursuit) {
            result.ramConeDegrees = Mathf.Clamp(chase.recklessRamConeDegrees, 1f, 89f);
            result.ramMaximumSurfaceGap = Mathf.Min(Mathf.Max(result.ramMaximumSurfaceGap, chase.recklessRamDistance), result.maxSweepDistance);
            // Obstacle escape is a quick tactical correction, not a civilian crash recovery.
            result.reverseMaxDistance = Mathf.Min(result.reverseMaxDistance, 1.4f);
            result.reverseMaxDuration = Mathf.Min(result.reverseMaxDuration, 1f);
            result.reverseCooldown = Mathf.Min(result.reverseCooldown, 0.25f);
            // Reckless police re-engage quickly and commit longer once a ram has started.
            result.ramCooldownSeconds = Mathf.Min(result.ramCooldownSeconds, 0.5f);
            result.ramMaximumActiveSeconds = Mathf.Max(result.ramMaximumActiveSeconds, 1.5f);
            if (result.recovery != null) {
                result.recovery.settleTimeoutSeconds = Mathf.Min(result.recovery.settleTimeoutSeconds, 0.75f);
                result.recovery.maneuverSpeed = Mathf.Max(result.recovery.maneuverSpeed, 2.5f);
            }
        }
        return result;
    }

    static NpcMotorSettings CreateMotorSettings(NpcMotorSettings authored, PoliceDriverTraits traits) {
        if (authored == null) return null;
        var result = new NpcMotorSettings {
            cruiseSpeed = authored.cruiseSpeed,
            maxSpeed = authored.maxSpeed,
            reverseSpeed = authored.reverseSpeed,
            acceleration = authored.acceleration,
            brakeDeceleration = authored.brakeDeceleration,
            maxEngineForce = authored.maxEngineForce,
            maxBrakeForce = authored.maxBrakeForce,
            turnRate = authored.turnRate,
            minimumTurningRadius = authored.minimumTurningRadius,
            lateralGrip = authored.lateralGrip,
            sensorInterval = authored.sensorInterval,
            reactionTime = authored.reactionTime * traits.reactionMultiplier,
            minimumGap = authored.minimumGap
        };
        return result;
    }


    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
}
