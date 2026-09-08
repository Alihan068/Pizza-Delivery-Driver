using UnityEngine;

[CreateAssetMenu(fileName = "NewVehicleData", menuName = "PizzaGame/Vehicle Data")]
public class VehicleData : ScriptableObject {
    // The defaults below mirror the balance table that is currently in use: the shared
    // step/maxLevel/costMult values are identical on both vehicles, and the base values come from
    // the starting vehicle, Scooter. When balance changes, UPDATE THESE TOO -- otherwise vehicles
    // created from the Create menu are silently born with the old curve.
    // VehicleValidator.ValidateScriptDefaults watches for that drift and warns.

    [Header("Identity & Visuals")]
    /// <summary>
    /// Permanent identifier this vehicle is saved and referenced by. Generated once when the asset
    /// is created and never changed afterwards.
    /// </summary>
    /// <remarks>
    /// Saves key off this rather than <see cref="vehicleName"/> so that renaming a vehicle — or
    /// localizing its name — cannot orphan a player's upgrades. It is also what lets externally
    /// supplied content be referenced safely: an id is stable across sources, an array index is not.
    /// </remarks>
    public string vehicleId;

    /// <summary>Name shown to the player. Safe to change or localize; nothing is saved against it.</summary>
    public string vehicleName;

    /// <summary>Optional localized display-name key. Empty retains the authored proper name for external vehicles.</summary>
    public string displayNameKey;

    /// <summary>Prefab PlayerSpawner instantiates at the start of a session.</summary>
    public GameObject vehiclePrefab;

    /// <summary>Icon shown in the garage. Falls back to the prefab's SpriteRenderer when empty.</summary>
    public Sprite vehicleIcon;

    /// <summary>Cost to unlock this vehicle. Never read for the first vehicle (allVehicles[0]).</summary>
    public int price;

    /// <summary>Rank the player must reach before this vehicle can be purchased. Zero means available from the start.</summary>
    public int requiredRank;

    [Header("Descriptions")]
    /// <summary>Localization key for the Speed card description.</summary>
    [TextArea] public string speedDesc = "stat.speed.description";

    /// <summary>Localization key for the Handling card description.</summary>
    [TextArea] public string turnDesc = "stat.turn.description";

    /// <summary>Localization key for the Chassis card description.</summary>
    [TextArea] public string healthDesc = "stat.health.description";

    /// <summary>Localization key for the Armor card description.</summary>
    [TextArea] public string armorDesc = "stat.armor.description";

    /// <summary>Localization key for the Storage card description.</summary>
    [TextArea] public string capacityDesc = "stat.capacity.description";

    /// <summary>Localization key for the Stabilizer card description.</summary>
    [TextArea] public string protectionDesc = "stat.protection.description";

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

    [Header("Driving")]
    /// <summary>Driving and drift tuning authored for this vehicle.</summary>
    public VehicleDrivingSettings drivingSettings = new VehicleDrivingSettings();

    // The four accessors below exist so that callers never have to know which field belongs to
    // which stat. Before this, the same six-way switch was repeated in GameManager and
    // GarageManager against magic strings, and a mistyped key silently charged the player without
    // granting a level.

    /// <summary>Value of a stat with no upgrades bought.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The unupgraded value, or zero for an unknown stat.</returns>
    public float GetBaseValue(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return baseSpeed;
            case VehicleStatId.Turn: return baseTurn;
            case VehicleStatId.Health: return baseHealth;
            case VehicleStatId.Armor: return baseArmor;
            case VehicleStatId.Capacity: return baseCapacity;
            case VehicleStatId.Protection: return baseProtection;
            default: return 0f;
        }
    }

    /// <summary>Amount one purchased level adds to a stat.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The per-level increment, or zero for an unknown stat.</returns>
    public float GetStep(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return speedStep;
            case VehicleStatId.Turn: return turnStep;
            case VehicleStatId.Health: return healthStep;
            case VehicleStatId.Armor: return armorStep;
            case VehicleStatId.Capacity: return capacityStep;
            case VehicleStatId.Protection: return protectionStep;
            default: return 0f;
        }
    }

    /// <summary>How many levels of a stat can be bought.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The level ceiling, or zero for an unknown stat.</returns>
    public int GetMaxLevel(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return maxSpeedLevel;
            case VehicleStatId.Turn: return maxTurnLevel;
            case VehicleStatId.Health: return maxHealthLevel;
            case VehicleStatId.Armor: return maxArmorLevel;
            case VehicleStatId.Capacity: return maxCapacityLevel;
            case VehicleStatId.Protection: return maxProtectionLevel;
            default: return 0;
        }
    }

    /// <summary>Price multiplier applied to this stat's upgrades.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>The multiplier, or one for an unknown stat so pricing never collapses to free.</returns>
    public float GetCostMultiplier(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return speedCostMult;
            case VehicleStatId.Turn: return turnCostMult;
            case VehicleStatId.Health: return healthCostMult;
            case VehicleStatId.Armor: return armorCostMult;
            case VehicleStatId.Capacity: return capacityCostMult;
            case VehicleStatId.Protection: return protectionCostMult;
            default: return 1f;
        }
    }

    /// <summary>Resolves this stat's description key without depending on the active language.</summary>
    /// <param name="stat">Which stat to read.</param>
    /// <returns>A localization key, or an empty string for an unknown stat.</returns>
    public string GetDescription(VehicleStatId stat) {
        switch (stat) {
            case VehicleStatId.Speed: return speedDesc;
            case VehicleStatId.Turn: return turnDesc;
            case VehicleStatId.Health: return healthDesc;
            case VehicleStatId.Armor: return armorDesc;
            case VehicleStatId.Capacity: return capacityDesc;
            case VehicleStatId.Protection: return protectionDesc;
            default: return string.Empty;
        }
    }

    /// <summary>Name shown to the player: the localized proper name when one is set, otherwise <see cref="vehicleName"/>.</summary>
    /// <returns>The text to display for this vehicle.</returns>
    public string GetDisplayName() {
        return string.IsNullOrEmpty(displayNameKey) ? vehicleName : LocalizationManager.Get(displayNameKey);
    }
}
