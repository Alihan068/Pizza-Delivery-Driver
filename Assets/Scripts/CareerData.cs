using UnityEngine;

/// <summary>
/// Every tunable number behind the career layer: day length, visual rank thresholds, courier rating,
/// rent formulas, and shift objective rewards. Authored once by design, read everywhere else.
/// </summary>
/// <remarks>
/// Numbers here come from a joint Game Designer / Economy Designer pass (2026-09-06); see
/// <c>memory-bank/productContext.md</c>, "Career foundation" for the reasoning behind each one.
/// </remarks>
[CreateAssetMenu(fileName = "CareerData", menuName = "PizzaGame/Career Data")]
public class CareerData : ScriptableObject {

    [Header("Day Structure")]
    [Tooltip("Shifts per day. Rent is charged once the day's last shift settles.")]
    public int shiftsPerDay = 3;

    [Header("Rank")]
    [Tooltip("Cumulative reputation required for each rank. Index 0 is rank 1 and must be 0.")]
    public int[] rankThresholds = { 0, 70, 190, 355, 575 };

    [Tooltip("Legacy threshold retained for old assets; courier rating never ends the career.")]
    // Legacy field retained for save and asset compatibility. Courier rating does not end the
    // career; finalShiftDay and finalShiftOrderTarget define the authored ending challenge.
    public int endingThreshold = 870;

    [Header("Career Ending")]
    [Tooltip("Once this day is reached, the next unsettled career shift becomes the final career challenge.")]
    [Min(1)] public int finalShiftDay = 5;

    [Tooltip("Fully completed customer orders required to win the final career challenge.")]
    [Min(1)] public int finalShiftOrderTarget = 8;

    [Header("Region Reputation Cap")]
    [Tooltip("Live reputation is clamped to the cap of the highest region tier unlocked so far. Index 0 is tier 1.")]
    public int[] regionCapByTier = { 190, 575, 870 };

    [Tooltip("Rank required to unlock each region tier past the first. Index 0 is tier 2's required rank. Must match the tier-1 entry of regionCapByTier exactly, or the cap can lock the player out of ever reaching the rank that would raise it.")]
    public int[] regionUnlockRank = { 3, 5 };

    [Header("Legacy Reputation Formula")]
    [Tooltip("Legacy positive-reputation value retained for save and asset compatibility. It is not used by courier-rating settlement.")]
    public float baseRepAtTier1 = 10f;
    [Tooltip("Legacy field retained for compatibility. Courier rating is not region-scaled.")]
    public float baseRepPerTier = 4f;
    [Tooltip("Legacy field retained for compatibility. Courier rating uses the signed tuning below.")]
    public float qualityOnTimeMin = 0.4f;
    public float qualityOnTimeWeight = 0.6f;
    [Tooltip("Legacy field retained for compatibility. Courier rating uses the signed tuning below.")]
    public float qualityHealthMin = 0.5f;
    public float qualityHealthWeight = 0.5f;
    [Tooltip("Legacy field retained for compatibility. Courier rating uses signed end-reason offsets.")]
    public EndReasonMultiplier[] endReasonMultipliers = {
        new EndReasonMultiplier { reason = EndReason.TimeUp, multiplier = 1.0f },
        new EndReasonMultiplier { reason = EndReason.Extracted, multiplier = 1.1f },
        new EndReasonMultiplier { reason = EndReason.Wrecked, multiplier = 0.5f },
        new EndReasonMultiplier { reason = EndReason.Abandoned, multiplier = 0.15f },
        new EndReasonMultiplier { reason = EndReason.Interrupted, multiplier = 0.5f },
        new EndReasonMultiplier { reason = EndReason.Arrested, multiplier = 1.0f }
    };

    [Header("Courier Rating")]
    [Tooltip("Positive rating points granted for each minute the player actively spends in a shift. Paused time contributes zero.")]
    public float ratingPointsPerActiveMinute = 2f;

    [Tooltip("Positive rating points granted for each order completed during the shift.")]
    public float ratingPointsPerCompletedOrder = 3f;

    [Tooltip("Rating points removed for each customer order that times out.")]
    public float ratingPenaltyPerMissedOrder = 4f;

    [Tooltip("Neutral quality ratio. On-time and health ratios above this add rating; ratios below it remove rating.")]
    [Range(0f, 1f)] public float ratingNeutralQuality = 0.5f;

    [Tooltip("Signed rating weight applied to the on-time completion ratio relative to ratingNeutralQuality.")]
    public float ratingOnTimeWeight = 8f;

    [Tooltip("Signed rating weight applied to retained health relative to ratingNeutralQuality.")]
    public float ratingHealthWeight = 4f;

    [Tooltip("Signed rating adjustment for the way a shift ended. Extraction and time-up are neutral by default.")]
    public EndReasonRatingOffset[] ratingEndReasonOffsets = {
        new EndReasonRatingOffset { reason = EndReason.TimeUp, offset = 0 },
        new EndReasonRatingOffset { reason = EndReason.Extracted, offset = 0 },
        new EndReasonRatingOffset { reason = EndReason.Wrecked, offset = -5 },
        new EndReasonRatingOffset { reason = EndReason.Abandoned, offset = -8 },
        new EndReasonRatingOffset { reason = EndReason.Interrupted, offset = -5 },
        new EndReasonRatingOffset { reason = EndReason.Arrested, offset = 0 }
    };

    [Header("Rent")]
    [Tooltip("Flat daily rent charged inside the day's final settlement. Rank never changes the amount.")]
    public int rentBase = 400;
    public int rentPerRankStep = 350;
    [Tooltip("Legacy fields retained for compatibility. Unpaid rent never changes courier rating.")]
    public int repShortfallDivisorBase = 60;
    public int repShortfallDivisorPerRank = 7;

    [Header("Shift Objectives")]
    [Tooltip("How many objectives are offered per shift, drawn without replacement from objectiveTypes.")]
    public int objectivesPerShift = 2;
    [Tooltip("A completed objective pays objectiveCapFraction * typical shift gross * its own reward fraction. Rank is visual only.")]
    public float objectiveCapFraction = 0.25f;
    [Tooltip("Authored typical shift gross used only to scale objective rewards. Rank is visual only.")]
    public int gTypicalBase = 400;
    public int gTypicalPerRankStep = 300;
    public ShiftObjectiveTuning[] objectiveTypes = {
        new ShiftObjectiveTuning { type = ShiftObjectiveType.Quota, rewardFraction = 0.90f, displayNameKey = "objective.quota.name", descriptionKey = "objective.quota.description", targetCapacityMultiplier = 2.2f, targetRankDivisor = 2, minimumTarget = 1 },
        new ShiftObjectiveTuning { type = ShiftObjectiveType.PerfectService, rewardFraction = 0.80f, displayNameKey = "objective.perfectService.name", descriptionKey = "objective.perfectService.description", zeroTarget = true },
        new ShiftObjectiveTuning { type = ShiftObjectiveType.CleanRun, rewardFraction = 0.75f, displayNameKey = "objective.cleanRun.name", descriptionKey = "objective.cleanRun.description", zeroTarget = true },
        new ShiftObjectiveTuning { type = ShiftObjectiveType.CargoGuard, rewardFraction = 1.00f, displayNameKey = "objective.cargoGuard.name", descriptionKey = "objective.cargoGuard.description", zeroTarget = true },
        new ShiftObjectiveTuning { type = ShiftObjectiveType.BigOrder, rewardFraction = 0.65f, displayNameKey = "objective.bigOrder.name", descriptionKey = "objective.bigOrder.description", targetBase = 1, targetRankDivisor = 3, minimumTarget = 1, parameterBase = 3, parameterCapacityMultiplier = 1f },
        new ShiftObjectiveTuning { type = ShiftObjectiveType.FastExtraction, rewardFraction = 1.00f, displayNameKey = "objective.fastExtraction.name", descriptionKey = "objective.fastExtraction.description", targetBase = 1, minimumTarget = 1 }
    };

    [Header("Objective Target Coefficients")]
    [Tooltip("Quota target = ceil(capacity * quotaCapacityMultiplier). Rank is visual only.")]
    public float quotaCapacityMultiplier = 2.2f;
    public int quotaRankDivisor = 2;
    [Tooltip("Big Order target order size = max(bigOrderMinPizzas, capacity). Rank is visual only.")]
    public int bigOrderMinPizzas = 3;
    public int bigOrderRankDivisor = 3;
    [Tooltip("Fast Extraction requires at least this many seconds left on the clock at extraction.")]
    public float fastExtractionMinSecondsRemaining = 60f;
    [Tooltip("Fast Extraction also requires deliveries >= floor(quotaTarget * this fraction), so it cannot be farmed by sprinting to the zone at shift start.")]
    public float fastExtractionQuotaFraction = 0.5f;

    /// <summary>Returns the authored tuning entry for one objective type.</summary>
    /// <param name="type">Objective type to find.</param>
    /// <returns>The matching entry, or null when it is not configured.</returns>
    public ShiftObjectiveTuning GetObjectiveTuning(ShiftObjectiveType type) {
        if (objectiveTypes == null) return null;
        foreach (var entry in objectiveTypes) {
            if (entry != null && entry.type == type) return entry;
        }
        return null;
    }

    /// <summary>Calculates an objective target from the tuning entry and capacity.</summary>
    /// <param name="type">Objective type to calculate.</param>
    /// <param name="capacity">Current pizza capacity.</param>
    /// <param name="rank">Legacy compatibility parameter; ignored because rank is visual only.</param>
    /// <returns>The non-negative target.</returns>
    public int GetObjectiveTarget(ShiftObjectiveType type, int capacity, int rank) {
        var tuning = GetObjectiveTuning(type);
        if (tuning == null || tuning.zeroTarget) return 0;

        int capacityContribution = Mathf.CeilToInt(Mathf.Max(0, capacity) * tuning.targetCapacityMultiplier);
        return Mathf.Max(tuning.minimumTarget, tuning.targetBase + capacityContribution);
    }

    /// <summary>Calculates the optional secondary parameter for an objective.</summary>
    /// <param name="type">Objective type to calculate.</param>
    /// <param name="capacity">Current pizza capacity.</param>
    /// <returns>The non-negative parameter.</returns>
    public int GetObjectiveParameter(ShiftObjectiveType type, int capacity) {
        var tuning = GetObjectiveTuning(type);
        if (tuning == null) return 0;
        return Mathf.Max(0, tuning.parameterBase + Mathf.CeilToInt(Mathf.Max(0, capacity) * tuning.parameterCapacityMultiplier));
    }

    /// <summary>Calculates the currency reward for one objective.</summary>
    /// <param name="type">Objective type to reward.</param>
    /// <param name="rank">Legacy compatibility parameter; ignored because rank is visual only.</param>
    /// <returns>The non-negative currency reward.</returns>
    public int GetObjectiveReward(ShiftObjectiveType type, int rank) {
        return Mathf.Max(0, Mathf.RoundToInt(GetObjectiveCap(rank) * GetObjectiveRewardFraction(type)));
    }

    /// <summary>Rent due for a day. Rank never changes the amount.</summary>
    /// <param name="rank">Legacy compatibility parameter; ignored because rank is visual only.</param>
    /// <returns>The currency amount due.</returns>
    public int GetRentAmount(int rank) {
        return Mathf.Max(0, rentBase);
    }

    /// <summary>Legacy rent shortfall divisor retained for compatibility.</summary>
    /// <param name="rank">Legacy compatibility parameter; ignored because rating is not rent-based.</param>
    /// <returns>The authored base divisor.</returns>
    public int GetRepShortfallDivisor(int rank) {
        return Mathf.Max(1, repShortfallDivisorBase);
    }

    /// <summary>Legacy reputation cap retained for old content and save compatibility.</summary>
    /// <param name="regionTier">One-based region tier.</param>
    /// <returns>The authored legacy cap, or the highest value when the tier exceeds the table.</returns>
    public int GetRegionCap(int regionTier) {
        if (regionCapByTier == null || regionCapByTier.Length == 0) return int.MaxValue;
        int index = Mathf.Clamp(regionTier - 1, 0, regionCapByTier.Length - 1);
        return regionCapByTier[index];
    }

    /// <summary>
    /// Computes the visual rank represented by a non-negative courier-rating total.
    /// </summary>
    /// <param name="totalReputation">Current courier rating. It is not region-capped.</param>
    /// <param name="reachedEnding">True once <see cref="endingThreshold"/> has been reached.</param>
    /// <returns>The one-based rank, never above the number of authored thresholds.</returns>
    public int ComputeRank(int totalReputation, out bool reachedEnding) {
        int rank = 1;
        if (rankThresholds != null) {
            for (int i = 0; i < rankThresholds.Length; i++) {
                if (totalReputation >= rankThresholds[i]) rank = i + 1;
            }
        }
        reachedEnding = totalReputation >= endingThreshold;
        return rank;
    }

    /// <summary>Legacy region-tier calculation retained for old content compatibility.</summary>
    /// <param name="rank">One-based rank.</param>
    /// <returns>The tier, starting at 1 (always unlocked).</returns>
    public int ComputeUnlockedRegionTier(int rank) {
        int tier = 1;
        if (regionUnlockRank != null) {
            for (int i = 0; i < regionUnlockRank.Length; i++) {
                if (rank >= regionUnlockRank[i]) tier = i + 2;
            }
        }
        return tier;
    }

    /// <summary>Legacy reputation multiplier retained for compatibility with old callers.</summary>
    /// <param name="reason">How the shift ended.</param>
    /// <returns>The multiplier, or 1 when the reason has no authored entry.</returns>
    public float GetEndReasonMultiplier(EndReason reason) {
        if (endReasonMultipliers == null) return 1f;
        foreach (var entry in endReasonMultipliers) {
            if (entry != null && entry.reason == reason) return entry.multiplier;
        }
        return 1f;
    }

    /// <summary>Returns the signed courier-rating adjustment for a shift ending.</summary>
    /// <param name="reason">How the shift ended.</param>
    /// <returns>The authored signed adjustment, or zero when no entry exists.</returns>
    public int GetRatingEndReasonOffset(EndReason reason) {
        if (ratingEndReasonOffsets == null) return 0;
        foreach (var entry in ratingEndReasonOffsets) {
            if (entry != null && entry.reason == reason) return entry.offset;
        }
        return 0;
    }

    /// <summary>The typical shift gross used to scale objective rewards.</summary>
    /// <param name="rank">Legacy compatibility parameter; ignored because rank is visual only.</param>
    /// <returns>The authored value.</returns>
    public int GetGTypical(int rank) {
        return Mathf.Max(0, gTypicalBase);
    }

    /// <summary>The currency ceiling a single completed objective can pay.</summary>
    /// <param name="rank">Legacy compatibility parameter; ignored because rank is visual only.</param>
    /// <returns>The cap; a type's actual payout is this times its own reward fraction.</returns>
    public int GetObjectiveCap(int rank) {
        return Mathf.RoundToInt(objectiveCapFraction * GetGTypical(rank));
    }

    /// <summary>The reward fraction authored for one objective type.</summary>
    /// <param name="type">Which objective type.</param>
    /// <returns>The fraction, or zero when the type has no authored entry.</returns>
    public float GetObjectiveRewardFraction(ShiftObjectiveType type) {
        if (objectiveTypes == null) return 0f;
        foreach (var entry in objectiveTypes) {
            if (entry != null && entry.type == type) return entry.rewardFraction;
        }
        return 0f;
    }
}
