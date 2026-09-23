using UnityEngine;

/// <summary>
/// Drives one already-active police life toward a live player on a proven road span.
/// Composition owns pooling, damage binding and lifecycle; this component only validates those
/// bindings, queries the accepted planner on its cadence, and sends safe commands to the motor.
/// A single certified 90-degree-style road bend may use a bounded measured turn transaction;
/// aligned initial/final connectors retain fixed geometry across compatible refreshes.
/// Holding requires actual target-surface standoff; explicit Ram intent additionally requires
/// a fixed straight stopping certificate. Bounded impact recovery preempts pursuit and Ram.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(PoliceVehicleBody))]
public sealed class PolicePursuitController : MonoBehaviour {
    internal interface IFreeDrive {
        bool IsRamming { get; }
        bool IsRecovering { get; }
        bool IsHolding { get; }
        bool TrySetTarget(VehicleDamageReceiver receiver);
        NpcDriveCommand Tick(float deltaTime, bool paused);
        bool PrepareRelocation();
        void Reset();
    }

    IFreeDrive freeDrive;

    internal bool TryBindFreeDrive(PoliceVehicleBody value, TrafficDamageWorld world, IFreeDrive policy, NpcVehicleProfile profile) {
        ResetForNewLife();
        if (value == null || value.gameObject != gameObject || world == null || !world.IsActive || policy == null ||
            profile == null || value.DamageReceiver == null || !value.DamageReceiver.CanTakeDamage) return false;
        body = value;
        damageWorld = world;
        coordinator = world.Coordinator;
        boundPoliceIdentity = value.DamageReceiver.Identity;
        sharedProfile = profile;
        freeDrive = policy;
        coordinator.Ended += HandleSessionEnded;
        bound = true;
        return true;
    }
    /// <summary>Current bounded traversal phase exposed for diagnostics without granting mutation.</summary>
    public enum TraversalPhase {
        /// <summary>Measured road traversal using the accepted cursor and existing corner transaction.</summary>
        Road,
        /// <summary>Measured straight approach to the certified road-start anchor before cursor advancement.</summary>
        InitialConnector,
        /// <summary>Measured straight approach beyond the completed road anchor toward the player.</summary>
        FinalConnector,
        /// <summary>Low actual speed at the live target's measured surface standoff, with continuing validation.</summary>
        Holding,
        /// <summary>Forward arc after a measured road-exit crossing, before tangent straight traversal.</summary>
        FinalTurn
    }

    /// <summary>Explicit dependencies supplied by the police composition after its pool life is active.</summary>
    public sealed class Binding {
        /// <summary>Same-root police component bundle controlled by this instance.</summary>
        public PoliceVehicleBody body;
        /// <summary>Police wrapper which selects the exact shared NPC profile.</summary>
        public PoliceVehicleProfile vehicle;
        /// <summary>Explicit Pursue, Intercept or Ram intent and its detached driving settings source.</summary>
        public PoliceBehaviorProfile behavior;
        /// <summary>Active damage world owning both police and player lives.</summary>
        public TrafficDamageWorld damageWorld;
        /// <summary>Directed graph used by the accepted pursuit planner.</summary>
        public RoadGraphRuntime graph;
        /// <summary>Detached navigation data and local bounds for planning.</summary>
        public MapNavigationDocument navigation;
        /// <summary>World origin applied exactly once to map-local coordinates.</summary>
        public Vector2 mapOriginWorld;
        /// <summary>Static geometry query shared by planning and fresh maneuver envelopes; required for committed turns.</summary>
        public IAreaClearanceQuery staticClearance;
        /// <summary>Validated navigation cadence and route constraints.</summary>
        public PoliceNavigationSettings navigationSettings;
    }

    PoliceVehicleBody body;
    PoliceVehicleProfile vehicle;
    PoliceBehaviorProfile behavior;
    TrafficDamageWorld damageWorld;
    TrafficSessionCoordinator coordinator;
    RoadGraphRuntime graph;
    MapNavigationDocument navigation;
    IAreaClearanceQuery staticClearance;
    PoliceNavigationSettings navigationSettings;
    PoliceDrivingSettings driving;
    PolicePursuitTargetPlanner planner;
    readonly PolicePathCursor cursor = new PolicePathCursor();
    readonly PoliceTurnTraversal turnTraversal = new PoliceTurnTraversal();
    readonly PoliceFinalApproachTraversal finalApproachTraversal = new PoliceFinalApproachTraversal();
    readonly PoliceRamDecision ramDecision = new PoliceRamDecision();
    readonly PoliceImpactRoadEvidence impactRoadEvidence = new PoliceImpactRoadEvidence();
    PoliceRamSafety ramSafety;
    PoliceStraightRoadClearance straightClearance;
    PoliceRecoveryIntegration recovery;
    PoliceTacticalRole boundIntent;
    VehicleDamageReceiver ramImpactReceiver;
    int ramStarts;
    VehicleDamageReceiver target;
    Rigidbody2D targetBody;
    NpcVehicleProfile sharedProfile;
    NpcMotorSettings motorSettings;
    Vector2 mapOriginWorld;
    Rect boundLocalBounds;
    float lastActiveClock;
    Vector2 footprint;
    Vector2 boundProfileColliderSize;
    Vector2 boundColliderOffsetWorld;
    Vector2 boundScale;
    Vector3 boundLocalScale;
    Vector2 boundColliderSize;
    Vector2 boundColliderOffset;
    float boundMass;
    float boundCrashMultiplier;
    MotorFacts boundMotorFacts;
    float cachedRouteEdgeSpeed;
    PoliceRoadTargetQuery.Result acceptedResult;
    VehicleIdentity boundPoliceIdentity;
    VehicleIdentity boundTargetIdentity;
    bool bound;
    bool awaitingFreshPlan;
    int provenRoadContinuations;
    int connectorMetadataContinuations;
    TraversalPhase phase = TraversalPhase.Road;
    readonly PoliceStraightConnector initialLine = new PoliceStraightConnector();
    readonly PoliceStraightConnector finalLine = new PoliceStraightConnector();
    PolicePathCursor.TrackingLimits trackingLimits;
    Collider2D observedTargetCollider;
    int finalConnectorRefreshes;
    RoadPathQuery.EdgeAnchor initialRoadStartAnchor;
    Collider2D[] finalStepHits;
    float finalObservedSpeed;
    bool finalDiagnosticEnabled;
    string firstFinalDiagnostic;

    /// <summary>Latest command issued to the bound motor; diagnostics never mutate it.</summary>
    public NpcDriveCommand LastCommand { get; private set; } = NpcDriveCommand.Stopped;
    /// <summary>Most recent safe planned speed before command brake selection.</summary>
    public float LastPlannedSpeed { get; private set; }
    /// <summary>Most recent world-space lookahead point, or zero while waiting.</summary>
    public Vector2 LastAimPoint { get; private set; }
    /// <summary>Measured route distance accepted by the bound cursor.</summary>
    public float CursorProgress => cursor.ProgressDistance;
    /// <summary>Number of expensive planner attempts made under this active binding.</summary>
    public int PlannerAttemptCount => planner != null ? planner.AttemptCount : 0;
    /// <summary>True while a bounded certified turn defers only strategic replanning.</summary>
    public bool IsTurnCommitted => turnTraversal.CurrentState == PoliceTurnTraversal.State.Active;
    /// <summary>True after the original turn bounds expire; ordinary waiting cannot renew this certificate.</summary>
    public bool IsTurnExpired => turnTraversal.CurrentState == PoliceTurnTraversal.State.Expired;
    /// <summary>Retained corner cap while a certified turn is active, or zero otherwise.</summary>
    public float ActiveTurnCap => turnTraversal.CurrentState == PoliceTurnTraversal.State.Active ? turnTraversal.Cap : 0f;
    /// <summary>Number of fresh route results proven to retain the measured cursor suffix.</summary>
    public int ProvenRoadContinuations => provenRoadContinuations;
    /// <summary>Number of proven suffix continuations whose metadata required a connector.</summary>
    public int ConnectorMetadataContinuations => connectorMetadataContinuations;
    /// <summary>True only after a police life has been accepted; target selection is separately observable through driving commands.</summary>
    public bool IsBound => bound;
    /// <summary>True only while the bound police life delegates to graph-independent driving.</summary>
    public bool UsesFreeDrive => bound && freeDrive != null;

    /// <summary>
    /// Prepares this active police binding for an owner-controlled off-screen relocation without
    /// changing its life identity, target identity, damage state, or planner retry cadence.
    /// </summary>
    /// <returns>True only when ordinary bound driving state was valid and relocation was admitted.</returns>
    public bool TryPrepareRelocation() {
        if (freeDrive != null) return bound && !IsRamming && freeDrive.PrepareRelocation();
        if (!bound || IsRamming || IsRecovering || IsTurnCommitted || !ValidatePoliceState()) return false;
        ClearRoute(true);
        planner?.InvalidateRoute();
        Stop();
        return true;
    }

    /// <summary>Current measured phase; connector transitions require real join-plane crossing.</summary>
    public TraversalPhase CurrentPhase => freeDrive != null && freeDrive.IsHolding ? TraversalPhase.Holding : phase;
    /// <summary>Measured monotonic projection along the fixed initial connector line.</summary>
    public float InitialConnectorProgress => phase == TraversalPhase.InitialConnector ? initialLine.Progress : initialLine.Length;
    /// <summary>Measured monotonic projection on the fixed final line, cleared on route invalidation.</summary>
    public float FinalConnectorProgress => finalApproachTraversal.CurrentState == PoliceFinalApproachTraversal.State.Active
        ? finalApproachTraversal.Progress : finalLine.Progress;
    /// <summary>Fresh compatible final/holding results accepted without resetting the completed road cursor.</summary>
    public int FinalConnectorRefreshes => finalConnectorRefreshes;
    /// <summary>True only during a bounded, currently certified straight ram attempt.</summary>
    public bool IsRamming => freeDrive != null ? freeDrive.IsRamming : ramDecision.IsActive;
    /// <summary>Number of admitted ram attempts in this binding, not damage or contact count.</summary>
    public int RamStarts => ramStarts;
    /// <summary>Original cancellation deadline; repeated blocked ticks cannot extend it.</summary>
    public float RamCooldownUntil => ramDecision.CooldownUntil;
    /// <summary>True while bounded impact recovery owns the motor, including exhausted waiting.</summary>
    public bool IsRecovering => freeDrive != null ? freeDrive.IsRecovering : recovery != null && recovery.IsRecovering;
    /// <summary>Current shared crash-policy phase, independent of receiver-owned crash mode.</summary>
    public CrashRecoveryPolicy.Phase RecoveryPhase => recovery != null ? recovery.Phase : CrashRecoveryPolicy.Phase.Driving;
    /// <summary>True after original recovery bounds forbid more powered recovery attempts.</summary>
    public bool IsRecoveryExhausted => recovery != null && recovery.IsExhausted;
    bool IsFinalPhase => phase == TraversalPhase.FinalTurn || phase == TraversalPhase.FinalConnector || phase == TraversalPhase.Holding;
    bool IsCurvedFinal => acceptedResult != null && acceptedResult.finalApproachCurved;

    /// <summary>
    /// Validates and captures one active police life without changing pool, motor, damage, or
    /// reservation state. A failed bind clears this controller and leaves a full-brake command.
    /// </summary>
    /// <param name="binding">Composition-owned dependencies for one already active police life.</param>
    /// <param name="reason">Stable diagnostic explaining why the candidate binding was refused.</param>
    /// <returns>True when a fully verified, detached pursuit binding was established.</returns>
    public bool TryBind(Binding binding, out string reason) {
        ResetForNewLife();
        reason = null;
        if (!ValidateBinding(binding, out reason)) return false;
        body = binding.body; vehicle = binding.vehicle; behavior = binding.behavior; damageWorld = binding.damageWorld;
        coordinator = damageWorld.Coordinator; graph = binding.graph; navigation = binding.navigation;
        staticClearance = binding.staticClearance; navigationSettings = binding.navigationSettings.Clone();
        driving = behavior.driving.Clone(); sharedProfile = vehicle.sharedNpc; motorSettings = sharedProfile.motorSettings;
        trackingLimits = new PolicePathCursor.TrackingLimits(driving.cursorWindow, driving.maxDeviation, driving.displacementSlack, driving.acquisitionTolerance);
        mapOriginWorld = binding.mapOriginWorld; footprint = body.WorldFootprint; boundMass = body.Body.mass;
        boundLocalBounds = navigation.localBounds; lastActiveClock = damageWorld.SessionTime;
        boundProfileColliderSize = sharedProfile.colliderSize;
        boundCrashMultiplier = body.Motor.crashModeForceMultiplier;
        boundMotorFacts = new MotorFacts(motorSettings);
        boundPoliceIdentity = body.DamageReceiver.Identity;
        boundScale = new Vector2(body.transform.lossyScale.x, body.transform.lossyScale.y);
        boundLocalScale = body.transform.localScale;
        boundColliderSize = body.MainCollider.size;
        boundColliderOffset = body.MainCollider.offset;
        boundColliderOffsetWorld = Vector2.Scale(body.MainCollider.offset, boundScale);
        boundIntent = behavior.tacticalRole;
        if (!ramDecision.TryConfigure(driving)) { reason = "invalid ram settings"; ResetForNewLife(); return false; }
        ramSafety = new PoliceRamSafety(body, graph, boundLocalBounds, mapOriginWorld, staticClearance, driving,
            motorSettings, footprint, boundColliderOffsetWorld, boundCrashMultiplier, navigationSettings.clearanceMargin);
        planner = new PolicePursuitTargetPlanner(graph, navigation, boundLocalBounds, sharedProfile,
            boundColliderOffsetWorld, staticClearance, navigationSettings, behavior.intercept, boundIntent,
            boundPoliceIdentity.lifeId, driving.planningDistance);
        recovery = new PoliceRecoveryIntegration(body, graph, planner, boundLocalBounds, mapOriginWorld,
            staticClearance, driving, motorSettings, footprint, boundColliderOffsetWorld,
            navigationSettings.clearanceMargin, boundCrashMultiplier);
        straightClearance = new PoliceStraightRoadClearance(body, graph, boundLocalBounds, mapOriginWorld,
            staticClearance, driving, motorSettings, footprint, boundColliderOffsetWorld,
            boundCrashMultiplier, navigationSettings.clearanceMargin);
        body.Sensor.Configure(motorSettings, driving.sensorBuffer);
        finalStepHits = new Collider2D[driving.sensorBuffer];
        coordinator.Ended += HandleSessionEnded;
        ramImpactReceiver = body.DamageReceiver;
        ramImpactReceiver.ImpactObserved += HandleRamImpact;
        bound = true;
        return true;
    }

    /// <summary>
    /// Selects an already registered player receiver. Replacing a target immediately brakes and
    /// invalidates route state without resetting the planner, preserving its hard retry cadence.
    /// </summary>
    /// <param name="receiver">Real player receiver from the same active damage world.</param>
    /// <param name="reason">Stable diagnostic explaining why the target was refused.</param>
    /// <returns>True when the live player target is cached for subsequent ticks.</returns>
    public bool TrySetTarget(VehicleDamageReceiver receiver, out string reason) {
        reason = null;
        if (freeDrive != null) {
            bool accepted = bound && freeDrive.TrySetTarget(receiver);
            if (!accepted) reason = "free-drive target is not the active player life";
            return accepted;
        }
        if (!bound || receiver == null || coordinator == null || !coordinator.PlayerIdentity.HasValue ||
            !receiver.IsBoundTo(damageWorld, coordinator.PlayerIdentity.Value) || receiver.Identity.role != VehicleRole.Player) {
            CancelTarget();
            reason = "target is not the active player life";
            return false;
        }
        var receiverBody = receiver.GetComponent<Rigidbody2D>();
        if (!IsActiveTargetCandidate(receiver, receiverBody) || receiverBody.gameObject.scene.GetPhysicsScene2D() != body.Body.gameObject.scene.GetPhysicsScene2D()) {
            CancelTarget();
            reason = "target physics scene does not match police";
            return false;
        }
        if (target != receiver || boundTargetIdentity.lifeId != receiver.Identity.lifeId) {
            recovery?.CancelMovement();
            target = receiver; targetBody = receiverBody;
            boundTargetIdentity = receiver.Identity;
            ClearRoute(true);
            planner.ResetForTargetLife(boundTargetIdentity.lifeId);
            Stop();
        }
        return true;
    }

    /// <summary>
    /// Issues one deterministic pursuit command using the damage world's active session clock.
    /// Paused, stale, unsupported, invalid, or budget-exhausted states stop without querying or
    /// propelling; equal catch-up clock values remain eligible for normal motor commands.
    /// </summary>
    /// <param name="deltaTime">Finite positive physics-step delta.</param>
    /// <param name="paused">Explicit caller pause gate; paused steps make no route or sensor query.</param>
    public void Tick(float deltaTime, bool paused) {
        if (freeDrive != null) {
            if (!bound || body == null || damageWorld == null || !damageWorld.IsActive ||
                coordinator == null || !coordinator.IsActive ||
                !body.DamageReceiver.IsBoundTo(damageWorld, boundPoliceIdentity, sharedProfile)) {
                ResetForNewLife();
                return;
            }
            LastCommand = freeDrive.Tick(deltaTime, paused || Time.timeScale <= 0f);
            LastPlannedSpeed = LastCommand.targetSpeed;
            body.Motor.SetReverseManeuverPermission(LastCommand.reverseAllowed);
            body.Motor.SetCommand(LastCommand);
            return;
        }
        impactRoadEvidence.Clear();
        if (!FinitePositive(deltaTime) || paused) { recovery?.Pause(); Stop(); return; }
        if (!ValidatePoliceState()) { CloseInvalidBinding(); return; }
        float clock = damageWorld.SessionTime;
        if (!FiniteNonnegative(clock) || clock < lastActiveClock) { CloseInvalidBinding(); return; }
        lastActiveClock = clock;

        Vector2 localPosition = MapNavigationCoordinates.WorldToLocal(body.Body.position, mapOriginWorld);
        Vector2 forward = body.transform.up;
        if (!Inside(localPosition, boundLocalBounds) || !Finite(body.Body.linearVelocity) ||
            !Finite(body.Body.rotation) || !Finite(body.Body.angularVelocity) || !Finite(forward) || !FinitePositive(forward.sqrMagnitude)) {
            CloseInvalidBinding(); return;
        }
        int work = driving.cursorWork;
        if (!ValidateTargetState()) {
            recovery.Step(deltaTime, clock, null, false, default, ref work);
            CancelTarget(); return;
        }
        bool recoveringBeforeStep = IsRecovering;
        if (recovery.Step(deltaTime, clock, targetBody, phase == TraversalPhase.Road && cursor.IsBound,
            cursor.IsBound ? cursor.CurrentAnchor : default, ref work)) {
            if (!recoveringBeforeStep || recovery.ResumeReady) ClearRoute();
            LastCommand = recovery.Command; LastPlannedSpeed = LastCommand.targetSpeed; LastAimPoint = Vector2.zero;
            body.Motor.SetReverseManeuverPermission(LastCommand.reverseAllowed); body.Motor.SetCommand(LastCommand);
            return;
        }
        if (IsRamming && (!ramDecision.Advance(clock, deltaTime) || !ramSafety.MatchesTarget(targetBody) || body.Motor.IsCrashMode)) {
            RevokeRam(); Stop(); return;
        }
        if (boundIntent == PoliceTacticalRole.Ram && !IsRamming &&
            !ramDecision.CanAttempt(clock, body.Body.linearVelocity.magnitude, driving.stoppedSpeedThreshold, body.Motor.IsCrashMode || IsRecovering)) { Stop(); return; }
        // IsBound becomes false as soon as the graph revision changes, even before Advance runs.
        if (acceptedResult != null && acceptedResult.graphVersion != graph.Version ||
            (IsTurnCommitted || IsTurnExpired || awaitingFreshPlan) && !cursor.IsBound) { FailRouteState(); return; }
        if (IsTurnExpired) { Stop(); return; }
        bool hasAnchor = cursor.IsBound;
        RoadPathQuery.EdgeAnchor anchor = hasAnchor ? cursor.CurrentAnchor : default;
        if (phase == TraversalPhase.Road && hasAnchor && !cursor.Advance(localPosition, ref work)) {
            bool committedCornerHandoff = IsTurnCommitted && work > 0 && cursor.TryAdvanceCommittedCorner(localPosition,
                turnTraversal.Token, turnTraversal.AbsoluteArc, turnTraversal.OutgoingDirection,
                motorSettings.minimumTurningRadius, turnTraversal.MaximumTravel, ref work);
            if (committedCornerHandoff) {
                hasAnchor = cursor.IsBound;
                anchor = cursor.CurrentAnchor;
            } else if (work <= 0 && IsTurnCommitted) {
                // A failed projection cannot release the turn, but waiting still spends its original bounds.
                AccountTurn(clock, localPosition, forward, 0f);
                Stop();
                return;
            } else {
                FailRouteState();
                return;
            }
        }
        hasAnchor = cursor.IsBound;
        if (hasAnchor) anchor = cursor.CurrentAnchor;
        if (phase == TraversalPhase.Road && hasAnchor) impactRoadEvidence.Observe(anchor, graph.Version, boundPoliceIdentity, boundTargetIdentity);

        if (IsTurnCommitted && !AccountTurn(clock, localPosition, forward,
            Mathf.Max(0f, cursor.ProgressDistance - turnTraversal.AbsoluteArc))) return;
        float forwardSpeed = Mathf.Max(0f, Vector2.Dot(body.Body.linearVelocity, forward));
        if (!PoliceDrivingMath.TryBrakeDeceleration(motorSettings, body.Body.mass, body.Motor.IsCrashMode,
            boundCrashMultiplier, driving.comfort, out float deceleration) ||
            !PoliceDrivingMath.TrySweepDistance(forwardSpeed, deceleration, motorSettings.reactionTime, driving.stopGap,
                driving.maxSweepDistance, out float sweepRange)) { Stop(); return; }
        if (!ConsumeWork(ref work)) { Stop(); return; }
        var sweep = body.Sensor.QuerySweep(forward, sweepRange, out float gap, out var obstacle);
        RememberTargetCollider(obstacle);
        if (sweep == VehicleObstacleSensor.SweepStatus.Invalid || sweep == VehicleObstacleSensor.SweepStatus.Saturated) { Stop(); return; }
        float lookahead = Mathf.Clamp(driving.lookaheadMin + forwardSpeed * driving.lookaheadTime, driving.lookaheadMin, driving.lookaheadMax);
        bool hadCertifiedRoute = cursor.IsBound && acceptedResult != null && acceptedResult.status == PoliceRoadTargetQuery.Status.Route;
        if (IsCurvedFinal && (IsFinalPhase || phase == TraversalPhase.Road && cursor.IsComplete && !IsTurnCommitted)) {
            if (!IsFinalPhase && !TryBeginCurvedFinal(localPosition, forward, deltaTime, ref work)) { Stop(); return; }
            DriveCurvedFinal(localPosition, forward, deltaTime, forwardSpeed, deceleration, sweep, gap, ref work);
            return;
        }
        if (phase == TraversalPhase.Road && !IsTurnCommitted && !awaitingFreshPlan && hadCertifiedRoute &&
            !TryCommitExistingTurn(lookahead, deltaTime, forward, forwardSpeed, deceleration, localPosition, ref work, out bool turnWasAhead)) {
            if (turnWasAhead) { Stop(); return; }
        }

        if (!IsTurnCommitted) {
            Vector2 targetPosition = MapNavigationCoordinates.WorldToLocal(targetBody.position, mapOriginWorld);
            if (boundIntent == PoliceTacticalRole.Intercept && acceptedResult != null &&
                acceptedResult.targetKind == PoliceRoadTargetQuery.TargetKind.InterceptJunction &&
                cursor.RemainingDistance <= lookahead) planner.RequestPursueHandoff();
            var result = boundIntent == PoliceTacticalRole.Intercept
                ? planner.Update(localPosition, forward, targetPosition, targetBody.linearVelocity, (Vector2)targetBody.transform.up,
                    boundTargetIdentity.lifeId,
                    damageWorld.SessionTime, hasAnchor, anchor)
                : planner.Update(localPosition, forward, targetPosition, damageWorld.SessionTime, hasAnchor, anchor);
            if (result == null || result.status != PoliceRoadTargetQuery.Status.Route) {
                if (!awaitingFreshPlan) ClearRoute();
                Stop();
                return;
            }
            if (result != acceptedResult) {
                // Every failed continuation waits. Neither a failed clearance nor a spent suffix
                // budget may fall through to new-route binding in this tick.
                if (hasAnchor) {
                    if (!TryAcceptContinuation(result, anchor, localPosition, ref work)) { FailRouteState(); return; }
                } else if (!BindNewRoute(result, localPosition, forward)) { FailRouteState(); return; }
            }
            awaitingFreshPlan = false;
            if (phase == TraversalPhase.Road && (!cursor.IsBound || !cursor.Advance(localPosition, ref work))) {
                ClearRoute(); planner.InvalidateRoute(); Stop(); return;
            }
            // A new result can be committed only on a later tick, before the next planner.Update.
        }

        if (!cursor.IsBound) { Stop(); return; }
        float connectorObstacleCap = float.PositiveInfinity;
        float connectorForwardSpeed = forwardSpeed;
        Collider2D connectorObstacle = null;
        if (phase == TraversalPhase.InitialConnector && !TryQueryConnectorSensor(initialLine, deceleration, ref work,
            out connectorObstacleCap, out connectorForwardSpeed, out connectorObstacle)) {
            Stop(); return;
        }
        if (phase == TraversalPhase.InitialConnector) {
            if (!initialLine.TryAdvance(localPosition, forward, ref work, out bool joined)) {
                if (work <= 0) Stop();
                else FailRouteState();
                return;
            }
            if (joined) {
                if (!TryValidateConnector(initialLine, localPosition, deltaTime, ref work)) { Stop(); return; }
                if (!cursor.Advance(localPosition, ref work)) { FailRouteState(); return; }
                phase = TraversalPhase.Road;
            }
        }
        bool finalReady = !IsTurnCommitted && TryPrepareFinalConnector(localPosition, forward, ref work);
        if (IsFinalPhase && !finalReady) { Stop(); return; }
        if (phase == TraversalPhase.Road && finalReady && cursor.IsComplete) {
            if (IsCurvedFinal) {
                if (!TryBeginCurvedFinal(localPosition, forward, deltaTime, ref work)) { Stop(); return; }
                DriveCurvedFinal(localPosition, forward, deltaTime, forwardSpeed, deceleration, sweep, gap, ref work);
                return;
            }
            if (Vector2.Dot(localPosition - finalLine.Start, finalLine.Direction) >= 0f) phase = TraversalPhase.FinalConnector;
        }
        bool atStandoff = false;
        if (IsFinalPhase) {
            if (!finalLine.TryAdvance(localPosition, forward, ref work, out _)) {
                if (work <= 0) Stop(); else FailRouteState();
                return;
            }
            if (!TryQueryConnectorSensor(finalLine, deceleration, ref work, out connectorObstacleCap, out connectorForwardSpeed, out connectorObstacle) ||
                !TryValidateConnector(finalLine, localPosition, deltaTime, ref work)) { Stop(); return; }
            atStandoff = IsAtTargetStandoff(ref work);
            phase = TraversalPhase.FinalConnector;
        }
        if (!IsCurvedFinal && TryRamMotion(deltaTime, forwardSpeed, connectorObstacle, ref work)) return;
        if (atStandoff) { phase = TraversalPhase.Holding; Stop(); return; }
        float connectorRemaining = phase == TraversalPhase.InitialConnector ? initialLine.Remaining : 0f;
        float turnScanDistance = Mathf.Max(Mathf.Max(driving.planningDistance, sweepRange), lookahead);
        if (phase == TraversalPhase.InitialConnector) turnScanDistance = Mathf.Max(0f, turnScanDistance - connectorRemaining);
        var turn = cursor.TryGetNextTurn(turnScanDistance, ref work, out _, out var incoming, out var outgoing, out float turnDistance);
        if (turn == PolicePathCursor.TurnQueryStatus.InvalidInput || turn == PolicePathCursor.TurnQueryStatus.BudgetExceeded ||
            turn == PolicePathCursor.TurnQueryStatus.StaleGraph) { Stop(); return; }
        float finalRemaining = finalReady ? (IsCurvedFinal ? acceptedResult.finalApproachPlan.TotalLength : finalLine.Remaining) : 0f;
        float endpointDistance = Mathf.Max(0f, connectorRemaining + cursor.RemainingDistance + finalRemaining - driving.stopGap - forwardSpeed * motorSettings.reactionTime);
        if (!PoliceDrivingMath.TryAllowedSpeed(0f, endpointDistance, deceleration, out float endpointSpeed)) { Stop(); return; }
        float planned = Mathf.Min(Mathf.Min(motorSettings.cruiseSpeed, motorSettings.maxSpeed), Mathf.Min(cachedRouteEdgeSpeed, endpointSpeed));
        planned = Mathf.Min(planned, connectorObstacleCap);
        if (finalReady && IsCurvedFinal) {
            float entryDistance = Mathf.Max(0f, cursor.RemainingDistance - forwardSpeed * motorSettings.reactionTime);
            if (!PoliceDrivingMath.TryAllowedSpeed(driving.cornerSpeed, entryDistance, deceleration, out float entryCap)) { Stop(); return; }
            planned = Mathf.Min(planned, entryCap);
        }
        if (turn == PolicePathCursor.TurnQueryStatus.Found) {
            float safeTurnDistance = Mathf.Max(0f, connectorRemaining + turnDistance - forwardSpeed * motorSettings.reactionTime);
            if (!PoliceDrivingMath.TryCornerTargetSpeed(motorSettings.cruiseSpeed, driving.cornerSpeed,
                Vector2.Angle(incoming, outgoing), driving.fullSlowdownAngle, out float cornerCap) ||
                !PoliceDrivingMath.TryAllowedSpeed(cornerCap, safeTurnDistance, deceleration, out float turnSpeed)) { Stop(); return; }
            planned = Mathf.Min(planned, turnSpeed);
        }
        if (sweep == VehicleObstacleSensor.SweepStatus.Blocked) {
            float obstacleDistance = Mathf.Max(0f, gap - driving.stopGap - forwardSpeed * motorSettings.reactionTime);
            if (!PoliceDrivingMath.TryAllowedSpeed(0f, obstacleDistance, deceleration, out float obstacleSpeed)) { Stop(); return; }
            planned = Mathf.Min(planned, obstacleSpeed);
        }
        if (!IsTurnCommitted && turn == PolicePathCursor.TurnQueryStatus.Found) lookahead = Mathf.Min(lookahead, connectorRemaining + turnDistance);
        Vector2 aimLocal = default;
        if (phase == TraversalPhase.InitialConnector) {
            float remaining = initialLine.Remaining;
            if (remaining > lookahead) aimLocal = initialLine.SampleAhead(lookahead);
            else {
                float roadAimDistance = Mathf.Max(0f, lookahead - remaining);
                if (turn == PolicePathCursor.TurnQueryStatus.Found) roadAimDistance = Mathf.Min(roadAimDistance, turnDistance);
                roadAimDistance = Mathf.Min(roadAimDistance, cursor.RemainingDistance);
                if (!cursor.TrySampleAhead(roadAimDistance, ref work, out aimLocal)) { Stop(); return; }
            }
        } else if (IsFinalPhase) aimLocal = finalLine.SampleAhead(lookahead);
        else if (IsTurnCommitted) {
            if (!ValidateCommittedCorridor(lookahead, deltaTime, forward, forwardSpeed, deceleration,
                localPosition, ref work, out aimLocal)) { Stop(); return; }
        }
        else if (finalReady && lookahead > cursor.RemainingDistance)
            aimLocal = IsCurvedFinal ? acceptedResult.roadTarget + acceptedResult.finalApproachPlan.entryDirection *
                (lookahead - cursor.RemainingDistance) : finalLine.SampleAhead(lookahead - cursor.RemainingDistance);
        else if (!cursor.TrySampleAhead(lookahead, ref work, out aimLocal)) { Stop(); return; }
        LastAimPoint = MapNavigationCoordinates.LocalToWorld(aimLocal, mapOriginWorld);
        // Use 1f only as a reference speed to classify geometry while stationary; never issue motor yaw at rest.
        if (!PoliceDrivingMath.TrySteering(forward, LastAimPoint - body.Body.position, forwardSpeed > 0f ? forwardSpeed : 1f,
            motorSettings, out float geometricSteering, out float yawSpeed)) { Stop(); return; }
        float steering = forwardSpeed > 0f ? geometricSteering : 0f;
        planned = Mathf.Min(planned, yawSpeed);
        if (phase == TraversalPhase.InitialConnector) {
            if (!TryValidateConnector(initialLine, localPosition, deltaTime, ref work)) { Stop(); return; }
        } else if (IsFinalPhase) {
            // The full remaining connector and actual discrete-step envelope were validated above.
        } else if (IsTurnCommitted) {
            planned = Mathf.Min(planned, turnTraversal.Cap);
        } else if (staticClearance != null) {
            bool farTurn = turn != PolicePathCursor.TurnQueryStatus.Found ||
                (FiniteNonnegative(turnDistance) && turnDistance > lookahead);
            bool farFinal = !finalReady || (FiniteNonnegative(cursor.RemainingDistance) && cursor.RemainingDistance > lookahead);
            bool legacyStraight = geometricSteering != 0f || !farTurn || !farFinal;
            float throttleLimit = 1f;
            if (!legacyStraight && straightClearance == null) { Stop(); return; }
            if (!legacyStraight) {
                var straightResult = straightClearance.Evaluate(cursor, acceptedResult != null ? acceptedResult.graphVersion : -1L, localPosition, aimLocal, planned, steering,
                    deltaTime, ref work, out throttleLimit, turn == PolicePathCursor.TurnQueryStatus.Found || finalReady);
                if (straightResult == PoliceStraightRoadClearance.Result.Stop) { Stop(); return; }
                legacyStraight = straightResult == PoliceStraightRoadClearance.Result.NotApplicable;
                if (legacyStraight) throttleLimit = 1f;
            }
            if (legacyStraight && !TryValidateMovementEnvelope(Vector2.Min(localPosition, aimLocal),
                Vector2.Max(localPosition, aimLocal), localPosition, deltaTime, ref work)) { Stop(); return; }
            IssueMotion(Mathf.Max(forwardSpeed, connectorForwardSpeed), planned, steering, throttleLimit);
            return;
        }
        IssueMotion(Mathf.Max(forwardSpeed, connectorForwardSpeed), planned, steering);
    }

    bool AccountTurn(float clock, Vector2 position, Vector2 forward, float outgoingProgress) {
        var outcome = turnTraversal.Tick(clock, position, outgoingProgress, forward, false);
        if (outcome == PoliceTurnTraversal.UpdateResult.Active) return true;
        planner.InvalidateRoute();
        if (outcome == PoliceTurnTraversal.UpdateResult.Released) {
            acceptedResult = null;
            awaitingFreshPlan = true;
        }
        Stop();
        return false;
    }

    /// <summary>Clears this controller's current pursuit state before an owning composition reuses the police life.</summary>
    public void ResetForNewLife() {
        Stop();
        freeDrive?.Reset();
        freeDrive = null;
        if (ramImpactReceiver != null) ramImpactReceiver.ImpactObserved -= HandleRamImpact;
        ramImpactReceiver = null; ramDecision.ResetForNewLife(); ramSafety = null; straightClearance = null; ramStarts = 0; recovery = null;
        if (coordinator != null) coordinator.Ended -= HandleSessionEnded;
        bound = false; body = null; vehicle = null; behavior = null; damageWorld = null; coordinator = null;
        graph = null; navigation = null; staticClearance = null; navigationSettings = null; driving = null; planner = null;
        target = null; targetBody = null; sharedProfile = null; motorSettings = null; acceptedResult = null; boundPoliceIdentity = default; boundTargetIdentity = default;
        boundLocalBounds = default; lastActiveClock = 0f;
        ClearRoute(true); trackingLimits = null; finalConnectorRefreshes = 0;
        provenRoadContinuations = 0; connectorMetadataContinuations = 0; LastAimPoint = Vector2.zero; LastPlannedSpeed = 0f;
    }

    void FixedUpdate() { Tick(Time.fixedDeltaTime, false); }
    void OnDisable() { ResetForNewLife(); }

    bool ValidateBinding(Binding value, out string reason) {
        reason = null;
        if (value == null || value.body == null || value.body.gameObject != gameObject || value.vehicle == null || value.vehicle.sharedNpc == null ||
            value.behavior == null || !SupportsDrivingIntent(value.behavior.tacticalRole) || value.behavior.driving == null ||
            value.behavior.tacticalRole == PoliceTacticalRole.Intercept && (value.behavior.intercept == null || !value.behavior.intercept.IsValid(out _)) ||
            !value.behavior.driving.IsValid(out _) || value.damageWorld == null || !value.damageWorld.IsActive || value.damageWorld.Coordinator == null ||
            value.graph == null || value.navigation == null || value.navigationSettings == null || !value.navigationSettings.IsValid(out _) ||
            !Finite(value.mapOriginWorld) || !ValidBounds(value.navigation.localBounds) || !FiniteNonnegative(value.damageWorld.SessionTime)) { reason = "pursuit binding has invalid dependencies"; return false; }
        if (value.vehicle.supportedTacticalRoles == null || !value.vehicle.supportedTacticalRoles.Contains(value.behavior.tacticalRole) || value.body.Body == null || value.body.MainCollider == null ||
            value.body.Motor == null || value.body.Sensor == null || value.body.DamageReceiver == null || value.body.Body.gameObject != gameObject ||
            value.body.MainCollider.gameObject != gameObject || value.body.Motor.gameObject != gameObject || value.body.Sensor.gameObject != gameObject || value.body.Body.gravityScale != 0f ||
            value.body.DamageReceiver.gameObject != gameObject || value.body.Body.bodyType != RigidbodyType2D.Dynamic || !value.body.Body.simulated ||
            !value.body.isActiveAndEnabled || !value.body.Motor.isActiveAndEnabled || !value.body.Sensor.isActiveAndEnabled || !value.body.DamageReceiver.isActiveAndEnabled ||
            !value.body.MainCollider.enabled || value.body.MainCollider.isTrigger || !Finite(value.body.MainCollider.offset) || !Finite(value.body.transform.localScale) || !FinitePositive(value.body.transform.lossyScale.x) ||
            !FinitePositive(value.body.transform.lossyScale.y) || !FinitePositive(value.body.Body.mass) || !Finite(value.body.WorldFootprint) ||
            !FinitePositive(value.body.WorldFootprint.x) || !FinitePositive(value.body.WorldFootprint.y) || !MatchesAuthoringFootprint(value.body.WorldFootprint, value.vehicle.sharedNpc.colliderSize)) { reason = "pursuit body is not a valid dynamic police envelope"; return false; }
        var identity = value.body.DamageReceiver.Identity;
        if (identity.role != VehicleRole.Police || !value.body.DamageReceiver.IsBoundTo(value.damageWorld, identity, value.vehicle.sharedNpc) ||
            value.body.Body.gameObject.scene.GetPhysicsScene2D() != value.damageWorld.gameObject.scene.GetPhysicsScene2D()) { reason = "pursuit police life is not bound to the active world"; return false; }
        if (!NpcVehicleBodyValidator.ValidateProfileBody(value.vehicle.sharedNpc, out _) || !FinitePositive(value.body.Motor.crashModeForceMultiplier)) { reason = "pursuit motor facts are invalid"; return false; }
        return true;
    }

    bool ValidatePoliceState() {
        if (!bound || body == null || damageWorld == null || !damageWorld.IsActive || coordinator == null || graph == null || navigation == null || sharedProfile == null || motorSettings == null || vehicle == null || behavior == null ||
            body.Body == null || body.MainCollider == null || body.Motor == null || body.Sensor == null || body.DamageReceiver == null || !body.isActiveAndEnabled ||
            !body.gameObject.activeInHierarchy || body.Body.bodyType != RigidbodyType2D.Dynamic || body.Body.gravityScale != 0f || !body.Body.simulated || !body.MainCollider.enabled || body.MainCollider.isTrigger || !body.Motor.isActiveAndEnabled ||
            !body.Sensor.isActiveAndEnabled || !body.DamageReceiver.isActiveAndEnabled || body.Motor.IsStoppedPermanently || !Finite(body.MainCollider.offset) ||
            !FinitePositive(body.transform.lossyScale.x) || !FinitePositive(body.transform.lossyScale.y) ||
            !body.DamageReceiver.IsBoundTo(damageWorld, boundPoliceIdentity, sharedProfile)) return false;
        return body.Body.mass == boundMass && body.Motor.crashModeForceMultiplier == boundCrashMultiplier && vehicle.sharedNpc == sharedProfile &&
            behavior.tacticalRole == boundIntent && vehicle.supportedTacticalRoles != null && vehicle.supportedTacticalRoles.Contains(boundIntent) &&
            sharedProfile.allowedRoles != null && sharedProfile.allowedRoles.Contains(VehicleRole.Police) &&
            ExactVector(sharedProfile.colliderSize, boundProfileColliderSize) &&
            ExactVector(body.transform.localScale, boundLocalScale) && ExactVector(body.MainCollider.size, boundColliderSize) &&
            ExactVector(body.MainCollider.offset, boundColliderOffset) && DerivedGeometryMatches(body.WorldFootprint, footprint) &&
            DerivedGeometryMatches(new Vector2(body.transform.lossyScale.x, body.transform.lossyScale.y), boundScale) &&
            DerivedGeometryMatches(Vector2.Scale(body.MainCollider.offset, new Vector2(body.transform.lossyScale.x, body.transform.lossyScale.y)), boundColliderOffsetWorld) &&
            body.Body.gameObject.scene.GetPhysicsScene2D() == damageWorld.gameObject.scene.GetPhysicsScene2D() && sharedProfile.motorSettings == motorSettings && boundMotorFacts.Matches(motorSettings);
    }

    bool ValidateTargetState() => target != null && targetBody != null && IsActiveTargetCandidate(target, targetBody) && coordinator != null && coordinator.PlayerIdentity.HasValue &&
        target.IsBoundTo(damageWorld, boundTargetIdentity) && coordinator.PlayerIdentity.Value.lifeId == boundTargetIdentity.lifeId && target.Identity.role == VehicleRole.Player &&
        Inside(MapNavigationCoordinates.WorldToLocal(targetBody.position, mapOriginWorld), boundLocalBounds) &&
        targetBody.gameObject.scene.GetPhysicsScene2D() == body.Body.gameObject.scene.GetPhysicsScene2D();

    bool BindNewRoute(PoliceRoadTargetQuery.Result result, Vector2 localPosition, Vector2 forward) {
        if (result.graphVersion != graph.Version || result.spans == null || result.spans.Count == 0) return false;
        var budget = new RoadPathQuery.SearchBudget(driving.bindWork);
        if (!cursor.TryBind(graph, result.graphVersion, result.spans, trackingLimits, budget)) return false;
        float minimum = float.PositiveInfinity;
        for (int i = 0; i < result.spans.Count; i++) {
            if (!budget.TryConsume(1)) { cursor.Reset(); return false; }
            var edge = graph.GetEdge(result.spans[i].edgeId);
            if (edge == null || !FinitePositive(edge.speedLimit)) { cursor.Reset(); return false; }
            minimum = Mathf.Min(minimum, edge.speedLimit);
        }
        if (!FinitePositive(minimum)) { cursor.Reset(); return false; }
        bool hasTangents = cursor.TryGetEndpointTangents(out var firstDirection, out _);
        if (result.requiresConnector) {
            if (!hasTangents || (!TryAcquireRoadCorridor(result, localPosition, forward, firstDirection, budget) &&
                !TryBeginInitialConnector(result, localPosition, forward, firstDirection))) { cursor.Reset(); return false; }
        } else if (hasTangents && Vector2.Angle(forward, firstDirection) > driving.connectorAlignmentDegrees) { cursor.Reset(); return false; }
        cachedRouteEdgeSpeed = minimum; acceptedResult = result;
        return true;
    }

    bool TryAcquireRoadCorridor(PoliceRoadTargetQuery.Result result, Vector2 position, Vector2 forward,
        Vector2 tangent, RoadPathQuery.SearchBudget budget) {
        // A small lateral tracking error is already inside the road. It must not require a
        // perpendicular initial connector that a forward-only car can never traverse.
        Vector2 delta = position - result.startRoadPoint;
        if (!Finite(delta) || delta.magnitude > driving.maxDeviation ||
            Mathf.Abs(Vector2.Dot(delta, tangent)) > driving.acquisitionTolerance ||
            Vector2.Angle(forward, tangent) > driving.connectorAlignmentDegrees || !budget.TryConsume(1)) return false;
        var edge = graph.GetEdge(result.startAnchor.edgeId);
        if (edge == null || !FinitePositive(edge.usableWidth)) return false;
        Vector2 normal = new Vector2(tangent.y, -tangent.x);
        Vector2 right = body.transform.right;
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        Vector2 centerOffset = right * boundColliderOffsetWorld.x + forward * boundColliderOffsetWorld.y;
        float support = (Mathf.Abs(Vector2.Dot(right, normal)) * padded.x +
            Mathf.Abs(Vector2.Dot(forward, normal)) * padded.y) * 0.5f;
        float lateral = Mathf.Abs(Vector2.Dot(delta + centerOffset, normal));
        if (!FiniteNonnegative(support) || !FiniteNonnegative(lateral) || lateral + support > edge.usableWidth * 0.5f ||
            !budget.TryConsume(1)) return false;
        return RoadFootprintClearance.IsSweepClear(position, result.startRoadPoint, padded, boundColliderOffsetWorld,
            body.Body.rotation, body.Body.rotation, boundLocalBounds, null, staticClearance);
    }

    bool TryBeginInitialConnector(PoliceRoadTargetQuery.Result result, Vector2 localPosition, Vector2 forward, Vector2 firstDirection) {
        if (result == null || !Finite(localPosition) || !Finite(result.startRoadPoint) || !Finite(forward) ||
            !FinitePositive(forward.sqrMagnitude) || !Finite(firstDirection) || !FinitePositive(firstDirection.sqrMagnitude)) return false;
        Vector2 delta = result.startRoadPoint - localPosition;
        float length = delta.magnitude;
        if (!FinitePositive(length) || Vector2.Dot(delta, forward) <= 0f) return false;
        Vector2 direction = delta / length;
        if (Vector2.Angle(forward, direction) > driving.connectorAlignmentDegrees ||
            Vector2.Angle(direction, firstDirection) > driving.connectorAlignmentDegrees) return false;
        if (!initialLine.TryBind(localPosition, result.startRoadPoint, localPosition, trackingLimits, driving.connectorAlignmentDegrees)) return false;
        initialRoadStartAnchor = result.startAnchor;
        phase = TraversalPhase.InitialConnector;
        return true;
    }

    bool TryQueryConnectorSensor(PoliceStraightConnector line, float deceleration, ref int work, out float allowedSpeed, out float lineSpeed, out Collider2D obstacle) {
        allowedSpeed = float.PositiveInfinity;
        obstacle = null;
        lineSpeed = Mathf.Max(0f, Vector2.Dot(body.Body.linearVelocity, line.Direction));
        Vector2 forward = ((Vector2)body.transform.up).normalized;
        if (!FiniteNonnegative(lineSpeed)) return false;
        if (ExactVector(forward, line.Direction)) return true;
        // Braking force is along the body axis; only its projection decelerates line travel.
        float lineDeceleration = deceleration * Vector2.Dot(forward, line.Direction);
        if (!PoliceDrivingMath.TrySweepDistance(lineSpeed, lineDeceleration, motorSettings.reactionTime,
            driving.stopGap, driving.maxSweepDistance, out float range) || !ConsumeWork(ref work)) return false;
        var status = body.Sensor.QuerySweep(line.Direction, range, out float gap, out obstacle);
        RememberTargetCollider(obstacle);
        if (status == VehicleObstacleSensor.SweepStatus.Invalid || status == VehicleObstacleSensor.SweepStatus.Saturated) return false;
        return status != VehicleObstacleSensor.SweepStatus.Blocked || PoliceDrivingMath.TryAllowedSpeed(0f,
            Mathf.Max(0f, gap - driving.stopGap - lineSpeed * motorSettings.reactionTime), lineDeceleration, out allowedSpeed);
    }

    bool TryValidateConnector(PoliceStraightConnector line, Vector2 localPosition, float deltaTime, ref int work) {
        if (!line.IsBound || staticClearance == null || !Finite(localPosition)) return false;
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        float heading = body.Body.rotation;
        float desiredHeading = MapNavigationCoordinates.DirectionToHeadingDegrees(line.Direction);
        return FinitePositive(padded.x) && FinitePositive(padded.y) && ConsumeWork(ref work) &&
            RoadFootprintClearance.IsSweepClear(localPosition, line.End, padded, boundColliderOffsetWorld,
                heading, desiredHeading, boundLocalBounds, null, staticClearance) &&
            TryValidateStepEnvelope(localPosition, deltaTime, padded, ref work);
    }

    bool TryPrepareFinalConnector(Vector2 localPosition, Vector2 forward, ref int work) {
        if (acceptedResult == null || !acceptedResult.hasFinalApproach || staticClearance == null ||
            !cursor.TryGetEndpointTangents(out _, out var lastDirection) ||
            !ExactVector(acceptedResult.roadTarget, acceptedResult.finalApproachStart)) return false;
        if (acceptedResult.finalApproachCurved) {
            finalLine.Reset();
            var plan = acceptedResult.finalApproachPlan;
            if (!ExactVector(plan.start, acceptedResult.roadTarget) || !ExactVector(plan.target, acceptedResult.finalApproachEnd) ||
                !FinitePositive(plan.TotalLength) || plan.TotalLength > driving.maximumTurnTravel) return false;
            return PoliceFinalApproachGeometry.IsRemainingClear(plan, 0f, footprint + Vector2.one * navigationSettings.clearanceMargin,
                boundColliderOffsetWorld, boundLocalBounds, staticClearance, ref work);
        }
        Vector2 delta = acceptedResult.finalApproachEnd - acceptedResult.finalApproachStart;
        float length = delta.magnitude;
        if (!FinitePositive(length) || !Finite(delta) || Vector2.Dot(delta, lastDirection) <= 0f ||
            Vector2.Dot(delta, forward) <= 0f || Vector2.Angle(delta, lastDirection) > driving.connectorAlignmentDegrees ||
            Vector2.Angle(delta, forward) > driving.connectorAlignmentDegrees) return false;
        if (finalLine.IsBound && !MatchesFinalLine(acceptedResult)) return false;
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        if (!ConsumeWork(ref work) || !RoadFootprintClearance.IsSweepClear(acceptedResult.finalApproachStart,
            acceptedResult.finalApproachEnd, padded, boundColliderOffsetWorld, body.Body.rotation,
            MapNavigationCoordinates.DirectionToHeadingDegrees(delta / length), boundLocalBounds, null, staticClearance)) return false;
        return finalLine.IsBound || finalLine.TryBind(acceptedResult.finalApproachStart, acceptedResult.finalApproachEnd,
            localPosition, trackingLimits, driving.connectorAlignmentDegrees);
    }

    bool MatchesFinalLine(PoliceRoadTargetQuery.Result result) => finalLine.IsBound && result != null && result.hasFinalApproach &&
        ExactVector(result.roadTarget, finalLine.Start) && ExactVector(result.finalApproachStart, finalLine.Start) &&
        ExactVector(result.finalApproachEnd, finalLine.End);

    bool MatchesFinalApproach(PoliceRoadTargetQuery.Result result) => result != null && result.hasFinalApproach &&
        result.finalApproachCurved && finalApproachTraversal.CurrentState == PoliceFinalApproachTraversal.State.Active &&
        ExactVector(result.finalApproachPlan.start, finalApproachTraversal.Plan.start) &&
        ExactVector(result.finalApproachPlan.target, finalApproachTraversal.Plan.target);

    bool TryBeginCurvedFinal(Vector2 position, Vector2 forward, float deltaTime, ref int work) {
        var planned = acceptedResult.finalApproachPlan;
        float along = Vector2.Dot(position - planned.start, planned.entryDirection);
        float crossingLimit = body.Body.linearVelocity.magnitude * deltaTime * driving.displacementSlack + driving.acquisitionTolerance;
        if (finalApproachTraversal.CurrentState != PoliceFinalApproachTraversal.State.Inactive || !cursor.IsComplete ||
            !cursor.TryGetEndpointTangents(out _, out _) || along < 0f || along > crossingLimit ||
            Vector2.Distance(position, planned.start + planned.entryDirection * along) > driving.acquisitionTolerance ||
            Vector2.Angle(forward, planned.entryDirection) > driving.connectorAlignmentDegrees) return false;
        // A measured discrete crossing may lie just beyond the planned entry. Certify from that
        // actual pose; never move the body or manufacture cursor/arc progress to match the plan.
        if (!PoliceFinalApproachGeometry.TrySolve(position, forward, acceptedResult.finalApproachEnd,
            PoliceRoadTargetQuery.ApproachRadius(sharedProfile, navigationSettings), driving.maximumTurnTravel, out var actual) ||
            !PoliceFinalApproachGeometry.IsRemainingClear(actual, 0f, footprint + Vector2.one * navigationSettings.clearanceMargin,
                boundColliderOffsetWorld, boundLocalBounds, staticClearance, ref work) ||
            !finalApproachTraversal.TryBegin(actual, damageWorld.SessionTime, position,
                driving.maximumTurnTravel, driving.maximumTurnActiveSeconds)) return false;
        finalObservedSpeed = body.Body.linearVelocity.magnitude;
        phase = TraversalPhase.FinalTurn;
        return true;
    }

    void DriveCurvedFinal(Vector2 position, Vector2 forward, float deltaTime, float speed, float deceleration,
        VehicleObstacleSensor.SweepStatus sweep, float gap, ref int work) {
        Vector2 targetPosition = MapNavigationCoordinates.WorldToLocal(targetBody.position, mapOriginWorld);
        if (phase == TraversalPhase.Holding && ExactVector(targetPosition, finalApproachTraversal.Plan.target) && IsAtTargetStandoff(ref work)) { Stop(); return; }
        if (phase == TraversalPhase.Holding) phase = TraversalPhase.FinalConnector;
        float acceleration = Mathf.Max(motorSettings.acceleration, motorSettings.brakeDeceleration) * Mathf.Max(1f, boundCrashMultiplier);
        float maximumStep = (Mathf.Max(finalObservedSpeed, body.Body.linearVelocity.magnitude) + acceleration * deltaTime) * deltaTime + driving.acquisitionTolerance;
        var outcome = finalApproachTraversal.Tick(damageWorld.SessionTime, position, forward, driving.displacementSlack,
            driving.turnExitAlignmentDegrees, false, driving.maxDeviation, driving.acquisitionTolerance, maximumStep);
        finalObservedSpeed = body.Body.linearVelocity.magnitude;
        if (outcome != PoliceFinalApproachTraversal.UpdateResult.Active) { Stop(); return; }
        var previous = finalApproachTraversal.Plan;
        if (!planner.TryRefreshFinalApproach(position, forward, targetPosition, damageWorld.SessionTime,
            finalApproachTraversal.TravelRemaining, previous, out var replacement, out bool attempted)) { Stop(); return; }
        if (attempted) {
            if (!ExactVector(previous.target, replacement.target) || !ExactVector(previous.start, replacement.start)) {
                if (!finalApproachTraversal.TryReplan(replacement)) { Stop(); return; }
            }
            finalConnectorRefreshes++;
        }
        var plan = finalApproachTraversal.Plan;
        float progress = finalApproachTraversal.Progress;
        bool onTangent = progress >= plan.arcLength && Vector2.Dot(position - plan.arcExit, plan.exitDirection) >= 0f &&
            Vector2.Angle(forward, plan.exitDirection) <= driving.connectorAlignmentDegrees;
        phase = onTangent ? TraversalPhase.FinalConnector : TraversalPhase.FinalTurn;
        if (onTangent && IsAtTargetStandoff(ref work)) { phase = TraversalPhase.Holding; Stop(); return; }
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        if (!PoliceFinalApproachGeometry.IsRemainingClear(plan, progress, padded, boundColliderOffsetWorld,
            boundLocalBounds, staticClearance, ref work) || !ConsumeWork(ref work) ||
            !RoadFootprintClearance.IsSweepClear(position, plan.Sample(progress), padded, boundColliderOffsetWorld,
                body.Body.rotation, MapNavigationCoordinates.DirectionToHeadingDegrees(PoliceFinalApproachGeometry.DirectionAt(plan, progress)),
                boundLocalBounds, null, staticClearance) || !TryValidateStepEnvelope(position, deltaTime, padded, ref work) ||
            !TryValidateFinalDynamicStep(position, deltaTime, padded, ref work)) { Stop(); return; }
        float lookahead = Mathf.Clamp(driving.lookaheadMin + speed * driving.lookaheadTime, driving.lookaheadMin, driving.lookaheadMax);
        if (!onTangent && plan.radius > 0f) lookahead = Mathf.Min(lookahead,
            Mathf.Max(speed * deltaTime * driving.displacementSlack + driving.acquisitionTolerance,
                plan.radius * driving.turnExitAlignmentDegrees * Mathf.Deg2Rad));
        Vector2 aim = finalApproachTraversal.SampleAhead(lookahead);
        LastAimPoint = MapNavigationCoordinates.LocalToWorld(aim, mapOriginWorld);
        if (!PoliceDrivingMath.TrySteering(forward, aim - position, speed > 0f ? speed : 1f, motorSettings,
            out float geometrySteering, out float yawCap)) { Stop(); return; }
        float remaining = Mathf.Max(0f, finalApproachTraversal.Remaining - driving.stopGap - speed * motorSettings.reactionTime);
        if (!PoliceDrivingMath.TryAllowedSpeed(0f, remaining, deceleration, out float endpointCap)) { Stop(); return; }
        float planned = Mathf.Min(endpointCap, Mathf.Min(yawCap, Mathf.Min(cachedRouteEdgeSpeed, motorSettings.cruiseSpeed)));
        if (!onTangent) planned = Mathf.Min(planned, driving.cornerSpeed);
        if (sweep == VehicleObstacleSensor.SweepStatus.Blocked) {
            if (!PoliceDrivingMath.TryAllowedSpeed(0f, Mathf.Max(0f, gap - driving.stopGap - speed * motorSettings.reactionTime),
                deceleration, out float obstacleCap)) { Stop(); return; }
            planned = Mathf.Min(planned, obstacleCap);
        }
        // The fresh actual-shape sweep also follows the aim direction, so a moving actor at the
        // inside of the turn is not missed by a forward-only ray. Full buffers always brake.
        Vector2 aimDirection = (aim - position).normalized;
        float projectedDeceleration = deceleration * Vector2.Dot(forward, aimDirection);
        float projectedSpeed = Mathf.Max(0f, Vector2.Dot(body.Body.linearVelocity, aimDirection));
        if (!PoliceDrivingMath.TrySweepDistance(projectedSpeed, projectedDeceleration, motorSettings.reactionTime,
            driving.stopGap, driving.maxSweepDistance, out float range) || !ConsumeWork(ref work)) { Stop(); return; }
        var aimSweep = body.Sensor.QuerySweep(aimDirection, range, out float aimGap, out var obstacle);
        RememberTargetCollider(obstacle);
        if (aimSweep == VehicleObstacleSensor.SweepStatus.Invalid || aimSweep == VehicleObstacleSensor.SweepStatus.Saturated) { Stop(); return; }
        if (aimSweep == VehicleObstacleSensor.SweepStatus.Blocked) {
            if (!PoliceDrivingMath.TryAllowedSpeed(0f, Mathf.Max(0f, aimGap - driving.stopGap - projectedSpeed * motorSettings.reactionTime),
                projectedDeceleration, out float obstacleCap)) { Stop(); return; }
            planned = Mathf.Min(planned, obstacleCap);
        }
        IssueMotion(speed, planned, speed > 0f ? geometrySteering : 0f);
    }

    bool TryValidateFinalDynamicStep(Vector2 position, float deltaTime, Vector2 padded, ref int work) {
        float multiplier = body.Motor.IsCrashMode ? boundCrashMultiplier : 1f;
        float acceleration = Mathf.Max(Mathf.Min(motorSettings.acceleration, motorSettings.maxEngineForce / boundMass),
            Mathf.Min(motorSettings.brakeDeceleration, motorSettings.maxBrakeForce / boundMass)) * multiplier;
        float travel = (body.Body.linearVelocity.magnitude + acceleration * deltaTime) * deltaTime;
        float extent = travel + padded.magnitude * 0.5f + boundColliderOffsetWorld.magnitude;
        if (!FinitePositive(extent) || finalStepHits == null || !ConsumeWork(ref work)) return false;
        // Encloses every yaw about the actual pivot for this force-bounded step, including offset.
        int count = body.gameObject.scene.GetPhysicsScene2D().OverlapBox(position + mapOriginWorld,
            Vector2.one * (2f * extent), 0f, new ContactFilter2D { useTriggers = false }, finalStepHits);
        if (count >= finalStepHits.Length) return false;
        for (int i = 0; i < count; i++) if (finalStepHits[i] != null && finalStepHits[i].attachedRigidbody != body.Body) return false;
        return true;
    }

    void RememberTargetCollider(Collider2D obstacle) {
        if (obstacle != null && obstacle.attachedRigidbody == targetBody) observedTargetCollider = obstacle;
    }

    bool IsAtTargetStandoff(ref int work) {
        float finalProgress = IsCurvedFinal ? finalApproachTraversal.MeasuredTravel : finalLine.Progress;
        if (!IsFinalPhase || finalProgress <= 0f || observedTargetCollider == null || !observedTargetCollider.enabled ||
            observedTargetCollider.isTrigger || !observedTargetCollider.gameObject.activeInHierarchy ||
            observedTargetCollider.attachedRigidbody != targetBody || body.Body.linearVelocity.magnitude > driving.stoppedSpeedThreshold ||
            !ConsumeWork(ref work)) return false;
        var separation = body.MainCollider.Distance(observedTargetCollider);
        return separation.isValid && !separation.isOverlapped && FiniteNonnegative(separation.distance) &&
            Mathf.Abs(separation.distance - driving.stopGap) <= driving.acquisitionTolerance;
    }

    bool TryCommitExistingTurn(float lookahead, float deltaTime, Vector2 forward, float speed, float deceleration,
        Vector2 localPosition, ref int work, out bool turnWasAhead) {
        turnWasAhead = false;
        var status = TryGetProfileTurnCorridor(lookahead, speed, deltaTime, -1, ref work, out var corridor, out float effectiveExit);
        if (status == PolicePathCursor.CorridorQueryStatus.Straight) return true;
        if (status != PolicePathCursor.CorridorQueryStatus.SingleBend) { turnWasAhead = true; return false; }
        turnWasAhead = true;
        float turnDistance = corridor.cornerArc - cursor.ProgressDistance;
        if (!FiniteNonnegative(turnDistance) || turnDistance > Mathf.Max(lookahead, effectiveExit + speed * deltaTime)) return false;
        var constraints = new RouteTransitionFilter.VehicleConstraints(footprint.x, navigationSettings.clearanceMargin,
            motorSettings.minimumTurningRadius, navigationSettings.maxTurnAngle);
        if (!RouteTransitionFilter.TransitionFits(corridor.incomingDirection, turnDistance, corridor.outgoingDirection,
            Mathf.Max(effectiveExit, corridor.clampedLookahead - turnDistance), constraints, out _, out _, out _) ||
            !TryValidateTurnEnvelope(corridor, deltaTime, speed, localPosition, ref work) ||
            !PoliceDrivingMath.TrySteering(forward, MapNavigationCoordinates.LocalToWorld(corridor.lookaheadPoint, mapOriginWorld) - body.Body.position,
                speed, motorSettings, out _, out _)) return false;
        float angle = Vector2.Angle(corridor.incomingDirection, corridor.outgoingDirection);
        if (!PoliceDrivingMath.TryCornerTargetSpeed(motorSettings.cruiseSpeed, driving.cornerSpeed, angle, driving.fullSlowdownAngle, out float cap)) return false;
        float available = Mathf.Max(0f, turnDistance - speed * motorSettings.reactionTime);
        if (!PoliceDrivingMath.TryAllowedSpeed(cap, available, deceleration, out _)) return false;
        var limits = new PoliceTurnTraversal.Limits(effectiveExit, driving.turnExitAlignmentDegrees, driving.maximumTurnTravel, driving.maximumTurnActiveSeconds);
        return turnTraversal.TryBegin(corridor.token, corridor.cornerArc, corridor.outgoingDirection, cap, limits, damageWorld.SessionTime, localPosition);
    }

    bool ValidateCommittedCorridor(float lookahead, float deltaTime, Vector2 forward, float speed, float deceleration,
        Vector2 localPosition, ref int work, out Vector2 aimLocal) {
        aimLocal = default;
        if (body.Body.gravityScale != 0f) return false;
        var status = TryGetProfileTurnCorridor(lookahead, speed, deltaTime, turnTraversal.Token, ref work, out var corridor, out _);
        if (status == PolicePathCursor.CorridorQueryStatus.BudgetExceeded) return false;
        if (status != PolicePathCursor.CorridorQueryStatus.SingleBend || corridor.token != turnTraversal.Token ||
            corridor.cornerArc != turnTraversal.AbsoluteArc || !ExactVector(corridor.outgoingDirection.normalized, turnTraversal.OutgoingDirection)) {
            FailRouteState();
            return false;
        }
        if (!TryValidateTurnEnvelope(corridor, deltaTime, speed, localPosition, ref work) ||
            !PoliceDrivingMath.TrySteering(forward, MapNavigationCoordinates.LocalToWorld(corridor.lookaheadPoint, mapOriginWorld) - body.Body.position,
                speed, motorSettings, out _, out _) ||
            !PoliceDrivingMath.TryAllowedSpeed(turnTraversal.Cap, Mathf.Max(0f, cursor.RemainingDistance - driving.stopGap), deceleration, out _)) return false;
        aimLocal = corridor.lookaheadPoint;
        return true;
    }

    PolicePathCursor.CorridorQueryStatus TryGetProfileTurnCorridor(float lookahead, float speed, float deltaTime,
        int latchedTurnIndex, ref int work, out PolicePathCursor.SingleBendCorridor corridor, out float effectiveExit) {
        corridor = default;
        effectiveExit = driving.turnExitDistance;
        if (latchedTurnIndex < 0) {
            var discovery = cursor.TryGetNextTurn(driving.lookaheadMax, ref work, out _, out var discoveredIncoming,
                out var discoveredOutgoing, out float discoveredDistance, out int discoveredToken);
            if (discovery == PolicePathCursor.TurnQueryStatus.None) return PolicePathCursor.CorridorQueryStatus.Straight;
            if (discovery == PolicePathCursor.TurnQueryStatus.BudgetExceeded) return PolicePathCursor.CorridorQueryStatus.BudgetExceeded;
            if (discovery == PolicePathCursor.TurnQueryStatus.StaleGraph) return PolicePathCursor.CorridorQueryStatus.StaleGraph;
            if (discovery != PolicePathCursor.TurnQueryStatus.Found) return PolicePathCursor.CorridorQueryStatus.InvalidInput;
            float discoveredAngle = Vector2.Angle(discoveredIncoming, discoveredOutgoing);
            float discoveredTangent = motorSettings.minimumTurningRadius * Mathf.Tan(Mathf.Min(discoveredAngle, 179f) * 0.5f * Mathf.Deg2Rad);
            float discoveredMargin = Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
            if (!FiniteNonnegative(discoveredTangent) || discoveredTangent > driving.lookaheadMax)
                return PolicePathCursor.CorridorQueryStatus.InsufficientLength;
            effectiveExit = Mathf.Max(driving.turnExitDistance, discoveredTangent);
            // Discover early enough for the physical radius, without committing every distant bend.
            if (discoveredDistance > Mathf.Max(lookahead, effectiveExit + discoveredMargin))
                return PolicePathCursor.CorridorQueryStatus.Straight;
            float certifiedLookahead = discoveredTangent > driving.turnExitDistance
                ? Mathf.Max(lookahead, discoveredDistance + discoveredTangent + discoveredMargin) : lookahead;
            if (certifiedLookahead > driving.lookaheadMax) return PolicePathCursor.CorridorQueryStatus.InsufficientLength;
            return cursor.TryGetSingleBendCorridor(certifiedLookahead, effectiveExit, discoveredToken, ref work, out corridor);
        }

        var status = cursor.TryGetSingleBendCorridor(lookahead, driving.turnExitDistance, latchedTurnIndex, ref work, out corridor);
        if (status != PolicePathCursor.CorridorQueryStatus.SingleBend) return status;
        float angle = Vector2.Angle(corridor.incomingDirection, corridor.outgoingDirection);
        float tangent = motorSettings.minimumTurningRadius * Mathf.Tan(Mathf.Min(angle, 179f) * 0.5f * Mathf.Deg2Rad);
        float arrivalMargin = Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        if (!FiniteNonnegative(tangent) || tangent > driving.lookaheadMax) return PolicePathCursor.CorridorQueryStatus.InsufficientLength;
        effectiveExit = Mathf.Max(driving.turnExitDistance, tangent);
        float turnDistance = corridor.cornerArc - cursor.ProgressDistance;
        float requiredLookahead = tangent > driving.turnExitDistance
            ? Mathf.Max(lookahead, turnDistance + tangent + arrivalMargin) : lookahead;
        if (!FiniteNonnegative(requiredLookahead) || requiredLookahead > driving.lookaheadMax) return PolicePathCursor.CorridorQueryStatus.InsufficientLength;
        if (requiredLookahead > corridor.clampedLookahead || effectiveExit > driving.turnExitDistance) {
            status = cursor.TryGetSingleBendCorridor(Mathf.Min(driving.lookaheadMax, requiredLookahead), effectiveExit,
                latchedTurnIndex, ref work, out corridor);
            if (status != PolicePathCursor.CorridorQueryStatus.SingleBend) return status;
        }
        return status;
    }

    bool TryValidateTurnEnvelope(PolicePathCursor.SingleBendCorridor corridor, float deltaTime, float speed, Vector2 localPosition, ref int work) {
        if (staticClearance == null || !FinitePositive(deltaTime) || !FiniteNonnegative(speed) || !Finite(localPosition) ||
            !Finite(corridor.vertex) || !Finite(corridor.lookaheadPoint) || !Finite(corridor.exitPoint)) return false;
        Vector2 minimum = Vector2.Min(Vector2.Min(localPosition, corridor.vertex), Vector2.Min(corridor.lookaheadPoint, corridor.exitPoint));
        Vector2 maximum = Vector2.Max(Vector2.Max(localPosition, corridor.vertex), Vector2.Max(corridor.lookaheadPoint, corridor.exitPoint));
        return TryValidateMovementEnvelope(minimum, maximum, localPosition, deltaTime, ref work);
    }

    bool TryValidateMovementEnvelope(Vector2 minimum, Vector2 maximum, Vector2 localPosition, float deltaTime, ref int work) {
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        if (!FinitePositive(padded.x) || !FinitePositive(padded.y)) return false;
        // Tracking deviation rejects acquisition drift; it is not a physical collider radius for future-path clearance.
        float expansion = padded.magnitude * 0.5f + boundColliderOffsetWorld.magnitude;
        if (!FiniteNonnegative(expansion)) return false;
        Vector2 maneuverSize = maximum - minimum + Vector2.one * (2f * expansion);
        if (!ConsumeWork(ref work) || !RoadFootprintClearance.IsPoseClear((minimum + maximum) * 0.5f, maneuverSize, Vector2.zero, 0f,
            boundLocalBounds, null, staticClearance)) return false;
        return TryValidateStepEnvelope(localPosition, deltaTime, padded, ref work);
    }

    bool TryValidateStepEnvelope(Vector2 localPosition, float deltaTime, Vector2 padded, ref int work) =>
        straightClearance != null && straightClearance.TryValidateStepEnvelope(localPosition, deltaTime, padded, ref work);

    bool TryAcceptContinuation(PoliceRoadTargetQuery.Result result, RoadPathQuery.EdgeAnchor anchor, Vector2 localPosition, ref int work) {
        if (boundIntent == PoliceTacticalRole.Intercept && phase == TraversalPhase.Road && result != null)
            return TryAcceptInterceptSuffix(result, anchor, localPosition, ref work);
        if (result == null || result.status != PoliceRoadTargetQuery.Status.Route || result.graphVersion != graph.Version ||
            result.startAnchor.edgeId != anchor.edgeId || result.startAnchor.distanceAlongEdge != anchor.distanceAlongEdge ||
            !cursor.MatchesRemainingSpans(result.spans, ref work)) return false;
        if (phase == TraversalPhase.InitialConnector && (result.startAnchor.edgeId != initialRoadStartAnchor.edgeId ||
            result.startAnchor.distanceAlongEdge != initialRoadStartAnchor.distanceAlongEdge ||
            !ExactVector(result.startRoadPoint, initialLine.End))) return false;
        if (IsFinalPhase && (!cursor.IsComplete || (result.finalApproachCurved ? !MatchesFinalApproach(result) : !MatchesFinalLine(result)))) return false;
        if (!IsFinalPhase && finalLine.IsBound) {
            if (!result.hasFinalApproach) finalLine.Reset();
            else if (result.finalApproachCurved) finalLine.Reset();
            else if (result.finalApproachCurved ? !MatchesFinalApproach(result) : !MatchesFinalLine(result)) return false;
        }
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        float heading = body.Body.rotation;
        if (!ConsumeWork(ref work) || !RoadFootprintClearance.IsSweepClear(localPosition, result.startRoadPoint, padded, boundColliderOffsetWorld,
            heading, heading, boundLocalBounds, null, staticClearance)) return false;
        acceptedResult = result;
        if (IsFinalPhase) finalConnectorRefreshes++;
        if (phase == TraversalPhase.Road) {
            provenRoadContinuations++;
            if (result.requiresConnector) connectorMetadataContinuations++;
        }
        return true;
    }

    bool TryAcceptInterceptSuffix(PoliceRoadTargetQuery.Result result, RoadPathQuery.EdgeAnchor anchor,
        Vector2 localPosition, ref int work) {
        if (result.status != PoliceRoadTargetQuery.Status.Route || result.graphVersion != graph.Version ||
            result.spans == null || result.spans.Count == 0 ||
            result.startAnchor.edgeId != anchor.edgeId || result.startAnchor.distanceAlongEdge != anchor.distanceAlongEdge ||
            result.requiresConnector || IsFinalPhase || IsTurnCommitted || !cursor.IsBound) return false;
        var bindBudget = new RoadPathQuery.SearchBudget(driving.bindWork);
        if (!RoadAnchorGeometry.TryGetPoint(graph, anchor.edgeId, anchor.distanceAlongEdge, bindBudget,
            out _, out Vector2 firstDirection)) return false;
        if (!Finite(firstDirection) || !FinitePositive(firstDirection.sqrMagnitude) ||
            Vector2.Angle(((Vector2)body.transform.up).normalized, firstDirection) > driving.connectorAlignmentDegrees) return false;
        Vector2 padded = footprint + Vector2.one * navigationSettings.clearanceMargin;
        if (!FinitePositive(padded.x) || !FinitePositive(padded.y) || !ConsumeWork(ref work) ||
            !RoadFootprintClearance.IsSweepClear(localPosition, result.startRoadPoint, padded, boundColliderOffsetWorld,
                body.Body.rotation, body.Body.rotation, boundLocalBounds, null, staticClearance)) return false;
        if (!cursor.TryBind(graph, result.graphVersion, result.spans, trackingLimits, bindBudget)) return false;
        float minimum = float.PositiveInfinity;
        for (int index = 0; index < result.spans.Count; index++) {
            if (!bindBudget.TryConsume(1)) { cursor.Reset(); return false; }
            var edge = graph.GetEdge(result.spans[index].edgeId);
            if (edge == null || !FinitePositive(edge.speedLimit)) { cursor.Reset(); return false; }
            minimum = Mathf.Min(minimum, edge.speedLimit);
        }
        if (!FinitePositive(minimum)) { cursor.Reset(); return false; }
        finalLine.Reset(); cachedRouteEdgeSpeed = minimum; acceptedResult = result;
        provenRoadContinuations++;
        return true;
    }
    void FailRouteState() { ClearRoute(); planner?.InvalidateRoute(); Stop(); }
    void ClearRoute(bool discardImpactEvidence = false) {
        if (discardImpactEvidence) impactRoadEvidence.Clear();
        if (IsRamming) ramDecision.Cancel(lastActiveClock);
        ramSafety?.ResetCertificate();
        cursor.Reset(); turnTraversal.Reset(); finalApproachTraversal.Reset(); acceptedResult = null; cachedRouteEdgeSpeed = 0f; awaitingFreshPlan = false;
        phase = TraversalPhase.Road; initialLine.Reset(); finalLine.Reset(); initialRoadStartAnchor = default;
        observedTargetCollider = null;
        firstFinalDiagnostic = null;
    }
    void CancelTarget() { recovery?.CancelMovement(); target = null; targetBody = null; ClearRoute(true); planner?.InvalidateRoute(); Stop(); }
    void CloseInvalidBinding() {
        planner?.InvalidateRoute(); ResetForNewLife();
    }
    void HandleSessionEnded() { ResetForNewLife(); }
    bool IsActiveTargetCandidate(VehicleDamageReceiver receiver, Rigidbody2D receiverBody) => receiver != null && receiver.gameObject.activeInHierarchy &&
        receiver.isActiveAndEnabled && receiverBody != null && receiverBody.simulated && receiverBody.gameObject.activeInHierarchy;
    void Stop([System.Runtime.CompilerServices.CallerLineNumber] int sourceLine = 0) {
        if (finalDiagnosticEnabled && firstFinalDiagnostic == null && IsCurvedFinal && IsFinalPhase && body != null) {
            var plan = finalApproachTraversal.Plan;
            Vector2 position = MapNavigationCoordinates.WorldToLocal(body.Body.position, mapOriginWorld);
            float observed = PoliceFinalApproachGeometry.ProjectProgress(plan, position);
            firstFinalDiagnostic = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "line={0}; t={1:R}; pose={2}; heading={3:R}; velocity={4}; phase={5}; state={6}; progress={7:R}; observed={8:R}; crossTrack={9:R}; headingError={10:R}; arc={11:R}; travel={12:R}; deadline={13:R}; aim={14}; target={15}",
                sourceLine, damageWorld.SessionTime, position, body.Body.rotation, body.Body.linearVelocity, phase,
                finalApproachTraversal.CurrentState, finalApproachTraversal.Progress, observed,
                Vector2.Distance(position, plan.Sample(observed)), Vector2.Angle(body.transform.up, PoliceFinalApproachGeometry.DirectionAt(plan, observed)),
                plan.arcLength, finalApproachTraversal.MeasuredTravel, finalApproachTraversal.Deadline, LastAimPoint, plan.target);
        }
        if (IsRamming) RevokeRam();
        LastCommand = NpcDriveCommand.Stopped; LastPlannedSpeed = 0f; LastAimPoint = Vector2.zero;
        if (body != null && body.Motor != null) { body.Motor.SetReverseManeuverPermission(false); body.Motor.SetCommand(LastCommand); }
    }
    void IssueMotion(float speed, float planned, float steering, float throttleLimit = 1f) {
        if (!FiniteNonnegative(speed) || !FiniteNonnegative(planned) || !Finite(steering)) { Stop(); return; }
        if (!FiniteNonnegative(throttleLimit)) { Stop(); return; }
        LastPlannedSpeed = planned;
        float excess = speed - planned;
        if (planned <= driving.stoppedSpeedThreshold || excess > 0f) {
            float brake = planned <= driving.stoppedSpeedThreshold ? 1f : Mathf.Clamp01(driving.comfort + excess / driving.brakeBand);
            LastCommand = new NpcDriveCommand(0f, brake, steering, planned, false);
        } else LastCommand = new NpcDriveCommand(Mathf.Min(1f, throttleLimit), 0f, steering, planned, false);
        body.Motor.SetReverseManeuverPermission(false); body.Motor.SetCommand(LastCommand);
    }
    bool TryRamMotion(float deltaTime, float speed, Collider2D connectorObstacle, ref int work) {
        if (boundIntent != PoliceTacticalRole.Ram) return false;
        if (phase == TraversalPhase.InitialConnector || IsTurnCommitted || IsTurnExpired || awaitingFreshPlan ||
            acceptedResult == null || (connectorObstacle != null && connectorObstacle.attachedRigidbody != targetBody)) {
            if (!IsRamming) return false;
            RevokeRam(); Stop(); return true;
        }
        var proof = ramSafety.Check(ramDecision, cursor, finalLine, IsFinalPhase, targetBody, deltaTime, ref work, out float reserveCap);
        if (proof != PoliceRamSafety.Proof.Clear) {
            if (!IsRamming && proof == PoliceRamSafety.Proof.Ineligible) return false;
            if (IsRamming) RevokeRam();
            Stop(); return true;
        }
        if (!IsRamming) {
            if (!ramDecision.TryBegin(lastActiveClock, deltaTime, body.Body.linearVelocity.magnitude,
                driving.stoppedSpeedThreshold, true, body.Motor.IsCrashMode || IsRecovering)) { ramSafety.ResetCertificate(); Stop(); return true; }
            ramStarts++;
        }
        LastAimPoint = ramSafety.FarEndpoint + mapOriginWorld;
        IssueMotion(speed, Mathf.Min(reserveCap, Mathf.Min(cachedRouteEdgeSpeed, Mathf.Min(motorSettings.cruiseSpeed, motorSettings.maxSpeed))), 0f);
        return true;
    }
    void HandleRamImpact(float impactSpeed) {
        if (!bound || ramImpactReceiver == null || !ramImpactReceiver.IsBoundTo(damageWorld, boundPoliceIdentity, sharedProfile)) return;
        bool onRoad = impactRoadEvidence.TryResolveImpactAnchor(phase == TraversalPhase.Road && cursor.IsBound,
            cursor.IsBound ? cursor.CurrentAnchor : default, graph.Version, boundPoliceIdentity, boundTargetIdentity, ValidateTargetState(), out var impactAnchor);
        bool opened = recovery.CaptureImpact(impactSpeed, damageWorld.SessionTime, onRoad, impactAnchor);
        if (boundIntent == PoliceTacticalRole.Ram) RevokeRam();
        else if (opened) { ClearRoute(); planner.InvalidateRoute(); }
        Stop();
    }
    void RevokeRam() {
        bool hadCertificate = ramSafety != null && ramSafety.IsCertified;
        bool cancelled = ramDecision.Cancel(lastActiveClock);
        ramSafety?.ResetCertificate();
        if (cancelled || hadCertificate) { ClearRoute(); planner?.InvalidateRoute(); }
    }
    static bool SupportsDrivingIntent(PoliceTacticalRole role) => role == PoliceTacticalRole.Pursue || role == PoliceTacticalRole.Intercept ||
        role == PoliceTacticalRole.Ram || role == PoliceTacticalRole.Shadow;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
    static bool ConsumeWork(ref int work) { if (work < 1) return false; work--; return true; }
    static bool ValidBounds(Rect value) => Finite(value.position) && Finite(value.size) && Finite(value.xMax) && Finite(value.yMax) && value.width > 0f && value.height > 0f;
    static bool Inside(Vector2 point, Rect bounds) => Finite(point) && point.x >= bounds.xMin && point.x <= bounds.xMax && point.y >= bounds.yMin && point.y <= bounds.yMax;
    static bool ExactVector(Vector2 first, Vector2 second) => first.x == second.x && first.y == second.y;
    static bool ExactVector(Vector3 first, Vector3 second) => first.x == second.x && first.y == second.y && first.z == second.z;

    // Derived transform decomposition and footprint/offset products may round during rotation.
    // Allow sixteen binary32 machine epsilons for these derived values, scaled per component with no
    // absolute floor: a zero component still must be exactly zero. Raw authoring stays exact above.
    static bool DerivedGeometryMatches(Vector2 actual, Vector2 expected) =>
        DerivedGeometryMatches(actual.x, expected.x) && DerivedGeometryMatches(actual.y, expected.y);

    static bool DerivedGeometryMatches(float actual, float expected) {
        const double relativeRoundoff = 16d / 8388608d;
        if (!Finite(actual) || !Finite(expected)) return false;
        double magnitude = System.Math.Max(System.Math.Abs((double)actual), System.Math.Abs((double)expected));
        return System.Math.Abs((double)actual - expected) <= relativeRoundoff * magnitude;
    }
    static bool MatchesAuthoringFootprint(Vector2 actual, Vector2 expected) => Finite(expected) && FinitePositive(expected.x) && FinitePositive(expected.y) &&
        Mathf.Abs(actual.x - expected.x) <= 0.001f && Mathf.Abs(actual.y - expected.y) <= 0.001f;

    struct MotorFacts {
        readonly float cruiseSpeed, maxSpeed, reverseSpeed, acceleration, brakeDeceleration, maxEngineForce, maxBrakeForce, turnRate, minimumTurningRadius, lateralGrip, reactionTime;
        public MotorFacts(NpcMotorSettings settings) {
            cruiseSpeed = settings.cruiseSpeed; maxSpeed = settings.maxSpeed; reverseSpeed = settings.reverseSpeed; acceleration = settings.acceleration;
            brakeDeceleration = settings.brakeDeceleration; maxEngineForce = settings.maxEngineForce; maxBrakeForce = settings.maxBrakeForce;
            turnRate = settings.turnRate; minimumTurningRadius = settings.minimumTurningRadius; lateralGrip = settings.lateralGrip; reactionTime = settings.reactionTime;
        }
        public bool Matches(NpcMotorSettings settings) => settings != null && settings.cruiseSpeed == cruiseSpeed && settings.maxSpeed == maxSpeed &&
            settings.reverseSpeed == reverseSpeed && settings.acceleration == acceleration && settings.brakeDeceleration == brakeDeceleration &&
            settings.maxEngineForce == maxEngineForce && settings.maxBrakeForce == maxBrakeForce && settings.turnRate == turnRate &&
            settings.minimumTurningRadius == minimumTurningRadius && settings.lateralGrip == lateralGrip && settings.reactionTime == reactionTime;
    }
}
