using UnityEngine;

/// <summary>
/// Drives one civilian NPC around a closed route by issuing <see cref="NpcDriveCommand"/>s to its
/// <see cref="NpcVehicleMotor"/>. Progress is tracked by a <see cref="RouteCursor"/> projected from
/// the real Rigidbody2D position each physics step; the aim point is sampled along the authored
/// polyline (never a chord across a corner) and shortened before a sharp turn. The speed target
/// combines the vehicle's cruise speed, the current and upcoming edge speed limits, and a corner
/// speed for the next turn with enough braking distance to reach it; when the vehicle is faster
/// than the plan it brakes proportionally instead of merely lifting off. This component never
/// writes the Rigidbody2D itself — the loop seam, like every other metre, is crossed by motor
/// forces, so the vehicle is never snapped back to the route start. A route the vehicle cannot
/// physically drive (too narrow, U-turn, corner tighter than its radius) is refused at bind time
/// via <see cref="RouteTransitionFilter"/>. Runs before the motor so a command issued here is
/// consumed in the same physics step.
/// </summary>
[RequireComponent(typeof(NpcVehicleMotor))]
[RequireComponent(typeof(Rigidbody2D))]
[DefaultExecutionOrder(-10)]
public sealed class CivilianRouteFollower : MonoBehaviour {
    [SerializeField] CivilianFollowerSettings settings = new CivilianFollowerSettings();

    NpcVehicleMotor motor;
    Rigidbody2D rb;
    VehicleObstacleSensor sensor;
    float sensorTimer;
    bool obstacleAhead;
    float obstacleGapAtQuery;
    float obstacleSpeedAtQuery;
    float timeSinceQuery;
    bool blockedByObstacle;
    JunctionReservationService junctions;
    RoadGraphRuntime junctionGraph;
    float halfLength;
    Vector2 footprintSize;
    CrashRecoveryPolicy recovery;
    StuckMonitor stuck;
    float lastArcPosition;
    VehicleDamageReceiver receiver;
    IAreaClearanceQuery rejoinClearance;
    Rect rejoinBounds;
    bool reversePermitted;
    int junctionOwnerId;
    string heldJunctionId;
    int heldEntryEdgeIndex = -1;
    int heldEntryLap = -1;
    NpcMotorSettings motorSettings;
    readonly RouteCursor cursor = new RouteCursor();
    Vector2 mapOriginWorld;
    bool following;

    /// <summary>Route progress state; read-only for callers, advanced only by this follower.</summary>
    public RouteCursor Cursor => cursor;

    /// <summary>True while a route is bound and commands are being issued.</summary>
    public bool IsFollowing => following;

    /// <summary>The command issued on the most recent step, for diagnostics and tests.</summary>
    public NpcDriveCommand LastCommand { get; private set; }

    /// <summary>World-space aim point of the most recent step, for diagnostics and gizmos.</summary>
    public Vector2 LastAimPointWorld { get; private set; }

    /// <summary>Planned speed target of the most recent step before any braking decision, for diagnostics and tests.</summary>
    public float LastPlannedSpeed { get; private set; }

    /// <summary>Signed angle (degrees) from vehicle forward to the aim point on the most recent step; large while mid-corner.</summary>
    public float LastAimAngle { get; private set; }

    /// <summary>True while the follower is held stopped behind a forward obstacle and waiting for the resume hysteresis gap.</summary>
    public bool IsBlockedByObstacle => blockedByObstacle;

    /// <summary>Estimated clearance to the forward obstacle on the most recent step, or +infinity when the road ahead is clear.</summary>
    public float LastObstacleGap { get; private set; } = float.PositiveInfinity;

    /// <summary>Junction id this follower currently holds a right-of-way grant for, or null.</summary>
    public string HeldJunctionId => heldJunctionId;

    /// <summary>Outcome of the most recent junction request, for diagnostics and tests.</summary>
    public JunctionReservationService.Outcome LastJunctionOutcome { get; private set; } = JunctionReservationService.Outcome.Granted;

    /// <summary>True while the follower is held at a junction stop line waiting for the zone.</summary>
    public bool IsWaitingAtJunction { get; private set; }

    /// <summary>Current post-impact recovery phase; Driving when nothing happened.</summary>
    public CrashRecoveryPolicy.Phase RecoveryPhase => recovery != null ? recovery.Current : CrashRecoveryPolicy.Phase.Driving;

    /// <summary>Rejoin attempts refused since the last successful rejoin (input for stuck handling).</summary>
    public int RefusedRejoinAttempts => recovery != null ? recovery.RefusedRejoinAttempts : 0;

    /// <summary>Seconds of continuous non-progress along the route.</summary>
    public float StuckSeconds => stuck != null ? stuck.StuckSeconds : 0f;

    /// <summary>True once the car has been without progress for the authored stuck time.</summary>
    public bool IsStuck => stuck != null && stuck.IsStuck;

    /// <summary>
    /// Asks whether this stuck car may be recycled now. The caller (traffic manager) owns the single
    /// lifecycle transition; this only answers, and answers RequestRecycle at most once per life.
    /// </summary>
    public StuckMonitor.Decision EvaluateStuck(bool visibleToPlayer, float distanceToPlayer, float now) {
        return stuck != null ? stuck.Evaluate(visibleToPlayer, distanceToPlayer, now) : StuckMonitor.Decision.None;
    }

    /// <summary>Lets the fleet-wide recycle cooldown apply to this car too.</summary>
    public void NoteFleetRecycle(float now) {
        stuck?.NoteFleetRecycle(now);
    }

    void Awake() {
        motor = GetComponent<NpcVehicleMotor>();
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>Replaces the follower tuning. Motor tuning is passed per route in <see cref="TryBeginRoute"/>.</summary>
    public void Configure(CivilianFollowerSettings followerSettings) {
        if (followerSettings != null) settings = followerSettings;
    }

    /// <summary>
    /// Binds a route and starts following from the route point nearest the vehicle's current
    /// position. Fails closed: an invalid route, or one this vehicle cannot physically drive,
    /// leaves the follower idle and the motor stopped.
    /// </summary>
    /// <param name="graph">Indexed graph the route lives in.</param>
    /// <param name="route">Closed (or open) route to follow.</param>
    /// <param name="motorTuning">Motor limits of this vehicle's profile, used for cruise speed, braking and turning radius.</param>
    /// <param name="footprint">Collider footprint (width, length): width is checked against every edge's usable width, half the length keeps the nose out of a junction zone while waiting.</param>
    /// <param name="originWorld">Map origin in Unity world space, applied once per coordinate conversion.</param>
    /// <param name="issue">Failure reason, or null when following began.</param>
    /// <returns>True when following began.</returns>
    public bool TryBeginRoute(RoadGraphRuntime graph, CivilianRouteRecord route, NpcMotorSettings motorTuning,
        Vector2 footprint, Vector2 originWorld, out string issue) {
        StopFollowing();
        if (motorTuning == null) {
            issue = "missing motor settings";
            return false;
        }
        if (!cursor.TryBind(graph, route, out issue)) return false;

        var constraints = new RouteTransitionFilter.VehicleConstraints(footprint.x, settings.widthSafetyMargin,
            motorTuning.minimumTurningRadius, settings.maxTurnAngle);
        if (!RouteTransitionFilter.IsDrivable(cursor, constraints, out issue)) {
            cursor.Unbind();
            return false;
        }

        motorSettings = motorTuning;
        mapOriginWorld = originWorld;
        halfLength = Mathf.Max(0f, footprint.y * 0.5f);
        footprintSize = new Vector2(Mathf.Max(0.01f, footprint.x), Mathf.Max(0.01f, footprint.y));
        recovery = new CrashRecoveryPolicy(new CrashRecoveryPolicy.Parameters(settings.lightImpactSpeed, settings.heavyImpactSpeed,
            settings.lightContactHoldSeconds, settings.settleSpeed, settings.settleTimeoutSeconds, settings.rejoinRetrySeconds,
            settings.reverseMaxSeconds, settings.maxRejoinDistance));
        stuck = new StuckMonitor(new StuckMonitor.Parameters(settings.stuckProgressThreshold, settings.stuckSeconds,
            settings.maxStuckSeconds, settings.stuckRecycleCooldownSeconds, settings.stuckRecycleMinPlayerDistance));
        SubscribeReceiver();
        junctionGraph = graph;
        sensor = GetComponent<VehicleObstacleSensor>();
        ResetObstacleState();
        cursor.TryPlaceAt(MapNavigationCoordinates.WorldToLocal(rb.position, mapOriginWorld));
        lastArcPosition = cursor.ArcPosition;
        following = true;
        return true;
    }

    /// <summary>
    /// Attaches the junction arbiter this follower asks for right of way, with the life id it reserves
    /// under. Without one, junctions are crossed on the obstacle sensor alone (legacy/test routes).
    /// </summary>
    public void SetJunctionArbiter(JunctionReservationService service, int lifeId) {
        ReleaseHeldJunction();
        junctions = service;
        junctionOwnerId = lifeId;
    }

    /// <summary>
    /// Injects the static-geometry clearance query (and map-local bounds) used to check a rejoin path
    /// after a crash. Without one, rejoin paths are assumed clear (test/legacy routes).
    /// </summary>
    public void SetRejoinClearance(IAreaClearanceQuery query, Rect localBounds) {
        rejoinClearance = query;
        rejoinBounds = localBounds;
    }

    /// <summary>Reports a physical impact (closing speed) so the recovery state machine can react. Wired automatically from a <see cref="VehicleDamageReceiver"/> on the same object; callable directly by tests and other adapters.</summary>
    public void NotifyImpact(float impactSpeed) {
        recovery?.NotifyImpact(impactSpeed);
    }

    void SubscribeReceiver() {
        UnsubscribeReceiver();
        receiver = GetComponent<VehicleDamageReceiver>();
        if (receiver != null) receiver.ImpactObserved += NotifyImpact;
    }

    void UnsubscribeReceiver() {
        if (receiver != null) receiver.ImpactObserved -= NotifyImpact;
        receiver = null;
    }

    void SetReversePermission(bool value) {
        if (reversePermitted == value) return;
        reversePermitted = value;
        if (motor != null) motor.SetReverseManeuverPermission(value);
    }

    /// <summary>Stops issuing commands and hands the motor a stopped command; the bound route is kept for diagnostics until reset.</summary>
    public void StopFollowing() {
        following = false;
        ReleaseHeldJunction();
        SetReversePermission(false);
        recovery?.Reset();
        UnsubscribeReceiver();
        if (motor != null) motor.SetCommand(NpcDriveCommand.Stopped);
    }

    /// <summary>Clears route, cursor and cached state for pool reuse (register as an NpcVehiclePool reset hook alongside the motor's).</summary>
    public void ResetForNewLife() {
        following = false;
        cursor.Unbind();
        motorSettings = null;
        LastCommand = NpcDriveCommand.Stopped;
        LastAimPointWorld = Vector2.zero;
        LastPlannedSpeed = 0f;
        LastAimAngle = 0f;
        ResetObstacleState();
        ReleaseHeldJunction();
        junctions = null;
        junctionOwnerId = 0;
        junctionGraph = null;
        recovery = null;
        stuck = null;
        rejoinClearance = null;
    }

    void OnDestroy() {
        UnsubscribeReceiver();
    }

    void ReleaseHeldJunction() {
        if (junctions != null) junctions.ReleaseAll(junctionOwnerId);
        heldJunctionId = null;
        heldEntryEdgeIndex = -1;
        IsWaitingAtJunction = false;
    }

    void ResetObstacleState() {
        sensorTimer = 0f;
        timeSinceQuery = 0f;
        obstacleAhead = false;
        obstacleGapAtQuery = float.PositiveInfinity;
        obstacleSpeedAtQuery = 0f;
        blockedByObstacle = false;
        LastObstacleGap = float.PositiveInfinity;
    }

    void FixedUpdate() {
        Tick(Time.fixedDeltaTime);
    }

    void Tick(float deltaTime) {
        if (!following || motor == null || rb == null || motorSettings == null) return;
        if (motor.IsStoppedPermanently) return;

        Vector2 local = MapNavigationCoordinates.WorldToLocal(rb.position, mapOriginWorld);
        cursor.Advance(local, settings.transitionSearchWindow);
        TickStuck(deltaTime);

        if (cursor.ReachedEnd) {
            LastCommand = NpcDriveCommand.Stopped;
            motor.SetCommand(LastCommand);
            return;
        }

        Vector2 forward = transform.up;
        float forwardSpeed = Mathf.Abs(Vector2.Dot(rb.linearVelocity, forward));
        if (TickRecovery(deltaTime, forwardSpeed, forward, local)) return;
        float steering = ComputeSteering(forwardSpeed, forward);
        float planned = ComputePlannedSpeed(forwardSpeed);
        planned = Mathf.Min(planned, ComputeJunctionSpeed());
        planned = Mathf.Min(planned, ComputeObstacleSpeed(forwardSpeed, forward, deltaTime, out bool emergency));
        LastPlannedSpeed = planned;

        float excess = forwardSpeed - planned;
        if (emergency || (planned <= settings.stoppedSpeedThreshold && forwardSpeed > 0f)) {
            // Hold the car with the pedal down rather than coasting toward what blocks it; never reverse.
            LastCommand = new NpcDriveCommand(0f, 1f, steering, 0f, false);
        } else if (excess > 0.05f) {
            // Feed-forward the planned deceleration fraction so the car actually tracks the plan,
            // plus a proportional term for any excess; a purely proportional pedal would settle
            // permanently above the plan.
            float brake = Mathf.Clamp01(settings.brakingComfortFactor + excess / Mathf.Max(0.01f, settings.brakeBand));
            LastCommand = new NpcDriveCommand(0f, brake, steering, planned, false);
        } else {
            LastCommand = new NpcDriveCommand(1f, 0f, steering, planned, false);
        }
        motor.SetCommand(LastCommand);
    }

    float ComputeSteering(float forwardSpeed, Vector2 forward) {
        float lookahead = Mathf.Clamp(settings.lookaheadMin + forwardSpeed * settings.lookaheadTime,
            settings.lookaheadMin, settings.lookaheadMax);
        float toCorner = cursor.DistanceToNextTurn(settings.cornerAngleThreshold, lookahead);
        if (toCorner < lookahead) {
            lookahead = Mathf.Min(lookahead, Mathf.Max(settings.lookaheadMin, toCorner + settings.cornerLookaheadMargin));
        }

        Vector2 aimLocal = cursor.SamplePoint(lookahead, out _);
        LastAimPointWorld = MapNavigationCoordinates.LocalToWorld(aimLocal, mapOriginWorld);
        Vector2 toAim = LastAimPointWorld - rb.position;
        if (toAim.sqrMagnitude <= 0.0001f) {
            LastAimAngle = 0f;
            return 0f;
        }
        float angle = Vector2.SignedAngle(forward, toAim);
        LastAimAngle = angle;
        return Mathf.Clamp(angle / Mathf.Max(1f, settings.fullLockAngle), -1f, 1f);
    }

    /// <summary>
    /// Speed the vehicle should hold right now: the lowest of cruise, this edge's limit, and the
    /// speed from which it can still brake down to the next corner's speed and the next edge's
    /// limit over the remaining distance.
    /// </summary>
    float ComputePlannedSpeed(float forwardSpeed) {
        float target = motorSettings.cruiseSpeed;
        var edge = cursor.CurrentEdge;
        if (edge != null && edge.speedLimit > 0f) target = Mathf.Min(target, edge.speedLimit);

        float deceleration = Mathf.Max(0.01f, motorSettings.brakeDeceleration * settings.brakingComfortFactor);

        if (cursor.TryGetNextTurn(settings.cornerAngleThreshold, settings.speedPlanningDistance, out float toTurn, out float turnAngle)) {
            float cornerSpeed = CornerSpeed(turnAngle);
            target = Mathf.Min(target, AllowedSpeedAtDistance(cornerSpeed, toTurn, deceleration));
        }

        var next = cursor.NextEdge;
        if (next != null && next.speedLimit > 0f && next.speedLimit < target) {
            float toEdge = cursor.DistanceToEdgeEnd;
            if (toEdge <= settings.speedPlanningDistance) {
                target = Mathf.Min(target, AllowedSpeedAtDistance(next.speedLimit, toEdge, deceleration));
            }
        }
        // Mid-corner the aim point sits far off the nose: hold corner speed until the heading is
        // back in line, so the car does not floor it through the apex just because the next
        // corner is far away.
        target = Mathf.Min(target, CornerSpeed(Mathf.Abs(LastAimAngle)));
        return Mathf.Max(0f, target);
    }

    /// <summary>
    /// Drives the post-impact state machine. Returns true when it issued this step's command (the
    /// car is recovering), false when normal route following should run. Observations (route
    /// distance, swept rejoin clearance, blocked-ahead, rear clearance) are only gathered while a
    /// rejoin decision can be pending, never every frame for healthy traffic. Reverse permission on
    /// the motor is opened only during the bounded Reversing phase and closed again immediately.
    /// </summary>
    bool TickRecovery(float deltaTime, float forwardSpeed, Vector2 forward, Vector2 local) {
        if (recovery == null) return false;
        var previous = recovery.Current;
        var observation = default(CrashRecoveryPolicy.RejoinObservation);
        bool needsObservation = previous == CrashRecoveryPolicy.Phase.Settling
            || previous == CrashRecoveryPolicy.Phase.WaitingForRejoin
            || previous == CrashRecoveryPolicy.Phase.Reversing;
        if (needsObservation) observation = ObserveRejoin(local, forward);
        recovery.Tick(deltaTime, forwardSpeed, observation);

        var phase = recovery.Current;
        SetReversePermission(phase == CrashRecoveryPolicy.Phase.Reversing);
        if (phase != previous) {
            IsWaitingAtJunction = false;
            if (phase == CrashRecoveryPolicy.Phase.Driving) sensorTimer = 0f; // re-poll the road on the first driving step
        }

        switch (phase) {
            case CrashRecoveryPolicy.Phase.Driving:
                return false;
            case CrashRecoveryPolicy.Phase.HoldingAfterContact:
            case CrashRecoveryPolicy.Phase.WaitingForRejoin:
                LastCommand = new NpcDriveCommand(0f, 1f, 0f, 0f, false);
                break;
            case CrashRecoveryPolicy.Phase.Settling:
                LastCommand = new NpcDriveCommand(0f, settings.settleBrake, 0f, 0f, false); // pressure cut, momentum settles
                break;
            case CrashRecoveryPolicy.Phase.Reversing:
                LastCommand = new NpcDriveCommand(-1f, 0f, 0f, motorSettings.reverseSpeed, true);
                break;
        }
        LastPlannedSpeed = 0f;
        motor.SetCommand(LastCommand);
        return true;
    }

    /// <summary>
    /// Accumulates non-progress time and, once stuck for reasons other than a visible queue or a
    /// junction wait, triggers one legal local recovery (rejoin/bounded reverse). Queued or
    /// junction-held cars simply keep waiting; they are never made to reverse into the car behind.
    /// </summary>
    void TickStuck(float deltaTime) {
        if (stuck == null) return;
        float arc = cursor.ArcPosition;
        stuck.Tick(deltaTime, arc - lastArcPosition);
        lastArcPosition = arc;
        if (stuck.TryTriggerLocalRecovery() && !blockedByObstacle && !IsWaitingAtJunction && recovery != null) recovery.BeginRecovery();
    }

    CrashRecoveryPolicy.RejoinObservation ObserveRejoin(Vector2 local, Vector2 forward) {
        Vector2 rejoin = cursor.SamplePoint(settings.lookaheadMin, out Vector2 tangent);
        float distanceToRoute = Vector2.Distance(local, cursor.SamplePoint(0f, out _));
        bool pathClear = true;
        if (rejoinClearance != null) {
            // Straight drive toward the rejoin point: an exact oriented box along that line, not the
            // conservative rotating envelope, so a narrow street beside buildings is not "blocked" by its own kerb.
            Vector2 toRejoin = rejoin - local;
            float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(toRejoin.sqrMagnitude > 0.0001f ? toRejoin : tangent);
            pathClear = RoadFootprintClearance.IsSweepClear(local, rejoin, footprintSize, Vector2.zero, heading, heading, rejoinBounds, null, rejoinClearance);
        }
        bool blockedAhead = false;
        bool rearClear = false;
        if (sensor != null) {
            blockedAhead = sensor.TryDetectForwardObstacle(out float ahead, out _) && ahead <= motorSettings.minimumGap;
            rearClear = !sensor.TryDetectRearObstacle(settings.reverseClearanceDistance, out _, out _) && !sensor.LastQuerySaturated;
        }
        return new CrashRecoveryPolicy.RejoinObservation(distanceToRoute, pathClear, blockedAhead, rearClear);
    }

    /// <summary>
    /// Approach → reserve → traverse → release against the junction arbiter. Without a grant the
    /// car brakes to the zone's stop line (minus a margin) and keeps asking; with one it drives
    /// through and releases once its cursor is a clear margin past the zone on the exit edge. A car
    /// that finds itself past the stop line without a grant keeps going rather than parking inside
    /// the zone. Returns +infinity when no junction is relevant.
    /// </summary>
    float ComputeJunctionSpeed() {
        IsWaitingAtJunction = false;
        if (junctions == null) return float.PositiveInfinity;

        // Traversing: release once clear of the zone on the exit edge.
        if (heldJunctionId != null) {
            if (cursor.EdgeIndex != heldEntryEdgeIndex || cursor.LapCount != heldEntryLap) {
                var exitEdge = cursor.CurrentEdge;
                var junction = exitEdge != null ? FindJunction(exitEdge.startJunctionId) : null;
                float exitOffset = 0f;
                if (junction != null) {
                    var polyline = cursor.PolylineAt(cursor.EdgeIndex);
                    exitOffset = JunctionGeometry.DistanceToZoneBoundary(junction.conflictZone, polyline[0], polyline[1] - polyline[0]);
                }
                if (cursor.DistanceAlongEdge >= exitOffset + settings.junctionClearMargin) {
                    junctions.Release(heldJunctionId, junctionOwnerId);
                    heldJunctionId = null;
                    heldEntryEdgeIndex = -1;
                }
            }
            return float.PositiveInfinity;
        }

        var edge = cursor.CurrentEdge;
        var next = cursor.NextEdge;
        if (edge == null || next == null || string.IsNullOrEmpty(edge.endJunctionId)) return float.PositiveInfinity;
        var record = FindJunction(edge.endJunctionId);
        if (record == null) return float.PositiveInfinity;

        var approach = cursor.PolylineAt(cursor.EdgeIndex);
        Vector2 node = approach[approach.Count - 1];
        Vector2 into = node - approach[approach.Count - 2];
        float stopLineOffset = JunctionGeometry.DistanceToZoneBoundary(record.conflictZone, node, -into);
        float toStopLine = cursor.DistanceToEdgeEnd - stopLineOffset - halfLength - settings.junctionStopMargin;
        if (toStopLine > settings.junctionApproachDistance) return float.PositiveInfinity;

        LastJunctionOutcome = junctions.Request(edge.endJunctionId, junctionOwnerId, edge.edgeId, next.edgeId);
        if (LastJunctionOutcome == JunctionReservationService.Outcome.Granted) {
            heldJunctionId = edge.endJunctionId;
            heldEntryEdgeIndex = cursor.EdgeIndex;
            heldEntryLap = cursor.LapCount;
            return float.PositiveInfinity;
        }
        if (toStopLine < -settings.junctionStopMargin) return float.PositiveInfinity; // already past the line: clear the zone, do not park in it
        IsWaitingAtJunction = true;
        float deceleration = Mathf.Max(0.01f, motorSettings.brakeDeceleration * settings.brakingComfortFactor);
        return toStopLine <= 0.05f ? 0f : AllowedSpeedAtDistance(0f, toStopLine, deceleration);
    }

    JunctionRecord FindJunction(string junctionId) {
        return string.IsNullOrEmpty(junctionId) || junctionGraph == null ? null : junctionGraph.GetJunction(junctionId);
    }

    /// <summary>
    /// Speed allowed by whatever is physically ahead (player, another civilian, a wreck, a wall):
    /// the sensor is polled every <see cref="NpcMotorSettings.sensorInterval"/> seconds and the gap
    /// extrapolated between polls, so a queue does not need a cast per car per step. The target is
    /// the speed from which the car can still brake down to the obstacle's own forward speed by the
    /// time the gap closes to the minimum gap; inside the emergency fraction of that gap the pedal
    /// goes to full. Once stopped, the car stays held until the gap re-opens by the resume
    /// hysteresis, so three cars in a queue do not chatter and none of them ever reverses.
    /// </summary>
    float ComputeObstacleSpeed(float forwardSpeed, Vector2 forward, float deltaTime, out bool emergency) {
        emergency = false;
        if (sensor == null) {
            LastObstacleGap = float.PositiveInfinity;
            return float.PositiveInfinity;
        }

        sensorTimer -= deltaTime;
        timeSinceQuery += deltaTime;
        if (sensorTimer <= 0f) {
            sensorTimer = Mathf.Max(0f, motorSettings.sensorInterval);
            timeSinceQuery = 0f;
            obstacleAhead = sensor.TryDetectForwardObstacle(out obstacleGapAtQuery, out Collider2D obstacle);
            obstacleSpeedAtQuery = 0f;
            if (obstacleAhead && obstacle != null && obstacle.attachedRigidbody != null) {
                obstacleSpeedAtQuery = Mathf.Max(0f, Vector2.Dot(obstacle.attachedRigidbody.linearVelocity, forward));
            }
            if (sensor.LastQuerySaturated) {
                // Too many shapes to trust the closest-hit answer: fail safe by treating the road as blocked.
                obstacleAhead = true;
                obstacleGapAtQuery = 0f;
                obstacleSpeedAtQuery = 0f;
            }
        }

        if (!obstacleAhead) {
            blockedByObstacle = false;
            LastObstacleGap = float.PositiveInfinity;
            return float.PositiveInfinity;
        }

        float gap = ObstacleFollowingRules.ExtrapolateGap(obstacleGapAtQuery, forwardSpeed, obstacleSpeedAtQuery, timeSinceQuery);
        LastObstacleGap = gap;
        var parameters = new ObstacleFollowingRules.Parameters(motorSettings.minimumGap, settings.emergencyGapFraction,
            settings.resumeGapHysteresis, motorSettings.brakeDeceleration * settings.brakingComfortFactor, settings.stoppedSpeedThreshold);
        return ObstacleFollowingRules.AllowedSpeed(true, gap, obstacleSpeedAtQuery, forwardSpeed, parameters, ref blockedByObstacle, out emergency);
    }

    /// <summary>Speed for a turn of the given angle: full corner speed sqrt(a·r) at fullSlowdownAngle and above, cruise at zero, linear between.</summary>
    float CornerSpeed(float turnAngleDegrees) {
        float fullCorner = Mathf.Sqrt(Mathf.Max(0f, settings.cornerLateralAcceleration) * Mathf.Max(0.01f, motorSettings.minimumTurningRadius));
        float cruise = motorSettings.cruiseSpeed;
        float t = Mathf.Clamp01(turnAngleDegrees / Mathf.Max(1f, settings.fullSlowdownAngle));
        return Mathf.Lerp(cruise, Mathf.Min(cruise, fullCorner), t);
    }

    /// <summary>Highest speed at which braking at the given deceleration over the distance still reaches the target speed (v² = v₀² + 2·a·d).</summary>
    static float AllowedSpeedAtDistance(float targetSpeed, float distance, float deceleration) {
        return Mathf.Sqrt(targetSpeed * targetSpeed + 2f * deceleration * Mathf.Max(0f, distance));
    }
}
