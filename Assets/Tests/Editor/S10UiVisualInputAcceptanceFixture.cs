using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Staged EditMode acceptance fixture for the production map-selection and result canvases.</summary>
/// <remarks>
/// Opens only MapSelectionScene and NarrowDistrict. Typed components are cloned through their Canvas
/// ancestors into an unsaved preview scene. GameScene, GameManager.Awake, save/apply APIs, assets,
/// scenes, preferences, and physical hardware input are outside this fixture's scope. ExecuteEvents
/// are synthetic pointer, submit, select, and move events.
/// </remarks>
public sealed class S10UiVisualInputAcceptanceFixture {
    const string MapSelectionScenePath = "Assets/Scenes/MapSelectionScene.unity";
    const string NarrowDistrictScenePath = "Assets/Scenes/NarrowDistrict.unity";
    const string CatalogPath = "Assets/ScriptableObjects/Localization/Catalog.asset";
    const string EvidenceDirectory = "memory-bank/traffic-police/evidence/s10-ui-proof";

    readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
    readonly List<Scene> openedSourceScenes = new List<Scene>();
    Scene previewScene;
    GameManager previousGameManager;
    LocalizationManager previousLocalizationManager;
    Delegate previousLanguageChanged;
    GameManager isolatedManager;
    LocalizationManager isolatedLocalization;

    /// <summary>Captures live static state and creates isolated preview-only managers without changing the active user scene.</summary>
    [SetUp]
    public void SetUp() {
        previousGameManager = GameManager.Instance;
        previousLocalizationManager = LocalizationManager.Instance;
        previousLanguageChanged = GetStaticField(typeof(LocalizationManager), "LanguageChanged") as Delegate;
        Assert.That(previousGameManager, Is.Null, "Refusing to overwrite a live GameManager.Instance.");
        Assert.That(previousLocalizationManager, Is.Null, "Refusing to overwrite a live LocalizationManager.Instance.");
        Assert.That(previousLanguageChanged, Is.Null, "Refusing to overwrite live LanguageChanged subscribers.");
        previewScene = EditorSceneManager.NewPreviewScene();
        Assert.That(previewScene.IsValid(), Is.True);
        CreateIsolatedLocalization();
        CreateIsolatedGameManager();
    }

    /// <summary>Destroys every fixture-owned object and preview scene before restoring captured static state.</summary>
    [TearDown]
    public void TearDown() {
        try {
            if (isolatedLocalization != null) UnityEngine.Object.DestroyImmediate(isolatedLocalization.gameObject);
            if (isolatedManager != null) UnityEngine.Object.DestroyImmediate(isolatedManager.gameObject);
            for (int i = 0; i < ownedObjects.Count; i++)
                if (ownedObjects[i] != null) UnityEngine.Object.DestroyImmediate(ownedObjects[i]);
            ownedObjects.Clear();
            if (previewScene.IsValid() && previewScene.isLoaded) EditorSceneManager.ClosePreviewScene(previewScene);
            for (int i = 0; i < openedSourceScenes.Count; i++)
                if (openedSourceScenes[i].IsValid() && openedSourceScenes[i].isLoaded)
                    EditorSceneManager.ClosePreviewScene(openedSourceScenes[i]);
        }
        finally {
            GameManager.Instance = previousGameManager;
            SetStaticBackingField(typeof(LocalizationManager), "Instance", previousLocalizationManager);
            SetStaticField(typeof(LocalizationManager), "LanguageChanged", previousLanguageChanged);
        }
    }

    /// <summary>Exercises real map-selection input while proving preview selections cannot mutate the isolated manager before apply.</summary>
    [Test]
    public void MapSelection_UsesProductionCanvasAndSeparatesAppliedFromPreview() {
        MapSelectionPanel panel = CloneProductionCanvasComponent<MapSelectionPanel>(MapSelectionScenePath);
        Invoke(panel, "Start");
        ScrollRect modifierScroll = GetField<ScrollRect>(panel, "modifierScroll");
        ScrollRect mapScroll = GetField<ScrollRect>(panel, "mapBrowserScroll");
        Assert.That(modifierScroll, Is.Not.Null);
        Assert.That(mapScroll, Is.Not.Null);

        EventSystem eventSystem = CreateSyntheticEventSystem();
        RectTransform modifierContent = GetField<RectTransform>(panel, "modifierContent");
        Toggle[] toggles = modifierContent.GetComponentsInChildren<Toggle>(true);
        Assert.That(toggles.Length, Is.GreaterThanOrEqualTo(4));

        // Applied Peaceful is unsupported on map-a but remains removable.
        Assert.That(toggles[0].isOn, Is.True);
        Assert.That(toggles[0].interactable, Is.True);
        ExecutePointerClick(eventSystem, toggles[0]);
        Assert.That(GetField<List<string>>(panel, "previewModifierIds"), Is.Empty);
        toggles = modifierContent.GetComponentsInChildren<Toggle>(true);
        Assert.That(toggles[0].interactable, Is.False);
        Invoke(panel, "ChangeMap", 1);
        toggles = modifierContent.GetComponentsInChildren<Toggle>(true);
        Assert.That(toggles[0].interactable, Is.True);
        ExecutePointerClick(eventSystem, toggles[0]);
        ExecutePointerClick(eventSystem, toggles[1]);
        ExecutePointerClick(eventSystem, toggles[2]);
        Assert.That(GetField<List<string>>(panel, "previewModifierIds"), Has.Count.EqualTo(3));
        Assert.That(isolatedManager.SelectedModifierIds, Has.Count.EqualTo(1));
        Assert.That(isolatedManager.SelectedModifierIds, Has.Member("peaceful"));
        Assert.That(isolatedManager.currentMap.mapId, Is.EqualTo("map-without-traffic"));

        modifierScroll.verticalNormalizedPosition = 0f;
        ExecuteSelect(eventSystem, toggles[toggles.Length - 1].gameObject);
        ExecuteMove(eventSystem, toggles[toggles.Length - 1].gameObject, MoveDirection.Down);
        panel.Close();
        panel.Open();
        Assert.That(GetField<List<string>>(panel, "previewModifierIds"), Has.Count.EqualTo(1));
        Assert.That(GetField<int>(panel, "mapIndex"), Is.EqualTo(0));
        Assert.That(isolatedManager.SelectedModifierIds, Has.Count.EqualTo(1));
        Assert.That(isolatedManager.currentMap.mapId, Is.EqualTo("map-without-traffic"));
        CaptureCanvas(panel.GetComponentInParent<Canvas>(), "map-selection-en-long-last-row.png");
        InvokeLanguage("tr");
        CaptureCanvas(panel.GetComponentInParent<Canvas>(), "map-selection-tr-long-last-row.png");
        InvokeLanguage("en");

        Button nextDuration = GetField<Button>(panel, "nextSessionDurationButton");
        Assert.That(ReadPreviewDuration(panel), Is.EqualTo(3));
        ExecuteSubmit(eventSystem, nextDuration);
        Assert.That(ReadPreviewDuration(panel), Is.EqualTo(5));
        ExecutePointerClick(eventSystem, nextDuration);
        Assert.That(ReadPreviewDuration(panel), Is.EqualTo(10));
        Assert.That(isolatedManager.SelectedShiftDurationMinutes, Is.EqualTo(3));
    }

    /// <summary>Renders the production result canvas in a preview-only scene and preserves the frozen final score.</summary>
    [Test]
    public void SessionResult_UsesNarrowDistrictCanvasAndFrozenScore() {
        SessionResultPanel resultPanel = CloneProductionCanvasComponent<SessionResultPanel>(NarrowDistrictScenePath);
        Assert.That(resultPanel, Is.Not.Null);
        GameConfig config = ScriptableObject.CreateInstance<GameConfig>();
        ownedObjects.Add(config);
        SetField(isolatedManager, "config", config);
        SessionResult result = new SessionResult {
            rawScore = 400,
            scoreMultiplier = 0.15f,
            finalScore = 60,
            appliedModifierIds = new[] { "peaceful", "no-traffic", "long-name" }
        };
        Assert.That(SessionResultPanel.GetDisplayedFinalScore(result), Is.EqualTo(60));
        Invoke(resultPanel, "Awake");
        resultPanel.Show(result, 12, 1, 60, config.garageScene);
        CaptureCanvas(resultPanel.GetComponentInParent<Canvas>(), "narrowdistrict-session-result-raw-product-final.png");
    }

    void CreateIsolatedGameManager() {
        GameObject objectRoot = EditorUtility.CreateGameObjectWithHideFlags("S10 isolated GameManager", HideFlags.HideAndDontSave);
        objectRoot.SetActive(false);
        MoveToPreview(objectRoot);
        isolatedManager = objectRoot.AddComponent<GameManager>();
        MapData unsupportedMap = CreateMap("map-without-traffic", false);
        MapData supportedMap = CreateMap("map-with-traffic", true);
        isolatedManager.allMaps = new[] { unsupportedMap, supportedMap };
        isolatedManager.currentMap = unsupportedMap;
        isolatedManager.currentDifficultyId = "tier-1";
        isolatedManager.ownedMapIds.Add(unsupportedMap.mapId);
        isolatedManager.ownedMapIds.Add(supportedMap.mapId);
        isolatedManager.allModifiers = new[] {
            CreateModifier("peaceful", "s10.modifier.peaceful", false, true, 0.85f),
            CreateModifier("no-traffic", "s10.modifier.noTraffic", true, false, 0.85f),
            CreateModifier("long-name", "s10.modifier.longName", false, false, 0.85f),
            CreateModifier("removable-unsupported", "s10.modifier.unsupported", true, false, 0.85f)
        };
        ContentRegistry registry = new ContentRegistry();
        registry.AddProvider(new BuiltInContentProvider(null, isolatedManager.allMaps));
        registry.Rebuild();
        SetAutoProperty(isolatedManager, "Content", registry);
        isolatedManager.SelectModifiers(new[] { "peaceful" });
        isolatedManager.SelectShiftDuration(3);
        GameManager.Instance = isolatedManager;
    }

    void CreateIsolatedLocalization() {
        GameObject objectRoot = EditorUtility.CreateGameObjectWithHideFlags("S10 isolated LocalizationManager", HideFlags.HideAndDontSave);
        objectRoot.SetActive(false);
        MoveToPreview(objectRoot);
        isolatedLocalization = objectRoot.AddComponent<LocalizationManager>();
        LocalizationCatalog source = AssetDatabase.LoadAssetAtPath<LocalizationCatalog>(CatalogPath);
        Assert.That(source, Is.Not.Null);
        Assert.That(source.languages, Is.Not.Null);
        Assert.That(source.languages.Length, Is.GreaterThan(0));
        LocalizationCatalog catalog = UnityEngine.Object.Instantiate(source);
        ownedObjects.Add(catalog);
        catalog.languages = CloneLanguages(source.languages);
        catalog.defaultLanguage = FindLanguage(catalog.languages, "en");
        Assert.That(catalog.defaultLanguage, Is.Not.Null);
        SetField(isolatedLocalization, "catalog", catalog);
        for (int i = 0; i < catalog.languages.Length; i++) ownedObjects.Add(catalog.languages[i]);
        SetStaticBackingField(typeof(LocalizationManager), "Instance", isolatedLocalization);
        Invoke(isolatedLocalization, "ApplyLanguage", catalog.defaultLanguage);
    }

    T CloneProductionCanvasComponent<T>(string scenePath) where T : Component {
        Scene sourceScene = GetOrOpenSourceScene(scenePath);
        T sourceComponent = SessionSceneRules.ResolveComponent<T>(sourceScene, true);
        Assert.That(sourceComponent, Is.Not.Null);
        Canvas sourceCanvas = sourceComponent.GetComponentInParent<Canvas>(true);
        Assert.That(sourceCanvas, Is.Not.Null);
        GameObject clone = UnityEngine.Object.Instantiate(sourceCanvas.gameObject);
        MoveToPreview(clone);
        Assert.That(clone.scene, Is.EqualTo(previewScene));
        T cloneComponent = clone.GetComponentInChildren<T>(true);
        Assert.That(cloneComponent, Is.Not.Null);
        clone.SetActive(true);
        return cloneComponent;
    }

    Scene GetOrOpenSourceScene(string path) {
        Scene existing = SceneManager.GetSceneByPath(path);
        if (existing.IsValid() && existing.isLoaded) return existing;
        Scene opened = EditorSceneManager.OpenPreviewScene(path);
        openedSourceScenes.Add(opened);
        return opened;
    }

    EventSystem CreateSyntheticEventSystem() {
        GameObject objectRoot = EditorUtility.CreateGameObjectWithHideFlags("S10 synthetic EventSystem", HideFlags.HideAndDontSave, typeof(EventSystem));
        MoveToPreview(objectRoot);
        Assert.That(objectRoot.scene, Is.EqualTo(previewScene));
        return objectRoot.GetComponent<EventSystem>();
    }

    static void ExecutePointerClick(EventSystem eventSystem, Component target) {
        eventSystem.SetSelectedGameObject(target.gameObject);
        PointerEventData data = new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    static void ExecuteSubmit(EventSystem eventSystem, Component target) {
        ExecuteEvents.Execute(target.gameObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
    }

    static void ExecuteSelect(EventSystem eventSystem, GameObject target) {
        ExecuteEvents.Execute(target, new BaseEventData(eventSystem), ExecuteEvents.selectHandler);
    }

    static void ExecuteMove(EventSystem eventSystem, GameObject target, MoveDirection direction) {
        AxisEventData data = new AxisEventData(eventSystem) { moveDir = direction };
        ExecuteEvents.Execute(target, data, ExecuteEvents.moveHandler);
    }

    void CaptureCanvas(Canvas canvas, string fileName) {
        Assert.That(canvas, Is.Not.Null);
        Assert.That(canvas.gameObject.scene, Is.EqualTo(previewScene));
        RenderMode oldMode = canvas.renderMode;
        Camera oldCamera = canvas.worldCamera;
        GameObject cameraObject = null;
        RenderTexture texture = null;
        Texture2D image = null;
        RenderTexture oldActive = RenderTexture.active;
        try {
            cameraObject = EditorUtility.CreateGameObjectWithHideFlags("S10 isolated preview camera", HideFlags.HideAndDontSave);
            MoveToPreview(cameraObject);
            Assert.That(cameraObject.scene, Is.EqualTo(previewScene));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = previewScene;
            camera.orthographic = true;
            camera.orthographicSize = 540f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 1f);
            texture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            texture.Create();
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
            image.Apply();
            string directory = Path.Combine(Directory.GetCurrentDirectory(), EvidenceDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), image.EncodeToPNG());
        }
        finally {
            RenderTexture.active = oldActive;
            canvas.renderMode = oldMode;
            canvas.worldCamera = oldCamera;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (texture != null) {
                if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    int ReadPreviewDuration(MapSelectionPanel panel) {
        object[] args = { 0 };
        MethodInfo method = panel.GetType().GetMethod("TryGetPreviewDuration", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        Assert.That((bool)method.Invoke(panel, args), Is.True);
        return (int)args[0];
    }

    void InvokeLanguage(string code) {
        LocalizationCatalog catalog = GetField<LocalizationCatalog>(isolatedLocalization, "catalog");
        LanguageData language = FindLanguage(catalog.languages, code);
        Assert.That(language, Is.Not.Null);
        Invoke(isolatedLocalization, "ApplyLanguage", language);
    }

    void MoveToPreview(GameObject objectRoot) => SceneManager.MoveGameObjectToScene(objectRoot, previewScene);

    MapData CreateMap(string id, bool trafficSupported) {
        MapData map = ScriptableObject.CreateInstance<MapData>();
        ownedObjects.Add(map);
        map.mapId = id;
        map.displayNameKey = "s10.map.longName";
        map.descriptionKey = "s10.map.description";
        map.sceneName = "NarrowDistrict";
        LevelData level = ScriptableObject.CreateInstance<LevelData>();
        ownedObjects.Add(level);
        level.difficultyLevels.Add(new MapDifficultyData {
            difficultyId = "tier-1", displayNameKey = "s10.tier", descriptionKey = "s10.tier.description",
            orderMin = 1, orderMax = 2, rewardMultiplier = 1f
        });
        map.levelData = level;
        if (trafficSupported) {
            TrafficMapData traffic = ScriptableObject.CreateInstance<TrafficMapData>();
            ownedObjects.Add(traffic);
            map.trafficMapData = traffic;
        }
        return map;
    }

    ShiftModifierData CreateModifier(string id, string key, bool disablesTraffic, bool disablesPolice, float multiplier) {
        ShiftModifierData modifier = ScriptableObject.CreateInstance<ShiftModifierData>();
        ownedObjects.Add(modifier);
        modifier.modifierId = id;
        modifier.displayNameKey = key;
        modifier.descriptionKey = key;
        modifier.disablesCivilianTraffic = disablesTraffic;
        modifier.disablesPolice = disablesPolice;
        modifier.scoreMultiplier = multiplier;
        return modifier;
    }

    static LanguageData[] CloneLanguages(LanguageData[] source) {
        LanguageData[] result = new LanguageData[source.Length];
        for (int i = 0; i < source.Length; i++) {
            Assert.That(source[i], Is.Not.Null);
            result[i] = UnityEngine.Object.Instantiate(source[i]);
            LocalizationEntry[] oldEntries = source[i].entries ?? new LocalizationEntry[0];
            LocalizationEntry[] entries = new LocalizationEntry[oldEntries.Length + 8];
            for (int j = 0; j < oldEntries.Length; j++)
                entries[j] = new LocalizationEntry { key = oldEntries[j].key, text = oldEntries[j].text };
            string suffix = result[i].languageCode == "tr" ? " Uzun Harita Adı" : " Long Map Name";
            entries[oldEntries.Length + 0] = new LocalizationEntry { key = "s10.map.longName", text = suffix };
            entries[oldEntries.Length + 1] = new LocalizationEntry { key = "s10.map.description", text = "S10 preview description" };
            entries[oldEntries.Length + 2] = new LocalizationEntry { key = "s10.tier", text = "Tier" };
            entries[oldEntries.Length + 3] = new LocalizationEntry { key = "s10.tier.description", text = "Authored tier" };
            entries[oldEntries.Length + 4] = new LocalizationEntry { key = "s10.modifier.peaceful", text = "Peaceful" };
            entries[oldEntries.Length + 5] = new LocalizationEntry { key = "s10.modifier.noTraffic", text = "No Traffic" };
            entries[oldEntries.Length + 6] = new LocalizationEntry { key = "s10.modifier.longName", text = "A Very Long Modifier Name" };
            entries[oldEntries.Length + 7] = new LocalizationEntry { key = "s10.modifier.unsupported", text = "Unsupported Modifier" };
            result[i].entries = entries;
        }
        return result;
    }

    static LanguageData FindLanguage(LanguageData[] languages, string code) {
        for (int i = 0; i < languages.Length; i++)
            if (languages[i] != null && languages[i].languageCode == code) return languages[i];
        return null;
    }

    static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void SetAutoProperty(object target, string name, object value) =>
        SetField(target, "<" + name + ">k__BackingField", value);
    static object GetStaticField(Type type, string name) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void SetStaticField(Type type, string name, object value) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    static void SetStaticBackingField(Type type, string name, object value) =>
        type.GetField("<" + name + ">k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
}
