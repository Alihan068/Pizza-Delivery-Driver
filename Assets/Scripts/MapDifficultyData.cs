using UnityEngine;

/// <summary>
/// Authored rules for one difficulty tier inside a playable map.
/// </summary>
/// <remarks>
/// The class is intentionally serializable rather than a ScriptableObject. A map keeps its
/// difficulty rows together with its LevelData so the Inspector and future map tools can move one
/// self-contained tuning payload without creating dangling asset references.
/// </remarks>
[System.Serializable]
public class MapDifficultyData {

    /// <summary>Permanent identifier used by saves and runtime selection.</summary>
    public string difficultyId;

    /// <summary>Localization key for the difficulty name.</summary>
    public string displayNameKey;

    /// <summary>Localization key for the difficulty description.</summary>
    public string descriptionKey;

    /// <summary>Smallest order size that can be rolled, inclusive.</summary>
    [Min(1)] public int orderMin;

    /// <summary>Largest order size that can be rolled, inclusive.</summary>
    [Min(1)] public int orderMax;

    /// <summary>
    /// Pizza loss budget applied to each damaging impact. Values above 100 are valid and produce
    /// guaranteed whole losses plus a fractional chance for one additional loss.
    /// </summary>
    [Min(0f)] public float pizzaLossChancePercent;

    /// <summary>Score removed for each pizza that is actually lost.</summary>
    [Min(0)] public int scorePenaltyPerPizzaLost;

    /// <summary>Multiplier applied to positive customer rewards in this tier.</summary>
    [Min(0f)] public float rewardMultiplier;

    /// <summary>Best score required in this tier to unlock the next tier.</summary>
    [Min(0)] public int unlockTargetScoreForNext;

    /// <summary>Returns the localized difficulty name, falling back to the permanent ID.</summary>
    /// <returns>The name shown in map selection.</returns>
    public string GetDisplayName() {
        return string.IsNullOrEmpty(displayNameKey) ? difficultyId : LocalizationManager.Get(displayNameKey);
    }

    /// <summary>Returns the localized difficulty description.</summary>
    /// <returns>The description shown in map selection.</returns>
    public string GetDescription() {
        return string.IsNullOrEmpty(descriptionKey) ? string.Empty : LocalizationManager.Get(descriptionKey);
    }

    /// <summary>Returns a safe inclusive order minimum after authoring data is validated.</summary>
    /// <returns>An order minimum of at least one pizza.</returns>
    public int GetSafeOrderMin() {
        return Mathf.Clamp(orderMin, 1, int.MaxValue - 1);
    }

    /// <summary>Returns a safe inclusive order maximum that is never below the minimum.</summary>
    /// <returns>An order maximum suitable for Random.Range.</returns>
    public int GetSafeOrderMax() {
        return Mathf.Clamp(Mathf.Max(GetSafeOrderMin(), orderMax), GetSafeOrderMin(), int.MaxValue - 1);
    }

    /// <summary>Rolls one order size from the authored inclusive range.</summary>
    /// <returns>A random order size between the safe minimum and maximum.</returns>
    public int RollOrderAmount() {
        return Random.Range(GetSafeOrderMin(), GetSafeOrderMax() + 1);
    }

    /// <summary>Applies this tier's positive reward multiplier to a base reward.</summary>
    /// <param name="baseReward">Reward before this tier's multiplier is applied.</param>
    /// <returns>The rounded, non-negative reward amount.</returns>
    public int ApplyRewardMultiplier(float baseReward) {
        return Mathf.RoundToInt(Mathf.Max(0f, baseReward) * Mathf.Max(0f, rewardMultiplier));
    }
}
