using UnityEngine;

/// <summary>
/// Pure, allocation-free rules for resolving optional per-vehicle tuning values.
/// </summary>
/// <remarks>
/// These methods do not read or mutate Unity objects, save records, or ScriptableObjects. Runtime
/// systems may use them to build a detached shift snapshot without changing authored content.
/// </remarks>
public static class VehicleTuningRules {

    /// <summary>Calculates the maximum speed unlocked by purchased Speed levels.</summary>
    /// <param name="baseSpeed">Vehicle speed with no purchased Speed levels.</param>
    /// <param name="speedStep">Speed added by one purchased Speed level.</param>
    /// <param name="purchasedLevel">Current purchased Speed level.</param>
    /// <param name="maxLevel">Authored maximum Speed level.</param>
    /// <returns>A non-negative unlocked maximum speed.</returns>
    public static float GetUnlockedMaxSpeed(float baseSpeed, float speedStep, int purchasedLevel, int maxLevel) {
        int safeMaxLevel = Mathf.Max(0, maxLevel);
        int safeLevel = Mathf.Clamp(purchasedLevel, 0, safeMaxLevel);
        float safeBaseSpeed = SanitizeFinite(baseSpeed, 0f);
        float safeSpeedStep = SanitizeFinite(speedStep, 0f);
        float calculatedMax = safeBaseSpeed + Mathf.Max(0f, safeSpeedStep) * safeLevel;
        return Mathf.Max(0f, SanitizeFinite(calculatedMax, float.MaxValue));
    }

    /// <summary>Clamps an authored tuning floor so it cannot exceed the unlocked maximum.</summary>
    /// <param name="minimumSpeed">Authored lower bound.</param>
    /// <param name="unlockedMaxSpeed">Current purchased upper bound.</param>
    /// <returns>A safe speed floor in the interval from zero to the unlocked maximum.</returns>
    public static float GetSafeMinimumSpeed(float minimumSpeed, float unlockedMaxSpeed) {
        float safeMax = Mathf.Max(0f, SanitizeFinite(unlockedMaxSpeed, 0f));
        return Mathf.Clamp(SanitizeFinite(minimumSpeed, 0f), 0f, safeMax);
    }

    /// <summary>Clamps a selected speed to the authored player range.</summary>
    /// <param name="minimumSpeed">Authored lower bound.</param>
    /// <param name="unlockedMaxSpeed">Current purchased upper bound.</param>
    /// <param name="selectedSpeed">Player-selected speed.</param>
    /// <returns>A speed inside the safe range.</returns>
    public static float ClampSpeed(float minimumSpeed, float unlockedMaxSpeed, float selectedSpeed) {
        float safeMax = Mathf.Max(0f, SanitizeFinite(unlockedMaxSpeed, 0f));
        float safeMin = GetSafeMinimumSpeed(minimumSpeed, safeMax);
        return Mathf.Clamp(SanitizeCandidate(selectedSpeed, safeMin), safeMin, safeMax);
    }

    /// <summary>Resolves the speed used by a shift without allowing tuning to bypass a purchased ceiling.</summary>
    /// <param name="advancedTuningEnabled">Whether the global Advanced Tuning preference is on.</param>
    /// <param name="hasCustomTuning">Whether the vehicle has a saved custom tuning snapshot.</param>
    /// <param name="savedSpeed">Saved player-selected speed.</param>
    /// <param name="minimumSpeed">Authored lower bound.</param>
    /// <param name="unlockedMaxSpeed">Current purchased upper bound.</param>
    /// <returns>The clamped custom speed, or the unlocked maximum for Classic/default behavior.</returns>
    public static float ResolveSpeed(bool advancedTuningEnabled, bool hasCustomTuning, float savedSpeed,
        float minimumSpeed, float unlockedMaxSpeed) {
        if (!advancedTuningEnabled || !hasCustomTuning) return Mathf.Max(0f, SanitizeFinite(unlockedMaxSpeed, 0f));
        return ClampSpeed(minimumSpeed, unlockedMaxSpeed, savedSpeed);
    }

    /// <summary>Clamps any player-tunable drift value to its authored range.</summary>
    /// <param name="value">Candidate value.</param>
    /// <param name="minimum">Authored lower bound.</param>
    /// <param name="maximum">Authored upper bound.</param>
    /// <returns>The candidate inside the ordered authored range.</returns>
    public static float ClampPlayerValue(float value, float minimum, float maximum) {
        float safeMinimum = SanitizeFinite(minimum, 0f);
        float safeMaximum = SanitizeFinite(maximum, safeMinimum);
        float lower = Mathf.Min(safeMinimum, safeMaximum);
        float upper = Mathf.Max(safeMinimum, safeMaximum);
        return Mathf.Clamp(SanitizeCandidate(value, lower), lower, upper);
    }

    /// <summary>Resolves a saved optional value while preserving the authored classic fallback.</summary>
    /// <param name="advancedTuningEnabled">Whether the global Advanced Tuning preference is on.</param>
    /// <param name="hasCustomTuning">Whether the vehicle has a saved custom tuning snapshot.</param>
    /// <param name="savedValue">Saved custom value.</param>
    /// <param name="classicValue">Authored value used by Classic/default behavior.</param>
    /// <param name="minimum">Authored lower bound.</param>
    /// <param name="maximum">Authored upper bound.</param>
    /// <returns>The selected value clamped to the authored range.</returns>
    public static float ResolvePlayerValue(bool advancedTuningEnabled, bool hasCustomTuning, float savedValue,
        float classicValue, float minimum, float maximum) {
        float candidate = advancedTuningEnabled && hasCustomTuning ? savedValue : classicValue;
        return ClampPlayerValue(candidate, minimum, maximum);
    }

    static float SanitizeFinite(float value, float fallback) {
        if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
        return value;
    }

    static float SanitizeCandidate(float value, float fallback) {
        if (float.IsNaN(value)) return fallback;
        if (float.IsPositiveInfinity(value)) return float.MaxValue;
        if (float.IsNegativeInfinity(value)) return -float.MaxValue;
        return value;
    }
}
