using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Reports table completeness, duplicate identifiers and unused translations without modifying scenes.</summary>
public class LocalizationValidationWindow : EditorWindow {
    [SerializeField] LocalizationCatalog catalog;
    readonly List<string> findings = new List<string>();
    Vector2 scroll;

    [MenuItem("Tools/PizzaGame/Validate Localization")]
    static void Open() { GetWindow<LocalizationValidationWindow>("Localization Validation"); }

    void OnGUI() {
        catalog = (LocalizationCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(LocalizationCatalog), false);
        EditorGUILayout.HelpBox("Scans build scenes, project prefabs, vehicles and maps. Open scenes are preserved. Key fields use the Key suffix; garage displayName and vehicle descriptions retain their existing serialized names.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)) {
            if (GUILayout.Button("Validate")) {
                findings.Clear();
                findings.AddRange(Validate(catalog));
            }
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (string finding in findings) EditorGUILayout.HelpBox(finding, MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    /// <summary>Scans serialized key consumers and installed tables. Additively opened scenes are always closed without saving.</summary>
    /// <param name="catalog">Installed languages and fallback to validate.</param>
    /// <returns>Diagnostic messages prefixed with ERROR, UNUSED or OK. Does not write assets or logs.</returns>
    public static List<string> Validate(LocalizationCatalog catalog) {
        var issues = new List<string>();
        if (catalog == null || catalog.languages == null) { issues.Add("ERROR: Assign a catalog with languages."); return issues; }
        if (catalog.defaultLanguage == null || System.Array.IndexOf(catalog.languages, catalog.defaultLanguage) < 0)
            issues.Add("ERROR: Fallback language must be in the catalog.");
        if (string.IsNullOrWhiteSpace(catalog.preferenceKey)) issues.Add("ERROR: Preference key is empty.");
        var used = new HashSet<string>();
        foreach (var sceneEntry in EditorBuildSettings.scenes) {
            if (!sceneEntry.enabled) continue;
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(sceneEntry.path);
            bool opened = !scene.isLoaded;
            try {
                if (opened) scene = EditorSceneManager.OpenScene(sceneEntry.path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects()) CollectComponents(root, used);
            }
            finally { if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })) {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (root != null) CollectComponents(root, used);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:VehicleData")) {
            var vehicle = AssetDatabase.LoadAssetAtPath<VehicleData>(AssetDatabase.GUIDToAssetPath(guid));
            CollectKeys(vehicle, used);
            foreach (VehicleStatId stat in System.Enum.GetValues(typeof(VehicleStatId))) AddKey(vehicle.GetDescription(stat), used);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:MapData"))
            CollectKeys(AssetDatabase.LoadAssetAtPath<MapData>(AssetDatabase.GUIDToAssetPath(guid)), used);

        var codes = new HashSet<string>(System.StringComparer.Ordinal);
        var fallbackFormats = new Dictionary<string, string>();
        if (catalog.defaultLanguage != null && catalog.defaultLanguage.entries != null)
            foreach (var entry in catalog.defaultLanguage.entries)
                if (entry != null && !string.IsNullOrEmpty(entry.key)) fallbackFormats[entry.key] = entry.text;
        foreach (var language in catalog.languages) {
            if (language == null) { issues.Add("ERROR: Null language in catalog."); continue; }
            if (string.IsNullOrWhiteSpace(language.languageCode) || !codes.Add(language.languageCode))
                issues.Add("ERROR: Empty or duplicate language code: " + language.languageCode);
            var keys = new HashSet<string>();
            if (language.entries != null) foreach (var entry in language.entries) {
                if (entry == null || string.IsNullOrWhiteSpace(entry.key)) { issues.Add("ERROR: Empty entry in " + language.name); continue; }
                if (!keys.Add(entry.key)) issues.Add("ERROR: Duplicate key in " + language.name + ": " + entry.key);
                if (string.IsNullOrEmpty(entry.text)) issues.Add("ERROR: Empty translation in " + language.name + ": " + entry.key);
                if (fallbackFormats.TryGetValue(entry.key, out string original) &&
                    !PlaceholderIds(original).SetEquals(PlaceholderIds(entry.text)))
                    issues.Add("ERROR: Placeholder mismatch in " + language.name + ": " + entry.key);
                if (!used.Contains(entry.key)) issues.Add("UNUSED: " + language.name + ": " + entry.key);
            }
            foreach (string key in used) if (!keys.Contains(key)) issues.Add("ERROR: Missing in " + language.name + ": " + key);
        }
        if (issues.Count == 0) issues.Add("OK: All " + used.Count + " referenced keys exist in every language; no unused keys.");
        return issues;
    }

    static void CollectComponents(GameObject root, HashSet<string> used) {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
            if (component != null) CollectKeys(component, used);
        }
    }

    static void CollectKeys(Object source, HashSet<string> used) {
        if (source == null) return;
        var serialized = new SerializedObject(source);
        var property = serialized.GetIterator();
        while (property.NextVisible(true)) {
            if (property.propertyType != SerializedPropertyType.String) continue;
            // These are editor authoring conventions, never runtime object identification.
            bool isKey = property.name.EndsWith("Key", System.StringComparison.Ordinal) || property.name == "localizationKey";
            if (source is GarageManager && property.name == "displayName") isKey = true;
            if (isKey) AddKey(property.stringValue, used);
        }
    }

    static void AddKey(string key, HashSet<string> used) {
        if (!string.IsNullOrWhiteSpace(key)) used.Add(key);
    }

    static HashSet<string> PlaceholderIds(string format) {
        var ids = new HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            format ?? string.Empty, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")) ids.Add(match.Groups[1].Value);
        return ids;
    }
}
