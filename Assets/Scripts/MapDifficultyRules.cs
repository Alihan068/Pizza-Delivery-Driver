using UnityEngine;

/// <summary>
/// Pure calculations shared by map difficulty gameplay and edit-mode tests.
/// </summary>
/// <remarks>
/// Pizza loss is represented as a percentage budget rather than a probability constrained to
/// zero-to-one. This keeps authoring flexible: 120 means one guaranteed loss plus a 20 percent
/// chance of a second loss, while Stabilizer reduces the effective budget before it is rolled.
/// </remarks>
public static class MapDifficultyRules {

    /// <summary>
    /// Determines whether a map difficulty tier is available from the active profile.
    /// </summary>
    /// <param name="mapOwned">Whether the profile owns the map containing the tier.</param>
    /// <param name="difficultyIndex">Zero-based index of the tier in the map data.</param>
    /// <param name="previousTier">The preceding tier, or null for an invalid later tier.</param>
    /// <param name="previousBestScore">Best settled score reached on the preceding tier.</param>
    /// <returns>True when the tier is validly unlocked.</returns>
    public static bool IsTierUnlocked(bool mapOwned, int difficultyIndex,
        MapDifficultyData previousTier, int previousBestScore) {
        if (!mapOwned) return false;
        if (difficultyIndex <= 0) return true;
        if (previousTier == null || previousTier.unlockTargetScoreForNext <= 0) return false;
        return Mathf.Max(0, previousBestScore) >= previousTier.unlockTargetScoreForNext;
    }

    /// <summary>
    /// Calculates how many carried pizzas one damaging impact removes.
    /// </summary>
    /// <param name="authoredLossPercent">Loss budget authored for the selected map tier.</param>
    /// <param name="stabilizerChance">Protection chance supplied by the persistent vehicle stat.</param>
    /// <param name="randomPercent">A random value in the range zero to one hundred.</param>
    /// <param name="availablePizzas">Pizzas currently carried by the driver.</param>
    /// <returns>The number of pizzas to remove, clamped to the available inventory.</returns>
    public static int CalculatePizzaLossCount(float authoredLossPercent, float stabilizerChance,
        float randomPercent, int availablePizzas) {
        int available = Mathf.Max(0, availablePizzas);
        if (available == 0) return 0;

        float safeBudget = Mathf.Max(0f, authoredLossPercent);
        float protection = Mathf.Clamp01(stabilizerChance);
        float effectiveBudget = safeBudget * (1f - protection);
        int guaranteedLosses = Mathf.FloorToInt(effectiveBudget / 100f);
        float fractionalBudget = effectiveBudget - guaranteedLosses * 100f;
        float safeRandom = Mathf.Clamp(randomPercent, 0f, 100f);
        int lossCount = guaranteedLosses;
        if (safeRandom < fractionalBudget) lossCount++;

        return Mathf.Clamp(lossCount, 0, available);
    }
}
