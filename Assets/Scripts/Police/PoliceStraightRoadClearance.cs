using System;
using UnityEngine;

/// <summary>Certifies one bounded, aligned, straight-road command and its fixed-heading stop envelope.</summary>
public sealed class PoliceStraightRoadClearance {
    /// <summary>Classifies a straight-road clearance evaluation without owning motor or route state.</summary>
    public enum Result {
        /// <summary>The command is outside this helper's deliberately narrow straight-road scope.</summary>
        NotApplicable,
        /// <summary>The current command has a finite positive throttle fraction that is statically clear.</summary>
        Clear,
        /// <summary>Inputs, work, geometry, query, or stopping proof require a stopped command.</summary>
        Stop
    }

    readonly PoliceVehicleBody body;
    readonly RoadGraphRuntime graph;
    readonly Rect bounds;
    readonly Vector2 origin;
    readonly IAreaClearanceQuery staticClearance;
    readonly PoliceDrivingSettings driving;
    readonly NpcMotorSettings motor;
    readonly Vector2 footprint;
    readonly Vector2 offset;
    readonly float crashMultiplier;
    readonly float clearanceMargin;

    /// <summary>Captures the validated dependencies used by every fresh straight-command proof.</summary>
    /// <param name="body">Bound police body whose actual pose, velocity, sensor, and motor facts are read.</param>
    /// <param name="graph">Current versioned road graph.</param>
    /// <param name="bounds">Copied map-local navigation bounds.</param>
    /// <param name="origin">Map-to-world origin already applied by the controller.</param>
    /// <param name="staticClearance">Static geometry query; missing dependencies fail closed at evaluation.</param>
    /// <param name="driving">Detached validated driving settings.</param>
    /// <param name="motor">Captured motor force, grip, reaction, and speed settings.</param>
    /// <param name="footprint">Actual unrotated police footprint.</param>
    /// <param name="offset">Captured scaled local collider offset.</param>
    /// <param name="crashMultiplier">Captured positive post-contact force multiplier.</param>
    /// <param name="clearanceMargin">Captured navigation padding.</param>
    public PoliceStraightRoadClearance(PoliceVehicleBody body, RoadGraphRuntime graph, Rect bounds, Vector2 origin,
        IAreaClearanceQuery staticClearance, PoliceDrivingSettings driving, NpcMotorSettings motor,
        Vector2 footprint, Vector2 offset, float crashMultiplier, float clearanceMargin) {
        this.body = body; this.graph = graph; this.bounds = bounds; this.origin = origin; this.staticClearance = staticClearance;
        this.driving = driving; this.motor = motor; this.footprint = footprint; this.offset = offset;
        this.crashMultiplier = crashMultiplier; this.clearanceMargin = clearanceMargin;
    }

    /// <summary>Evaluates the current straight segment, sensor sweep, stopping reserve, and discrete safety envelope.</summary>
    /// <param name="cursor">Currently bound route cursor whose anchor and remaining suffix are authoritative.</param>
    /// <param name="resultVersion">Planner result graph version accepted by the controller.</param>
    /// <param name="position">Actual map-local body pivot.</param>
    /// <param name="aim">Actual map-local aim point selected for this command.</param>
    /// <param name="plannedSpeed">Existing controller speed cap before this proof.</param>
    /// <param name="steering">Existing requested steering; only exact zero steering is certifiable here.</param>
    /// <param name="deltaTime">Actual positive physics step.</param>
    /// <param name="work">Shared bounded work remaining for this tick.</param>
    /// <param name="throttleLimit">Certified throttle fraction, or zero when stopped.</param>
    /// <param name="hasUpcomingManeuver">True when the caller reports an upcoming turn or prepared final connector; delegation still requires the legacy proof.</param>
    /// <returns>NotApplicable for pre-proof classification, otherwise Clear or fail-closed Stop.</returns>
    public Result Evaluate(PolicePathCursor cursor, long resultVersion, Vector2 position, Vector2 aim, float plannedSpeed,
        float steering, float deltaTime, ref int work, out float throttleLimit, bool hasUpcomingManeuver = false) {
        throttleLimit = 0f;
        if (cursor == null || graph == null || staticClearance == null || body == null || body.Body == null || body.Sensor == null || driving == null || motor == null ||
            !Finite(bounds.position) || !Finite(bounds.size) || !Finite(bounds.xMax) || !Finite(bounds.yMax) || !Finite(origin) ||
            bounds.width <= 0f || bounds.height <= 0f || !Finite(footprint) || !Finite(offset) || !Finite(body.Body.rotation) ||
            !FinitePositive(footprint.x) || !FinitePositive(footprint.y) || !Finite(clearanceMargin) || clearanceMargin < 0f) return Result.Stop;
        if (!Finite(position) || !Finite(aim) || !FiniteNonnegative(plannedSpeed) || !Finite(steering) || !FinitePositive(deltaTime)) return Result.Stop;
        if (steering != 0f) return Result.NotApplicable;
        if (resultVersion != graph.Version || !cursor.IsBound || !FinitePositive(deltaTime) || !FinitePositive(offset.sqrMagnitude + footprint.sqrMagnitude)) return Result.Stop;
        var anchor = cursor.CurrentAnchor;
        if (!Consume(ref work)) return Result.Stop;
        var edge = graph.GetEdge(anchor.edgeId);
        if (edge == null || !Consume(ref work)) return Result.Stop;
        var geometry = graph.GetGeometry(anchor.edgeId);
        if (geometry == null) return Result.Stop;
        if (edge.allowedRoles != null && edge.allowedRoles.Count > 0 && !edge.allowedRoles.Contains(VehicleRole.Police)) return Result.Stop;
        if (string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId) ||
            !Consume(ref work) || graph.GetNode(edge.fromNodeId) == null || !Consume(ref work) || graph.GetNode(edge.toNodeId) == null)
            return Result.Stop;
        if (!string.IsNullOrEmpty(edge.startJunctionId) && (!Consume(ref work) || graph.GetJunction(edge.startJunctionId) == null)) return Result.Stop;
        if (!string.IsNullOrEmpty(edge.endJunctionId) && (!Consume(ref work) || graph.GetJunction(edge.endJunctionId) == null)) return Result.Stop;
        if (geometry.Points == null || geometry.CumulativeLengths == null || geometry.Points.Count < 2 || geometry.CumulativeLengths.Count != geometry.Points.Count ||
            !FinitePositive(edge.usableWidth) || !FinitePositive(edge.speedLimit)) return Result.Stop;

        int segment = -1;
        for (int i = 0; i < geometry.Points.Count - 1; i++) {
            if (!Consume(ref work)) return Result.Stop;
            if (anchor.distanceAlongEdge >= geometry.CumulativeLengths[i] && anchor.distanceAlongEdge < geometry.CumulativeLengths[i + 1]) { segment = i; break; }
        }
        if (segment < 0) return Result.Stop;
        double c0 = geometry.CumulativeLengths[segment], c1 = geometry.CumulativeLengths[segment + 1];
        double anchorArc = anchor.distanceAlongEdge, endArc = Math.Min(c1, anchorArc + cursor.RemainingDistance);
        if (!Finite(c0) || !Finite(c1) || !Finite(anchorArc) || !Finite(endArc) || c1 <= c0 || anchorArc < c0 || anchorArc >= c1 || endArc <= c0 || endArc > c1) return Result.Stop;
        double clipT = (endArc - c0) / (c1 - c0);
        if (!Finite(clipT) || clipT <= 0d || clipT > 1d) return Result.Stop;
        Vector2 a = geometry.Points[segment], b = geometry.Points[segment + 1];
        double dx = (double)b.x - a.x, dy = (double)b.y - a.y, segmentLength = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(segmentLength) || segmentLength <= 0d) return Result.Stop;
        double clipLength = segmentLength * clipT;
        double clipX = a.x + dx * clipT, clipY = a.y + dy * clipT;
        double tx = dx / segmentLength, ty = dy / segmentLength, nx = -ty, ny = tx;
        Vector2 tangent = new Vector2((float)tx, (float)ty);
        Vector2 forward = ((Vector2)body.transform.up).normalized;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 aimDelta = aim - position;
        if (!Finite(forward) || !FinitePositive(forward.sqrMagnitude) || !Finite(body.Body.linearVelocity) || !Finite(body.Body.angularVelocity) ||
            body.Body.angularVelocity != 0f || Vector2.Dot(forward, tangent) <= 0f || Vector2.Angle(forward, tangent) > driving.connectorAlignmentDegrees ||
            Vector2.Dot(forward, aimDelta) <= 0f) return Result.Stop;
        Vector2 padded = footprint + Vector2.one * clearanceMargin;
        float grip = motor.lateralGrip;
        double lateral = (double)body.Body.linearVelocity.x * right.x + (double)body.Body.linearVelocity.y * right.y;
        double lateralReserve;
        if (!Finite(grip) || grip < 0f || grip > 1f || !Finite(lateral)) return Result.Stop;
        if (grip == 0f) { if (lateral != 0f) return Result.Stop; lateralReserve = 0d; }
        else { lateralReserve = Math.Abs((double)lateral) * (1d - grip) * deltaTime / grip; }
        if (!Finite(lateralReserve)) return Result.Stop;

        double halfWidth = ((double)footprint.x + clearanceMargin) * 0.5d;
        double halfLength = ((double)footprint.y + clearanceMargin) * 0.5d;
        double radians = body.Body.rotation * Mathf.Deg2Rad;
        double cosine = Mathf.Cos((float)radians), sine = Mathf.Sin((float)radians);
        Vector2 bodyRight = new Vector2((float)cosine, (float)sine), bodyForward = new Vector2((float)-sine, (float)cosine);
        double px = (double)body.Body.position.x - origin.x, py = (double)body.Body.position.y - origin.y;
        double cx0 = px + (double)bodyRight.x * offset.x + (double)bodyForward.x * offset.y;
        double cy0 = py + (double)bodyRight.y * offset.x + (double)bodyForward.y * offset.y;
        double fx = forward.x, fy = forward.y, rx = right.x, ry = right.y;
        double supportT = Support(tx, ty, bodyRight, bodyForward, halfWidth, halfLength, rx, ry, lateralReserve);
        double supportN = Support(nx, ny, bodyRight, bodyForward, halfWidth, halfLength, rx, ry, lateralReserve);
        double ax = a.x, ay = a.y;
        if (!TryForces(deltaTime, out double acceleration, out double braking, out double forwardSpeed, out double response)) return Result.Stop;
        double requiredAtZero = lateralReserve + forwardSpeed * response + forwardSpeed * forwardSpeed / (2d * braking) + driving.stopGap;
        double fullEnd = forwardSpeed + acceleration * deltaTime;
        double d1 = lateralReserve + fullEnd * response + fullEnd * fullEnd / (2d * braking) + driving.stopGap;
        if (!Finite(requiredAtZero) || !Finite(d1) || !LateralClear(cx0, cy0, 0d, fx, fy, nx, ny, ax, ay, supportN, edge.usableWidth) ||
            !LateralClear(cx0, cy0, d1, fx, fy, nx, ny, ax, ay, supportN, edge.usableWidth)) return Result.Stop;
        double trialFront = (cx0 - ax) * tx + (cy0 - ay) * ty + d1 * (fx * tx + fy * ty) + supportT;
        double pivotProjection = (px - ax) * tx + (py - ay) * ty;
        if (!Finite(pivotProjection) || pivotProjection < 0d || pivotProjection >= clipLength) return Result.Stop;
        bool rearOverrun = (cx0 - ax) * tx + (cy0 - ay) * ty - supportT < 0d;
        bool aimBeyond = ((double)aim.x - clipX) * tx + ((double)aim.y - clipY) * ty > 0d;
        bool frontOverrun = trialFront >= clipLength;
        bool boundaryDelegation = rearOverrun || aimBeyond || frontOverrun;
        bool beyond = false;
        if (boundaryDelegation && (aimBeyond || frontOverrun)) beyond = endArc == c1 && (double)cursor.RemainingDistance > c1 - anchorArc;
        if (beyond) {
            float sampleDistance = (float)Math.Min((double)cursor.RemainingDistance, c1 - anchorArc + driving.lookaheadMin);
            if (!Consume(ref work) || !cursor.TrySampleAhead(sampleDistance, ref work, out Vector2 after)) return Result.Stop;
            double beyondProjection = ((double)after.x - b.x) * tx + ((double)after.y - b.y) * ty;
            if (!Finite(beyondProjection) || beyondProjection <= 0d) beyond = false;
        }
        if (hasUpcomingManeuver && (aimBeyond || frontOverrun)) return Result.NotApplicable;
        if (rearOverrun) return !(aimBeyond || frontOverrun) || beyond ? Result.NotApplicable : Result.Stop;
        if ((aimBeyond || frontOverrun) && beyond) return Result.NotApplicable;
        // A terminal constrains throttle; a full-throttle trial is not the final stopping proof.
        if (aimBeyond) return Result.Stop;
        double lower = 0d, upper = double.PositiveInfinity;
        if (!AddInterval(ref lower, ref upper, (cx0 - ax) * tx + (cy0 - ay) * ty, fx * tx + fy * ty, supportT, clipLength - supportT) ||
            !AddInterval(ref lower, ref upper, (cx0 - ax) * nx + (cy0 - ay) * ny, fx * nx + fy * ny, -(double)edge.usableWidth * 0.5d + supportN, (double)edge.usableWidth * 0.5d - supportN)) return Result.Stop;
        double roadRange = upper;
        if (!Finite(roadRange) || roadRange <= 0d || lower > 0d) return Result.Stop;
        double maxRange = Math.Min(roadRange, driving.maxSweepDistance);
        if (!Finite(maxRange) || maxRange <= 0d || maxRange > float.MaxValue || requiredAtZero > maxRange) return Result.Stop;
        float range = (float)maxRange;
        if (!FinitePositive(range)) return Result.Stop;
        maxRange = Math.Min(maxRange, range);
        if (requiredAtZero > maxRange || !Consume(ref work)) return Result.Stop;
        var sensorStatus = body.Sensor.QuerySweep(forward, range, out float gap, out var obstacle);
        if (sensorStatus == VehicleObstacleSensor.SweepStatus.Invalid || sensorStatus == VehicleObstacleSensor.SweepStatus.Saturated) return Result.Stop;
        if (sensorStatus == VehicleObstacleSensor.SweepStatus.Blocked) {
            if (!FiniteNonnegative(gap) || obstacle == null || body.MainCollider == null || !Consume(ref work)) return Result.Stop;
            // Native cast and surface-distance queries have different contact margins. Never
            // spend a cast-only gap that is larger than the current actual collider separation.
            var separation = body.MainCollider.Distance(obstacle);
            if (!separation.isValid || separation.isOverlapped || !FiniteNonnegative(separation.distance)) return Result.Stop;
            maxRange = Math.Min(maxRange, Math.Min(gap, separation.distance));
        }
        double room = maxRange - driving.stopGap - lateralReserve;
        if (!Finite(room) || room <= 0d) return Result.Stop;
        double root = Math.Sqrt(response * response + 2d * room / braking);
        double maximumEndSpeed = 2d * room / (root + response);
        double fraction = (maximumEndSpeed - forwardSpeed) / (acceleration * deltaTime);
        if (!Finite(fraction) || fraction <= 0d) return Result.Stop;
        float q = Mathf.Clamp01((float)(fraction * (1d - 4d / 8388608d)));
        if (!FinitePositive(q) || Mathf.Approximately(q, 0f)) return Result.Stop;
        double endSpeed = forwardSpeed + acceleration * q * deltaTime;
        double required = lateralReserve + endSpeed * response + endSpeed * endSpeed / (2d * braking) + driving.stopGap;
        if (!Finite(required) || required > maxRange) return Result.Stop;
        double stopDistance = lateralReserve + endSpeed * response + endSpeed * endSpeed / (2d * braking);
        if (!Finite(stopDistance) || !TryEnclosedQuery(px, py, fx, fy, rx, ry, bodyRight, bodyForward, stopDistance, halfWidth, halfLength, lateralReserve, ref work) || !TryValidateStepEnvelope(position, deltaTime, padded, ref work)) return Result.Stop;
        throttleLimit = q;
        return Result.Clear;
    }

    /// <summary>Validates the unchanged one-step conservative envelope used by legacy maneuvers.</summary>
    /// <param name="localPosition">Actual map-local body pivot.</param><param name="deltaTime">Current physics step.</param>
    /// <param name="padded">Footprint including navigation margin.</param><param name="work">Shared bounded work budget.</param>
    /// <returns>True only when the original finite force and pose checks remain clear.</returns>
    public bool TryValidateStepEnvelope(Vector2 localPosition, float deltaTime, Vector2 padded, ref int work) {
        if (body == null || body.Body == null || body.Motor == null || motor == null || staticClearance == null ||
            !FinitePositive(deltaTime) || !FinitePositive(padded.x) || !FinitePositive(padded.y)) return false;
        float forceMultiplier = body.Motor.IsCrashMode ? crashMultiplier : 1f;
        float engine = Mathf.Min(motor.acceleration, motor.maxEngineForce / body.Body.mass) * forceMultiplier;
        float braking = Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / body.Body.mass) * forceMultiplier;
        float accelerationBound = Mathf.Max(engine, braking);
        if (!FinitePositive(accelerationBound)) return false;
        float radius = (body.Body.linearVelocity.magnitude + accelerationBound * deltaTime) * deltaTime;
        if (!FiniteNonnegative(radius)) return false;
        float oneStepHalf = radius + padded.magnitude * 0.5f + offset.magnitude;
        return FiniteNonnegative(oneStepHalf) && Consume(ref work) && RoadFootprintClearance.IsPoseClear(localPosition,
            Vector2.one * (2f * oneStepHalf), Vector2.zero, 0f, bounds, null, staticClearance);
    }

    bool TryEnclosedQuery(double px, double py, double fx, double fy, double rx, double ry, Vector2 bodyRight,
        Vector2 bodyForward, double distance, double halfWidth, double halfLength, double lateral, ref int work) {
        double c0x = px + (double)bodyRight.x * offset.x + (double)bodyForward.x * offset.y;
        double c0y = py + (double)bodyRight.y * offset.x + (double)bodyForward.y * offset.y;
        double c1x = c0x + fx * distance, c1y = c0y + fy * distance;
        double hx = Math.Abs(bodyRight.x) * halfWidth + Math.Abs(bodyForward.x) * halfLength + Math.Abs(rx) * lateral;
        double hy = Math.Abs(bodyRight.y) * halfWidth + Math.Abs(bodyForward.y) * halfLength + Math.Abs(ry) * lateral;
        double lox = Math.Min(c0x, c1x) - hx, hix = Math.Max(c0x, c1x) + hx;
        double loy = Math.Min(c0y, c1y) - hy, hiy = Math.Max(c0y, c1y) + hy;
        if (!Finite(lox) || !Finite(hix) || !Finite(loy) || !Finite(hiy) || lox < bounds.xMin || hix > bounds.xMax || loy < bounds.yMin || hiy > bounds.yMax) return false;
        if (!TryEncloseAxis(lox, hix, hx, origin.x, out float centerX, out float sizeX) ||
            !TryEncloseAxis(loy, hiy, hy, origin.y, out float centerY, out float sizeY) ||
            !SubmittedWithinBounds(centerX, sizeX, origin.x, bounds.xMin, bounds.xMax) ||
            !SubmittedWithinBounds(centerY, sizeY, origin.y, bounds.yMin, bounds.yMax)) return false;
        if (!Consume(ref work)) return false;
        return staticClearance.IsAreaClear(new Vector2(centerX, centerY), new Vector2(sizeX, sizeY), 0f);
    }

    static bool TryEncloseAxis(double low, double high, double bodyExtent, float originValue, out float center, out float size) {
        center = 0f; size = 0f;
        double midpoint = low * 0.5d + high * 0.5d;
        if (!Finite(midpoint)) return false;
        center = (float)midpoint;
        if (!Finite(center)) return false;
        float worldCenter = center + originValue;
        if (!Finite(worldCenter)) return false;
        double halfNeed = Math.Max(bodyExtent, Math.Max(Math.Max((double)center - low, high - center), Math.Max((double)worldCenter - (low + originValue), (high + originValue) - worldCenter)));
        if (!Finite(halfNeed) || halfNeed < 0d || !Up(2d * halfNeed, out size)) return false;
        float half = size * 0.5f;
        if ((double)center - half > low || (double)center + half < high || (double)worldCenter - half > low + originValue || (double)worldCenter + half < high + originValue) {
            int bits = BitConverter.SingleToInt32Bits(size);
            if (bits <= 0 || bits == int.MaxValue) return false;
            size = BitConverter.Int32BitsToSingle(bits + 1);
            half = size * 0.5f;
        }
        return FinitePositive(size) && (double)center - half <= low && (double)center + half >= high &&
            (double)worldCenter - half <= low + originValue && (double)worldCenter + half >= high + originValue;
    }

    static bool SubmittedWithinBounds(float center, float size, float originValue, float minimum, float maximum) {
        float worldCenter = center + originValue;
        double half = size * 0.5f;
        return Finite(worldCenter) && (double)center - half >= minimum && (double)center + half <= maximum &&
            (double)worldCenter - originValue - half >= minimum && (double)worldCenter - originValue + half <= maximum;
    }

    static bool Up(double value, out float result) {
        result = 0f;
        if (!Finite(value) || value < 0d || value > float.MaxValue) return false;
        result = (float)value;
        if (!Finite(result)) return false;
        if ((double)result < value) {
            int bits = BitConverter.SingleToInt32Bits(result);
            if (bits < 0 || bits == int.MaxValue) return false;
            result = BitConverter.Int32BitsToSingle(bits + 1);
        }
        return FinitePositive(result) && (double)result >= value;
    }

    bool TryForces(float deltaTime, out double acceleration, out double braking, out double forwardSpeed, out double response) {
        acceleration = braking = forwardSpeed = response = 0d;
        if (!FinitePositive(body.Body.mass) || !FinitePositive(motor.acceleration) || !FinitePositive(motor.maxEngineForce) ||
            !FinitePositive(motor.brakeDeceleration) || !FinitePositive(motor.maxBrakeForce) || !FinitePositive(crashMultiplier) ||
            !FiniteNonnegative(motor.reactionTime) || !FinitePositive(driving.comfort) || driving.comfort > 1f) return false;
        Vector2 forward = ((Vector2)body.transform.up).normalized;
        forwardSpeed = (double)body.Body.linearVelocity.x * forward.x + (double)body.Body.linearVelocity.y * forward.y;
        if (!Finite(forwardSpeed) || forwardSpeed < 0d) return false;
        double multiplier = Math.Max(1d, crashMultiplier);
        acceleration = Math.Min((double)motor.acceleration, (double)motor.maxEngineForce / (double)body.Body.mass) * multiplier;
        braking = Math.Min((double)motor.brakeDeceleration, (double)motor.maxBrakeForce / (double)body.Body.mass) * (double)driving.comfort * Math.Min(1d, (double)crashMultiplier);
        response = motor.reactionTime + 2d * deltaTime;
        return Finite(acceleration) && Finite(braking) && Finite(response) && acceleration > 0d && braking > 0d && response >= 0d;
    }

    static double Support(double ax, double ay, Vector2 right, Vector2 forward, double halfWidth, double halfLength, double rx, double ry, double lateral) =>
        Math.Abs(right.x * ax + right.y * ay) * halfWidth + Math.Abs(forward.x * ax + forward.y * ay) * halfLength + lateral * Math.Abs(rx * ax + ry * ay);
    static bool LateralClear(double cx, double cy, double distance, double fx, double fy, double nx, double ny, double ax, double ay, double support, float width) {
        double value = (cx + distance * fx - ax) * nx + (cy + distance * fy - ay) * ny;
        return Finite(value) && Math.Abs(value) + support <= width * 0.5d;
    }
    static bool AddInterval(ref double lower, ref double upper, double value, double direction, double minimum, double maximum) {
        if (!Finite(value) || !Finite(direction) || !Finite(minimum) || !Finite(maximum) || minimum > maximum) return false;
        double left = minimum - value, right = maximum - value;
        if (direction == 0d) return left <= 0d && right >= 0d;
        double a = left / direction, b = right / direction;
        if (a > b) { double swap = a; a = b; b = swap; }
        lower = Math.Max(lower, a); upper = Math.Min(upper, b);
        return lower <= upper;
    }
    static bool Consume(ref int work) { if (work < 1) return false; work--; return true; }
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
