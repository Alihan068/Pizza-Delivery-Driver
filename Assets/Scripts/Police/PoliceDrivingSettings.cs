using System;

/// <summary>Serializable, bounded authoring values shared by the police pursuit driving phases.</summary>
[Serializable]
public sealed class PoliceDrivingSettings {
    /// <summary>Maximum lateral acceleration used to cap speed in a curve.</summary>
    public float lateralAcceleration = 4f;
    /// <summary>Finite horizon used by local avoidance candidate sweeps.</summary>
    public float avoidanceHorizon = 0.75f;
    /// <summary>Time for which a selected avoidance candidate remains committed.</summary>
    public float avoidanceCommitment = 0.5f;
    /// <summary>Weight assigned to forward progress during avoidance scoring.</summary>
    public float avoidanceProgressWeight = 1f;
    /// <summary>Weight assigned to physical clearance during avoidance scoring.</summary>
    public float avoidanceClearanceWeight = 1f;
    /// <summary>Weight assigned to heading error during avoidance scoring.</summary>
    public float avoidanceHeadingWeight = 0.5f;
    /// <summary>Weight assigned to changing an already committed avoidance side.</summary>
    public float avoidanceSwitchWeight = 0.25f;
    /// <summary>Minimum distance used to classify a no-progress pursuit as stuck.</summary>
    public float stuckDuration = 2f;
    /// <summary>Maximum distance permitted in one free-drive reverse recovery.</summary>
    public float reverseMaxDistance = 2f;
    /// <summary>Maximum duration permitted in one free-drive reverse recovery.</summary>
    public float reverseMaxDuration = 2f;
    /// <summary>Maximum number of free-drive reverse attempts in one episode.</summary>
    public int reverseMaxAttempts = 2;
    /// <summary>Cooldown between bounded free-drive recovery attempts.</summary>
    public float reverseCooldown = 1f;
    /// <summary>Minimum forward lookahead distance used by pursuit planning.</summary>
    public float lookaheadMin = 2.5f;
    /// <summary>Maximum forward lookahead distance used by pursuit planning.</summary>
    public float lookaheadMax = 8f;
    /// <summary>Time horizon used to acquire a forward lookahead target.</summary>
    public float lookaheadTime = 0.5f;
    /// <summary>Maximum route distance inspected by one bounded planning request.</summary>
    public float planningDistance = 20f;
    /// <summary>Fraction of the available braking envelope retained for comfort.</summary>
    public float comfort = 0.7f;
    /// <summary>Minimum stopping gap maintained after braking.</summary>
    public float stopGap = 1.2f;
    /// <summary>Absolute target speed at a fully slowed corner.</summary>
    public float cornerSpeed = 2f;
    /// <summary>Angle at which corner speed reaches its full slowdown value.</summary>
    public float fullSlowdownAngle = 90f;
    /// <summary>Additional braking band around a target speed change.</summary>
    public float brakeBand = 1.5f;
    /// <summary>Speed below which the vehicle is treated as stopped.</summary>
    public float stoppedSpeedThreshold = 0.1f;
    /// <summary>Maximum allowed heading error for a straight connector.</summary>
    public float connectorAlignmentDegrees = 5f;
    /// <summary>Maximum caller-requested sensor sweep distance.</summary>
    public float maxSweepDistance = 64f;
    /// <summary>Initial capacity of the explicit sensor hit buffer.</summary>
    public int sensorBuffer = 16;
    /// <summary>Runtime cursor work allowance per bounded step.</summary>
    public int cursorWork = 64;
    /// <summary>Bind-time cursor work allowance.</summary>
    public int bindWork = 4096;
    /// <summary>Forward projection window supplied to the cursor.</summary>
    public float cursorWindow = 6f;
    /// <summary>Maximum accepted distance from the authored route.</summary>
    public float maxDeviation = 2f;
    /// <summary>Multiplier applied to measured displacement for acquisition.</summary>
    public float displacementSlack = 1.5f;
    /// <summary>Small non-stationary acquisition allowance.</summary>
    public float acquisitionTolerance = 0.05f;
    /// <summary>Measured outgoing distance required before a latched corner may release.</summary>
    public float turnExitDistance = 1.5f;
    /// <summary>Maximum heading error from the certified outgoing tangent at corner release.</summary>
    public float turnExitAlignmentDegrees = 8f;
    /// <summary>Maximum measured pivot travel while one corner latch is active.</summary>
    public float maximumTurnTravel = 16f;
    /// <summary>Maximum active-session duration while one corner latch is active.</summary>
    public float maximumTurnActiveSeconds = 8f;
    /// <summary>Maximum target angle accepted by the bounded ram decision.</summary>
    public float ramConeDegrees = 12f;
    /// <summary>Maximum target surface gap accepted by the bounded ram decision.</summary>
    public float ramMaximumSurfaceGap = 4f;
    /// <summary>Maximum active duration of one bounded ram decision.</summary>
    public float ramMaximumActiveSeconds = 1f;
    /// <summary>Cooldown applied after a bounded ram decision is cancelled.</summary>
    public float ramCooldownSeconds = 2f;
    /// <summary>Nested finite limits for impact-triggered bounded police recovery.</summary>
    public PoliceRecoverySettings recovery = new PoliceRecoverySettings();

    /// <summary>Validates finite values, ordered bounds, positive denominators, and positive work capacities.</summary>
    /// <param name="reason">A stable English diagnostic when validation fails.</param>
    /// <returns>True when every authored value is safe for bounded driving work.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!Finite(lateralAcceleration) || !Finite(avoidanceHorizon) || !Finite(avoidanceCommitment) ||
            !Finite(avoidanceProgressWeight) || !Finite(avoidanceClearanceWeight) || !Finite(avoidanceHeadingWeight) ||
            !Finite(avoidanceSwitchWeight) || !Finite(stuckDuration) || !Finite(reverseMaxDistance) ||
            !Finite(reverseMaxDuration) || !Finite(reverseCooldown) || !Finite(lookaheadMin) || !Finite(lookaheadMax) || !Finite(lookaheadTime) || !Finite(planningDistance) ||
            !Finite(comfort) || !Finite(stopGap) || !Finite(cornerSpeed) || !Finite(fullSlowdownAngle) ||
            !Finite(brakeBand) || !Finite(stoppedSpeedThreshold) || !Finite(connectorAlignmentDegrees) ||
            !Finite(maxSweepDistance) || !Finite(cursorWindow) || !Finite(maxDeviation) ||
            !Finite(displacementSlack) || !Finite(acquisitionTolerance) || !Finite(turnExitDistance) || !Finite(turnExitAlignmentDegrees) ||
            !Finite(maximumTurnTravel) || !Finite(maximumTurnActiveSeconds) || !Finite(ramConeDegrees) ||
            !Finite(ramMaximumSurfaceGap) || !Finite(ramMaximumActiveSeconds) || !Finite(ramCooldownSeconds)) {
            reason = "police driving settings contain a non-finite value";
            return false;
        }
        if (lateralAcceleration <= 0f || avoidanceHorizon <= 0f || avoidanceCommitment <= 0f || avoidanceProgressWeight < 0f ||
            avoidanceClearanceWeight < 0f || avoidanceHeadingWeight < 0f || avoidanceSwitchWeight < 0f || stuckDuration <= 0f ||
            reverseMaxDistance <= 0f || reverseMaxDuration <= 0f || reverseMaxAttempts <= 0 || reverseCooldown <= 0f ||
            lookaheadMin <= 0f || lookaheadMax < lookaheadMin || lookaheadTime <= 0f || planningDistance <= 0f ||
            comfort <= 0f || comfort > 1f || stopGap <= 0f || cornerSpeed <= 0f || fullSlowdownAngle <= 0f ||
            fullSlowdownAngle > 180f || brakeBand <= 0f || stoppedSpeedThreshold <= 0f ||
            connectorAlignmentDegrees < 0f || connectorAlignmentDegrees > 180f || maxSweepDistance <= 0f ||
            sensorBuffer <= 0 || cursorWork <= 0 || bindWork <= 0 || cursorWindow <= 0f || maxDeviation < 0f ||
            displacementSlack <= 0f || acquisitionTolerance < 0f || turnExitDistance <= 0f || turnExitAlignmentDegrees <= 0f ||
            turnExitAlignmentDegrees > 180f || maximumTurnTravel < turnExitDistance || maximumTurnActiveSeconds <= 0f ||
            ramConeDegrees <= 0f || ramConeDegrees >= 90f || ramMaximumSurfaceGap <= 0f || ramMaximumSurfaceGap > maxSweepDistance ||
            ramMaximumActiveSeconds <= 0f || ramCooldownSeconds <= 0f || !Finite((double)maxDeviation * maxDeviation)) {
            reason = "police driving settings contain an out-of-range value";
            return false;
        }
        if (recovery == null) {
            reason = "police recovery settings are missing";
            return false;
        }
        if (!recovery.IsValid(stoppedSpeedThreshold, out reason)) return false;
        return true;
    }

    /// <summary>Creates a detached settings snapshot so callers can safely modify authored values.</summary>
    /// <returns>A new settings object with the same primitive values.</returns>
    public PoliceDrivingSettings Clone() {
        return new PoliceDrivingSettings {
            lateralAcceleration = lateralAcceleration, avoidanceHorizon = avoidanceHorizon,
            avoidanceCommitment = avoidanceCommitment, avoidanceProgressWeight = avoidanceProgressWeight,
            avoidanceClearanceWeight = avoidanceClearanceWeight, avoidanceHeadingWeight = avoidanceHeadingWeight,
            avoidanceSwitchWeight = avoidanceSwitchWeight, stuckDuration = stuckDuration,
            reverseMaxDistance = reverseMaxDistance, reverseMaxDuration = reverseMaxDuration,
            reverseMaxAttempts = reverseMaxAttempts, reverseCooldown = reverseCooldown,
            lookaheadMin = lookaheadMin, lookaheadMax = lookaheadMax, lookaheadTime = lookaheadTime,
            planningDistance = planningDistance, comfort = comfort, stopGap = stopGap, cornerSpeed = cornerSpeed,
            fullSlowdownAngle = fullSlowdownAngle, brakeBand = brakeBand, stoppedSpeedThreshold = stoppedSpeedThreshold,
            connectorAlignmentDegrees = connectorAlignmentDegrees, maxSweepDistance = maxSweepDistance,
            sensorBuffer = sensorBuffer, cursorWork = cursorWork, bindWork = bindWork, cursorWindow = cursorWindow,
            maxDeviation = maxDeviation, displacementSlack = displacementSlack, acquisitionTolerance = acquisitionTolerance,
            turnExitDistance = turnExitDistance, turnExitAlignmentDegrees = turnExitAlignmentDegrees,
            maximumTurnTravel = maximumTurnTravel, maximumTurnActiveSeconds = maximumTurnActiveSeconds,
            ramConeDegrees = ramConeDegrees, ramMaximumSurfaceGap = ramMaximumSurfaceGap,
            ramMaximumActiveSeconds = ramMaximumActiveSeconds, ramCooldownSeconds = ramCooldownSeconds,
            recovery = recovery != null ? recovery.Clone() : null
        };
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
