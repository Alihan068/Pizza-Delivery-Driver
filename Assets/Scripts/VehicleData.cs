using UnityEngine;

[CreateAssetMenu(fileName = "NewVehicleData", menuName = "PizzaGame/Vehicle Data")]
public class VehicleData : ScriptableObject {
    // The defaults below mirror the balance table that is currently in use: the shared
    // step/maxLevel/costMult values are identical on both vehicles, and the base values come from
    // the starting vehicle, Scooter. When balance changes, UPDATE THESE TOO -- otherwise vehicles
    // created from the Create menu are silently born with the old curve.
    // VehicleValidator.ValidateScriptDefaults watches for that drift and warns.

    [Header("Identity & Visuals")]
    /// <summary>Unique name the save system matches this vehicle by. Must not be empty.</summary>
    public string vehicleName;

    /// <summary>Prefab PlayerSpawner instantiates at the start of a session.</summary>
    public GameObject vehiclePrefab;

    /// <summary>Icon shown in the garage. Falls back to the prefab's SpriteRenderer when empty.</summary>
    public Sprite vehicleIcon;

    /// <summary>Cost to unlock this vehicle. Never read for the first vehicle (allVehicles[0]).</summary>
    public int price;

    [Header("Descriptions")]
    /// <summary>Description shown on the Speed card in the garage.</summary>
    [TextArea] public string speedDesc = "Increases max speed.";

    /// <summary>Description shown on the Handling card in the garage.</summary>
    [TextArea] public string turnDesc = "Better handling in corners.";

    /// <summary>Description shown on the Chassis card in the garage.</summary>
    [TextArea] public string healthDesc = "More durability against crashes.";

    /// <summary>Description shown on the Armor card in the garage.</summary>
    [TextArea] public string armorDesc = "Reduces damage taken.";

    /// <summary>Description shown on the Storage card in the garage.</summary>
    [TextArea] public string capacityDesc = "Carry more pizzas.";

    /// <summary>Description shown on the Stabilizer card in the garage.</summary>
    [TextArea] public string protectionDesc = "Chance to save pizza on crash.";

    [Header("Speed")]
    /// <summary>Speed with no upgrades bought.</summary>
    public float baseSpeed = 4.4f;

    /// <summary>Amount each speed upgrade adds.</summary>
    public float speedStep = 0.1f;

    /// <summary>Number of speed levels that can be bought.</summary>
    public int maxSpeedLevel = 25;

    /// <summary>Base cost multiplier for speed upgrades.</summary>
    public float speedCostMult = 0.5f;

    [Header("Turn")]
    /// <summary>Turn rate with no upgrades bought, in degrees per second.</summary>
    public float baseTurn = 210f;

    /// <summary>Amount each turn upgrade adds.</summary>
    public float turnStep = 10f;

    /// <summary>Number of turn levels that can be bought.</summary>
    public int maxTurnLevel = 12;

    /// <summary>Base cost multiplier for turn upgrades.</summary>
    public float turnCostMult = 0.5f;

    [Header("Health")]
    /// <summary>Health with no upgrades bought.</summary>
    public float baseHealth = 120f;

    /// <summary>Amount each health upgrade adds.</summary>
    public float healthStep = 10f;

    /// <summary>Number of health levels that can be bought.</summary>
    public int maxHealthLevel = 12;

    /// <summary>Base cost multiplier for health upgrades.</summary>
    public float healthCostMult = 0.9f;

    [Header("Armor (Damage Reduction %)")]
    /// <summary>Damage reduction with no upgrades bought, from 0 to 1.</summary>
    [Range(0, 1)] public float baseArmor = 0f;

    /// <summary>Damage reduction each armor upgrade adds.</summary>
    [Range(0, 0.1f)] public float armorStep = 0.02f;

    /// <summary>Number of armor levels that can be bought.</summary>
    public int maxArmorLevel = 12;

    /// <summary>Base cost multiplier for armor upgrades.</summary>
    public float armorCostMult = 1.0f;

    [Header("Capacity (Pizza Storage)")]
    /// <summary>Pizza carrying capacity with no upgrades bought.</summary>
    public int baseCapacity = 2;

    /// <summary>Number of pizzas each capacity upgrade adds.</summary>
    public int capacityStep = 1;

    /// <summary>Number of capacity levels that can be bought.</summary>
    public int maxCapacityLevel = 5;

    /// <summary>Base cost multiplier for capacity upgrades.</summary>
    public float capacityCostMult = 3.5f;

    [Header("Protection (Drop Chance %)")]
    /// <summary>Chance to save a pizza with no upgrades bought, from 0 to 1.</summary>
    [Range(0, 1)] public float baseProtection = 0f;

    /// <summary>Chance each protection upgrade adds.</summary>
    [Range(0, 0.25f)] public float protectionStep = 0.06f;

    /// <summary>Number of protection levels that can be bought.</summary>
    public int maxProtectionLevel = 9;

    /// <summary>Base cost multiplier for protection upgrades.</summary>
    public float protectionCostMult = 0.45f;
}
