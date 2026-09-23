using System;

/// <summary>Authorable, detached tuning contract for the road-independent police chase.</summary>
[Serializable]
public sealed class PoliceFreeChaseSettings {
    /// <summary>Allows every police class to commit toward player and civilian vehicles.</summary>
    public bool recklessPursuit = true;
    /// <summary>Wider target cone used by police impact commitment.</summary>
    public float recklessRamConeDegrees = 45f;
    /// <summary>Maximum surface gap used by police impact commitment.</summary>
    public float recklessRamDistance = 6f;
    /// <summary>Accepted distance from the target pivot for a free-drive goal.</summary>
    public float goalRadius = 0.75f;
    /// <summary>Delay before a newly observed driving intent is applied.</summary>
    public float reactionSeconds = 0.3f;
    /// <summary>Minimum active time between driver decisions.</summary>
    public float decisionIntervalSeconds = 0.15f;
    /// <summary>Maximum lateral acceleration used to derive a bounded speed target.</summary>
    public float lateralAcceleration = 4f;
    /// <summary>Look-ahead horizon used by local avoidance scoring.</summary>
    public float avoidanceHorizonSeconds = 0.75f;
    /// <summary>Minimum time for which a selected avoidance side is retained.</summary>
    public float avoidanceCommitmentSeconds = 0.5f;
    /// <summary>Weight for normalized progress in avoidance scoring.</summary>
    public float progressWeight = 1f;
    /// <summary>Weight for normalized clearance in avoidance scoring.</summary>
    public float clearanceWeight = 1f;
    /// <summary>Weight for normalized heading error in avoidance scoring.</summary>
    public float headingWeight = 0.5f;
    /// <summary>Weight for switching away from the current avoidance side.</summary>
    public float switchWeight = 0.25f;
    /// <summary>Small positive denominator guard used by later free-drive math.</summary>
    public float numericalEpsilon = 0.0001f;
    /// <summary>Turn cost in authored distance units per radian.</summary>
    public float turnPenalty = 0.1f;
    /// <summary>Maximum reverse distance used by bounded recovery.</summary>
    public float reverseDistance = 2f;
    /// <summary>Maximum active reverse duration used by bounded recovery.</summary>
    public float reverseDurationSeconds = 2f;
    /// <summary>Maximum reverse attempts for one recovery episode.</summary>
    public int maximumReverseAttempts = 2;
    /// <summary>Cooldown between reverse recovery episodes.</summary>
    public float reverseCooldownSeconds = 1f;
    /// <summary>Active duration before a genuinely blocked pursuit may enter recovery.</summary>
    public float stuckDurationSeconds = 2f;
    /// <summary>Active cadence at which tactical roles may be reassigned.</summary>
    public float roleCadenceSeconds = 0.5f;
    /// <summary>Minimum active duration for a tactical role assignment.</summary>
    public float minimumRoleHoldSeconds = 2f;
    /// <summary>Initial measured-velocity prediction horizon for interception.</summary>
    public float predictionSeconds = 0.75f;
    /// <summary>Maximum distance permitted for an interception lead.</summary>
    public float maximumLeadDistance = 6f;
    /// <summary>Surface-gap tolerance for a genuine low-speed holding state.</summary>
    public float holdingTolerance = 0.1f;
    /// <summary>Active window over which net world displacement decides that a throttling unit is physically jammed.</summary>
    public float jamWindowSeconds = 1f;
    /// <summary>Net displacement a throttling unit must cover within the jam window to count as progressing.</summary>
    public float jamMinimumDisplacement = 0.35f;
    /// <summary>Active seconds a reckless unit keeps ramming a blocked corridor before it starts steering around it.</summary>
    public float forceThroughSeconds = 1.2f;
    /// <summary>Active seconds bounded replanning may own a reckless unit before pursuit is forcibly resumed.</summary>
    public float replanGiveUpSeconds = 2f;
    /// <summary>Surface gap at or below which a reckless unit starts shoving the player instead of realigning for a new ram.</summary>
    public float pinEngageGap = 0.35f;
    /// <summary>Surface gap above which the player counts as breaking away from contact pressure.</summary>
    public float pinReleaseGap = 1.25f;
    /// <summary>Seconds the player must stay beyond the release gap before contact pressure ends.</summary>
    public float pinReleaseGraceSeconds = 0.6f;
    /// <summary>Longest single contact-pressure episode.</summary>
    public float pinMaximumSeconds = 4f;
    /// <summary>Seconds after a completed contact-pressure episode before another may begin.</summary>
    public float pinCooldownSeconds = 0.75f;
    /// <summary>Surface gap to the player, in multiples of this unit's own body length, inside which it stops planning and dives straight at the player at full speed.</summary>
    public float diveRangeBodyLengths = 3.5f;
    /// <summary>Seconds of player velocity the dive leads by, so a close unit cuts in front of the player instead of trailing it.</summary>
    public float diveLeadSeconds = 0.25f;
    /// <summary>Seconds after a jam during which the dive is not retried through geometry that still blocks the way.</summary>
    public float diveRetryAfterJamSeconds = 4f;

    /// <summary>Creates a detached copy of this authored chase contract.</summary>
    /// <returns>An independent settings snapshot.</returns>
    public PoliceFreeChaseSettings Clone() => new PoliceFreeChaseSettings {
        recklessPursuit = recklessPursuit,
        recklessRamConeDegrees = recklessRamConeDegrees,
        recklessRamDistance = recklessRamDistance,
        goalRadius = goalRadius,
        reactionSeconds = reactionSeconds,
        decisionIntervalSeconds = decisionIntervalSeconds,
        lateralAcceleration = lateralAcceleration,
        avoidanceHorizonSeconds = avoidanceHorizonSeconds,
        avoidanceCommitmentSeconds = avoidanceCommitmentSeconds,
        progressWeight = progressWeight,
        clearanceWeight = clearanceWeight,
        headingWeight = headingWeight,
        switchWeight = switchWeight,
        numericalEpsilon = numericalEpsilon,
        turnPenalty = turnPenalty,
        reverseDistance = reverseDistance,
        reverseDurationSeconds = reverseDurationSeconds,
        maximumReverseAttempts = maximumReverseAttempts,
        reverseCooldownSeconds = reverseCooldownSeconds,
        stuckDurationSeconds = stuckDurationSeconds,
        roleCadenceSeconds = roleCadenceSeconds,
        minimumRoleHoldSeconds = minimumRoleHoldSeconds,
        predictionSeconds = predictionSeconds,
        maximumLeadDistance = maximumLeadDistance,
        holdingTolerance = holdingTolerance,
        jamWindowSeconds = jamWindowSeconds,
        jamMinimumDisplacement = jamMinimumDisplacement,
        forceThroughSeconds = forceThroughSeconds,
        replanGiveUpSeconds = replanGiveUpSeconds,
        pinEngageGap = pinEngageGap,
        pinReleaseGap = pinReleaseGap,
        pinReleaseGraceSeconds = pinReleaseGraceSeconds,
        pinMaximumSeconds = pinMaximumSeconds,
        pinCooldownSeconds = pinCooldownSeconds,
        diveRangeBodyLengths = diveRangeBodyLengths,
        diveLeadSeconds = diveLeadSeconds,
        diveRetryAfterJamSeconds = diveRetryAfterJamSeconds
    };

    /// <summary>Validates finite and bounded authored values without mutating them.</summary>
    /// <param name="reason">Failure description, or null when valid.</param>
    /// <returns>True only when every value is usable by a later free-drive packet.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!FinitePositive(recklessRamConeDegrees) || recklessRamConeDegrees >= 90f || !FinitePositive(recklessRamDistance) ||
            !FinitePositive(goalRadius) || !FiniteNonnegative(reactionSeconds) || !FinitePositive(decisionIntervalSeconds) ||
            !FinitePositive(lateralAcceleration) || !FinitePositive(avoidanceHorizonSeconds) || !FiniteNonnegative(avoidanceCommitmentSeconds) ||
            !FiniteNonnegative(progressWeight) || !FiniteNonnegative(clearanceWeight) || !FiniteNonnegative(headingWeight) ||
            !FiniteNonnegative(switchWeight) || !FinitePositive(numericalEpsilon) || !FiniteNonnegative(turnPenalty) ||
            !FinitePositive(reverseDistance) || !FinitePositive(reverseDurationSeconds) || maximumReverseAttempts <= 0 ||
            !FiniteNonnegative(reverseCooldownSeconds) || !FinitePositive(stuckDurationSeconds) || !FinitePositive(roleCadenceSeconds) ||
            !FiniteNonnegative(minimumRoleHoldSeconds) || !FinitePositive(predictionSeconds) || !FinitePositive(maximumLeadDistance) ||
            !FiniteNonnegative(holdingTolerance) || !FinitePositive(jamWindowSeconds) ||
            !FinitePositive(jamMinimumDisplacement) || !FiniteNonnegative(forceThroughSeconds) ||
            !FinitePositive(replanGiveUpSeconds) || !FiniteNonnegative(pinEngageGap) ||
            !FinitePositive(pinReleaseGap) || pinReleaseGap < pinEngageGap || !FiniteNonnegative(pinReleaseGraceSeconds) ||
            !FinitePositive(pinMaximumSeconds) || !FiniteNonnegative(pinCooldownSeconds) ||
            !FiniteNonnegative(diveRangeBodyLengths) || !FiniteNonnegative(diveLeadSeconds) || !FiniteNonnegative(diveRetryAfterJamSeconds)) {
            reason = "invalid police free chase settings";
            return false;
        }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
