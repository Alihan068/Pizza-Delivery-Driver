using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused EditMode coverage for replay identity retention and safe failure behavior.</summary>
public sealed class SessionReplayTests {
    readonly List<Object> objects = new List<Object>();
    GameManager manager;
    MapData staleMap;
    VehicleData staleVehicle;

    [SetUp]
    public void SetUp() {
        manager = new GameObject("ReplayTestGameManager").AddComponent<GameManager>();
        staleMap = Asset<MapData>();
        staleMap.mapId = "stale-map";
        staleMap.sceneName = "NarrowDistrict";
        staleVehicle = Asset<VehicleData>();
        staleVehicle.vehicleId = "stale-vehicle";
        manager.currentMap = staleMap;
        manager.currentVehicle = staleVehicle;
        GameManager.Instance = manager;
    }

    [TearDown]
    public void TearDown() {
        GameManager.Instance = null;
        if (manager != null) Object.DestroyImmediate(manager.gameObject);
        foreach (Object item in objects) if (item != null) Object.DestroyImmediate(item);
    }

    /// <summary>Verifies replay identity remains detached from later selection changes.</summary>
    [Test]
    public void ReplayLastSession_RestoresFrozenMapBuildTierModifiersAndDuration() {
        var replayMap = Asset<MapData>();
        replayMap.mapId = "replay-map";
        replayMap.sceneName = "NarrowDistrict";
        var replayVehicle = Asset<VehicleData>();
        replayVehicle.vehicleId = "replay-vehicle";
        var modifier = Asset<ShiftModifierData>();
        modifier.modifierId = "replay-modifier";
        manager.allModifiers = new[] { modifier };

        string[] selectedModifierIds = { modifier.modifierId };
        var draft = new SessionSetupDraft("completed", replayMap.mapId, replayVehicle.vehicleId,
            "tier-2", 7, false, selectedModifierIds);
        var context = new TrafficSessionContext(draft, new FrozenModifierRules(new[] { modifier }));
        context.FreezeSnapshot(new SessionRulesSnapshot(draft.sessionId, draft.mapId, draft.vehicleId,
            draft.difficultyId, draft.shiftDurationMinutes, draft.isFreeplay, 0,
            context.Modifiers.Ids, context.Modifiers.ScoreMultiplier, true, true, "navigation",
            context.Modifiers));

        context.SetPhase(TrafficSessionPhase.Ended);
        Set(manager, "<ActiveSession>k__BackingField", context);
        var registry = new ContentRegistry();
        registry.AddProvider(new BuiltInContentProvider(new[] { replayVehicle }, new[] { replayMap }));
        registry.Rebuild();
        Set(manager, "<Content>k__BackingField", registry);
        manager.allModifiers = new[] { modifier };

        selectedModifierIds[0] = "changed-after-capture";
        manager.currentMap = staleMap;
        manager.currentVehicle = staleVehicle;
        Assert.IsTrue(manager.ReplayLastSession());
        Assert.AreSame(replayMap, manager.currentMap);
        Assert.AreSame(replayVehicle, manager.currentVehicle);
        Assert.AreEqual("tier-2", manager.currentDifficultyId);
        Assert.AreEqual(7, manager.SelectedShiftDurationMinutes);
        CollectionAssert.AreEqual(new[] { modifier.modifierId }, manager.SelectedModifierIds);
        Assert.IsFalse(manager.ReplayLastSession(), "The same completed session must accept only one replay request.");
    }

    /// <summary>Verifies an unavailable frozen map cannot overwrite the current selection or load a stale map.</summary>
    [Test]
    public void ReplayLastSession_MissingFrozenMapFailsWithoutMutatingSelection() {
        var vehicle = Asset<VehicleData>();
        vehicle.vehicleId = "completed-vehicle";
        var draft = new SessionSetupDraft("completed", "missing-map", vehicle.vehicleId,
            "tier", 5, false, null);
        var context = new TrafficSessionContext(draft);
        context.FreezeSnapshot(new SessionRulesSnapshot(draft.sessionId, draft.mapId, draft.vehicleId,
            draft.difficultyId, draft.shiftDurationMinutes, draft.isFreeplay, 0, null, 1f, true, true, "navigation"));
        context.SetPhase(TrafficSessionPhase.Ended);
        Set(manager, "<ActiveSession>k__BackingField", context);
        var registry = new ContentRegistry();
        registry.AddProvider(new BuiltInContentProvider(new[] { vehicle }, new[] { staleMap }));
        registry.Rebuild();
        Set(manager, "<Content>k__BackingField", registry);

        Assert.IsFalse(manager.ReplayLastSession());
        Assert.AreSame(staleMap, manager.currentMap);
        Assert.AreSame(staleVehicle, manager.currentVehicle);
    }

    T Asset<T>() where T : ScriptableObject {
        T asset = ScriptableObject.CreateInstance<T>();
        objects.Add(asset);
        return asset;
    }

    static void Set(object target, string field, object value) {
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
