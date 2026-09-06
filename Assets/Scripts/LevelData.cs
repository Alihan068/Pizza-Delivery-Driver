using UnityEngine;

[CreateAssetMenu(fileName = "NewLevelData", menuName = "PizzaGame/Level Data")]
public class LevelData : ScriptableObject {
    [Header("Order Size")]
    public int orderMin = 1;
    public float orderScale = 0.6f;

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
}
