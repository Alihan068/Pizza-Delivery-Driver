using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="VehicleData"/> that adds live validation and an upgrade curve
/// summary below the normal fields.
/// </summary>
/// <remarks>
/// It uses the same validation engine as the wizard, so someone editing a vehicle asset without
/// ever opening the wizard sees exactly the same warnings.
/// </remarks>
[CustomEditor(typeof(VehicleData))]
public class VehicleDataEditor : Editor {

    List<VehicleIssue> issues = new List<VehicleIssue>();
    bool showCostPreview = true;

    void OnEnable() {
        Revalidate();
    }

    void Revalidate() {
        issues = VehicleValidator.ValidateData((VehicleData)target)
            .OrderByDescending(i => (int)i.severity)
            .ToList();
    }

    /// <summary>Draws the default fields, then the validation results and the cost summary.</summary>
    public override void OnInspectorGUI() {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) Revalidate();

        EditorGUILayout.Space();

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

        if (issues.Count > 0) {
            VehicleIssueListDrawer.Draw(issues, string.Empty);
        }

        DrawCostPreview();
    }

    void DrawCostPreview() {
        showCostPreview = EditorGUILayout.Foldout(showCostPreview, "Upgrade curve", true);
        if (!showCostPreview) return;

        var summaries = VehicleCostPreview.Build((VehicleData)target);
        if (summaries == null) {
            EditorGUILayout.HelpBox("Could not read the cost constants from the GameManager prefab.", MessageType.None);
            return;
        }

        int grandTotal = 0;
        int levelTotal = 0;
        foreach (var s in summaries) {
            grandTotal += s.totalCost;
            levelTotal += s.maxLevel;
            EditorGUILayout.LabelField(s.statName,
                s.maxLevel + " levels   " + s.baseValue.ToString("0.##") + " > " + s.maxValue.ToString("0.##") +
                "   first " + s.firstCost + "   total " + s.totalCost);
        }
        EditorGUILayout.LabelField("TOTAL", levelTotal + " levels   " + grandTotal + " currency", EditorStyles.boldLabel);
    }
}
