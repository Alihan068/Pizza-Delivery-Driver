using UnityEngine;

/// <summary>Captures and rechecks one fixed straight ram corridor; never commands movement, damage or lifecycle.</summary>
public sealed class PoliceRamSafety {
    /// <summary>Distinguishes ordinary ineligibility from a query that cannot safely authorize any ram command.</summary>
    public enum Proof {
        /// <summary>Current observations fit the fixed certified corridor and stopping reserve.</summary>
        Clear,
        /// <summary>No admissible straight target/corridor exists; ordinary pursuit may retain its own limits.</summary>
        Ineligible,
        /// <summary>Invalid, saturated or exhausted observations require a stopped command.</summary>
        Unsafe
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
    readonly Collider2D[] overlaps;
    readonly Collider2D[] contacts;
    readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
    Vector2 start, direction, farEnd, targetPosition, finalStart, finalEnd, lastPosition;
    float heading, maximumStep, lateralReserve, travelled;
    long graphVersion;
    string edgeId;
    int segmentIndex;
    bool finalPhase;
    Rigidbody2D target;

    /// <summary>True while an immutable corridor is retained; fresh proof is still required before every command.</summary>
    public bool IsCertified { get; private set; }
    /// <summary>Absolute map-local reserve endpoint; zero when no certificate exists.</summary>
    public Vector2 FarEndpoint => IsCertified ? farEnd : Vector2.zero;

    /// <summary>Captures verified bind-time dependencies and allocates only fixed-capacity query buffers.</summary>
    /// <param name="body">Already bound police component bundle.</param><param name="graph">Versioned route graph.</param>
    /// <param name="bounds">Copied map-local bounds.</param><param name="origin">Map world origin.</param>
    /// <param name="staticClearance">Static-only geometry adapter.</param><param name="driving">Detached validated driving settings.</param>
    /// <param name="motor">Motor tuning whose identity and values the owner guards each tick.</param>
    /// <param name="footprint">Captured actual unrotated collider dimensions.</param><param name="offset">Captured scaled local collider offset.</param>
    /// <param name="crashMultiplier">Captured motor force multiplier, included in post-contact braking reserves.</param>
    /// <param name="clearanceMargin">Captured finite nonnegative navigation padding.</param>
    public PoliceRamSafety(PoliceVehicleBody body, RoadGraphRuntime graph, Rect bounds, Vector2 origin,
        IAreaClearanceQuery staticClearance, PoliceDrivingSettings driving, NpcMotorSettings motor,
        Vector2 footprint, Vector2 offset, float crashMultiplier, float clearanceMargin) {
        this.body = body; this.graph = graph; this.bounds = bounds; this.origin = origin;
        this.staticClearance = staticClearance; this.driving = driving; this.motor = motor;
        this.footprint = footprint; this.offset = offset; this.crashMultiplier = crashMultiplier;
        this.clearanceMargin = clearanceMargin;
        overlaps = new Collider2D[driving.sensorBuffer]; contacts = new Collider2D[driving.sensorBuffer];
    }

    /// <summary>Checks the fixed target reference and admission pose without a query or certificate mutation.</summary>
    /// <param name="candidate">The currently validated live target body.</param>
    /// <returns>True only for the retained target within the authored acquisition tolerance.</returns>
    public bool MatchesTarget(Rigidbody2D candidate) => IsCertified && target != null && candidate == target &&
        Finite(candidate.position) && Vector2.Distance(candidate.position, targetPosition) <= driving.acquisitionTolerance;

    /// <summary>Proves a new corridor or rechecks the original corridor without sliding its endpoint or renewing time.</summary>
    /// <param name="decision">Configured pure ram decision, supplying eligibility and original remaining duration.</param>
    /// <param name="cursor">The current accepted measured road cursor.</param><param name="line">The retained final line, if joined.</param>
    /// <param name="isFinalPhase">True only after measured road-to-final joining.</param>
    /// <param name="candidate">Validated same-world live player rigidbody.</param><param name="deltaTime">Positive physics step.</param>
    /// <param name="work">Shared per-tick finite work allowance.</param>
    /// <param name="speedCap">Reserve-endpoint stopping cap, meaningful only on Clear.</param>
    /// <returns>Clear only after all current static, dynamic, contact and stopping proofs succeed.</returns>
    public Proof Check(PoliceRamDecision decision, PolicePathCursor cursor, PoliceStraightConnector line,
        bool isFinalPhase, Rigidbody2D candidate, float deltaTime, ref int work, out float speedCap) {
        speedCap = 0f;
        if (body == null || candidate == null || staticClearance == null || cursor == null || !cursor.IsBound ||
            !FinitePositive(deltaTime) || body.Motor.IsCrashMode) return Proof.Ineligible;
        Vector2 position = body.Body.position - origin;
        Vector2 forward = ((Vector2)body.transform.up).normalized;
        Vector2 velocity = body.Body.linearVelocity;
        if (!Finite(position) || !Finite(forward) || !Finite(velocity) || !Finite(body.Body.angularVelocity) ||
            !Finite(body.Body.rotation)) return Proof.Unsafe;
        if (body.Body.angularVelocity != 0f || Vector2.Dot(velocity, forward) < 0f) return Proof.Ineligible;
        bool continuing = decision.IsActive;
        if (continuing && (!IsCertified || !MatchesTarget(candidate) || graphVersion != graph.Version || finalPhase != isFinalPhase ||
            deltaTime > maximumStep || body.Body.rotation != heading || !CompatibleGeometry(cursor, line))) return Proof.Unsafe;

        var acquisition = AcquireTarget(decision, candidate, forward, ref work, out float targetGap);
        if (acquisition != Proof.Clear) return acquisition;
        var contactProof = CheckContacts(candidate, ref work);
        if (contactProof != Proof.Clear) return contactProof;
        float acceleration = Mathf.Min(motor.acceleration, motor.maxEngineForce / body.Body.mass);
        float braking = Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / body.Body.mass) * driving.comfort * Mathf.Min(1f, crashMultiplier);
        float duration = continuing ? decision.RemainingActiveSeconds : driving.ramMaximumActiveSeconds;
        if (!PoliceRamDecision.TryReserve(velocity.magnitude, acceleration, braking, duration, motor.reactionTime, deltaTime,
            driving.stopGap, targetGap, driving.maxSweepDistance, out float reserve, out _) ||
            !PoliceRamDecision.TryLateralAllowance(Cross(forward, velocity), motor.lateralGrip, deltaTime, out float lateral)) return Proof.Ineligible;

        Vector2 proofStart = continuing ? start : position;
        Vector2 proofDirection = continuing ? direction : forward;
        Vector2 proofEnd = continuing ? farEnd : position + forward * reserve;
        float proofLateral = continuing ? lateralReserve : lateral + driving.acquisitionTolerance;
        if (!Finite(proofEnd) || !FiniteNonnegative(proofLateral)) return Proof.Unsafe;
        if (continuing) {
            float along = Vector2.Dot(position - start, direction);
            float usedLateral = Mathf.Abs(Cross(direction, position - start));
            float nextTravel = travelled + Vector2.Distance(lastPosition, position);
            float fixedLength = Vector2.Distance(start, farEnd);
            if (!FiniteNonnegative(along) || !FiniteNonnegative(nextTravel) || nextTravel > fixedLength ||
                usedLateral + lateral > proofLateral || along + reserve > fixedLength) return Proof.Unsafe;
        } else if (isFinalPhase) {
            if (!cursor.IsComplete || line == null || !line.IsBound || Vector2.Dot(forward, line.Direction) <= 0f ||
                Vector2.Angle(forward, line.Direction) > driving.connectorAlignmentDegrees) return Proof.Ineligible;
        }

        Vector2 padded = footprint + Vector2.one * clearanceMargin;
        float radius = padded.magnitude * 0.5f + offset.magnitude + proofLateral;
        if (!FinitePositive(radius)) return Proof.Unsafe;
        int roadSegment = continuing ? segmentIndex : -1;
        if (!isFinalPhase) {
            var roadProof = CheckRoad(cursor.CurrentAnchor, proofStart, proofEnd, candidate.position - origin,
                proofDirection, radius, ref roadSegment, ref work);
            if (roadProof != Proof.Clear) return roadProof;
        }
        Vector2 minimum = Vector2.Min(proofStart, proofEnd) - Vector2.one * radius;
        Vector2 maximum = Vector2.Max(proofStart, proofEnd) + Vector2.one * radius;
        Vector2 size = maximum - minimum;
        Vector2 center = (minimum + maximum) * 0.5f;
        if (!Finite(size) || !Finite(center) || !Consume(ref work)) return Proof.Unsafe;
        if (!RoadFootprintClearance.IsPoseClear(center, size, Vector2.zero, 0f, bounds, null, staticClearance)) return Proof.Ineligible;
        var occupancy = CheckOccupancy(center, size, candidate, ref work);
        if (occupancy != Proof.Clear) return occupancy;
        float remaining = Vector2.Dot(proofEnd - position, proofDirection);
        if (!FinitePositive(remaining) || remaining > driving.maxSweepDistance || !Consume(ref work)) return Proof.Unsafe;
        var sweep = body.Sensor.QuerySweep(proofDirection, remaining, candidate, out _, out _);
        if (sweep == VehicleObstacleSensor.SweepStatus.Invalid || sweep == VehicleObstacleSensor.SweepStatus.Saturated) return Proof.Unsafe;
        if (sweep != VehicleObstacleSensor.SweepStatus.Clear) return Proof.Ineligible;
        float stepAcceleration = Mathf.Max(acceleration, Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / body.Body.mass));
        float stepHalf = (velocity.magnitude + stepAcceleration * deltaTime) * deltaTime + padded.magnitude * 0.5f + offset.magnitude;
        if (!FinitePositive(stepHalf) || !Consume(ref work)) return Proof.Unsafe;
        if (!RoadFootprintClearance.IsPoseClear(position, Vector2.one * (2f * stepHalf), Vector2.zero, 0f, bounds, null, staticClearance)) return Proof.Ineligible;
        if (!PoliceDrivingMath.TryAllowedSpeed(0f, Mathf.Max(0f, remaining - driving.stopGap - velocity.magnitude * motor.reactionTime), braking, out speedCap)) return Proof.Unsafe;

        if (!continuing) {
            start = position; direction = forward; farEnd = proofEnd; target = candidate; targetPosition = candidate.position;
            heading = body.Body.rotation; maximumStep = deltaTime; lateralReserve = proofLateral; travelled = 0f;
            graphVersion = graph.Version; edgeId = cursor.CurrentAnchor.edgeId; segmentIndex = roadSegment; finalPhase = isFinalPhase;
            finalStart = isFinalPhase ? line.Start : Vector2.zero; finalEnd = isFinalPhase ? line.End : Vector2.zero;
            IsCertified = true;
        } else travelled += Vector2.Distance(lastPosition, position);
        lastPosition = position;
        return Proof.Clear;
    }

    Proof AcquireTarget(PoliceRamDecision decision, Rigidbody2D candidate, Vector2 forward, ref int work, out float gap) {
        gap = 0f;
        if (!Consume(ref work)) return Proof.Unsafe;
        var sweep = body.Sensor.QuerySweep(forward, driving.ramMaximumSurfaceGap, out gap, out var collider);
        if (sweep == VehicleObstacleSensor.SweepStatus.Invalid || sweep == VehicleObstacleSensor.SweepStatus.Saturated) return Proof.Unsafe;
        if (sweep != VehicleObstacleSensor.SweepStatus.Blocked || collider == null || !collider.enabled || collider.isTrigger ||
            !collider.gameObject.activeInHierarchy || collider.attachedRigidbody != candidate || !FinitePositive(gap)) return Proof.Ineligible;
        if (!Consume(ref work)) return Proof.Unsafe;
        var separation = body.MainCollider.Distance(collider);
        if (!separation.isValid || separation.isOverlapped || !FinitePositive(separation.distance) ||
            !decision.IsEligible(forward, candidate.position - body.Body.position, separation.distance,
                decision.IsActive ? 0f : driving.acquisitionTolerance)) return Proof.Ineligible;
        return Proof.Clear;
    }

    Proof CheckContacts(Rigidbody2D candidate, ref int work) {
        if (!Consume(ref work)) return Proof.Unsafe;
        int count = body.Body.GetContacts(filter, contacts);
        if (count >= contacts.Length) return Proof.Unsafe;
        for (int i = 0; i < count; i++) {
            if (!Consume(ref work)) return Proof.Unsafe;
            if (contacts[i] != null && contacts[i].attachedRigidbody == candidate) return Proof.Ineligible;
        }
        return Proof.Clear;
    }

    Proof CheckOccupancy(Vector2 center, Vector2 size, Rigidbody2D candidate, ref int work) {
        if (!Consume(ref work)) return Proof.Unsafe;
        int count = body.Body.gameObject.scene.GetPhysicsScene2D().OverlapBox(center + origin, size, 0f, filter, overlaps);
        if (count >= overlaps.Length) return Proof.Unsafe;
        for (int i = 0; i < count; i++) {
            if (!Consume(ref work)) return Proof.Unsafe;
            var collider = overlaps[i];
            if (collider == null || collider.isTrigger || collider.attachedRigidbody == body.Body || collider.attachedRigidbody == candidate) continue;
            return Proof.Ineligible;
        }
        return Proof.Clear;
    }

    Proof CheckRoad(RoadPathQuery.EdgeAnchor anchor, Vector2 first, Vector2 last, Vector2 targetPoint,
        Vector2 forward, float radius, ref int selected, ref int work) {
        if (!Consume(ref work)) return Proof.Unsafe;
        var edge = graph.GetEdge(anchor.edgeId);
        var geometry = graph.GetGeometry(anchor.edgeId);
        // Empty or null road roles are unrestricted, matching RoadPathQuery's edge policy.
        if (edge == null || geometry == null || (edge.allowedRoles != null && edge.allowedRoles.Count > 0 && !edge.allowedRoles.Contains(VehicleRole.Police)) ||
            !FinitePositive(edge.speedLimit) || !FinitePositive(edge.usableWidth)) return Proof.Ineligible;
        if (selected < 0) {
            for (int i = 0; i < geometry.Points.Count - 1; i++) {
                if (!Consume(ref work)) return Proof.Unsafe;
                if (anchor.distanceAlongEdge >= geometry.CumulativeLengths[i] && anchor.distanceAlongEdge < geometry.CumulativeLengths[i + 1]) {
                    selected = i; break;
                }
            }
        }
        if (selected < 0 || selected >= geometry.Points.Count - 1) return Proof.Ineligible;
        Vector2 segmentStart = geometry.Points[selected];
        Vector2 delta = geometry.Points[selected + 1] - segmentStart;
        float length = delta.magnitude;
        if (!FinitePositive(length) || Vector2.Dot(forward, delta) <= 0f ||
            Vector2.Angle(forward, delta) > driving.connectorAlignmentDegrees) return Proof.Ineligible;
        Vector2 tangent = delta / length;
        float firstArc = Vector2.Dot(first - segmentStart, tangent), lastArc = Vector2.Dot(last - segmentStart, tangent);
        float targetArc = Vector2.Dot(targetPoint - segmentStart, tangent);
        float side = Mathf.Max(Mathf.Abs(Cross(tangent, first - segmentStart)), Mathf.Abs(Cross(tangent, last - segmentStart)));
        if (!Finite(firstArc) || !Finite(lastArc) || !Finite(targetArc) || !FiniteNonnegative(side) ||
            firstArc < radius || lastArc < firstArc || lastArc + radius > length || targetArc < firstArc || targetArc > length ||
            side + radius > edge.usableWidth * 0.5f) return Proof.Ineligible;
        return Proof.Clear;
    }

    bool CompatibleGeometry(PolicePathCursor cursor, PoliceStraightConnector line) => finalPhase
        ? cursor.IsComplete && line != null && line.IsBound && Exact(line.Start, finalStart) && Exact(line.End, finalEnd)
        : cursor.CurrentAnchor.edgeId == edgeId;

    /// <summary>Releases only this geometric certificate; cooldown/separation state belongs to the pure decision.</summary>
    public void ResetCertificate() {
        IsCertified = false; target = null; edgeId = null; graphVersion = 0; segmentIndex = -1;
        start = direction = farEnd = targetPosition = finalStart = finalEnd = lastPosition = Vector2.zero;
        heading = maximumStep = lateralReserve = travelled = 0f; finalPhase = false;
    }
    static bool Consume(ref int work) { if (work < 1) return false; work--; return true; }
    static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;
    static bool Exact(Vector2 first, Vector2 second) => first.x == second.x && first.y == second.y;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
