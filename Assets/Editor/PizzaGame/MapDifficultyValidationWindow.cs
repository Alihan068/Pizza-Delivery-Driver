using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Editor window for reviewing map difficulty authoring and preview coverage.</summary>
public class MapDifficultyValidationWindow : EditorWindow {
    List<string> findings = new List<string>();
    Vector2 scroll;

    /// <summary>Opens the map difficulty validation window and scans the project.</summary>
    [MenuItem("Tools/PizzaGame/Validate Map Difficulties", false, 12)]
    public static void ShowWindow() {
        MapDifficultyValidationWindow window = GetWindow<MapDifficultyValidationWindow>(false, "Map Difficulty Validation", true);
        window.minSize = new Vector2(520f, 320f);
        window.Scan();
        window.Show();
    }

    void Scan() {
        findings = MapDifficultyValidator.ValidateProject();
        Repaint();
    }

    void OnGUI() {
        if (GUILayout.Button("Scan", GUILayout.Height(24))) Scan();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (findings.Count == 0) {
            EditorGUILayout.HelpBox("All map difficulty data is valid.", MessageType.Info);
        }
        else {
            foreach (string finding in findings) EditorGUILayout.HelpBox(finding, finding.StartsWith("ERROR") ? MessageType.Error : MessageType.Warning);
        }
        EditorGUILayout.EndScrollView();
    }
}
