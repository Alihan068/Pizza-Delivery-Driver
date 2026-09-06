using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wizard for creating a new vehicle. Pick a template, edit the identity and balance fields in the
/// Inspector, and create the prefab plus data asset and register them with a single click.
/// </summary>
/// <remarks>
/// The balance fields are drawn by iterating a <see cref="SerializedObject"/>, so the field list is
/// never written out in code. When <see cref="VehicleData"/> gains a field the wizard shows it
/// automatically and nothing here needs updating.
/// </remarks>
public class VehicleCreatorWindow : EditorWindow {

    const string DefaultTemplatePrefabPath = "Assets/Prefabs/Vehicles/Scooter.prefab";
    const string DefaultTemplateDataPath = "Assets/ScriptableObjects/ScooterData.asset";

    [SerializeField] GameObject templatePrefab;
    [SerializeField] VehicleData templateData;
    [SerializeField] Sprite bodySprite;
    [SerializeField] VehicleColliderMode colliderMode = VehicleColliderMode.AutoFromSprite;
    [SerializeField] bool registerInGameManager = true;

    VehicleData draft;
    SerializedObject draftSerialized;
    List<VehicleIssue> cachedIssues = new List<VehicleIssue>();
    VehicleCreationResult lastResult;
    Vector2 scroll;
    bool showBalance = true;
    bool showCostPreview = true;

    /// <summary>Opens the vehicle creation wizard.</summary>
    [MenuItem("Tools/PizzaGame/Vehicle Creator", false, 10)]
    public static void ShowWindow() {
        var window = GetWindow<VehicleCreatorWindow>(false, "Vehicle Creator", true);
        window.minSize = new Vector2(460f, 560f);
        window.Show();
    }

    void OnEnable() {
        if (templatePrefab == null) templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTemplatePrefabPath);
        if (templateData == null) templateData = AssetDatabase.LoadAssetAtPath<VehicleData>(DefaultTemplateDataPath);
        RebuildDraft(string.Empty);
    }

    void OnDisable() {
        DestroyDraft();
    }

    void DestroyDraft() {
        draftSerialized = null;
        if (draft != null) {
            DestroyImmediate(draft);
            draft = null;
        }
    }

    void RebuildDraft(string keepName) {
        DestroyDraft();
        if (templateData == null) return;

        // Object.Instantiate copies the values stored ON DISK. Using
        // ScriptableObject.CreateInstance would bring in the C# field initializers from
        // VehicleData.cs, and if those ever drift the vehicle is silently born with the wrong curve.
        draft = Instantiate(templateData);
        draft.hideFlags = HideFlags.DontSave;
        draft.name = "New Vehicle (draft)";
        draft.vehicleName = keepName;
        // A new vehicle must not inherit the template's translated proper name.
        draft.displayNameKey = string.Empty;
        draft.vehiclePrefab = null;
        draft.vehicleIcon = null;
        draftSerialized = new SerializedObject(draft);
        RefreshValidation();
    }

    void RefreshValidation() {
        var issues = new List<VehicleIssue>();
        if (draft != null) {
            var request = BuildRequest();
            issues.AddRange(VehicleCreationService.ValidateRequest(request));
        }
        issues.AddRange(VehicleValidator.ValidateOpenScenes());
        cachedIssues = issues.OrderByDescending(i => (int)i.severity).ToList();
    }

    VehicleCreationRequest BuildRequest() {
        return new VehicleCreationRequest {
            templatePrefab = templatePrefab,
            draftData = draft,
            bodySprite = bodySprite,
            colliderMode = colliderMode,
            registerInGameManager = registerInGameManager
        };
    }

    void OnGUI() {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawTemplateSection();
        EditorGUILayout.Space();
        DrawAppearanceSection();
        EditorGUILayout.Space();
        DrawBalanceSection();
        EditorGUILayout.Space();
        DrawCostPreviewSection();
        EditorGUILayout.Space();
        DrawTargetSection();
        EditorGUILayout.Space();
        DrawValidationSection();
        EditorGUILayout.Space();
        DrawCreateButton();
        DrawLastResult();

        EditorGUILayout.EndScrollView();
    }

    void DrawTemplateSection() {
        EditorGUILayout.LabelField("1. Template", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The new vehicle is cloned from the selected template rather than built from scratch. Rigidbody " +
            "settings, the input map, audio clips, the navigation arrow and every balance field are inherited. " +
            "Pick GreenSedan as the template if you want headlights.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        templatePrefab = (GameObject)EditorGUILayout.ObjectField("Template prefab", templatePrefab, typeof(GameObject), false);
        var newTemplateData = (VehicleData)EditorGUILayout.ObjectField("Template data", templateData, typeof(VehicleData), false);
        bool templateDataChanged = newTemplateData != templateData;
        templateData = newTemplateData;

        if (EditorGUI.EndChangeCheck()) {
            if (templateDataChanged) RebuildDraft(draft != null ? draft.vehicleName : string.Empty);
            else RefreshValidation();
        }

        if (GUILayout.Button("Reset draft from template")) {
            RebuildDraft(string.Empty);
        }
    }

    void DrawAppearanceSection() {
        EditorGUILayout.LabelField("2. Appearance and collision", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        bodySprite = (Sprite)EditorGUILayout.ObjectField("Body sprite", bodySprite, typeof(Sprite), false);
        colliderMode = (VehicleColliderMode)EditorGUILayout.EnumPopup("Collider", colliderMode);
        if (EditorGUI.EndChangeCheck()) RefreshValidation();

        if (colliderMode == VehicleColliderMode.AutoFromSprite && bodySprite != null) {
            int shapes = bodySprite.GetPhysicsShapeCount();
            if (shapes <= 0) {
                EditorGUILayout.HelpBox(
                    "This sprite has no Custom Physics Shape, so the automatic mode will fall back to a rectangle " +
                    "built from its bounds. Draw a shape under Sprite Editor > Custom Physics Shape for a tighter fit.",
                    MessageType.Warning);
            }
            else {
                EditorGUILayout.HelpBox("Found " + shapes + " physics shape(s) on the sprite; the collider will be generated from them.", MessageType.Info);
            }
        }
    }

    void DrawBalanceSection() {
        showBalance = EditorGUILayout.Foldout(showBalance, "3. Identity and balance (every field is editable)", true, EditorStyles.foldoutHeader);
        if (!showBalance) return;

        if (draftSerialized == null || draft == null) {
            EditorGUILayout.HelpBox("Select a template data asset to see the fields.", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "Starting values come from the template asset, not from the script defaults. Change anything you " +
            "like; whatever you leave alone keeps the template's balance.",
            MessageType.None);

        draftSerialized.Update();
        EditorGUI.BeginChangeCheck();

        var iterator = draftSerialized.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren)) {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script") continue;
            if (iterator.propertyPath == "vehiclePrefab") {
                using (new EditorGUI.DisabledScope(true)) {
                    EditorGUILayout.TextField("Vehicle Prefab", "(linked automatically on creation)");
                }
                continue;
            }
            EditorGUILayout.PropertyField(iterator, true);
        }

        if (EditorGUI.EndChangeCheck()) {
            draftSerialized.ApplyModifiedPropertiesWithoutUndo();
            RefreshValidation();
        }
    }

    void DrawCostPreviewSection() {
        showCostPreview = EditorGUILayout.Foldout(showCostPreview, "4. Upgrade curve preview", true, EditorStyles.foldoutHeader);
        if (!showCostPreview) return;
        if (draft == null) return;

        var summaries = VehicleCostPreview.Build(draft);
        if (summaries == null) {
            EditorGUILayout.HelpBox("Could not read the cost constants from the GameManager prefab.", MessageType.Warning);
            return;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.miniBoldLabel)) {
            EditorGUILayout.LabelField("Stat", GUILayout.Width(90));
            EditorGUILayout.LabelField("Levels", GUILayout.Width(55));
            EditorGUILayout.LabelField("Base > Max", GUILayout.Width(130));
            EditorGUILayout.LabelField("First", GUILayout.Width(50));
            EditorGUILayout.LabelField("Total");
        }

        int grandTotal = 0;
        int levelTotal = 0;
        foreach (var s in summaries) {
            grandTotal += s.totalCost;
            levelTotal += s.maxLevel;
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(s.statName, GUILayout.Width(90));
                EditorGUILayout.LabelField(s.maxLevel.ToString(), GUILayout.Width(55));
                EditorGUILayout.LabelField(s.baseValue.ToString("0.##") + " > " + s.maxValue.ToString("0.##"), GUILayout.Width(130));
                EditorGUILayout.LabelField(s.firstCost.ToString(), GUILayout.Width(50));
                EditorGUILayout.LabelField(s.totalCost.ToString());
            }
        }

        EditorGUILayout.LabelField("Total: " + levelTotal + " levels, " + grandTotal + " currency" +
            (draft.price > 0 ? "  (+ " + draft.price + " vehicle price)" : string.Empty), EditorStyles.miniBoldLabel);
    }

    void DrawTargetSection() {
        EditorGUILayout.LabelField("5. Target files", EditorStyles.boldLabel);
        string name = draft != null ? VehicleCreationService.SanitizeName(draft.vehicleName) : string.Empty;
        if (string.IsNullOrEmpty(name)) {
            EditorGUILayout.HelpBox("Enter a vehicle name and the target paths will appear here.", MessageType.None);
            return;
        }
        using (new EditorGUI.DisabledScope(true)) {
            EditorGUILayout.TextField("Prefab", VehicleCreationService.GetPrefabPath(name));
            EditorGUILayout.TextField("Data", VehicleCreationService.GetDataPath(name));
        }

        EditorGUI.BeginChangeCheck();
        registerInGameManager = EditorGUILayout.Toggle("Add to registry", registerInGameManager);
        if (EditorGUI.EndChangeCheck()) RefreshValidation();

        EditorGUILayout.HelpBox(
            "Registration appends to the END of the allVehicles array on the GameManager prefab and opens no scene. " +
            "The GameManager in all three scenes is an instance of that prefab, so the vehicle shows up in all of them " +
            "on its own. Inserting at the front would change allVehicles[0] and lock the starting vehicle, which is why " +
            "the index cannot be chosen.",
            MessageType.None);
    }

    void DrawValidationSection() {
        using (new EditorGUILayout.HorizontalScope()) {
            EditorGUILayout.LabelField("6. Validation", EditorStyles.boldLabel);
            if (GUILayout.Button("Re-check", GUILayout.Width(140))) RefreshValidation();
        }
        VehicleIssueListDrawer.Draw(cachedIssues, "No blocking issues.");
    }

    void DrawCreateButton() {
        bool blocked = cachedIssues.Any(i => i.severity == VehicleIssueSeverity.Error);
        using (new EditorGUI.DisabledScope(blocked || draft == null)) {
            if (GUILayout.Button(blocked ? "Fix the errors first" : "Create Vehicle", GUILayout.Height(32))) {
                lastResult = VehicleCreationService.Create(BuildRequest());
                VehicleIssueListDrawer.LogToConsole(lastResult.issues, "[Vehicle Creator]");
                if (lastResult.success && lastResult.createdData != null) {
                    Selection.activeObject = lastResult.createdData;
                    EditorGUIUtility.PingObject(lastResult.createdData);
                }
                RefreshValidation();
            }
        }
    }

    void DrawLastResult() {
        if (lastResult == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last creation", EditorStyles.boldLabel);

        if (!lastResult.success && !string.IsNullOrEmpty(lastResult.failureReason)) {
            EditorGUILayout.HelpBox(lastResult.failureReason, MessageType.Error);
        }
        else if (lastResult.success) {
            EditorGUILayout.HelpBox("The vehicle was created and passed validation.", MessageType.Info);
        }

        foreach (var step in lastResult.steps) {
            EditorGUILayout.LabelField("- " + step, EditorStyles.wordWrappedMiniLabel);
        }
        if (lastResult.issues.Count > 0) {
            VehicleIssueListDrawer.Draw(lastResult.issues, string.Empty);
        }
    }
}
