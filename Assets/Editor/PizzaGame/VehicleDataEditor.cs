using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="VehicleData"/> with grouped balance and driving controls.
/// </summary>
/// <remarks>
/// The editor keeps every authored field visible, but separates identity, upgrade economy and
/// driving behavior so a new vehicle can be tuned without searching through one long flat list.
/// Validation and the upgrade preview use the same runtime data as the creation wizard.
/// </remarks>
[CustomEditor(typeof(VehicleData))]
public class VehicleDataEditor : Editor {

    List<VehicleIssue> issues = new List<VehicleIssue>();
    bool showIdentity = true;
    bool showDescriptions;
    bool showUpgrades = true;
    bool showDriving = true;
    bool showCostPreview = true;

    void OnEnable() {
        Revalidate();
    }

    void Revalidate() {
        issues = VehicleValidator.ValidateData((VehicleData)target)
            .OrderByDescending(i => (int)i.severity)
            .ToList();
    }

    /// <summary>Draws grouped editable data, validation findings and the upgrade curve summary.</summary>
    public override void OnInspectorGUI() {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();

        DrawIdentityGroup();
        DrawDescriptionsGroup();
        DrawUpgradeGroup();
        DrawDrivingGroup();

        if (EditorGUI.EndChangeCheck()) {
            serializedObject.ApplyModifiedProperties();
            Revalidate();
        }
        else {
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();
        DrawValidationSummary();
        DrawCostPreview();
    }

    void DrawIdentityGroup() {
        showIdentity = EditorGUILayout.BeginFoldoutHeaderGroup(showIdentity, "Identity and visuals");
        if (showIdentity) {
            DrawProperty("vehicleId", "Vehicle ID", "Stable save/content identifier. Do not change after release.");
            DrawProperty("vehicleName", "Vehicle Name", "Player-facing name; save ownership uses Vehicle ID.");
            DrawProperty("displayNameKey", "Display Name Key", "Localization key for the player-facing name.");
            DrawProperty("vehiclePrefab", "Vehicle Prefab", "Prefab spawned at the start of a shift.");
            DrawProperty("vehicleIcon", "Garage Icon", "Icon shown in the garage.");
            DrawProperty("price", "Unlock Price", "One-time currency cost. The first registered vehicle is always free.");
            DrawProperty("requiredRank", "Required Rank", "Visual progression gate for this vehicle purchase.");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawDescriptionsGroup() {
        showDescriptions = EditorGUILayout.BeginFoldoutHeaderGroup(showDescriptions, "Localization description keys");
        if (showDescriptions) {
            DrawProperty("speedDesc", "Speed Description");
            DrawProperty("turnDesc", "Handling Description");
            DrawProperty("healthDesc", "Chassis Description");
            DrawProperty("armorDesc", "Armor Description");
            DrawProperty("capacityDesc", "Storage Description");
            DrawProperty("protectionDesc", "Stabilizer Description");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawUpgradeGroup() {
        showUpgrades = EditorGUILayout.BeginFoldoutHeaderGroup(showUpgrades, "Upgrade values and limits");
        if (showUpgrades) {
            EditorGUILayout.HelpBox("Base is the starting value. Step is the value added per level. Max Level is the purchase limit. Cost Mult changes only this stat's upgrade price.", MessageType.None);
            DrawStatGroup("Speed", "baseSpeed", "speedStep", "maxSpeedLevel", "speedCostMult", "world units per second");
            DrawStatGroup("Handling", "baseTurn", "turnStep", "maxTurnLevel", "turnCostMult", "degrees per second");
            DrawStatGroup("Chassis", "baseHealth", "healthStep", "maxHealthLevel", "healthCostMult", "health points");
            DrawStatGroup("Armor", "baseArmor", "armorStep", "maxArmorLevel", "armorCostMult", "damage reduction 0..1");
            DrawStatGroup("Storage", "baseCapacity", "capacityStep", "maxCapacityLevel", "capacityCostMult", "pizzas");
            DrawStatGroup("Stabilizer", "baseProtection", "protectionStep", "maxProtectionLevel", "protectionCostMult", "pizza protection 0..1");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawStatGroup(string title, string baseField, string stepField, string maxField, string multiplierField, string unit) {
        EditorGUILayout.LabelField(title + " (" + unit + ")", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope()) {
            DrawProperty(baseField, "Base");
            DrawProperty(stepField, "Step");
        }
        using (new EditorGUILayout.HorizontalScope()) {
            DrawProperty(maxField, "Max Level");
            DrawProperty(multiplierField, "Cost Mult");
        }
    }

    void DrawDrivingGroup() {
        showDriving = EditorGUILayout.BeginFoldoutHeaderGroup(showDriving, "Driving and drift tuning");
        if (showDriving) {
            SerializedProperty driving = serializedObject.FindProperty("drivingSettings");
            if (driving == null) {
                EditorGUILayout.HelpBox("Driving settings field is missing. Reimport the runtime scripts.", MessageType.Error);
            }
            else {
                EditorGUILayout.HelpBox("These values control momentum, braking, steering response, grip and drift detection. They are copied into a runtime snapshot when the vehicle spawns.", MessageType.None);
                EditorGUILayout.PropertyField(driving, new GUIContent("Driving Settings"), true);
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawProperty(string propertyName, string label, string tooltip = null) {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) {
            EditorGUILayout.HelpBox("Missing VehicleData field: " + propertyName, MessageType.Error);
            return;
        }
        EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip), true);
    }

    void DrawValidationSummary() {
        int errors = issues.Count(i => i.severity == VehicleIssueSeverity.Error);
        int warnings = issues.Count(i => i.severity == VehicleIssueSeverity.Warning);

        if (errors > 0) {
            EditorGUILayout.HelpBox(errors + " error(s) found. This vehicle will not work in the game as it is.", MessageType.Error);
        }
        else if (warnings > 0) {
            EditorGUILayout.HelpBox(warnings + " warning(s).", MessageType.Warning);
        }
        else {
            EditorGUILayout.HelpBox("This vehicle passes validation.", MessageType.Info);
        }

        if (issues.Count > 0) VehicleIssueListDrawer.Draw(issues, string.Empty);
    }

    void DrawCostPreview() {
        showCostPreview = EditorGUILayout.BeginFoldoutHeaderGroup(showCostPreview, "Upgrade curve preview");
        if (!showCostPreview) {
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        var summaries = VehicleCostPreview.Build((VehicleData)target);
        if (summaries == null) {
            EditorGUILayout.HelpBox("Could not read the cost constants from the GameManager prefab.", MessageType.None);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return;
        }

        int grandTotal = 0;
        int levelTotal = 0;
        foreach (var summary in summaries) {
            grandTotal += summary.totalCost;
            levelTotal += summary.maxLevel;
            EditorGUILayout.LabelField(summary.statName,
                summary.maxLevel + " levels   " + summary.baseValue.ToString("0.##") + " > " + summary.maxValue.ToString("0.##") +
                "   first " + summary.firstCost + "   total " + summary.totalCost);
        }
        EditorGUILayout.LabelField("TOTAL", levelTotal + " levels   " + grandTotal + " currency", EditorStyles.boldLabel);
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}
