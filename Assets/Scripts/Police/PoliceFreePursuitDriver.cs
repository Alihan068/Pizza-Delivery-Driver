using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Pure free-drive pursuit policy that converts an actual pose and valid path into one motor command.</summary>
public sealed class PoliceFreePursuitDriver {
    /// <summary>Measured target state used by the Intercept role.</summary>
    public readonly struct InterceptTarget {
        /// <summary>Creates a measured target snapshot.</summary>
        public InterceptTarget(Vector2 position, Vector2 velocity) { this.position = position; this.velocity = velocity; }
        /// <summary>Measured target world position.</summary>
        public readonly Vector2 position;
        /// <summary>Measured target world velocity.</summary>
        public readonly Vector2 velocity;
    }

    /// <summary>Computes a bounded intercept point from measured player velocity only.</summary>
    /// <param name="target">Measured target position and velocity.</param>
    /// <param name="predictionSeconds">Authored prediction horizon.</param>
    /// <param name="maximumLeadDistance">Maximum permitted lead distance.</param>
    /// <param name="predictedPosition">Current position plus bounded measured-velocity lead.</param>
    /// <returns>True when all inputs are finite and the bounded point was produced.</returns>
    public static bool TryPredictIntercept(InterceptTarget target, float predictionSeconds, float maximumLeadDistance, out Vector2 predictedPosition) {
        predictedPosition = target.position;
        if (!Finite(target.position) || !Finite(target.velocity) || !Finite(predictionSeconds) || !Finite(maximumLeadDistance) ||
            predictionSeconds < 0f || maximumLeadDistance < 0f) return false;
        Vector2 lead = target.velocity * predictionSeconds;
        float length = lead.magnitude;
        if (length > maximumLeadDistance && length > 0.0001f) lead *= maximumLeadDistance / length;
        predictedPosition = target.position + lead;
        return Finite(predictedPosition);
    }

    /// <summary>Result of one full swept avoidance candidate query.</summary>
    public readonly struct AvoidanceResult {
        /// <summary>Creates a candidate query result.</summary>
        public AvoidanceResult(bool clear, float clearance) { this.clear = clear; this.clearance = clearance; }
        /// <summary>Whether the complete candidate sweep was clear.</summary>
        public readonly bool clear;
        /// <summary>Measured candidate clearance; positive infinity means no obstacle was hit.</summary>
        public readonly float clearance;
    }

    /// <summary>Injected local sensor API; implementations may use Physics2D without coupling this policy to a scene.</summary>
    public interface IAvoidanceQuery {
        /// <summary>Checks the complete swept candidate corridor.</summary>
        AvoidanceResult Check(Vector2 start, Vector2 end, float headingDegrees, float horizonSeconds, float lateralOffset);
    }

    /// <summary>Actual body pose supplied by the owner each tick.</summary>
    public readonly struct Pose {
        /// <summary>Creates an actual pose snapshot.</summary>
        public Pose(Vector2 position, Vector2 forward, Vector2 velocity) { this.position = position; this.forward = forward; this.velocity = velocity; }
        /// <summary>Actual world position.</summary>
        public readonly Vector2 position;
        /// <summary>Actual transform.up direction.</summary>
        public readonly Vector2 forward;
        /// <summary>Actual world velocity.</summary>
        public readonly Vector2 velocity;
    }

    readonly NpcMotorSettings motor;
    readonly PoliceDrivingSettings driving;
    readonly float mass;
    IReadOnlyList<PoliceFreePathPlanner.Primitive> path;
    int committedIndex = -1;
    float committedUntil;
    float clock;
    float pathProgress;

    /// <summary>True when the last tick found no clear local avoidance candidate.</summary>
    public bool NeedsReplan { get; private set; }
    /// <summary>Last selected avoidance candidate index, or -1 when none was selected.</summary>
    public int SelectedAvoidanceIndex { get; private set; } = -1;
    /// <summary>Creates a pure policy with detached driving settings and a positive mass fact.</summary>
    public PoliceFreePursuitDriver(NpcMotorSettings motorSettings, PoliceDrivingSettings drivingSettings, float bodyMass = 1f) {
        motor = motorSettings == null ? null : new NpcMotorSettings {
            cruiseSpeed = motorSettings.cruiseSpeed, maxSpeed = motorSettings.maxSpeed,
            reverseSpeed = motorSettings.reverseSpeed, acceleration = motorSettings.acceleration,
            brakeDeceleration = motorSettings.brakeDeceleration, maxEngineForce = motorSettings.maxEngineForce,
            maxBrakeForce = motorSettings.maxBrakeForce, turnRate = motorSettings.turnRate,
            minimumTurningRadius = motorSettings.minimumTurningRadius, lateralGrip = motorSettings.lateralGrip,
            sensorInterval = motorSettings.sensorInterval, reactionTime = motorSettings.reactionTime,
            minimumGap = motorSettings.minimumGap
        };
        driving = drivingSettings != null ? drivingSettings.Clone() : null;
        mass = bodyMass;
    }

    /// <summary>Replaces the valid free path consumed by subsequent ticks.</summary>
    public void SetPath(IReadOnlyList<PoliceFreePathPlanner.Primitive> validPath) { path = validPath; pathProgress = 0f; NeedsReplan = false; }
    /// <summary>Clears path, avoidance commitment, and output diagnostics for reuse.</summary>
    public void Reset() { path = null; pathProgress = 0f; committedIndex = -1; committedUntil = 0f; clock = 0f; NeedsReplan = false; SelectedAvoidanceIndex = -1; }

    /// <summary>Produces a command only; this method never writes a transform, velocity, or rotation.</summary>
    public NpcDriveCommand Tick(Pose pose, float deltaTime, IAvoidanceQuery avoidance = null) {
        NeedsReplan = false; SelectedAvoidanceIndex = -1;
        if (!ValidInputs(pose, deltaTime)) return NpcDriveCommand.Stopped;
        clock += deltaTime;
        float speed = Vector2.Dot(pose.velocity, pose.forward.normalized);
        float lookahead = Mathf.Clamp(Mathf.Abs(speed) * driving.lookaheadTime, driving.lookaheadMin, driving.lookaheadMax);
        UpdateProgress(pose.position);
        Vector2 aim = SampleAt(pathProgress + lookahead, out float pathHeading);
        if (!Finite(aim)) { NeedsReplan = true; return BrakeCommand(speed, driving.stoppedSpeedThreshold); }
        Vector2 right = new Vector2(pose.forward.y, -pose.forward.x);
        Vector2 delta = aim - pose.position;
        float denominator = Mathf.Max(delta.sqrMagnitude, 0.0001f);
        float curvature = -2f * Vector2.Dot(delta, right) / denominator;
        curvature = Mathf.Clamp(curvature, -1f / motor.minimumTurningRadius, 1f / motor.minimumTurningRadius);
        float steering = Steering(curvature, speed);
        float cornerCap = Mathf.Sqrt(driving.lateralAcceleration / Mathf.Max(Mathf.Abs(curvature), 0.0001f));
        float targetSpeed = Mathf.Min(motor.maxSpeed, Mathf.Min(motor.cruiseSpeed, cornerCap));
        float remaining = 0f;
        for (int index = 0; index < path.Count; index++) remaining += path[index].length;
        remaining = Mathf.Max(0f, remaining - pathProgress);
        float deceleration = Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / mass);
        if (!FinitePositive(deceleration)) return NpcDriveCommand.Stopped;
        float reactionDistance = Mathf.Abs(speed) * motor.reactionTime;
        float arrivalCap = Mathf.Sqrt(2f * deceleration * Mathf.Max(0f, remaining - reactionDistance));
        targetSpeed = Mathf.Min(targetSpeed, arrivalCap);
        if (remaining <= driving.stopGap && Vector2.Distance(pose.position, path[path.Count - 1].endPivot) <= driving.stopGap)
            return BrakeCommand(speed, 0f);
        if (avoidance != null) {
            int selected = ChooseAvoidance(pose, aim, pathHeading, speed, avoidance);
            if (selected < 0) { NeedsReplan = true; return BrakeCommand(speed, targetSpeed); }
            if (selected != 2) {
                aim += right * CandidateOffsets[selected];
                delta = aim - pose.position;
                curvature = Mathf.Clamp(-2f * Vector2.Dot(delta, right) / Mathf.Max(delta.sqrMagnitude, 0.0001f), -1f / motor.minimumTurningRadius, 1f / motor.minimumTurningRadius);
                steering = Steering(curvature, speed);
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(driving.lateralAcceleration / Mathf.Max(Mathf.Abs(curvature), 0.0001f)));
            }
        }
        if (Mathf.Abs(speed) <= driving.stoppedSpeedThreshold) steering = 0f;
        return speed > targetSpeed ? BrakeCommand(speed, targetSpeed, steering) : new NpcDriveCommand(1f, 0f, steering, targetSpeed, false);
    }

    int ChooseAvoidance(Pose pose, Vector2 aim, float heading, float speed, IAvoidanceQuery query) {
        int best = -1; float bestScore = float.NegativeInfinity;
        for (int index = 0; index < CandidateOffsets.Length; index++) {
            float offset = CandidateOffsets[index]; Vector2 right = new Vector2(pose.forward.y, -pose.forward.x);
            var result = query.Check(pose.position, aim + right * offset, heading, driving.avoidanceHorizon, offset);
            if (!result.clear || float.IsNaN(result.clearance) || result.clearance < 0f) continue;
            float progress = Mathf.Clamp01(1f - Vector2.Distance(pose.position, aim + right * offset) / Mathf.Max(Vector2.Distance(pose.position, aim), 0.0001f));
            float clearance = Mathf.Clamp01(result.clearance / Mathf.Max(Mathf.Abs(speed) * driving.avoidanceHorizon + driving.stopGap, 0.0001f));
            float headingError = Mathf.Clamp01(Mathf.Abs(offset) / Mathf.Max(driving.lookaheadMax, 0.0001f));
            float switchCost = committedIndex >= 0 && committedIndex != index && clock < committedUntil ? 1f : 0f;
            float score = driving.avoidanceProgressWeight * progress + driving.avoidanceClearanceWeight * clearance -
                driving.avoidanceHeadingWeight * headingError - driving.avoidanceSwitchWeight * switchCost;
            bool tie = Mathf.Abs(score - bestScore) <= 0.0001f;
            if (best < 0 || score > bestScore + 0.0001f || (tie && index == committedIndex) || (tie && index < best)) { best = index; bestScore = score; }
        }
        if (best >= 0) {
            if (committedIndex != best || clock >= committedUntil) committedUntil = clock + driving.avoidanceCommitment;
            committedIndex = best;
            SelectedAvoidanceIndex = best;
        }
        return best;
    }

    Vector2 SampleAt(float distance, out float heading) {
        heading = 0f; if (path == null || path.Count == 0) return new Vector2(float.NaN, float.NaN);
        float remaining = Mathf.Max(0f, distance);
        for (int index = 0; index < path.Count; index++) { var p = path[index]; if (remaining <= p.length) return Sample(p, remaining, out heading); remaining -= p.length; }
        var last = path[path.Count - 1]; heading = last.endHeadingDegrees; return last.endPivot;
    }

    void UpdateProgress(Vector2 position) {
        if (path == null) return;
        float offset = 0f;
        float nearest = float.PositiveInfinity;
        float progress = pathProgress;
        for (int index = 0; index < path.Count; index++) {
            var primitive = path[index];
            if (offset + primitive.length < pathProgress) { offset += primitive.length; continue; }
            float t;
            if (primitive.kind == PoliceFreePathPlanner.PrimitiveKind.Straight) {
                Vector2 line = primitive.endPivot - primitive.startPivot;
                t = Mathf.Clamp01(Vector2.Dot(position - primitive.startPivot, line) / Mathf.Max(line.sqrMagnitude, 0.0001f));
            } else {
                Vector2 forward = Rotate(Vector2.up, primitive.startHeadingDegrees);
                Vector2 center = primitive.startPivot + new Vector2(-forward.y, forward.x) *
                    Mathf.Sign(primitive.deltaHeadingDegrees) * primitive.turnRadius;
                float angle = Vector2.SignedAngle(primitive.startPivot - center, position - center);
                t = Mathf.Clamp01(angle / primitive.deltaHeadingDegrees);
            }
            float distance = Mathf.Max(pathProgress - offset, t * primitive.length);
            Vector2 projected = Sample(primitive, distance, out _);
            float error = (position - projected).sqrMagnitude;
            if (error < nearest) { nearest = error; progress = offset + distance; }
            offset += primitive.length;
        }
        pathProgress = progress;
    }

    static Vector2 Sample(PoliceFreePathPlanner.Primitive p, float distance, out float heading) {
        float t = Mathf.Clamp01(distance / Mathf.Max(p.length, 0.0001f));
        if (p.kind == PoliceFreePathPlanner.PrimitiveKind.Straight) { heading = p.startHeadingDegrees; return Vector2.Lerp(p.startPivot, p.endPivot, t); }
        float radius = Mathf.Max(p.turnRadius, 0.0001f); float delta = p.deltaHeadingDegrees * t * Mathf.Deg2Rad;
        Vector2 forward = Rotate(Vector2.up, p.startHeadingDegrees); Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 center = p.startPivot + left * Mathf.Sign(p.deltaHeadingDegrees) * radius; Vector2 from = p.startPivot - center;
        float angle = Mathf.Atan2(from.y, from.x) + delta; heading = p.startHeadingDegrees + p.deltaHeadingDegrees * t;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    float Steering(float curvature, float speed) { if (Mathf.Abs(speed) <= driving.stoppedSpeedThreshold) return 0f; float yaw = Mathf.Abs(speed * curvature) * Mathf.Rad2Deg; float limit = Mathf.Min(motor.turnRate, Mathf.Abs(speed) / motor.minimumTurningRadius * Mathf.Rad2Deg); return Mathf.Clamp(curvature < 0f ? -yaw / Mathf.Max(limit, 0.0001f) : yaw / Mathf.Max(limit, 0.0001f), -1f, 1f); }
    NpcDriveCommand BrakeCommand(float speed, float target, float steering = 0f) {
        float decel = Mathf.Min(motor.brakeDeceleration, motor.maxBrakeForce / Mathf.Max(mass, 0.0001f));
        if (!FinitePositive(decel) || !Finite(motor.reactionTime) || motor.reactionTime < 0f) return NpcDriveCommand.Stopped;
        double required = (double)Mathf.Abs(speed) * motor.reactionTime + (double)speed * speed / (2d * decel) + driving.stopGap;
        if (double.IsNaN(required) || double.IsInfinity(required) || required > float.MaxValue) return NpcDriveCommand.Stopped;
        float brake = target <= driving.stoppedSpeedThreshold ? 1f : Mathf.Clamp01(driving.comfort + Mathf.Max(0f, (float)required - driving.stopGap) / Mathf.Max(driving.brakeBand, 0.0001f));
        return Finite(brake) ? new NpcDriveCommand(0f, brake, steering, target, false) : NpcDriveCommand.Stopped;
    }
    static readonly float[] CandidateOffsets = { -1f, -0.5f, 0f, 0.5f, 1f };
    static Vector2 Rotate(Vector2 v, float degrees) { float r = degrees * Mathf.Deg2Rad; return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r)); }
    bool ValidInputs(Pose p, float dt) => motor != null && driving != null && mass > 0f && Finite(p.position) && Finite(p.forward) && Finite(p.velocity) && p.forward.sqrMagnitude > 0.001f && dt > 0f && Finite(dt) && motor.minimumTurningRadius > 0f && motor.turnRate > 0f;
    static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    static bool FinitePositive(float v) => Finite(v) && v > 0f;
}
