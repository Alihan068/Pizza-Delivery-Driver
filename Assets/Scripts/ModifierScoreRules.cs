/// <summary>
/// Pure final-score math, kept in exactly one place so no other system re-derives or re-rounds it.
/// A shift's raw score (the plain sum of every positive/negative AddScore event) is multiplied by
/// the combined modifier coefficient exactly once, at the end, never per event.
/// </summary>
public static class ModifierScoreRules {
    /// <summary>Computes the final integer score from a raw score and the combined modifier coefficient.</summary>
    /// <param name="rawScore">Plain sum of every AddScore event for the shift. Negative totals are treated as zero.</param>
    /// <param name="combinedScoreMultiplier">Product of every selected modifier's score multiplier. An empty selection must pass 1 (neutral).</param>
    /// <returns><c>floor(max(0, rawScore) * combinedScoreMultiplier)</c>, clamped to a valid non-negative int. A NaN/Infinity/negative multiplier is rejected (treated as 0) rather than propagated.</returns>
    public static int ComputeFinalScore(int rawScore, float combinedScoreMultiplier) {
        float multiplier = combinedScoreMultiplier;
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) multiplier = 0f;

        double clampedRaw = System.Math.Max(0, rawScore);
        double product = clampedRaw * (double)multiplier;
        double floored = System.Math.Floor(product);

        if (floored <= 0d) return 0;
        if (floored >= int.MaxValue) return int.MaxValue;
        return (int)floored;
    }
}
