using UnityEngine;

[CreateAssetMenu(fileName = "NewVehicleData", menuName = "PizzaGame/Vehicle Data")]
public class VehicleData : ScriptableObject {
    [Header("Identity & Visuals")]
    public string vehicleName;
    public GameObject vehiclePrefab;
    public Sprite vehicleIcon;
    public int price;

    [Header("Descriptions")]
    [TextArea] public string speedDesc = "Increases max speed.";
    [TextArea] public string turnDesc = "Better handling in corners.";
    [TextArea] public string healthDesc = "More durability against crashes.";
    [TextArea] public string armorDesc = "Reduces damage taken.";
    [TextArea] public string capacityDesc = "Carry more pizzas.";
    [TextArea] public string protectionDesc = "Chance to save pizza on crash.";

    [Header("Speed")]
    public float baseSpeed = 4f;
    public float speedStep = 0.5f;
    public int maxSpeedLevel = 5;
    public float speedCostMult = 1.5f;

    [Header("Turn")]
    public float baseTurn = 180f;
    public float turnStep = 40f;
    public int maxTurnLevel = 3;
    public float turnCostMult = 0.9f;

    [Header("Health")]
    public float baseHealth = 150f;
    public float healthStep = 30f;
    public int maxHealthLevel = 4;
    public float healthCostMult = 1.0f;

    [Header("Armor (Damage Reduction %)")]
    [Range(0, 1)] public float baseArmor = 0f;
    [Range(0, 0.1f)] public float armorStep = 0.06f;
    public int maxArmorLevel = 4;
    public float armorCostMult = 1.2f;

    [Header("Capacity (Pizza Storage)")]
    public int baseCapacity = 2;
    public int capacityStep = 1;
    public int maxCapacityLevel = 5;
    public float capacityCostMult = 0.7f;

    [Header("Protection (Drop Chance %)")]
    [Range(0, 1)] public float baseProtection = 0f;
    [Range(0, 0.25f)] public float protectionStep = 0.18f;
    public int maxProtectionLevel = 3;
    public float protectionCostMult = 0.5f;
}
