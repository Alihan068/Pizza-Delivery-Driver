using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewLevelData", menuName = "PizzaGame/Level Data")]
public class LevelData : ScriptableObject {
    [Header("Order Size")]
    [Tooltip("Legacy order minimum used only when difficultyLevels is empty.")]
    public int orderMin = 1;
    [Tooltip("Legacy capacity scaling used only when difficultyLevels is empty.")]
    public float orderScale = 0.6f;

    [Header("Map Difficulty")]
    [Tooltip("Difficulty tiers authored for this map. The list can contain any number of tiers.")]
    /// <summary>Ordered difficulty tiers authored for this map.</summary>
    public List<MapDifficultyData> difficultyLevels = new List<MapDifficultyData>();

    [Header("Wait Time")]
    public float waitBase = 30f;
    public float waitPerOrderPizza = 8f;
    public float partialExtension = 12f;
    public float partialExtensionCapMult = 1.0f;

    [Header("Reward")]
    public int pizzaBaseReward = 9;
    public int tipPerPizza = 7;
    public float completionBonusBase = 6f;
    public float bonusExponent = 1.6f;
    public int failPenaltyPerPizza = 8;

    [Header("Despawn Delay")]
    public float completedDespawnDelay = 3f;
    public float timedOutDespawnDelay = 2f;

    [Header("Pizza Supply")]
    public int pizzaCost = 1;
    public int maxActiveCustomers = 4;

    [Header("First Shift Onboarding")]
    [Tooltip("When enabled, the first career shift starts with one order larger than the driver's capacity so partial delivery is demonstrated once.")]
    public bool demonstratePartialDeliveryOnFirstShift = true;

    [Header("Difficulty Ramp")]
    /// <summary>Normalized shift progress at which the authored difficulty ramp begins.</summary>
    [Range(0f, 1f)] public float difficultyRampStart = 0.25f;
    /// <summary>Normalized shift progress at which the authored difficulty ramp reaches its endpoint.</summary>
    [Range(0f, 1f)] public float difficultyRampEnd = 1f;
    /// <summary>Obstacle interval multiplier at the end of the shift.</summary>
    [Min(0.01f)] public float obstacleSpawnIntervalMultiplierAtEnd = 0.7f;
    /// <summary>Customer wait-time multiplier at the end of the shift.</summary>
    [Range(0.01f, 1f)] public float customerWaitMultiplierAtEnd = 0.8f;

    /// <summary>Returns normalized shift difficulty for a normalized shift progress value.</summary>
    /// <param name="shiftProgress01">Shift progress from zero to one.</param>
    /// <returns>Difficulty progress from zero to one.</returns>
    public float EvaluateDifficulty(float shiftProgress01) {
        float start = Mathf.Clamp01(difficultyRampStart);
        float end = Mathf.Clamp01(difficultyRampEnd);
        float progress = Mathf.Clamp01(shiftProgress01);
        if (end <= start) return progress >= end ? 1f : 0f;
        return Mathf.InverseLerp(start, end, progress);
    }

    /// <summary>Returns the obstacle spawn interval multiplier at shift progress.</summary>
    /// <param name="shiftProgress01">Shift progress from zero to one.</param>
    /// <returns>Multiplier applied to an authored obstacle interval.</returns>
    public float GetObstacleSpawnIntervalMultiplier(float shiftProgress01) {
        return Mathf.Lerp(1f, Mathf.Max(0.01f, obstacleSpawnIntervalMultiplierAtEnd), EvaluateDifficulty(shiftProgress01));
    }

    /// <summary>Returns the customer wait-time multiplier at shift progress.</summary>
    /// <param name="shiftProgress01">Shift progress from zero to one.</param>
    /// <returns>Multiplier applied to an authored customer wait time.</returns>
    public float GetCustomerWaitMultiplier(float shiftProgress01) {
        return Mathf.Lerp(1f, Mathf.Clamp(customerWaitMultiplierAtEnd, 0.01f, 1f), EvaluateDifficulty(shiftProgress01));
    }

    /// <summary>Returns the difficulty tier at an authored list index.</summary>
    /// <param name="index">Zero-based difficulty index.</param>
    /// <returns>The tier, or null when the index is invalid.</returns>
    public MapDifficultyData GetDifficultyAt(int index) {
        if (difficultyLevels == null || index < 0 || index >= difficultyLevels.Count) return null;
        return difficultyLevels[index];
    }

    /// <summary>Finds a difficulty tier by its permanent identifier.</summary>
    /// <param name="difficultyId">Identifier stored by the active profile.</param>
    /// <returns>The matching tier, or null when it is not authored.</returns>
    public MapDifficultyData GetDifficulty(string difficultyId) {
        if (string.IsNullOrEmpty(difficultyId) || difficultyLevels == null) return null;
        foreach (var difficulty in difficultyLevels) {
            if (difficulty != null && difficulty.difficultyId == difficultyId) return difficulty;
        }
        return null;
    }

    /// <summary>Returns whether this LevelData contains at least one authored difficulty tier.</summary>
    /// <returns>True when difficultyLevels has a usable first entry.</returns>
    public bool HasDifficultyLevels() {
        return difficultyLevels != null && difficultyLevels.Count > 0 && difficultyLevels[0] != null;
    }
}
