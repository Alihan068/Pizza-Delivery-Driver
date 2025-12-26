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
    public float baseSpeed = 3f;
    public float speedStep = 1.5f;
    public int maxSpeedLevel = 5;

    [Header("Turn")]
    public float baseTurn = 150f;
    public float turnStep = 10f;
    public int maxTurnLevel = 10;

    [Header("Health")]
    public float baseHealth = 100f;
    public float healthStep = 20f;
    public int maxHealthLevel = 10;

    [Header("Armor (Damage Reduction %)")]
    [Range(0, 1)] public float baseArmor = 0f;
    [Range(0, 0.1f)] public float armorStep = 0.05f;
    public int maxArmorLevel = 5;

    [Header("Capacity (Pizza Storage)")]
    public int baseCapacity = 2;
    public int capacityStep = 1;
    public int maxCapacityLevel = 8;

    [Header("Protection (Drop Chance %)")]
    [Range(0, 1)] public float baseProtection = 0f;
    [Range(0, 0.1f)] public float protectionStep = 0.1f;
    public int maxProtectionLevel = 5;
}