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
}
