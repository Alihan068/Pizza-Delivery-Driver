using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Standalone window that audits the existing vehicles.
/// </summary>
/// <remarks>
/// Meant for vehicles that were not made with the wizard, were wired by hand, or broke later.
/// The most valuable finding is an empty <c>vehiclePrefab</c>: the garage gives no sign of it and
/// the game only fails later, in PlayerSpawner.
/// </remarks>
public class VehicleValidationWindow : EditorWindow {

    List<VehicleIssue> issues = new List<VehicleIssue>();
    Vector2 scroll;
    bool hasScanned;

    /// <summary>Opens the validation window and runs a first scan.</summary>
    [MenuItem("Tools/PizzaGame/Validate Vehicles", false, 11)]
    public static void ShowWindow() {
        var window = GetWindow<VehicleValidationWindow>(false, "Vehicle Validation", true);
        window.minSize = new Vector2(460f, 320f);
        window.Scan();
        window.Show();
    }

    void OnEnable() {
        if (!hasScanned) Scan();
    }

    void Scan() {
        issues = VehicleValidator.ValidateProject();
        hasScanned = true;
        Repaint();
    }

    void OnGUI() {
        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("Scan", GUILayout.Height(24))) Scan();
            if (GUILayout.Button("Log to console", GUILayout.Height(24), GUILayout.Width(120))) {
                VehicleIssueListDrawer.LogToConsole(issues, "[Vehicle Validation]");
            }
        }

        EditorGUILayout.HelpBox(
            "The registry is the allVehicles array on the GameManager prefab. The GameManager instances in the " +
            "open scenes are also checked for overrides on that array -- an override means new vehicles will not " +
            "appear in that scene.",
            MessageType.None);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        VehicleIssueListDrawer.Draw(issues, "All vehicles are clean. Nothing was found.");
        EditorGUILayout.EndScrollView();
    }
}
