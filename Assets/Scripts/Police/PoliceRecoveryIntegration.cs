using System.Text;
using UnityEngine;

/// <summary>
/// Observes one impact recovery against the captured directed road and the existing planner.
/// It returns commands only: the controller owns motor permissions, and the receiver owns damage,
/// crash mode and lifecycle. Unsupported joins or incomplete observations remain stopped.
/// </summary>
public sealed class PoliceRecoveryIntegration {
    readonly PoliceVehicleBody body;
    readonly RoadGraphRuntime graph;
    readonly PolicePursuitTargetPlanner planner;
    readonly Rect bounds;
    readonly Vector2 origin, footprint, offset;
    readonly IAreaClearanceQuery staticClearance;
    readonly PoliceDrivingSettings driving;
    readonly PoliceRecoverySettings settings;
    readonly NpcMotorSettings motor;
    readonly float crashMultiplier;
    readonly PoliceRecoveryTraversal traversal;
    readonly Collider2D[] overlaps;
    readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
    readonly PoliceStraightConnector rejoin = new PoliceStraightConnector();
    readonly PolicePathCursor.TrackingLimits tracking;
    RoadPathQuery.EdgeAnchor floor, gateAnchor;
    long version;
    bool hasEvidence, hasGate, arrived, reverseCertified, rejoinCertified;
    int arrivalAttempt;
    float retryAt, currentClock, segmentLength, segmentArc, roadWidth, edgeSpeed;
    Vector2 segmentStart, tangent, gate;
    Rigidbody2D retainedTarget;
    Vector2 reverseStart, reverseEnd, reverseDirection, rejoinEnd, crossingStop;
    float reverseHeading, reverseStep, reverseLateral, rejoinHeading, rejoinStep;
    string firstDiagnostic;
    bool diagnosticEnabled;
    StringBuilder diagnosticHistory;
    int diagnosticEntryCount;
    string lastDiagnosticLabel;

    /// <summary>True while recovery, including exhausted waiting, owns driving priority.</summary>
    public bool IsRecovering => traversal.IsRecovering;
    /// <summary>The shared policy phase; exhausted waiting is observable separately.</summary>
    public CrashRecoveryPolicy.Phase Phase => traversal.Phase;
    /// <summary>True after the original episode bound forbids further powered maneuvers.</summary>
    public bool IsExhausted => traversal.IsExhausted;
    /// <summary>Latest bounded recovery command; all ordinary waits return full brake.</summary>
    public NpcDriveCommand Command { get; private set; } = NpcDriveCommand.Stopped;
    /// <summary>True for one handled step only after a post-arrival fresh route authorizes normal resumption.</summary>
    public bool ResumeReady { get; private set; }
    /// <summary>Immutable map-local rejoin gate, meaningful only after road evidence was resolved.</summary>
    public Vector2 Gate => gate;
    /// <summary>Actual reverse and braking travel accounted by the pure wrapper.</summary>
    public float ReverseTravel => traversal.ReverseTravel;

    /// <summary>Captures validated binding facts and allocates fixed query storage; no planning or movement occurs.</summary>
    public PoliceRecoveryIntegration(PoliceVehicleBody body, RoadGraphRuntime graph, PolicePursuitTargetPlanner planner,
        Rect bounds, Vector2 origin, IAreaClearanceQuery staticClearance, PoliceDrivingSettings driving,
        NpcMotorSettings motor, Vector2 footprint, Vector2 offset, float margin, float crashMultiplier) {
        this.body = body; this.graph = graph; this.planner = planner; this.bounds = bounds; this.origin = origin;
        this.staticClearance = staticClearance; this.driving = driving; settings = driving.recovery;
        this.motor = motor; this.footprint = footprint + Vector2.one * margin; this.offset = offset;
        this.crashMultiplier = crashMultiplier;
        traversal = new PoliceRecoveryTraversal(settings, driving.stoppedSpeedThreshold);
        tracking = new PolicePathCursor.TrackingLimits(driving.cursorWindow, driving.maxDeviation,
            driving.displacementSlack, driving.acquisitionTolerance);
        overlaps = new Collider2D[driving.sensorBuffer];
    }

    /// <summary>
    /// Records an actual own-body impact before the controller clears its cursor. Queries are
    /// deferred to Step, and later contacts cannot replace the original directed floor or timer.
    /// </summary>
    /// <returns>True only when a new recovery episode was opened.</returns>
    public bool CaptureImpact(float impact, float clock, bool onRoad, RoadPathQuery.EdgeAnchor anchor) {
        bool opened = traversal.NotifyImpact(impact, clock, body.Body.position - origin);
        CancelMovement();
        if (opened || !IsRecovering) CaptureEvidence(onRoad, anchor);
        return opened;
    }

    /// <summary>Revokes certificates and reverse permission intent without renewing clocks, route floor or credit.</summary>
    public void CancelMovement() {
        traversal.CancelMovement(); reverseCertified = rejoinCertified = false; rejoin.Reset();
        Command = NpcDriveCommand.Stopped;
    }

    /// <summary>Closes motion on pause; the caller must also freeze its active session clock.</summary>
    public void Pause() { traversal.Pause(); CancelMovement(); }

    /// <summary>
    /// Accounts time/travel before observations, uses the same planner at most once, and returns
    /// true whenever recovery owns this step, including the stopped handoff after successful release.
    /// </summary>
    public bool Step(float deltaTime, float clock, Rigidbody2D target, bool onRoad,
        RoadPathQuery.EdgeAnchor currentAnchor, ref int work) {
        firstDiagnostic = null;
        Command = NpcDriveCommand.Stopped; ResumeReady = false; currentClock = clock;
        Vector2 position = body.Body.position - origin;
        Vector2 forward = body.transform.up;
        float speed = body.Body.linearVelocity.magnitude;
        bool wasRecovering = IsRecovering;
        if (!traversal.Account(clock, deltaTime, position, speed, body.Motor.IsCrashMode)) { CancelMovement(); return true; }
        if (!IsRecovering) { hasEvidence = false; return false; }
        if (!wasRecovering) {
            if (onRoad || !hasEvidence) CaptureEvidence(onRoad, currentAnchor);
            planner.InvalidateRoute();
        }
        if (target == null || !Finite(position) || !Finite(forward) || !Finite(speed) || !Finite(body.Body.rotation) ||
            !Finite(body.Body.angularVelocity) || !hasEvidence || graph.Version != version) return Deny(speed);
        // Recovery excludes no target collider. Fresh corridor proofs handle motion; only an
        // identity change invalidates here, so normal movement cannot keep postponing retries.
        if (retainedTarget != null && retainedTarget != target) {
            CancelMovement(); retainedTarget = target; retryAt = clock;
            planner.InvalidateRoute();
            return Deny(speed);
        }
        if (retainedTarget == null) retainedTarget = target;
        if (!hasGate && speed > driving.stoppedSpeedThreshold) {
            traversal.ObservePolicy(speed, default, false);
            if (!IsExhausted && traversal.Phase == CrashRecoveryPolicy.Phase.Settling)
                Command = new NpcDriveCommand(0f, settings.settleBrake, 0f, 0f, false);
            return true;
        }
        if (!hasGate && !TryChooseGate(position, forward, ref work)) return Deny(speed);
        if (!Consume(ref work)) return Deny(speed);
        var route = planner.Update(position, forward, target.position - origin, clock, true, gateAnchor);
        bool validRoute = ValidRoute(route);
        if (!validRoute) return Deny(speed);
        if (clock < retryAt && !reverseCertified && !rejoinCertified && !arrived) {
            return true;
        }
        if (!TryFront(deltaTime, ref work, out bool blockedAhead, out float braking)) return Deny(speed);
        float distance = Vector2.Distance(position, gate);
        bool atGate = AtGate(position, forward, deltaTime);
        if (arrived && !atGate) { arrived = false; arrivalAttempt = -1; planner.InvalidateRoute(); return Deny(speed); }
        bool forwardClear = !blockedAhead && distance <= settings.maxRejoinDistance;
        bool rearClear = false;
        if (!IsExhausted && blockedAhead && (traversal.IsReversing ||
            speed <= driving.stoppedSpeedThreshold && traversal.Phase != CrashRecoveryPolicy.Phase.HoldingAfterContact)) {
            rearClear = TryReverseProof(position, forward, speed, deltaTime, braking, ref work);
        }
        var observation = new CrashRecoveryPolicy.RejoinObservation(distance, forwardClear, blockedAhead, rearClear);
        traversal.ObservePolicy(speed, observation, true);
        if (traversal.IsExhausted) {
            CancelMovement();
            bool owned = TryResume(atGate, forwardClear, route, speed, deltaTime, ref work);
            // An exhausted episode that cannot reach its gate used to keep issuing Stopped forever,
            // parking the unit next to the obstacle. Release the episode and hand the motor back to
            // ordinary pursuit instead; a genuinely blocked unit simply re-enters recovery later.
            if (owned && !ResumeReady && !atGate) {
                RecordDiagnostic("exhausted.release");
                traversal.ResetForNewLife();
                return false;
            }
            return owned;
        }
        switch (traversal.Phase) {
            case CrashRecoveryPolicy.Phase.HoldingAfterContact:
                return true;
            case CrashRecoveryPolicy.Phase.Settling:
                Command = new NpcDriveCommand(0f, settings.settleBrake, 0f, 0f, false);
                return true;
            case CrashRecoveryPolicy.Phase.Reversing:
                if (!rearClear) { RecordDiagnostic("reverse-command.rear-proof"); return Deny(speed); }
                if (!reverseCertified) { RecordDiagnostic("reverse-command.certificate"); return Deny(speed); }
                if (!traversal.IsReversing && !traversal.TryStartReverse()) {
                    RecordDiagnostic("reverse-command.start");
                    return Deny(speed);
                }
                IssueReverse(speed, deltaTime, braking);
                return true;
            case CrashRecoveryPolicy.Phase.WaitingForRejoin:
                CancelMovement();
                if (TryNextRetry(clock, out float next)) retryAt = next;
                return true;
            default:
                if (reverseCertified) { CancelMovement(); return true; }
                if (!forwardClear) return Deny(speed);
                if (atGate) return TryResume(true, true, route, speed, deltaTime, ref work);
                if (body.Motor.IsCrashMode || Vector2.Dot(body.Body.linearVelocity, forward) < 0f) return true;
                if (!TryRejoin(position, forward, speed, deltaTime, braking, ref work)) return Deny(speed);
                return true;
        }
    }

    void CaptureEvidence(bool onRoad, RoadPathQuery.EdgeAnchor anchor) {
        hasEvidence = onRoad && !string.IsNullOrEmpty(anchor.edgeId);
        floor = anchor; version = graph.Version; hasGate = arrived = false; arrivalAttempt = -1;
        retryAt = 0f; retainedTarget = null; gate = Vector2.zero;
    }

    bool TryChooseGate(Vector2 position, Vector2 forward, ref int work) {
        if (!Consume(ref work)) return false;
        var edge = graph.GetEdge(floor.edgeId);
        var geometry = graph.GetGeometry(floor.edgeId);
        // Empty or null road roles are unrestricted, matching RoadPathQuery's edge policy.
        if (edge == null || geometry == null || (edge.allowedRoles != null && edge.allowedRoles.Count > 0 && !edge.allowedRoles.Contains(VehicleRole.Police)) ||
            !Positive(edge.speedLimit) || !Positive(edge.usableWidth)) return false;
        int selected = -1;
        for (int i = 0; i < geometry.Points.Count - 1; i++) {
            if (!Consume(ref work)) return false;
            if (floor.distanceAlongEdge >= geometry.CumulativeLengths[i] && floor.distanceAlongEdge < geometry.CumulativeLengths[i + 1]) { selected = i; break; }
        }
        if (selected < 0) return false;
        segmentStart = geometry.Points[selected];
        Vector2 delta = geometry.Points[selected + 1] - segmentStart;
        segmentLength = delta.magnitude; segmentArc = geometry.CumulativeLengths[selected];
        if (!Positive(segmentLength)) return false;
        tangent = delta / segmentLength; roadWidth = edge.usableWidth; edgeSpeed = edge.speedLimit;
        float arc = Mathf.Max(floor.distanceAlongEdge - segmentArc, Vector2.Dot(position - segmentStart, tangent));
        if (!Finite(arc) || arc < 0f || arc > segmentLength || Vector2.Angle(forward, tangent) > driving.connectorAlignmentDegrees) return false;
        gate = segmentStart + tangent * arc;
        if (!Finite(gate) || Vector2.Distance(position, gate) > settings.maxRejoinDistance) return false;
        gateAnchor = new RoadPathQuery.EdgeAnchor(floor.edgeId, segmentArc + arc);
        hasGate = true;
        return true;
    }

    bool ValidRoute(PoliceRoadTargetQuery.Result route) => route != null && route.status == PoliceRoadTargetQuery.Status.Route &&
        route.graphVersion == version && route.startAnchor.edgeId == gateAnchor.edgeId &&
        Mathf.Abs(route.startAnchor.distanceAlongEdge - gateAnchor.distanceAlongEdge) <= driving.acquisitionTolerance;

    bool TryFront(float deltaTime, ref int work, out bool blocked, out float braking) {
        blocked = false; braking = Braking();
        float speed = body.Body.linearVelocity.magnitude;
        if (!Positive(braking) || !PoliceDrivingMath.TrySweepDistance(speed, braking, motor.reactionTime,
            driving.stopGap, driving.maxSweepDistance, out float range) || !Consume(ref work)) return false;
        var status = body.Sensor.QuerySweep(body.transform.up, range, out float gap, out _);
        if (status == VehicleObstacleSensor.SweepStatus.Invalid || status == VehicleObstacleSensor.SweepStatus.Saturated) return false;
        blocked = status == VehicleObstacleSensor.SweepStatus.Blocked && gap <= driving.stopGap + driving.acquisitionTolerance;
        return true;
    }

    bool TryReverseProof(Vector2 position, Vector2 forward, float speed, float deltaTime, float braking, ref int work) {
        float alignment = Vector2.Angle(forward, tangent);
        float forwardVelocity = Vector2.Dot(body.Body.linearVelocity, forward);
        if (staticClearance == null) {
            RecordDiagnostic("reverse.initial.static-clearance");
            return false;
        }
        if (!RotationStepWithinTolerance(body.Body.angularVelocity, deltaTime)) {
            RecordDiagnostic("reverse.initial.rotation", body.Body.angularVelocity, deltaTime, speed);
            return false;
        }
        if (alignment > driving.connectorAlignmentDegrees) {
            RecordDiagnostic("reverse.initial.alignment", alignment, driving.connectorAlignmentDegrees, speed);
            return false;
        }
        if (forwardVelocity > 0f) {
            RecordDiagnostic("reverse.initial.forward-velocity", forwardVelocity, speed);
            return false;
        }
        if (!reverseCertified && speed > driving.stoppedSpeedThreshold) {
            RecordDiagnostic("reverse.initial.speed", speed, driving.stoppedSpeedThreshold);
            return false;
        }
        float remaining = reverseCertified ? settings.reverseMaxSeconds - traversal.ReverseSeconds : settings.reverseMaxSeconds;
        float cap = Mathf.Min(settings.maneuverSpeed, Mathf.Min(motor.reverseSpeed, edgeSpeed));
        float acceleration = Acceleration();
        float lateralSpeed = Mathf.Abs(Cross(forward, body.Body.linearVelocity));
        double peak = System.Math.Max(speed, (double)cap + acceleration * deltaTime + lateralSpeed);
        double range = peak * ((double)remaining + motor.reactionTime + deltaTime) + peak * peak / (2d * braking) + peak * deltaTime + driving.stopGap;
        if (!Positive(remaining) || !Finite(range) || range <= 0d || range > driving.maxSweepDistance) {
            RecordDiagnostic("reverse.bounds", remaining, range, driving.maxSweepDistance);
            return false;
        }
        if (!PoliceRamDecision.TryLateralAllowance(lateralSpeed, motor.lateralGrip, deltaTime, out float lateral)) {
            RecordDiagnostic("reverse.lateral", lateralSpeed, motor.lateralGrip, deltaTime);
            return false;
        }
        Vector2 start = reverseCertified ? reverseStart : position;
        Vector2 direction = reverseCertified ? reverseDirection : -forward;
        Vector2 end = reverseCertified ? reverseEnd : position + direction * (float)range;
        float drift = reverseCertified ? reverseLateral : lateral + driving.acquisitionTolerance;
        if (reverseCertified && (deltaTime > reverseStep || !HeadingWithinTolerance(body.Body.rotation, reverseHeading) ||
            Mathf.Abs(Cross(direction, position - start)) + lateral > drift ||
            Vector2.Dot(position - start, direction) < 0f || Vector2.Dot(position - start, direction) + range > Vector2.Distance(start, end))) {
            RecordDiagnostic("reverse.continuation");
            return false;
        }
        if (!ProofCorridor(start, end, drift, ref work)) {
            RecordDiagnostic("reverse.corridor");
            return false;
        }
        if (!ProofStep(position, speed, deltaTime, ref work)) {
            RecordDiagnostic("reverse.step");
            return false;
        }
        float cast = Vector2.Dot(end - position, direction);
        if (!Positive(cast)) {
            RecordDiagnostic("reverse.cast", cast);
            return false;
        }
        if (!Consume(ref work)) {
            RecordDiagnostic("reverse.cast");
            return false;
        }
        if (body.Sensor.QuerySweep(direction, cast, out _, out _) != VehicleObstacleSensor.SweepStatus.Clear) {
            RecordDiagnostic("reverse.sensor", cast);
            return false;
        }
        if (!reverseCertified) {
            reverseStart = position; reverseEnd = end; reverseDirection = direction;
            reverseHeading = body.Body.rotation; reverseStep = deltaTime; reverseLateral = drift;
            reverseCertified = true;
        }
        return true;
    }

    void IssueReverse(float speed, float deltaTime, float braking) {
        double remaining = (double)settings.maximumReverseTravel - traversal.ReverseTravel;
        float cap = Mathf.Min(settings.maneuverSpeed, Mathf.Min(motor.reverseSpeed, edgeSpeed));
        if (speed > cap) {
            RecordDiagnostic("reverse.issue.speed", speed, cap);
            CancelMovement();
            return;
        }
        if (!TryBoundedThrottle(deltaTime, braking, remaining, out float throttle)) {
            RecordDiagnostic("reverse.issue.throttle", speed, cap, (float)remaining);
            CancelMovement();
            return;
        }
        Command = new NpcDriveCommand(-throttle, 0f, 0f, cap, true);
    }

    bool TryRejoin(Vector2 position, Vector2 forward, float speed, float deltaTime, float braking, ref int work) {
        if (!RotationStepWithinTolerance(body.Body.angularVelocity, deltaTime)) {
            RecordDiagnostic("rejoin.rotation", body.Body.angularVelocity, deltaTime, speed);
            return false;
        }
        float angle = Vector2.Angle(forward, tangent);
        if (angle > driving.connectorAlignmentDegrees) {
            RecordDiagnostic("rejoin.connector-angle", angle, driving.connectorAlignmentDegrees, speed);
            return false;
        }
        float lateral = Mathf.Abs(Cross(tangent, position - gate));
        if (lateral > driving.acquisitionTolerance) {
            RecordDiagnostic("rejoin.lateral", lateral, driving.acquisitionTolerance, speed);
            return false;
        }
        float progress = Vector2.Dot(gate - position, tangent);
        if (progress <= 0f) {
            RecordDiagnostic("rejoin.progress", progress, speed);
            return false;
        }
        if (!rejoinCertified) {
            if (!rejoin.TryBind(position, gate, position, tracking, driving.connectorAlignmentDegrees)) {
                RecordDiagnostic("rejoin.bind", speed);
                return false;
            }
            rejoinHeading = body.Body.rotation; rejoinStep = deltaTime;
            rejoinEnd = gate + tangent * driving.stopGap;
            if (!TryCaptureCrossingStop(position, forward, deltaTime)) {
                RecordDiagnostic("rejoin.crossing-stop", speed);
                return false;
            }
            rejoinCertified = true;
        }
        if (deltaTime > rejoinStep) { RecordDiagnostic("rejoin.step-time", deltaTime, rejoinStep); return false; }
        if (!HeadingWithinTolerance(body.Body.rotation, rejoinHeading)) { RecordDiagnostic("rejoin.heading", body.Body.rotation, rejoinHeading); return false; }
        if (!rejoin.TryAdvance(position, forward, ref work, out _)) { RecordDiagnostic("rejoin.advance", speed); return false; }
        if (!ProofCorridor(rejoin.Start, rejoinEnd, driving.acquisitionTolerance, ref work)) { RecordDiagnostic("rejoin.corridor", speed); return false; }
        if (!ProofStep(position, speed, deltaTime, ref work)) { RecordDiagnostic("rejoin.step", speed); return false; }
        float remaining = Vector2.Dot(rejoinEnd - position, forward);
        if (!Positive(remaining)) { RecordDiagnostic("rejoin.remaining", remaining); return false; }
        if (remaining > driving.maxSweepDistance) { RecordDiagnostic("rejoin.range", remaining, driving.maxSweepDistance); return false; }
        if (!Consume(ref work)) { RecordDiagnostic("rejoin.work", speed); return false; }
        if (body.Sensor.QuerySweep(forward, remaining, out _, out _) != VehicleObstacleSensor.SweepStatus.Clear) { RecordDiagnostic("rejoin.sensor", remaining); return false; }
        float stopDistance = Vector2.Dot(crossingStop - position, forward);
        Vector2 actualStop = position + forward * stopDistance;
        if (!Positive(stopDistance)) { RecordDiagnostic("rejoin.stop-distance", stopDistance); return false; }
        if (stopDistance > remaining) { RecordDiagnostic("rejoin.stop-range", stopDistance, remaining); return false; }
        if (!TryLateralDrift(deltaTime, out double drift)) { RecordDiagnostic("rejoin.drift", speed); return false; }
        if (!InsideCrossingWindow(actualStop, forward, drift)) { RecordDiagnostic("rejoin.crossing-window", actualStop, drift); return false; }
        if (!ProofCorridor(position, actualStop, (float)(driving.acquisitionTolerance + drift), ref work)) { RecordDiagnostic("rejoin.crossing-corridor", speed); return false; }
        float cap = Mathf.Min(settings.maneuverSpeed, Mathf.Min(edgeSpeed, motor.maxSpeed));
        if (speed <= cap && TryBoundedThrottle(deltaTime, braking, stopDistance, out float throttle))
            Command = new NpcDriveCommand(throttle, 0f, 0f, cap, false);
        return true;
    }

    bool TryCaptureCrossingStop(Vector2 position, Vector2 forward, float deltaTime) {
        // Intersect the actual forward ray with the unchanged gate's tolerance disk and plane.
        // The interior stop point leaves room for finite braking after a genuine crossing.
        Vector2 delta = position - gate;
        double along = Vector2.Dot(delta, forward);
        double alignment = Vector2.Dot(forward, tangent);
        if (!TryLateralDrift(deltaTime, out double lateral)) return false;
        double radius = driving.acquisitionTolerance - lateral;
        double discriminant = along * along - (double)delta.x * delta.x - (double)delta.y * delta.y + radius * radius;
        if (radius <= 0d || !Finite(discriminant) || discriminant <= 0d || !Finite(alignment) || alignment <= 0d) return false;
        double root = System.Math.Sqrt(discriminant);
        double plane = (-Vector2.Dot(delta, tangent) + lateral * System.Math.Abs(Cross(forward, tangent))) / alignment;
        double lower = System.Math.Max(0d, System.Math.Max(-along - root, plane));
        double upper = System.Math.Min(-along + root, Vector2.Dot(rejoinEnd - position, forward));
        if (!Finite(lower) || !Finite(upper) || upper <= lower || upper > float.MaxValue) return false;
        crossingStop = position + forward * (float)(lower + (upper - lower) * 0.5d);
        return InsideCrossingWindow(crossingStop, forward, lateral);
    }

    bool InsideCrossingWindow(Vector2 position, Vector2 forward, double lateral) => Finite(position) && Finite(lateral) && lateral >= 0d &&
        Vector2.Dot(position - gate, tangent) > lateral * System.Math.Abs(Cross(forward, tangent)) &&
        Vector2.Distance(position, gate) + lateral <= driving.acquisitionTolerance;

    bool TryLateralDrift(float deltaTime, out double lateral) {
        float speed = Mathf.Abs(Cross(body.transform.up, body.Body.linearVelocity));
        lateral = 0d;
        if (!PoliceRamDecision.TryLateralAllowance(speed, motor.lateralGrip, deltaTime, out _)) return false;
        lateral = speed == 0f ? 0d : (double)speed * (1d - motor.lateralGrip) * deltaTime / motor.lateralGrip;
        return Finite(lateral) && lateral >= 0d;
    }

    bool TryBoundedThrottle(float deltaTime, float braking, double distance, out float throttle) {
        throttle = 0f;
        float acceleration = Acceleration();
        Vector2 forward = body.transform.up;
        if (!Positive(deltaTime) || !Positive(braking) || !Positive(acceleration) || !Finite(distance) || distance <= 0d ||
            !TryLateralDrift(deltaTime, out double lateral)) return false;
        double room = distance - lateral;
        double speed = System.Math.Abs(Vector2.Dot(body.Body.linearVelocity, forward));
        double response = (double)motor.reactionTime + 2d * deltaTime;
        if (!Finite(room) || room <= 0d || !Finite(speed) || !Finite(response)) return false;
        // Stable positive root of u*response + u^2/(2*b) = room, where u is END-of-pulse speed.
        double maximumEndSpeed = 2d * room / (System.Math.Sqrt(response * response + 2d * room / braking) + response);
        double fraction = (maximumEndSpeed - speed) / ((double)acceleration * deltaTime);
        if (!Finite(fraction) || fraction <= 0d) return false;
        // Leave four binary32 machine epsilons for converting the command fraction to float.
        throttle = (float)(System.Math.Min(1d, fraction) * (1d - 4d / 8388608d));
        double endSpeed = speed + (double)acceleration * throttle * deltaTime;
        double predicted = lateral + endSpeed * response + endSpeed * endSpeed / (2d * braking);
        if (!Positive(throttle) || !Finite(predicted) || predicted > distance) { throttle = 0f; return false; }
        return true;
    }

    bool TryResume(bool atGate, bool forwardClear, PoliceRoadTargetQuery.Result route, float speed, float deltaTime, ref int work) {
        if (!atGate || !forwardClear || !ValidRoute(route)) return true;
        Vector2 position = body.Body.position - origin;
        if (!ProofCorridor(position, position, driving.acquisitionTolerance, ref work) || !ProofStep(position, speed, deltaTime, ref work)) return Deny(speed);
        if (!arrived) {
            arrived = true; arrivalAttempt = planner.AttemptCount;
            CancelMovement(); planner.InvalidateRoute();
            return true;
        }
        if (planner.AttemptCount <= arrivalAttempt || speed > driving.stoppedSpeedThreshold || body.Motor.IsCrashMode) return true;
        ResumeReady = traversal.TryRelease(true, false, speed);
        return true;
    }

    bool AtGate(Vector2 position, Vector2 forward, float deltaTime) {
        float distance = Vector2.Distance(position, gate);
        if (!(distance <= driving.acquisitionTolerance)) return false;
        float progress = Vector2.Dot(position - gate, tangent);
        if (!(progress >= 0f)) return false;
        float angle = Vector2.Angle(forward, tangent);
        return angle <= driving.connectorAlignmentDegrees && RotationStepWithinTolerance(body.Body.angularVelocity, deltaTime);
    }

    bool ProofCorridor(Vector2 start, Vector2 end, float lateral, ref int work) {
        if (!Finite(start) || !Finite(end) || !Finite(lateral) || lateral < 0f || staticClearance == null || !Consume(ref work)) return false;
        float radius = footprint.magnitude * 0.5f + offset.magnitude + lateral;
        float first = Vector2.Dot(start - segmentStart, tangent), last = Vector2.Dot(end - segmentStart, tangent);
        float side = Mathf.Max(Mathf.Abs(Cross(tangent, start - segmentStart)), Mathf.Abs(Cross(tangent, end - segmentStart)));
        if (!Finite(radius) || !Finite(side) || Mathf.Min(first, last) < radius || Mathf.Max(first, last) + radius > segmentLength ||
            side + radius > roadWidth * 0.5f) return false;
        Vector2 size = footprint + new Vector2(lateral * 2f, 0f);
        float heading = body.Body.rotation;
        if (!Consume(ref work) || !RoadFootprintClearance.IsSweepClear(start, end, size, offset, heading, heading, bounds, null, staticClearance)) return false;
        Vector2 forward = body.transform.up;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 delta = end - start;
        size += new Vector2(Mathf.Abs(Vector2.Dot(delta, right)), Mathf.Abs(Vector2.Dot(delta, forward)));
        Vector2 center = (start + end) * 0.5f + right * offset.x + forward * offset.y;
        if (!Finite(size) || !Finite(center) || !Consume(ref work)) return false;
        int count = body.gameObject.scene.GetPhysicsScene2D().OverlapBox(center + origin, size, heading, filter, overlaps);
        if (count >= overlaps.Length) return false;
        for (int i = 0; i < count; i++) {
            if (!Consume(ref work)) return false;
            var hit = overlaps[i];
            if (hit != null && !hit.isTrigger && hit.attachedRigidbody != body.Body) return false;
        }
        return true;
    }

    bool ProofStep(Vector2 position, float speed, float deltaTime, ref int work) {
        double acceleration = System.Math.Max(Acceleration(), System.Math.Min(motor.brakeDeceleration,
            motor.maxBrakeForce / body.Body.mass) * System.Math.Max(1f, crashMultiplier));
        double radius = ((double)speed + acceleration * deltaTime) * deltaTime + footprint.magnitude * 0.5f + offset.magnitude;
        if (!Finite(radius) || radius <= 0d || radius * 2d > float.MaxValue || !Consume(ref work)) return false;
        return RoadFootprintClearance.IsPoseClear(position, Vector2.one * (float)(radius * 2d), Vector2.zero, 0f, bounds, null, staticClearance);
    }

    bool Deny(float speed) {
        CancelMovement(); traversal.ObservePolicy(Finite(speed) && speed >= 0f ? speed : 0f, default, false);
        if (traversal.Phase == CrashRecoveryPolicy.Phase.WaitingForRejoin && TryNextRetry(currentClock, out float next)) retryAt = next;
        return true;
    }

    static bool RotationStepWithinTolerance(float angularVelocity, float deltaTime) {
        const float machineStepTolerance = 4f * (1f / 8388608f);
        return Finite(angularVelocity) && Finite(deltaTime) && deltaTime >= 0f &&
            Mathf.Abs(angularVelocity) * deltaTime <= machineStepTolerance;
    }

    static bool HeadingWithinTolerance(float actual, float expected) {
        const float machineHeadingTolerance = 4f * (1f / 8388608f);
        return Finite(actual) && Finite(expected) && Mathf.Abs(Mathf.DeltaAngle(actual, expected)) <= machineHeadingTolerance;
    }

    void RecordDiagnostic(string label) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Empty);
    }

    void RecordDiagnostic(string label, float first) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "a={0:R}", first));
    }

    void RecordDiagnostic(string label, float first, float second) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "a={0:R};b={1:R}", first, second));
    }

    void RecordDiagnostic(string label, float first, float second, float third) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "a={0:R};b={1:R};c={2:R}", first, second, third));
    }

    void RecordDiagnostic(string label, double first, double second, double third) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "a={0:R};b={1:R};c={2:R}", first, second, third));
    }

    void RecordDiagnostic(string label, Vector2 first, float second) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0:R};y={1:R};a={2:R}", first.x, first.y, second));
    }

    void RecordDiagnostic(string label, Vector2 first, double second) {
        if (!diagnosticEnabled) return;
        RecordDiagnosticCore(label, string.Format(System.Globalization.CultureInfo.InvariantCulture, "x={0:R};y={1:R};a={2:R}", first.x, first.y, second));
    }

    void RecordDiagnosticCore(string label, string payload) {
        if (!diagnosticEnabled || diagnosticEntryCount >= 32 || lastDiagnosticLabel == label) return;
        if (diagnosticHistory == null) diagnosticHistory = new StringBuilder();
        string entry = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "t={0:R};{1};forwardVelocity={2:R};speed={3:R};canStartReverse={4};{5}", currentClock, label,
            Vector2.Dot(body.Body.linearVelocity, body.transform.up), body.Body.linearVelocity.magnitude,
            traversal.CanStartReverse, payload);
        if (firstDiagnostic == null) firstDiagnostic = entry;
        if (diagnosticHistory.Length > 0) diagnosticHistory.Append('|');
        diagnosticHistory.Append(entry);
        lastDiagnosticLabel = label;
        diagnosticEntryCount++;
    }

    bool TryNextRetry(float clock, out float next) {
        double value = (double)clock + settings.rejoinRetrySeconds;
        next = (float)value;
        if (Finite(value) && value <= float.MaxValue && next > clock) return true;
        retryAt = float.MaxValue;
        return false;
    }
    float Braking() => Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / body.Body.mass) * driving.comfort * Mathf.Min(1f, crashMultiplier);
    float Acceleration() => Mathf.Min(motor.acceleration, motor.maxEngineForce / body.Body.mass) * Mathf.Max(1f, crashMultiplier);
    static bool Consume(ref int work) { if (work < 1) return false; work--; return true; }
    static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool Positive(float value) => Finite(value) && value > 0f;
}
