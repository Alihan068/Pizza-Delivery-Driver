using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the prefab and the data asset for a new vehicle and writes it into the vehicle registry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clone a template, never build from scratch.</b> The prefab is copied with
/// <c>PrefabUtility.LoadPrefabContents</c> and the data with <c>Object.Instantiate</c>, for two reasons:
/// </para>
/// <para>
/// 1. Cloning remaps internal prefab references automatically; <c>Delivery.pizzaObject</c> ends up
/// pointing at the new prefab's own PizzaHolder. Building it by hand would mean reaching into
/// private [SerializeField] fields through SerializedObject.
/// </para>
/// <para>
/// 2. <c>ScriptableObject.CreateInstance&lt;VehicleData&gt;()</c> must NEVER be used here. If the C#
/// field initializers in VehicleData.cs ever drift from the balance table, a vehicle created that
/// way is silently born with the wrong upgrade curve and raises no error at all. A later developer
/// may find CreateInstance "cleaner" -- it is not. VehicleValidator.ValidateScriptDefaults watches
/// for that drift.
/// </para>
/// </remarks>
public static class VehicleCreationService {

    /// <summary>Folder the generated vehicle prefabs are written to.</summary>
    public const string PrefabFolder = "Assets/Prefabs/Vehicles";

    /// <summary>Folder the generated vehicle data assets are written to.</summary>
    public const string DataFolder = "Assets/ScriptableObjects";

    /// <summary>
    /// Makes a vehicle name safe to use as a file name.
    /// </summary>
    /// <param name="raw">The raw name entered by the user.</param>
    /// <returns>The name trimmed and stripped of characters that are invalid in file names.</returns>
    public static string SanitizeName(string raw) {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in raw.Trim()) {
            if (!invalid.Contains(c)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Returns the target prefab path for the given vehicle name.</summary>
    /// <param name="vehicleName">A sanitized vehicle name.</param>
    /// <returns>A project relative asset path.</returns>
    public static string GetPrefabPath(string vehicleName) {
        return PrefabFolder + "/" + vehicleName + ".prefab";
    }

    /// <summary>Returns the target data asset path for the given vehicle name.</summary>
    /// <param name="vehicleName">A sanitized vehicle name.</param>
    /// <returns>A project relative asset path.</returns>
    public static string GetDataPath(string vehicleName) {
        return DataFolder + "/" + vehicleName + "Data.asset";
    }

    /// <summary>
    /// Checks whether a request is valid before creation starts. Every Error here blocks creation
    /// and no asset is written.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <returns>The findings; an empty list when nothing is wrong.</returns>
    public static List<VehicleIssue> ValidateRequest(VehicleCreationRequest request) {
        var issues = new List<VehicleIssue>();

        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            issues.Add(VehicleIssue.Error("Vehicles cannot be created while in Play Mode. Exit Play Mode first."));
        }
        if (EditorApplication.isCompiling) {
            issues.Add(VehicleIssue.Error("Vehicles cannot be created while scripts are compiling. Wait for compilation to finish."));
        }

        if (request == null) {
            issues.Add(VehicleIssue.Error("The request is empty."));
            return issues;
        }

        if (request.templatePrefab == null) {
            issues.Add(VehicleIssue.Error("No template prefab selected."));
        }
        else {
            string templatePath = AssetDatabase.GetAssetPath(request.templatePrefab);
            if (string.IsNullOrEmpty(templatePath)) {
                issues.Add(VehicleIssue.Error("The template prefab is not an asset. A scene object cannot be used as a template."));
            }
            else {
                // If the template itself is broken, every vehicle made from it inherits the same flaw.
                foreach (var issue in VehicleValidator.ValidatePrefab(request.templatePrefab)) {
                    if (issue.severity == VehicleIssueSeverity.Error) {
                        issues.Add(VehicleIssue.Error("The template prefab fails validation: " + issue.message, issue.context));
                    }
                }
            }
        }

        if (request.draftData == null) {
            issues.Add(VehicleIssue.Error("There is no vehicle data draft. Select a template data asset."));
            return issues;
        }

        string name = SanitizeName(request.draftData.vehicleName);
        if (string.IsNullOrEmpty(name)) {
            issues.Add(VehicleIssue.Error("The vehicle name is empty. The save system matches vehicles by name, so a name is required."));
            return issues;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(GetPrefabPath(name)) != null) {
            issues.Add(VehicleIssue.Error("A prefab already exists at " + GetPrefabPath(name) + ". It will not be overwritten."));
        }
        if (AssetDatabase.LoadAssetAtPath<VehicleData>(GetDataPath(name)) != null) {
            issues.Add(VehicleIssue.Error("A data asset already exists at " + GetDataPath(name) + ". It will not be overwritten."));
        }

        foreach (var guid in AssetDatabase.FindAssets("t:VehicleData")) {
            var existing = AssetDatabase.LoadAssetAtPath<VehicleData>(AssetDatabase.GUIDToAssetPath(guid));
            if (existing == null || existing == request.draftData) continue;
            if (!string.IsNullOrWhiteSpace(existing.vehicleName) &&
                string.Equals(existing.vehicleName.Trim(), name, System.StringComparison.OrdinalIgnoreCase)) {
                issues.Add(VehicleIssue.Error("This vehicleName is already used by " + existing.name +
                    ". The save system would treat the two as one vehicle and their upgrades would mix.", existing));
            }
        }

        if (request.registerInGameManager &&
            AssetDatabase.LoadAssetAtPath<GameObject>(VehicleValidator.GameManagerPrefabPath) == null) {
            issues.Add(VehicleIssue.Error("GameManager prefab not found at " + VehicleValidator.GameManagerPrefabPath +
                ", so the vehicle cannot be registered."));
        }

        // The balance fields are validated before creation too, but the prefab does not exist yet
        // so only the data side is meaningful.
        foreach (var issue in VehicleValidator.ValidateData(request.draftData)) {
            bool aboutMissingPrefab = issue.message.Contains("empty vehiclePrefab");
            if (!aboutMissingPrefab) issues.Add(issue);
        }

        return issues;
    }

    /// <summary>
    /// Applies the request: clones the prefab, clones the data, links them together and, when
    /// asked, appends the vehicle to the registry on the GameManager prefab.
    /// </summary>
    /// <param name="request">The creation inputs.</param>
    /// <returns>What was created, which steps were taken and any findings.</returns>
    public static VehicleCreationResult Create(VehicleCreationRequest request) {
        var blocking = ValidateRequest(request).Where(i => i.severity == VehicleIssueSeverity.Error).ToList();
        if (blocking.Count > 0) {
            var result = VehicleCreationResult.Fail("Pre-creation validation failed. No file was created.");
            result.issues = blocking;
            return result;
        }

        string name = SanitizeName(request.draftData.vehicleName);
        string prefabPath = GetPrefabPath(name);
        string dataPath = GetDataPath(name);
        var outcome = new VehicleCreationResult();

        EnsureFolder(PrefabFolder);
        EnsureFolder(DataFolder);

        GameObject savedPrefab = ClonePrefab(request, name, prefabPath, outcome);
        if (savedPrefab == null) {
            return VehicleCreationResult.Fail("Could not save the prefab at " + prefabPath);
        }
        outcome.createdPrefab = savedPrefab;
        outcome.steps.Add("Created prefab: " + prefabPath);

        var newData = Object.Instantiate(request.draftData);
        newData.hideFlags = HideFlags.None;
        newData.name = name + "Data";
        newData.vehicleName = name;
        newData.vehiclePrefab = savedPrefab;

        // The draft was cloned from a template, so it carries the template's id. A fresh one is
        // generated here: two vehicles sharing an id would share one save record, and the player
        // would see upgrades bought for one appear on the other.
        newData.vehicleId = System.Guid.NewGuid().ToString("N");
        if (newData.vehicleIcon == null && request.bodySprite != null) {
            newData.vehicleIcon = request.bodySprite;
            outcome.steps.Add("Garage icon was empty, so the body sprite was used instead.");
        }
        AssetDatabase.CreateAsset(newData, dataPath);
        outcome.createdData = newData;
        outcome.steps.Add("Created vehicle data: " + dataPath + " (balance fields inherited from the template)");

        if (request.registerInGameManager) {
            if (RegisterInGameManager(newData, out string registerMessage)) {
                outcome.steps.Add(registerMessage);
            }
            else {
                outcome.issues.Add(VehicleIssue.Error(registerMessage, newData));
            }
        }
        else {
            outcome.issues.Add(VehicleIssue.Warning("The vehicle was not added to the registry, so it will not appear in the game.", newData));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        outcome.issues.AddRange(VehicleValidator.ValidateData(newData));
        outcome.success = !outcome.issues.Any(i => i.severity == VehicleIssueSeverity.Error);
        return outcome;
    }

    static GameObject ClonePrefab(VehicleCreationRequest request, string name, string prefabPath, VehicleCreationResult outcome) {
        string templatePath = AssetDatabase.GetAssetPath(request.templatePrefab);

        // LoadPrefabContents opens the prefab in an isolated scene and never touches the scene the
        // user has open. (InstantiatePrefab + DestroyImmediate would dirty the open scene.)
        GameObject root = PrefabUtility.LoadPrefabContents(templatePath);
        try {
            root.name = name;

            if (request.bodySprite != null) {
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr != null) {
                    sr.sprite = request.bodySprite;
                    outcome.steps.Add("Assigned body sprite: " + request.bodySprite.name);
                }
            }

            var spriteForCollider = request.bodySprite != null
                ? request.bodySprite
                : root.GetComponent<SpriteRenderer>() != null ? root.GetComponent<SpriteRenderer>().sprite : null;

            if (VehicleColliderFitter.Apply(root, spriteForCollider, request.colliderMode, out string colliderMessage)) {
                outcome.steps.Add(colliderMessage);
            }
            else if (!string.IsNullOrEmpty(colliderMessage)) {
                outcome.steps.Add(colliderMessage);
            }

            // Every player vehicle shares the low-friction, slightly bouncy contact material, whatever template it came from.
            var contactMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(VehicleValidator.PlayerContactMaterialPath);
            if (contactMaterial != null) {
                foreach (var body in root.GetComponentsInChildren<Rigidbody2D>(true)) body.sharedMaterial = contactMaterial;
                outcome.steps.Add("Assigned contact physics material: " + contactMaterial.name);
            }
            else {
                outcome.issues.Add(VehicleIssue.Warning("Contact physics material not found at " +
                    VehicleValidator.PlayerContactMaterialPath + "; the vehicle uses Unity's default friction.", null));
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
            if (!saved) return null;
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    /// <summary>
    /// Appends the vehicle to the END of the <c>allVehicles</c> array on the GameManager prefab.
    /// </summary>
    /// <remarks>
    /// The index is deliberately not selectable: <c>InitializeVehicles</c> treats
    /// <c>allVehicles[0]</c> as free and unconditionally unlocked, so inserting a new vehicle at
    /// the front would lock the Scooter and make the new vehicle free.
    /// <para>
    /// A single asset is written and no scene is opened. The GameManager in all three scenes is an
    /// instance of this prefab and carries no override on <c>allVehicles</c>, so the change
    /// propagates everywhere on its own.
    /// </para>
    /// </remarks>
    /// <param name="data">The vehicle data to register.</param>
    /// <param name="message">Summary of what was done, or why it could not be done.</param>
    /// <returns>True when the vehicle was registered.</returns>
    public static bool RegisterInGameManager(VehicleData data, out string message) {
        var gmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleValidator.GameManagerPrefabPath);
        if (gmPrefab == null) {
            message = "GameManager prefab not found at " + VehicleValidator.GameManagerPrefabPath;
            return false;
        }
        var gm = gmPrefab.GetComponent<GameManager>();
        if (gm == null) {
            message = "The GameManager prefab has no GameManager component.";
            return false;
        }

        var so = new SerializedObject(gm);
        var array = so.FindProperty("allVehicles");
        if (array == null || !array.isArray) {
            message = "Could not find the allVehicles array on GameManager.";
            return false;
        }

        for (int i = 0; i < array.arraySize; i++) {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == data) {
                message = "The vehicle is already registered at index " + i + "; it was not added again.";
                return true;
            }
        }

        int index = array.arraySize;
        array.arraySize = index + 1;
        array.GetArrayElementAtIndex(index).objectReferenceValue = data;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(gmPrefab);
        AssetDatabase.SaveAssets();

        message = "Registered as GameManager.allVehicles[" + index + "]. All three scenes read from this prefab; no scene was opened.";
        return true;
    }

    static void EnsureFolder(string folder) {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
