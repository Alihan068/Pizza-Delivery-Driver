using UnityEditor;
using UnityEngine;

/// <summary>
/// Summarises the upgrade curve of a <see cref="VehicleData"/>: first purchase price, total cost
/// and fully upgraded value per stat.
/// </summary>
/// <remarks>
/// Neither the cost formula nor its constants are hard coded here. The formula matches
/// <c>GameManager.GetUpgradeCost</c> exactly and the constants are read from the GameManager
/// prefab, so the preview stays correct when balance is changed on the prefab.
/// </remarks>
public static class VehicleCostPreview {

    /// <summary>Upgrade curve summary for a single stat.</summary>
    public struct StatSummary {
        /// <summary>Key this stat uses in the GameManager switch statements.</summary>
        public string statName;

        /// <summary>Number of upgrade levels defined.</summary>
        public int maxLevel;

        /// <summary>Cost of going from level 0 to level 1.</summary>
        public int firstCost;

        /// <summary>Cost of buying every level.</summary>
        public int totalCost;

        /// <summary>Value with no upgrades bought.</summary>
        public float baseValue;

        /// <summary>Value after every level has been bought.</summary>
        public float maxValue;
    }

    /// <summary>
    /// Reads the upgrade cost constants from the GameManager prefab.
    /// </summary>
    /// <param name="costBase">Base cost at level 0.</param>
    /// <param name="costStep">Amount added to the cost per level.</param>
    /// <returns>True when the constants could be read.</returns>
    public static bool TryReadCostConstants(out float costBase, out float costStep) {
        costBase = 0f;
        costStep = 0f;

        var gmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleValidator.GameManagerPrefabPath);
        if (gmPrefab == null) return false;
        var gm = gmPrefab.GetComponent<GameManager>();
        if (gm == null) return false;

        var so = new SerializedObject(gm);
        var baseProp = so.FindProperty("upgradeCostBase");
        var stepProp = so.FindProperty("upgradeCostStep");
        if (baseProp == null || stepProp == null) return false;

        // Both fields are floats. Reading them once with .intValue returned 0 and nearly led to
        // reporting that upgrades were free.
        costBase = baseProp.floatValue;
        costStep = stepProp.floatValue;
        return true;
    }

    /// <summary>
    /// Builds the upgrade curve summary for all six stats of the given vehicle.
    /// </summary>
    /// <param name="data">The vehicle data to summarise.</param>
    /// <returns>Six summaries, or null when the cost constants could not be read.</returns>
    public static StatSummary[] Build(VehicleData data) {
        if (data == null) return null;
        if (!TryReadCostConstants(out float costBase, out float costStep)) return null;

        return new[] {
            Summarize("Capacity", data.baseCapacity, data.capacityStep, data.maxCapacityLevel, data.capacityCostMult, costBase, costStep),
            Summarize("Speed", data.baseSpeed, data.speedStep, data.maxSpeedLevel, data.speedCostMult, costBase, costStep),
            Summarize("Turn", data.baseTurn, data.turnStep, data.maxTurnLevel, data.turnCostMult, costBase, costStep),
            Summarize("Health", data.baseHealth, data.healthStep, data.maxHealthLevel, data.healthCostMult, costBase, costStep),
            Summarize("Armor", data.baseArmor, data.armorStep, data.maxArmorLevel, data.armorCostMult, costBase, costStep),
            Summarize("Protection", data.baseProtection, data.protectionStep, data.maxProtectionLevel, data.protectionCostMult, costBase, costStep)
        };
    }

    static StatSummary Summarize(string statName, float baseValue, float step, int maxLevel, float costMult, float costBase, float costStep) {
        int total = 0;
        for (int level = 0; level < maxLevel; level++) {
            total += Mathf.RoundToInt((costBase + costStep * level) * costMult);
        }
        return new StatSummary {
            statName = statName,
            maxLevel = maxLevel,
            firstCost = maxLevel > 0 ? Mathf.RoundToInt(costBase * costMult) : 0,
            totalCost = total,
            baseValue = baseValue,
            maxValue = baseValue + step * maxLevel
        };
    }
}
