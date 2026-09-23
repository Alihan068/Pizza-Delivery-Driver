#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Focused integration acceptance for the staged editor preview. The preview type is resolved by
/// reflection because this test assembly cannot reference Unity's predefined editor assembly;
/// production traffic types remain statically checked by their own runtime assembly.
/// </summary>
public sealed class TrafficEditorPreviewSessionTests {
    Type sessionType;
    Type configurationType;
    Type bindingType;

    /// <summary>Finds the predefined editor-assembly type without making a runtime assembly depend on UnityEditor.</summary>
    [SetUp]
    public void SetUp() {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            sessionType = assembly.GetType("TrafficEditorPreviewSession");
            if (sessionType != null) break;
        }
        Assert.That(sessionType, Is.Not.Null, "Apply the S11.5 editor preview source before running this acceptance set.");
        configurationType = sessionType.GetNestedType("Configuration", BindingFlags.Public);
        bindingType = sessionType.GetNestedType("ProfilePrefabBinding", BindingFlags.Public);
        Assert.That(configurationType, Is.Not.Null);
        Assert.That(bindingType, Is.Not.Null);
    }

    /// <summary>Production body/follower/motor move in the isolated scene while the source draft and source profile remain frozen.</summary>
    [Test]
    public void PreviewMovesProductionComponentsWithoutMutatingSourceInputs() {
        var fixture = PreviewFixture.Create(configurationType, bindingType, 1f);
        object session = Activator.CreateInstance(sessionType);
        try {
            AssertInvalidProfileAndBodyBindings(session, fixture);
            Assert.That(Start(session, fixture.Configuration, out string issue), Is.True, issue);
            CivilianVehicleBody body = FirstBody(session);
            Assert.That(body, Is.Not.Null, "Validated fixture must materialize one civilian through TrafficManager.");
            Vector2 before = body.Body.position;
            for (int i = 0; i < 20; i++) Assert.That(Tick(session, 0.02f), Is.True);
            Assert.That(body.Body.position, Is.Not.EqualTo(before));
            Assert.That(JsonUtility.ToJson(fixture.Document), Is.EqualTo(fixture.DocumentBefore));
            Assert.That(fixture.Profile.motorSettings.cruiseSpeed, Is.EqualTo(fixture.ProfileCruiseBefore));
            Assert.That(JsonUtility.ToJson(fixture.Budget), Is.EqualTo(fixture.BudgetBefore));
            Assert.That(JsonUtility.ToJson(fixture.Rules), Is.EqualTo(fixture.RulesBefore));
        }
        finally { Stop(session); fixture.Destroy(); }
    }

    /// <summary>Manual ticking reaches the configured bound, then closes callbacks, render target, preview scene and shared services.</summary>
    [Test]
    public void PreviewDurationAutomaticallyStopsAndCleansEveryOwnedResource() {
        var fixture = PreviewFixture.Create(configurationType, bindingType, 0.06f);
        object session = Activator.CreateInstance(sessionType);
        try {
            Assert.That(Start(session, fixture.Configuration, out string issue), Is.True, issue);
            Assert.That(Get(session, "PreviewTexture"), Is.Not.Null);
            Assert.That(CameraUsesOwnedScene(session), Is.True);
            for (int i = 0; i < 8 && (bool)Get(session, "IsRunning"); i++) Tick(session, 0.02f);
            Assert.That(Get(session, "IsRunning"), Is.False);
            Assert.That(Get(session, "IsPreviewSceneOpen"), Is.False);
            Assert.That(Get(session, "PreviewTexture"), Is.Null);
            Assert.That(Get(session, "WereServicesClosed"), Is.True);
        }
        finally { Stop(session); fixture.Destroy(); }
    }

    /// <summary>Invalid edge settings reject before changing any serialized graph value or revision-bearing document content.</summary>
    [Test]
    public void InvalidEdgeSettingsRejectAtomically() {
        var fixture = PreviewFixture.Create(configurationType, bindingType, 1f);
        try {
            string before = JsonUtility.ToJson(fixture.Document);
            Assert.That(MapNavigationEditCommands.TryEditEdgeSettings(fixture.Document, "ab", 0f, 3f, out _), Is.False);
            Assert.That(JsonUtility.ToJson(fixture.Document), Is.EqualTo(before));
        }
        finally { fixture.Destroy(); }
    }

    bool Start(object session, object configuration, out string issue) {
        object[] arguments = { configuration, null };
        bool started = (bool)sessionType.GetMethod("Start").Invoke(session, arguments);
        issue = arguments[1] as string;
        return started;
    }

    bool Tick(object session, float deltaTime) => (bool)sessionType.GetMethod("TickEditorPreview").Invoke(session, new object[] { deltaTime });
    void Stop(object session) => sessionType.GetMethod("Stop").Invoke(session, null);
    object Get(object session, string property) => sessionType.GetProperty(property).GetValue(session);

    bool CameraUsesOwnedScene(object session) {
        var camera = (Camera)sessionType.GetField("previewCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        var scene = (Scene)sessionType.GetField("previewScene", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        return camera != null && scene.IsValid() && camera.scene == scene;
    }

    CivilianVehicleBody FirstBody(object session) {
        var live = Get(session, "LiveBodies") as IEnumerable;
        if (live == null) return null;
        foreach (object value in live) return value as CivilianVehicleBody;
        return null;
    }

    void AssertInvalidProfileAndBodyBindings(object session, PreviewFixture fixture) {
        PreviewFixture.Set(configurationType, fixture.Configuration, "profiles", new NpcVehicleProfile[] { null });
        Assert.That(Start(session, fixture.Configuration, out _), Is.False);
        PreviewFixture.Set(configurationType, fixture.Configuration, "profiles", new[] { fixture.Profile });

        Array bindings = (Array)configurationType.GetField("prefabCatalog").GetValue(fixture.Configuration);
        object binding = bindings.GetValue(0);
        FieldInfo prefab = bindingType.GetField("prefab");
        object validBody = prefab.GetValue(binding);
        prefab.SetValue(binding, null);
        Assert.That(Start(session, fixture.Configuration, out _), Is.False);
        prefab.SetValue(binding, validBody);
    }

    sealed class PreviewFixture {
        internal MapNavigationDocument Document;
        internal NpcVehicleProfile Profile;
        internal PopulationBudgetData Budget;
        internal RespawnTimingRules Rules;
        internal object Configuration;
        internal string DocumentBefore;
        internal float ProfileCruiseBefore;
        internal string BudgetBefore;
        internal string RulesBefore;
        Scene prefabScene;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        internal static PreviewFixture Create(Type configurationType, Type bindingType, float duration) {
            var fixture = new PreviewFixture();
            fixture.Document = new MapNavigationDocument { mapId = "preview-test", documentId = "preview-test", localBounds = new Rect(-5f, -5f, 30f, 30f) };
            fixture.Document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
            fixture.Document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 20f, y = 0f });
            fixture.Document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 20f, y = 20f });
            fixture.Document.nodes.Add(new RoadNodeRecord { nodeId = "d", x = 0f, y = 20f });
            fixture.Document.edges.Add(new RoadEdgeRecord { edgeId = "ab", fromNodeId = "a", toNodeId = "b", orderedPoints = new List<Vector2> { new Vector2(10f, 0f) }, usableWidth = 3f, speedLimit = 6f, allowedRoles = new List<VehicleRole> { VehicleRole.Civilian } });
            fixture.Document.edges.Add(new RoadEdgeRecord { edgeId = "bc", fromNodeId = "b", toNodeId = "c", usableWidth = 3f, speedLimit = 6f, allowedRoles = new List<VehicleRole> { VehicleRole.Civilian } });
            fixture.Document.edges.Add(new RoadEdgeRecord { edgeId = "cd", fromNodeId = "c", toNodeId = "d", usableWidth = 3f, speedLimit = 6f, allowedRoles = new List<VehicleRole> { VehicleRole.Civilian } });
            fixture.Document.edges.Add(new RoadEdgeRecord { edgeId = "da", fromNodeId = "d", toNodeId = "a", usableWidth = 3f, speedLimit = 6f, allowedRoles = new List<VehicleRole> { VehicleRole.Civilian } });
            fixture.Document.vehiclePools.Add(new VehiclePoolRecord { poolId = "pool", entries = new List<VehiclePoolEntry> { new VehiclePoolEntry { vehicleProfileId = "civilian", weight = 1f } } });
            fixture.Document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "loop", edgeIds = new List<string> { "ab", "bc", "cd", "da" }, loop = true, vehiclePoolId = "pool", targetCount = 1 });
            fixture.Document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "ab", distanceAlongEdge = 1f, routeId = "loop", role = VehicleRole.Civilian, clearanceWidth = 1f, clearanceLength = 2f });

            fixture.Profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
            fixture.Profile.vehicleProfileId = "civilian";
            fixture.Profile.visualCatalogId = "visual";
            fixture.Profile.allowedRoles.Add(VehicleRole.Civilian);
            fixture.Profile.colliderSize = new Vector2(1f, 2f);
            fixture.owned.Add(fixture.Profile);
            fixture.Budget = ScriptableObject.CreateInstance<PopulationBudgetData>();
            fixture.Budget.maxCivilianMoving = 1;
            fixture.Budget.maxTotalMoving = 1;
            fixture.Budget.maxTotalPhysicsObjects = 4;
            fixture.owned.Add(fixture.Budget);
            fixture.Rules = ScriptableObject.CreateInstance<RespawnTimingRules>();
            fixture.owned.Add(fixture.Rules);
            fixture.prefabScene = EditorSceneManager.NewPreviewScene();
            var prefabObject = EditorUtility.CreateGameObjectWithHideFlags("TrafficEditorPreviewFixture", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(prefabObject, fixture.prefabScene);
            Rigidbody2D fixtureBody = prefabObject.AddComponent<Rigidbody2D>();
            fixtureBody.gravityScale = 0f;
            fixtureBody.freezeRotation = true;
            prefabObject.AddComponent<BoxCollider2D>();
            CivilianVehicleBody body = prefabObject.AddComponent<CivilianVehicleBody>();
            fixture.owned.Add(prefabObject);

            Array bindings = Array.CreateInstance(bindingType, 1);
            object binding = Activator.CreateInstance(bindingType);
            bindingType.GetField("visualCatalogId").SetValue(binding, "visual");
            bindingType.GetField("prefab").SetValue(binding, body);
            bindings.SetValue(binding, 0);
            fixture.Configuration = Activator.CreateInstance(configurationType);
            Set(configurationType, fixture.Configuration, "draft", fixture.Document);
            Set(configurationType, fixture.Configuration, "profiles", new[] { fixture.Profile });
            Set(configurationType, fixture.Configuration, "prefabCatalog", bindings);
            Set(configurationType, fixture.Configuration, "budget", fixture.Budget);
            Set(configurationType, fixture.Configuration, "respawnRules", fixture.Rules);
            Set(configurationType, fixture.Configuration, "limits", new TrafficValidationLimits());
            Set(configurationType, fixture.Configuration, "selectedPoolId", "pool");
            Set(configurationType, fixture.Configuration, "durationSeconds", duration);
            Set(configurationType, fixture.Configuration, "fixedStep", 0.02f);
            Set(configurationType, fixture.Configuration, "maxStepsPerEditorUpdate", 4);
            Set(configurationType, fixture.Configuration, "physicsQueryCapacity", 8);
            Set(configurationType, fixture.Configuration, "renderWidth", 128);
            Set(configurationType, fixture.Configuration, "renderHeight", 72);
            Set(configurationType, fixture.Configuration, "cameraPadding", 1f);
            fixture.DocumentBefore = JsonUtility.ToJson(fixture.Document);
            fixture.ProfileCruiseBefore = fixture.Profile.motorSettings.cruiseSpeed;
            fixture.BudgetBefore = JsonUtility.ToJson(fixture.Budget);
            fixture.RulesBefore = JsonUtility.ToJson(fixture.Rules);
            return fixture;
        }

        internal void Destroy() {
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            if (prefabScene.IsValid()) EditorSceneManager.ClosePreviewScene(prefabScene);
            prefabScene = default;
        }

        internal static void Set(Type type, object target, string field, object value) => type.GetField(field).SetValue(target, value);
    }
}
#endif
