using UnityEngine;

/// <summary>
/// Every tunable number behind the career layer: day length, rank thresholds, reputation and rent
/// formulas, and shift objective rewards. Authored once by design, read everywhere else.
/// </summary>
/// <remarks>
/// Numbers here come from a joint Game Designer / Economy Designer pass (2026-09-06); see
/// <c>memory-bank/productContext.md</c>, "Kariyer omurgası" for the reasoning behind each one.
/// </remarks>
[CreateAssetMenu(fileName = "CareerData", menuName = "PizzaGame/Career Data")]
public class CareerData : ScriptableObject {

    [Header("Day Structure")]
    [Tooltip("Shifts per day. Rent is charged once the day's last shift settles.")]
    public int shiftsPerDay = 3;

    [Header("Rank")]
    [Tooltip("Cumulative reputation required for each rank. Index 0 is rank 1 and must be 0.")]
    public int[] rankThresholds = { 0, 70, 190, 355, 575 };

    [Tooltip("Reputation that triggers the generic ending, once the highest rank is already reached.")]
    public int endingThreshold = 870;

    [Header("Region Reputation Cap")]
    [Tooltip("Live reputation is clamped to the cap of the highest region tier unlocked so far. Index 0 is tier 1.")]
    public int[] regionCapByTier = { 190, 575, 870 };

    [Tooltip("Rank required to unlock each region tier past the first. Index 0 is tier 2's required rank. Must match the tier-1 entry of regionCapByTier exactly, or the cap can lock the player out of ever reaching the rank that would raise it.")]
    public int[] regionUnlockRank = { 3, 5 };

    [Header("Reputation Formula")]
    [Tooltip("Base reputation for a region tier-1 shift. Each tier above adds baseRepPerTier.")]
    public float baseRepAtTier1 = 10f;
    public float baseRepPerTier = 4f;
    [Tooltip("Quality multiplier from on-time delivery rate: clamp(onTimeMin + onTimeWeight*rate, onTimeMin, 1).")]
    public float qualityOnTimeMin = 0.4f;
    public float qualityOnTimeWeight = 0.6f;
    [Tooltip("Quality multiplier from health retained: clamp(healthMin + healthWeight*ratio, healthMin, 1).")]
    public float qualityHealthMin = 0.5f;
    public float qualityHealthWeight = 0.5f;
    [Tooltip("Reputation earned is multiplied by the entry matching how the shift ended.")]
    public EndReasonMultiplier[] endReasonMultipliers = {
        new EndReasonMultiplier { reason = EndReason.TimeUp, multiplier = 1.0f },
        new EndReasonMultiplier { reason = EndReason.Extracted, multiplier = 1.1f },
        new EndReasonMultiplier { reason = EndReason.Wrecked, multiplier = 0.5f },
        new EndReasonMultiplier { reason = EndReason.Abandoned, multiplier = 0.15f },
        new EndReasonMultiplier { reason = EndReason.Interrupted, multiplier = 0.5f }
    };

    [Header("Rent")]
    [Tooltip("rent(rank) = rentBase + rentPerRankStep * (rank - 1). Charged inside the day's final settlement, never as a separate check the player can spend ahead of.")]
    public int rentBase = 400;
    public int rentPerRankStep = 350;
    [Tooltip("Unpaid rent converts to reputation loss, never debt: penalty = round(shortfall / divisor(rank)).")]
    public int repShortfallDivisorBase = 60;
    public int repShortfallDivisorPerRank = 7;

    [Header("Shift Objectives")]
    [Tooltip("How many objectives are offered per shift, drawn without replacement from objectiveTypes.")]
    public int objectivesPerShift = 2;
    [Tooltip("A completed objective pays objectiveCapFraction * GTypical(rank) * its own reward fraction.")]
    public float objectiveCapFraction = 0.25f;
    [Tooltip("GTypical(rank) = gTypicalBase + gTypicalPerRankStep * (rank - 1): a rank-indexed stand-in for typical shift gross, used only to scale objective rewards.")]
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
    [Tooltip("Quota target = ceil(capacity * quotaCapacityMultiplier) + floor(rank / quotaRankDivisor).")]
    public float quotaCapacityMultiplier = 2.2f;
    public int quotaRankDivisor = 2;
    [Tooltip("Big Order target order size = max(bigOrderMinPizzas, capacity); target count = 1 + floor(rank / bigOrderRankDivisor).")]
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

    /// <summary>Calculates a target from the tuning entry, capacity and rank.</summary>
    /// <param name="type">Objective type to calculate.</param>
    /// <param name="capacity">Current pizza capacity.</param>
    /// <param name="rank">Current career rank.</param>
    /// <returns>The non-negative target.</returns>
    public int GetObjectiveTarget(ShiftObjectiveType type, int capacity, int rank) {
        var tuning = GetObjectiveTuning(type);
        if (tuning == null || tuning.zeroTarget) return 0;

        int rankContribution = tuning.targetRankDivisor > 0
            ? Mathf.FloorToInt((float)Mathf.Max(0, rank) / tuning.targetRankDivisor)
            : 0;
        int capacityContribution = Mathf.CeilToInt(Mathf.Max(0, capacity) * tuning.targetCapacityMultiplier);
        return Mathf.Max(tuning.minimumTarget, tuning.targetBase + capacityContribution + rankContribution);
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

    /// <summary>Calculates the currency reward for one objective at a rank.</summary>
    /// <param name="type">Objective type to reward.</param>
    /// <param name="rank">Current career rank.</param>
    /// <returns>The non-negative currency reward.</returns>
    public int GetObjectiveReward(ShiftObjectiveType type, int rank) {
        return Mathf.Max(0, Mathf.RoundToInt(GetObjectiveCap(rank) * GetObjectiveRewardFraction(type)));
    }

    /// <summary>Rent due for a given rank.</summary>
    /// <param name="rank">One-based rank.</param>
    /// <returns>The currency amount due.</returns>
    public int GetRentAmount(int rank) {
        return rentBase + rentPerRankStep * Mathf.Max(0, rank - 1);
    }

    /// <summary>How much unpaid rent currency converts to one point of reputation loss.</summary>
    /// <param name="rank">One-based rank.</param>
    /// <returns>The divisor; higher means a gentler penalty.</returns>
    public int GetRepShortfallDivisor(int rank) {
        return repShortfallDivisorBase + repShortfallDivisorPerRank * Mathf.Max(0, rank - 1);
    }

    /// <summary>The reputation cap in effect for a region tier.</summary>
    /// <param name="regionTier">One-based region tier.</param>
    /// <returns>The cap, or the highest authored cap when the tier exceeds the table.</returns>
    public int GetRegionCap(int regionTier) {
        if (regionCapByTier == null || regionCapByTier.Length == 0) return int.MaxValue;
        int index = Mathf.Clamp(regionTier - 1, 0, regionCapByTier.Length - 1);
        return regionCapByTier[index];
    }

    /// <summary>
    /// Computes the rank a total reputation total has reached, and whether it has cleared the
    /// generic ending.
    /// </summary>
    /// <param name="totalReputation">Live, cappable reputation total.</param>
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

    /// <summary>The highest region tier a given rank has unlocked.</summary>
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

    /// <summary>The reputation-earning multiplier for how a shift ended.</summary>
    /// <param name="reason">How the shift ended.</param>
    /// <returns>The multiplier, or 1 when the reason has no authored entry.</returns>
    public float GetEndReasonMultiplier(EndReason reason) {
        if (endReasonMultipliers == null) return 1f;
        foreach (var entry in endReasonMultipliers) {
            if (entry != null && entry.reason == reason) return entry.multiplier;
        }
        return 1f;
    }

    /// <summary>The rank-indexed stand-in for typical shift gross, used to scale objective rewards.</summary>
    /// <param name="rank">One-based rank.</param>
    /// <returns>The scaled value.</returns>
    public int GetGTypical(int rank) {
        return gTypicalBase + gTypicalPerRankStep * Mathf.Max(0, rank - 1);
    }

    /// <summary>The currency ceiling a single completed objective can pay at a given rank.</summary>
    /// <param name="rank">One-based rank.</param>
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
