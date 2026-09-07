using UnityEngine;

/// <summary>
/// Pure calculations for the career layer: signed courier rating, rank from rating, and
/// a day's rent settlement. Holds no mutable state of its own — <see cref="GameManager"/> owns the
/// live totals and applies whatever this returns, the same division of labour as
/// <see cref="SaveSlotService"/>.
/// </summary>
public class CareerManager {

    readonly CareerData data;

    /// <summary>Creates the calculator over one career's tuning data.</summary>
    /// <param name="data">Source of every formula constant. Required.</param>
    public CareerManager(CareerData data) {
        this.data = data;
    }

    /// <summary>Calculates the signed courier-rating change for one completed shift.</summary>
    /// <param name="activeSessionSeconds">Unpaused seconds spent in the shift.</param>
    /// <param name="ordersCompleted">Orders fully completed during the shift.</param>
    /// <param name="ordersOffered">Orders offered during the shift.</param>
    /// <param name="missedCustomers">Orders that timed out during the shift.</param>
    /// <param name="healthRetainedRatio">Health remaining divided by max health, from zero to one.</param>
    /// <param name="reason">How the shift ended.</param>
    /// <returns>A signed rating delta. It may be negative, but the caller owns the floor on the total.</returns>
    public int ComputeRatingDelta(float activeSessionSeconds, int ordersCompleted, int ordersOffered, int missedCustomers, float healthRetainedRatio, EndReason reason) {
        float activeMinutes = Mathf.Max(0f, activeSessionSeconds) / 60f;
        float onTimeRate = ordersOffered > 0
            ? Mathf.Clamp01((float)Mathf.Max(0, ordersCompleted) / ordersOffered)
            : 0f;
        float retainedHealth = Mathf.Clamp01(healthRetainedRatio);
        float qualityDelta = (onTimeRate - data.ratingNeutralQuality) * data.ratingOnTimeWeight
            + (retainedHealth - data.ratingNeutralQuality) * data.ratingHealthWeight;
        float performanceDelta = Mathf.Max(0, ordersCompleted) * data.ratingPointsPerCompletedOrder
            - Mathf.Max(0, missedCustomers) * data.ratingPenaltyPerMissedOrder;
        float participationDelta = activeMinutes * data.ratingPointsPerActiveMinute;
        return Mathf.RoundToInt(participationDelta + performanceDelta + qualityDelta + data.GetRatingEndReasonOffset(reason));
    }

    /// <summary>Legacy positive-reputation calculation retained for older callers.</summary>
    /// <param name="regionTier">Legacy map region tier.</param>
    /// <param name="onTimeRate">Orders completed divided by orders offered, from zero to one.</param>
    /// <param name="healthRetainedRatio">Health remaining divided by max health, from zero to one.</param>
    /// <param name="reason">How the shift ended.</param>
    /// <returns>The legacy non-negative reputation amount.</returns>
    public int ComputeShiftReputation(int regionTier, float onTimeRate, float healthRetainedRatio, EndReason reason) {
        float baseRep = data.baseRepAtTier1 + data.baseRepPerTier * Mathf.Max(0, regionTier - 1);
        float onTimeQuality = Mathf.Clamp(data.qualityOnTimeMin + data.qualityOnTimeWeight * onTimeRate, data.qualityOnTimeMin, 1f);
        float healthQuality = Mathf.Clamp(data.qualityHealthMin + data.qualityHealthWeight * healthRetainedRatio, data.qualityHealthMin, 1f);
        float multiplier = data.GetEndReasonMultiplier(reason);
        return Mathf.Max(0, Mathf.RoundToInt(baseRep * onTimeQuality * healthQuality * multiplier));
    }

    /// <summary>The rank a reputation total has reached.</summary>
    /// <param name="totalReputation">Live reputation total.</param>
    /// <param name="reachedEnding">True once the career's ending threshold is reached.</param>
    /// <returns>The one-based rank.</returns>
    public int ComputeRank(int totalReputation, out bool reachedEnding) {
        return data.ComputeRank(totalReputation, out reachedEnding);
    }

    /// <summary>The highest region tier a rank has unlocked.</summary>
    /// <param name="rank">One-based rank.</param>
    /// <returns>The tier.</returns>
    public int ComputeUnlockedRegionTier(int rank) {
        return data.ComputeUnlockedRegionTier(rank);
    }

    /// <summary>The reputation cap for a region tier.</summary>
    /// <param name="regionTier">One-based region tier.</param>
    /// <returns>The cap.</returns>
    public int GetRegionCap(int regionTier) {
        return data.GetRegionCap(regionTier);
    }

    /// <summary>Creates a runtime objective state from the career tuning.</summary>
    /// <param name="type">Objective type to create.</param>
    /// <param name="capacity">Current pizza capacity.</param>
    /// <param name="rank">Current career rank.</param>
    /// <returns>The state, or null when the type is not configured.</returns>
    public ShiftObjectiveState CreateObjective(ShiftObjectiveType type, int capacity, int rank) {
        var tuning = data.GetObjectiveTuning(type);
        if (tuning == null) return null;
        return new ShiftObjectiveState(tuning, data.GetObjectiveTarget(type, capacity, rank), data.GetObjectiveParameter(type, capacity), data.GetObjectiveReward(type, rank));
    }

    /// <summary>
    /// Settles a day's rent. Charged at the rank the day is ending at, except the first day after a
    /// rank-up is charged at the previous (lower) rank's rate, and a career's very first rent day is
    /// free outright.
    /// </summary>
    /// <param name="rankAtDayEnd">Rank as of the day that just finished.</param>
    /// <param name="rankRentLastChargedAt">Rank recorded the last time rent was settled.</param>
    /// <param name="everPaidRent">False before this career's first rent day.</param>
    /// <param name="bankBeforeRent">Bank balance before rent is deducted.</param>
    /// <returns>What was charged and what remained unpaid. Rent never changes courier rating.</returns>
    public RentSettlementResult ComputeRentSettlement(int rankAtDayEnd, int rankRentLastChargedAt, bool everPaidRent, int bankBeforeRent) {
        if (!everPaidRent) {
            return new RentSettlementResult { wasFree = true, rentDue = 0, bankAfter = bankBeforeRent, shortfall = 0, reputationPenalty = 0 };
        }

        // Keep the historical grace-day parameters for save/API compatibility. The authored rent
        // amount is flat now, so rank cannot change the actual charge.
        int chargeRank = rankAtDayEnd > rankRentLastChargedAt ? rankRentLastChargedAt : rankAtDayEnd;
        int rent = data.GetRentAmount(chargeRank);
        int paid = Mathf.Min(bankBeforeRent, rent);
        int shortfall = rent - paid;

        return new RentSettlementResult {
            wasFree = false,
            rentDue = rent,
            bankAfter = bankBeforeRent - paid,
            shortfall = shortfall,
            reputationPenalty = 0
        };
    }
}
