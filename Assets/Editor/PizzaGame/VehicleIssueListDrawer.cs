using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared helper that draws lists of findings with IMGUI.
/// The creation wizard and the validation window use the same presentation.
/// </summary>
public static class VehicleIssueListDrawer {

    /// <summary>
    /// Draws the findings grouped by severity, with a button on each row that selects the target.
    /// </summary>
    /// <param name="issues">The findings to draw. May be null or empty.</param>
    /// <param name="emptyMessage">Message shown when there are no findings.</param>
    public static void Draw(IReadOnlyList<VehicleIssue> issues, string emptyMessage) {
        if (issues == null || issues.Count == 0) {
            EditorGUILayout.HelpBox(emptyMessage, MessageType.Info);
            return;
        }

        int errors = issues.Count(i => i.severity == VehicleIssueSeverity.Error);
        int warnings = issues.Count(i => i.severity == VehicleIssueSeverity.Warning);
        int infos = issues.Count(i => i.severity == VehicleIssueSeverity.Info);

        EditorGUILayout.LabelField(errors + " errors   " + warnings + " warnings   " + infos + " info", EditorStyles.miniBoldLabel);

        foreach (var issue in issues.OrderByDescending(i => (int)i.severity)) {
            DrawOne(issue);
        }
    }

    static void DrawOne(VehicleIssue issue) {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
            EditorGUILayout.LabelField(Prefix(issue.severity), GUILayout.Width(60));

            var style = new GUIStyle(EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField(issue.message, style);

            using (new EditorGUI.DisabledScope(issue.context == null)) {
                if (GUILayout.Button("Select", GUILayout.Width(60))) {
                    EditorGUIUtility.PingObject(issue.context);
                    Selection.activeObject = issue.context;
                }
            }
        }
    }

    static string Prefix(VehicleIssueSeverity severity) {
        switch (severity) {
            case VehicleIssueSeverity.Error: return "ERROR";
            case VehicleIssueSeverity.Warning: return "WARN";
            default: return "INFO";
        }
    }

    /// <summary>
    /// Writes the findings to the Unity console so they survive the window being closed and each
    /// line can be clicked to reach its target object.
    /// </summary>
    /// <param name="issues">The findings to log.</param>
    /// <param name="header">Prefix added to every console line.</param>
    public static void LogToConsole(IReadOnlyList<VehicleIssue> issues, string header) {
        if (issues == null) return;
        foreach (var issue in issues) {
            string line = header + " " + issue.message;
            switch (issue.severity) {
                case VehicleIssueSeverity.Error:
                    Debug.LogError(line, issue.context);
                    break;
                case VehicleIssueSeverity.Warning:
                    Debug.LogWarning(line, issue.context);
                    break;
                default:
                    Debug.Log(line, issue.context);
                    break;
            }
        }
    }
}
