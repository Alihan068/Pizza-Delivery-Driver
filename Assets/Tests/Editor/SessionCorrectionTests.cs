using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Isolated C01/C05/C06 regressions; never edits or saves an authored scene or career profile.</summary>
public sealed class SessionCorrectionTests {
    readonly List<Object> assets = new List<Object>();
    readonly List<string> savePaths = new List<string>();
    Scene scene;
    Scene previousScene;
    GameManager previousManager;
    float previousTimeScale;

    /// <summary>Creates an empty additive fixture scene and retains the owner's global state for restoration.</summary>
    [SetUp]
    public void SetUp() {
        previousScene = SceneManager.GetActiveScene();
        previousManager = GameManager.Instance;
        previousTimeScale = Time.timeScale;
        scene = EditorSceneManager.NewPreviewScene();
        GameManager.Instance = null;
    }

    /// <summary>Closes only the fixture scene and removes only uniquely named fixture save files.</summary>
    [TearDown]
    public void TearDown() {
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
        foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        assets.Clear();
        foreach (string path in savePaths) if (File.Exists(path)) File.Delete(path);
        savePaths.Clear();
        GameManager.Instance = previousManager;
        Time.timeScale = previousTimeScale;
        if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
    }

    /// <summary>Player and separate civilian/police pools share one sequence, including reused slots.</summary>
    [Test]
    public void SharedRegistry_PoolReuseNeverCollidesWithPlayerOrOtherPool() {
        var coordinator = new TrafficSessionCoordinator(new TrafficSessionContext(
            new SessionSetupDraft("session", "map", "vehicle", "difficulty", 5, false, null)));
        coordinator.NotifyPlayerReady();
        var profile = Asset<NpcVehicleProfile>();
        profile.vehicleProfileId = "shared";
        profile.allowedRoles = new List<VehicleRole> { VehicleRole.Civilian, VehicleRole.Police };
        var budget = Asset<PopulationBudgetData>();
        budget.maxWreckSlots = 1;
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var civilians = new NpcVehiclePool(budget, catalog, coordinator.IdentityRegistry);
        var police = new NpcVehiclePool(budget, catalog, coordinator.IdentityRegistry);
        Assert.IsTrue(civilians.TryAcquire("shared", VehicleRole.Civilian, out var civilian));
        Assert.IsTrue(police.TryAcquire("shared", VehicleRole.Police, out var officer));
        int oldId = civilian.Identity.Value.lifeId;
        var ids = new HashSet<int> { coordinator.PlayerIdentity.Value.lifeId, oldId, officer.Identity.Value.lifeId };
        Assert.AreEqual(3, ids.Count);
        civilians.Release(civilian);
        Assert.IsTrue(civilians.TryAcquire("shared", VehicleRole.Civilian, out var reused));
        Assert.AreSame(civilian, reused);
        Assert.IsTrue(ids.Add(reused.Identity.Value.lifeId));
    }

    /// <summary>Frozen effects and their collection views survive destructive edits to source assets and lists.</summary>
    [Test]
    public void FrozenRules_KeepResolvedStatsFlagsScoreLightAndReadOnlyIds() {
        var modifier = Modifier();
        modifier.lightColor = Color.red;
        modifier.lightBlend = 0.4f;
        modifier.lightIntensityMultiplier = 0.7f;
        modifier.disablesCivilianTraffic = true;
        var source = new List<ShiftModifierData> { modifier };
        var frozen = new FrozenModifierRules(source);
        ModifierEffectResolver.GetCombinedLight(source, Color.blue, 2f, out var expectedColor, out float expectedIntensity);
        var draft = new SessionSetupDraft("session", "map", "car", "difficulty", 5, false, frozen.Ids);
        var context = new TrafficSessionContext(draft, frozen);
        SessionSceneRules.Freeze(context, true, "navigation");
        source.Clear();
        modifier.scoreMultiplier = 0f;
        modifier.speedDelta = 900f;
        modifier.disablesCivilianTraffic = false;
        modifier.lightColor = Color.green;
        modifier.lightIntensityMultiplier = 10f;
        frozen.ApplyLight(Color.blue, 2f, out var actualColor, out float actualIntensity);
        Assert.AreEqual(expectedColor, actualColor);
        Assert.AreEqual(expectedIntensity, actualIntensity, 0.0001f);
        Assert.AreEqual(0.5f, context.Snapshot.combinedScoreMultiplier);
        Assert.AreEqual(2f, context.Snapshot.Modifiers.GetStatDelta(VehicleStatId.Speed));
        Assert.IsFalse(context.Snapshot.trafficEnabled);
        Assert.IsTrue(context.Snapshot.policeEnabled);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)frozen.Ids)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)draft.selectedModifierIds)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)context.Snapshot.resolvedModifierIds)[0] = "changed");
        context.Integrity.MarkInvalid("test");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)context.Integrity.InvalidationReasons).Clear());
    }

    /// <summary>Either readiness order produces exactly one active session and one player registration.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public void Host_ReadinessOrderAndDuplicateNotificationsAreSafe(bool playerFirst) {
        var host = Host(ValidMap());
        var player = Node();
        int activated = 0;
        int ready = 0;
        host.Activated += () => activated++;
        host.PlayerReady += value => { Assert.AreSame(player, value); ready++; };
        if (playerFirst) host.RegisterPlayer(player);
        host.PrepareSession(null);
        if (!playerFirst) {
            Assert.IsFalse(host.IsActive);
            host.RegisterPlayer(player);
        }
        host.RegisterPlayer(player);
        host.PrepareSession(null);
        Assert.IsTrue(host.IsActive);
        Assert.AreEqual(1, activated);
        Assert.AreEqual(1, ready);
        Assert.IsFalse(host.Coordinator.Context.Integrity.IsValid);
        Assert.AreEqual("navigation", host.Coordinator.Context.Snapshot.navigationDocumentId);
        host.EndSession();
        host.RegisterPlayer(Node());
        host.Coordinator.NotifyMapReady();
        Assert.IsFalse(host.IsActive);
        Assert.AreEqual(1, activated);
    }

    /// <summary>Missing or mismatched explicit bindings cannot silently adopt GameManager.currentMap.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Host_InvalidExplicitBindingNeverFallsBackToSelectedMap(bool mismatch) {
        var manager = Manager();
        manager.currentMap = ValidMap();
        var wrongMap = mismatch ? ValidMap() : null;
        if (wrongMap != null) wrongMap.sceneName = "differentScene";
        var host = Host(wrongMap);
        host.PrepareSession(manager);
        host.RegisterPlayer(Node());
        Assert.IsFalse(host.IsActive);
        Assert.IsNull(host.Navigation);
        Assert.IsFalse(host.Coordinator.Context.Integrity.IsValid);
        Assert.IsFalse(host.Coordinator.Context.Snapshot.trafficEnabled);
        if (!mismatch) Assert.IsEmpty(host.Coordinator.Context.Draft.mapId);
        var score = Node().AddComponent<ScoreHandler>();
        Invoke(score, "Start");
        Assert.IsFalse(score.IsGameActive);
    }

    /// <summary>A matching map without traffic data keeps legacy scoring active and traffic disabled.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void LegacyScene_WithoutTrafficSupportStillStartsScoring(bool authoredHost) {
        var manager = Manager();
        manager.currentMap = ValidMap();
        manager.currentMap.trafficMapData = null;
        if (authoredHost) Host(manager.currentMap);
        var score = Node().AddComponent<ScoreHandler>();
        Invoke(score, "Start");
        Assert.IsTrue(score.IsGameActive);
        Assert.IsFalse(manager.ActiveSession.Snapshot.trafficEnabled);
        Assert.IsFalse(manager.ActiveSession.Snapshot.policeEnabled);
    }

    /// <summary>Supported scoring waits for the player, ends pending traffic before saving, and settles the frozen HUD score.</summary>
    [Test]
    public void ScoreHandler_WaitsForHostAndEndsTrafficBeforeFrozenSettlement() {
        var manager = Manager();
        var modifier = Modifier();
        manager.allModifiers = new[] { modifier };
        manager.SelectModifier(modifier);
        var host = Host(ValidMap());
        var score = Node().AddComponent<ScoreHandler>();
        Invoke(score, "Start");
        Assert.IsFalse(score.IsGameActive);
        Assert.IsFalse(manager.HasInterruptedShift);
        float speed = manager.GetEffectiveShiftSpeed();
        float health = manager.GetShiftStatValue(VehicleStatId.Health);
        host.RegisterPlayer(Node());
        Assert.IsTrue(score.IsGameActive);
        Assert.IsTrue(manager.HasInterruptedShift);
        score.AddScore(101);
        int expectedFinal = score.FinalScore;
        modifier.scoreMultiplier = 0f;
        modifier.speedDelta = -500f;
        modifier.healthDelta = -500f;
        manager.SelectModifier(null);
        manager.currentVehicle.baseHealth = 1f;
        Assert.AreEqual(speed, manager.GetEffectiveShiftSpeed());
        Assert.AreEqual(health, manager.GetShiftStatValue(VehicleStatId.Health));
        Assert.AreEqual(expectedFinal, score.FinalScore);
        var population = new VehiclePopulationService(Asset<PopulationBudgetData>());
        var gate = new TrafficSessionLifecycleGate(host.Coordinator, population);
        Assert.IsTrue(gate.TryReserveSpawn(VehicleRole.Civilian));
        bool ended = false;
        host.Ended += () => { ended = true; Assert.IsTrue(manager.HasInterruptedShift); };
        int saves = 0;
        Action<bool> onSave = success => {
            Assert.IsTrue(success);
            Assert.IsTrue(ended);
            Assert.IsFalse(gate.IsAttachedToSession);
            Assert.AreEqual(0, population.PendingCivilianCount);
            saves++;
        };
        GameManager.GameSaved += onSave;
        try { score.EndLevel(EndReason.TimeUp, "fixture"); }
        finally { GameManager.GameSaved -= onSave; }
        Assert.AreEqual(1, saves);
        Assert.AreEqual(expectedFinal, manager.bestShiftScore);
        Assert.AreEqual(expectedFinal, score.FinalScore);
        Assert.IsFalse(host.IsActive);
        Assert.IsFalse(manager.HasInterruptedShift);
    }

    /// <summary>PlayerSpawner captures the same session rules before it instantiates or emits the player event.</summary>
    [Test]
    public void PlayerSpawner_PreparesFrozenRulesBeforePlayerSpawned() {
        var manager = Manager();
        var modifier = Modifier();
        manager.allModifiers = new[] { modifier };
        manager.SelectModifier(modifier);
        manager.currentVehicle.vehiclePrefab = Node(false);
        var host = Host(ValidMap());
        var spawner = Node().AddComponent<PlayerSpawner>();
        GameObject spawned = null;
        spawner.PlayerSpawned += player => {
            spawned = player;
            Assert.IsNotNull(manager.ActiveSession.Snapshot);
            Assert.AreSame(manager.ActiveSession, host.Coordinator.Context);
            modifier.speedDelta = -900f;
            Assert.AreEqual(2f, manager.ActiveSession.Snapshot.Modifiers.GetStatDelta(VehicleStatId.Speed));
        };
        LogAssert.Expect(LogType.Error, "'Virtual Camera' slotu is emtpy in spawnObject");
        Invoke(spawner, "Start");
        Assert.AreSame(spawned, host.Player);
        Assert.IsTrue(host.IsActive);
    }

    /// <summary>Releasing an old context does not erase a newer capture or retain the previous modifiers.</summary>
    [Test]
    public void SessionRelease_RecapturesRulesAndIgnoresStaleCleanup() {
        var manager = Manager();
        var modifier = Modifier();
        manager.allModifiers = new[] { modifier };
        manager.SelectModifier(modifier);
        var first = manager.PrepareSession(scene);
        manager.ReleaseSession(first);
        manager.SelectModifier(null);
        var second = manager.PrepareSession(scene);
        manager.ReleaseSession(first);
        Assert.AreSame(second, manager.ActiveSession);
        Assert.AreEqual(1f, second.Modifiers.ScoreMultiplier);
        Assert.AreEqual(0f, second.Modifiers.GetStatDelta(VehicleStatId.Speed));
        Assert.AreNotEqual(first.SessionId, second.SessionId);
    }

    T Asset<T>() where T : ScriptableObject {
        var asset = ScriptableObject.CreateInstance<T>();
        assets.Add(asset);
        return asset;
    }

    GameObject Node(bool active = true) {
        var node = new GameObject("SessionFixture");
        node.SetActive(active);
        SceneManager.MoveGameObjectToScene(node, scene);
        return node;
    }

    GameManager Manager() {
        var manager = Node(false).AddComponent<GameManager>();
        manager.currentVehicle = Asset<VehicleData>();
        manager.currentVehicle.vehicleId = "vehicle";
        var config = Asset<GameConfig>();
        config.saveFilePattern = "session-correction-" + Guid.NewGuid().ToString("N") + "-{0}.json";
        var saves = new SaveSlotService(config, value => value);
        savePaths.Add(saves.GetPath(0));
        savePaths.Add(saves.GetPath(0) + config.backupSuffix);
        savePaths.Add(saves.GetPath(0) + ".tmp");
        Set(manager, "config", config);
        Set(manager, "<Saves>k__BackingField", saves);
        GameManager.Instance = manager;
        return manager;
    }

    ShiftModifierData Modifier() {
        var modifier = Asset<ShiftModifierData>();
        modifier.modifierId = "modifier";
        modifier.scoreMultiplier = 0.5f;
        modifier.speedDelta = 2f;
        modifier.healthDelta = 20f;
        return modifier;
    }

    MapData ValidMap() {
        var map = Asset<MapData>();
        map.mapId = "map";
        map.sceneName = scene.name;
        map.trafficMapData = Asset<TrafficMapData>();
        var document = new MapNavigationDocument { mapId = map.mapId, documentId = "navigation", localBounds = new Rect(-10, -10, 20, 20) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0, y = 0 });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0, y = 5 });
        document.edges.Add(new RoadEdgeRecord { edgeId = "ab", fromNodeId = "a", toNodeId = "b", usableWidth = 2, speedLimit = 5,
            allowedRoles = new List<VehicleRole> { VehicleRole.Civilian, VehicleRole.Police } });
        Set(map.trafficMapData, "document", document);
        return map;
    }

    TrafficSessionHost Host(MapData map) {
        var host = Node().AddComponent<TrafficSessionHost>();
        Set(host, "map", map);
        return host;
    }

    static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void Invoke(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
