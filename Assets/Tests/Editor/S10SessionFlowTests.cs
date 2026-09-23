using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Small S10 session-flow seams; fixtures use isolated preview scenes and save slots.</summary>
public sealed class S10SessionFlowTests {
    readonly List<Object> assets = new List<Object>();
    readonly List<string> savePaths = new List<string>();
    Scene scene;
    GameManager previousManager;
    float previousTimeScale;

    /// <summary>Creates an isolated preview scene and preserves shared session state.</summary>
    [SetUp]
    public void SetUp() {
        previousManager = GameManager.Instance;
        previousTimeScale = Time.timeScale;
        scene = EditorSceneManager.NewPreviewScene();
        GameManager.Instance = null;
    }

    /// <summary>Closes the preview scene, removes only fixture saves, and restores shared state.</summary>
    [TearDown]
    public void TearDown() {
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
        foreach (Object asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        foreach (string path in savePaths) if (File.Exists(path)) File.Delete(path);
        GameManager.Instance = previousManager;
        Time.timeScale = previousTimeScale;
    }

    /// <summary>Verifies one arrest settlement keeps 75 percent of income and charges repair from remaining health.</summary>
    [Test]
    public void ScoreHandler_ArrestEventSettlesOnceWithRemainingHealthAndIgnoresRepeat() {
        GameManager manager = Manager();
        manager.totalMoney = 100;
        GameObject player = Node("Player");
        player.AddComponent<Rigidbody2D>();
        var driver = player.AddComponent<Driver>();
        driver.maxHealth = 100f;
        driver.currentHealth = 63f;

        GameObject root = Node("Session");
        var director = root.AddComponent<PoliceDirector>();
        var score = root.AddComponent<ScoreHandler>();
        Invoke(score, "Start");
        score.AddMoney(40);

        int saves = 0;
        Action<bool> onSaved = success => { Assert.IsTrue(success); saves++; };
        GameManager.GameSaved += onSaved;
        try {
            var arrestEvent = (Action)typeof(PoliceDirector)
                .GetField("ArrestRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(director);
            Assert.IsNotNull(arrestEvent, "ScoreHandler must subscribe to the real PoliceDirector arrest event.");
            int expectedKeptIncome = SessionSettlementMath.CalculateKeptEarnings(40, EndReason.Arrested, 0.5f, 0.75f);
            int expectedRepair = manager.CalculateRepairCost(63f, 100f, false);
            Assert.AreEqual(30, expectedKeptIncome);
            Assert.AreEqual(26, expectedRepair);
            arrestEvent();
            Assert.AreEqual(104, manager.totalMoney,
                "Bank must be 100 + 30 retained income - 26 repair for 63/100 HP.");
            int bankAfterArrest = manager.totalMoney;
            arrestEvent();
            Assert.AreEqual(bankAfterArrest, manager.totalMoney);
        }
        finally { GameManager.GameSaved -= onSaved; }

        Assert.IsFalse(score.IsGameActive);
        Assert.AreEqual(1, saves, "Repeated arrest callbacks must not bank/save a second time.");
        Assert.AreEqual(63f, driver.currentHealth, 0.001f);
    }

    /// <summary>Verifies frozen normal and freeplay duration drafts reset modifier selection between sessions.</summary>
    [Test]
    public void SessionContexts_FreezeNormalAndFreeplayDurationsAndDropPreviousModifiers() {
        GameManager manager = Manager();
        var modifier = ScriptableObject.CreateInstance<ShiftModifierData>();
        assets.Add(modifier);
        modifier.modifierId = "s10-flow-modifier";
        modifier.scoreMultiplier = 0.8f;
        manager.allModifiers = new[] { modifier };

        int[] durations = { 3, 5, 10 };
        for (int i = 0; i < durations.Length; i++) {
            Assert.IsTrue(manager.SelectShiftDuration(durations[i]));
            manager.SelectModifiers(new[] { modifier.modifierId });
            TrafficSessionContext normal = manager.PrepareSession(scene);
            Assert.AreEqual(durations[i], normal.Draft.shiftDurationMinutes);
            Assert.AreEqual(1, normal.Modifiers.Ids.Count);
            manager.ReleaseSession(normal);
            manager.SelectModifiers(null);
            TrafficSessionContext next = manager.PrepareSession(scene);
            Assert.AreEqual(durations[i], next.Draft.shiftDurationMinutes);
            Assert.IsFalse(next.Draft.isFreeplay);
            Assert.IsEmpty(next.Modifiers.Ids);
            manager.ReleaseSession(next);
        }

        for (int i = 0; i < durations.Length; i++) {
            var draft = new SessionSetupDraft("freeplay-" + durations[i], "map", "vehicle", "", durations[i], true, null);
            var context = new TrafficSessionContext(draft);
            Assert.IsTrue(context.Draft.isFreeplay);
            Assert.AreEqual(durations[i], context.Draft.shiftDurationMinutes);
            Assert.IsEmpty(context.Modifiers.Ids);
        }
    }

    /// <summary>Verifies manually invoked session-local reset seams; production closure wiring is intentionally out of scope.</summary>
    [Test]
    public void SessionEnd_ResetSeamsClearContactPopulationHeatAndNewContextState() {
        var first = new TrafficSessionContext(new SessionSetupDraft("s10-first", "map", "vehicle", "", 5, false, null));
        var coordinator = new TrafficSessionCoordinator(first);
        coordinator.NotifyMapReady();
        coordinator.NotifyPlayerReady();
        var tracker = new PoliceContactTracker();
        tracker.Begin(Vector2.zero);
        tracker.QueueEnter(17);
        tracker.CommitStep(new[] { 17 }, Vector2.zero, Vector2.zero, 0.1f, 0.05f, 0.05f, 5f, false);
        Assert.AreEqual(1, tracker.ContactCount);

        var budget = ScriptableObject.CreateInstance<PopulationBudgetData>();
        assets.Add(budget);
        budget.maxCivilianMoving = 2;
        budget.maxPoliceMoving = 2;
        budget.maxTotalMoving = 4;
        budget.maxWreckSlots = 4;
        budget.maxTotalPhysicsObjects = 4;
        var population = new VehiclePopulationService(budget);
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Police));
        Assert.IsTrue(population.CommitSpawn(VehicleRole.Police));
        Assert.AreEqual(1, population.ActivePoliceCount);

        var directorData = ScriptableObject.CreateInstance<PoliceDirectorData>();
        assets.Add(directorData);
        directorData.profileId = "s10-flow-director";
        directorData.earliestPoliceTime = 0f;
        directorData.baseHeatPerActiveSecond = 1f;
        directorData.maximumHeat = 10f;
        directorData.heatTiers = new List<PoliceHeatTier> {
            new PoliceHeatTier {
                tierId = "s10", minimumHeat = 0f, targetCount = 1,
                compositions = new List<PoliceCompositionEntry> {
                    new PoliceCompositionEntry { vehicleProfileId = "vehicle", behaviorProfileId = "behavior", weight = 1f }
                }
            }
        };
        var runtime = new PoliceDirectorRuntime(directorData, true, 17);
        runtime.Tick(2f);
        Assert.Greater(runtime.EffectiveHeat, 0f);

        coordinator.NotifySessionEnded();
        tracker.End();
        population.ResetAll();
        runtime.End();

        var second = new TrafficSessionContext(new SessionSetupDraft("s10-second", "map", "vehicle", "", 5, false, null));
        var secondCoordinator = new TrafficSessionCoordinator(second);
        secondCoordinator.NotifyMapReady();
        secondCoordinator.NotifyPlayerReady();
        var freshTracker = new PoliceContactTracker();
        var freshPopulation = new VehiclePopulationService(budget);
        Assert.AreEqual(0, freshTracker.ContactCount);
        Assert.AreEqual(0, freshPopulation.ActivePoliceCount);
        Assert.IsFalse(second.Draft.isFreeplay);
        Assert.IsEmpty(second.Modifiers.Ids);
        Assert.IsTrue(secondCoordinator.IsActive);
        Assert.IsFalse(runtime.IsEnabled);
    }

    GameManager Manager() {
        var node = Node("GameManager");
        node.SetActive(false);
        var manager = node.AddComponent<GameManager>();
        manager.currentVehicle = Asset<VehicleData>();
        manager.currentVehicle.vehicleId = "s10-vehicle";
        var config = Asset<GameConfig>();
        config.saveFilePattern = "s10-session-flow-" + Guid.NewGuid().ToString("N") + "-{0}.json";
        var saves = new SaveSlotService(config, value => value);
        savePaths.Add(saves.GetPath(0));
        savePaths.Add(saves.GetPath(0) + config.backupSuffix);
        savePaths.Add(saves.GetPath(0) + ".tmp");
        Set(manager, "config", config);
        Set(manager, "<Saves>k__BackingField", saves);
        GameManager.Instance = manager;
        return manager;
    }

    T Asset<T>() where T : ScriptableObject {
        T asset = ScriptableObject.CreateInstance<T>();
        assets.Add(asset);
        return asset;
    }

    GameObject Node(string name) {
        var node = new GameObject(name);
        SceneManager.MoveGameObjectToScene(node, scene);
        return node;
    }

    static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    static void Invoke(object target, string method) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
