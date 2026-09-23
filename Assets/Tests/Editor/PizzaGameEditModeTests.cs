using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// EditMode coverage for the deterministic domain calculations that do not require a running
/// Unity scene. These tests protect economy, order state, rating, and save migration contracts.
/// </summary>
public class PizzaGameEditModeTests {

    /// <summary>Verifies that an order never accepts more pizzas than remain outstanding.</summary>
    [Test]
    public void CustomerOrder_ClampsDeliveryAndTracksRemaining() {
        var order = new CustomerOrder(3, 40f);

        order.RegisterDelivery(1);
        order.RegisterDelivery(9);

        Assert.AreEqual(3, order.deliveredPizzas);
        Assert.AreEqual(0, order.RemainingPizzas);
        Assert.IsTrue(order.IsComplete);
    }

    /// <summary>Verifies that distant customers gain wait time only beyond the free radius and never past the cap.</summary>
    [Test]
    public void LevelData_DistanceWaitBonusRespectsFreeRadiusAndCap() {
        var level = ScriptableObject.CreateInstance<LevelData>();
        level.waitDistanceFreeRadius = 20f;
        level.waitPerDistanceUnit = 0.5f;
        level.waitDistanceBonusCap = 30f;

        Assert.AreEqual(0f, level.GetDistanceWaitBonus(10f), 0.001f);
        Assert.AreEqual(0f, level.GetDistanceWaitBonus(20f), 0.001f);
        Assert.AreEqual(15f, level.GetDistanceWaitBonus(50f), 0.001f);
        Assert.AreEqual(30f, level.GetDistanceWaitBonus(500f), 0.001f);

        level.waitDistanceBonusCap = 0f;
        Assert.AreEqual(240f, level.GetDistanceWaitBonus(500f), 0.001f);

        Object.DestroyImmediate(level);
    }

    /// <summary>Verifies that two session drafts/snapshots never share the same modifier-id backing collection.</summary>
    [Test]
    public void SessionSetupDraft_TwoSessionsDoNotShareModifierCollection() {
        var sourceList = new List<string> { "night" };

        var draftA = new SessionSetupDraft("sessionA", "map1", "veh1", "diff1", 5, false, sourceList);
        var draftB = new SessionSetupDraft("sessionB", "map1", "veh1", "diff1", 5, false, sourceList);

        Assert.AreNotSame(draftA.selectedModifierIds, draftB.selectedModifierIds);

        sourceList.Add("rain");
        Assert.AreEqual(1, draftA.selectedModifierIds.Count);
        Assert.AreEqual(1, draftB.selectedModifierIds.Count);
    }

    /// <summary>Verifies that mutating the caller's list after freezing a snapshot never reaches the frozen copy.</summary>
    [Test]
    public void SessionRulesSnapshot_MutatingSourceAfterConstructionDoesNotLeak() {
        var sourceList = new List<string> { "busyHour" };
        var snapshot = new SessionRulesSnapshot("session1", "map1", "veh1", "diff1", 5, false, 42,
            sourceList, 0.5f, true, true, "nav1");

        sourceList.Clear();
        sourceList.Add("night");

        Assert.AreEqual(1, snapshot.resolvedModifierIds.Count);
        Assert.AreEqual("busyHour", snapshot.resolvedModifierIds[0]);
    }

    /// <summary>Verifies that freezing a snapshot a second time never replaces the first one, and that the phase cannot leave Ended.</summary>
    [Test]
    public void TrafficSessionContext_FreezesOnceAndPhaseCannotLeaveEnded() {
        var draft = new SessionSetupDraft("session1", "map1", "veh1", "diff1", 5, false, null);
        var context = new TrafficSessionContext(draft);

        var first = new SessionRulesSnapshot("session1", "map1", "veh1", "diff1", 5, false, 1,
            null, 1f, false, false, string.Empty);
        var second = new SessionRulesSnapshot("session1", "map1", "veh1", "diff1", 5, false, 2,
            null, 1f, true, true, "nav2");

        context.FreezeSnapshot(first);
        context.FreezeSnapshot(second);
        Assert.AreSame(first, context.Snapshot);

        context.SetPhase(TrafficSessionPhase.PlayerReady);
        context.SetPhase(TrafficSessionPhase.Active);
        context.SetPhase(TrafficSessionPhase.Ended);
        context.SetPhase(TrafficSessionPhase.Active);
        Assert.AreEqual(TrafficSessionPhase.Ended, context.Phase);
    }

    /// <summary>Verifies that the coordinator initializes exactly once regardless of player/map notification order.</summary>
    [Test]
    public void TrafficSessionCoordinator_InitializesOnceInEitherNotificationOrder() {
        var contextA = new TrafficSessionContext(new SessionSetupDraft("a", "map1", "veh1", "diff1", 5, false, null));
        var coordinatorA = new TrafficSessionCoordinator(contextA);
        int firedA = 0;
        coordinatorA.Initialized += () => firedA++;
        coordinatorA.NotifyPlayerReady();
        Assert.IsFalse(coordinatorA.IsInitialized);
        coordinatorA.NotifyMapReady();
        Assert.IsTrue(coordinatorA.IsInitialized);
        Assert.AreEqual(1, firedA);
        Assert.AreEqual(TrafficSessionPhase.Active, contextA.Phase);

        var contextB = new TrafficSessionContext(new SessionSetupDraft("b", "map1", "veh1", "diff1", 5, false, null));
        var coordinatorB = new TrafficSessionCoordinator(contextB);
        int firedB = 0;
        coordinatorB.Initialized += () => firedB++;
        coordinatorB.NotifyMapReady();
        Assert.IsFalse(coordinatorB.IsInitialized);
        coordinatorB.NotifyPlayerReady();
        Assert.IsTrue(coordinatorB.IsInitialized);
        Assert.AreEqual(1, firedB);
    }

    /// <summary>Verifies that duplicate notifications never trigger a second initialization or reassign the player identity.</summary>
    [Test]
    public void TrafficSessionCoordinator_DuplicateNotificationsDoNotReinitialize() {
        var context = new TrafficSessionContext(new SessionSetupDraft("a", "map1", "veh1", "diff1", 5, false, null));
        var coordinator = new TrafficSessionCoordinator(context);
        int fired = 0;
        coordinator.Initialized += () => fired++;

        coordinator.NotifyPlayerReady();
        var firstIdentity = coordinator.PlayerIdentity;
        coordinator.NotifyMapReady();
        coordinator.NotifyMapReady();
        coordinator.NotifyPlayerReady();

        Assert.AreEqual(1, fired);
        Assert.AreEqual(firstIdentity.Value.lifeId, coordinator.PlayerIdentity.Value.lifeId);
    }

    /// <summary>Verifies that a notification arriving after session end can never open or reinitialize the coordinator.</summary>
    [Test]
    public void TrafficSessionCoordinator_NotificationAfterEndNeverInitializes() {
        var context = new TrafficSessionContext(new SessionSetupDraft("a", "map1", "veh1", "diff1", 5, false, null));
        var coordinator = new TrafficSessionCoordinator(context);
        int fired = 0;
        coordinator.Initialized += () => fired++;

        coordinator.NotifySessionEnded();
        coordinator.NotifyMapReady();
        coordinator.NotifyPlayerReady();

        Assert.IsFalse(coordinator.IsInitialized);
        Assert.AreEqual(0, fired);
        Assert.AreEqual(TrafficSessionPhase.Ended, context.Phase);
        Assert.IsNull(coordinator.PlayerIdentity);
    }

    /// <summary>Verifies that the three pre-existing modifier assets were explicitly migrated to a neutral score and no capability flags.</summary>
    [Test]
    public void ShiftModifierData_ExistingAssetsMigratedToNeutralScore() {
        string[] paths = {
            "Assets/ScriptableObjects/Modifiers/Night.asset",
            "Assets/ScriptableObjects/Modifiers/Rain.asset",
            "Assets/ScriptableObjects/Modifiers/BusyHour.asset"
        };
        foreach (var path in paths) {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ShiftModifierData>(path);
            Assert.IsNotNull(asset, path + " should exist");
            Assert.AreEqual(1f, asset.scoreMultiplier, 0.0001f, path);
            Assert.IsFalse(asset.disablesCivilianTraffic, path);
            Assert.IsFalse(asset.disablesPolice, path);
        }
    }

    /// <summary>Verifies that NoTraffic disables only civilian traffic and Peaceful disables only police, each with its own permanent id.</summary>
    [Test]
    public void ShiftModifierData_NoTrafficAndPeacefulCarryExactlyOneFlagEach() {
        var noTraffic = UnityEditor.AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/NoTraffic.asset");
        var peaceful = UnityEditor.AssetDatabase.LoadAssetAtPath<ShiftModifierData>("Assets/ScriptableObjects/Modifiers/Peaceful.asset");
        Assert.IsNotNull(noTraffic);
        Assert.IsNotNull(peaceful);
        Assert.IsTrue(noTraffic.disablesCivilianTraffic);
        Assert.IsFalse(noTraffic.disablesPolice);
        Assert.IsTrue(peaceful.disablesPolice);
        Assert.IsFalse(peaceful.disablesCivilianTraffic);
        Assert.AreNotEqual(noTraffic.modifierId, peaceful.modifierId);
        Assert.IsFalse(string.IsNullOrEmpty(noTraffic.modifierId));
        Assert.IsFalse(string.IsNullOrEmpty(peaceful.modifierId));
    }

    /// <summary>Verifies that an invalid authored score multiplier is rejected rather than silently clamped.</summary>
    [Test]
    public void ShiftModifierData_HasValidScoreMultiplier_RejectsInvalidValues() {
        var modifier = ScriptableObject.CreateInstance<ShiftModifierData>();

        modifier.scoreMultiplier = 0.5f;
        Assert.IsTrue(modifier.HasValidScoreMultiplier());
        modifier.scoreMultiplier = 0f;
        Assert.IsTrue(modifier.HasValidScoreMultiplier());
        modifier.scoreMultiplier = 1f;
        Assert.IsTrue(modifier.HasValidScoreMultiplier());

        modifier.scoreMultiplier = -0.1f;
        Assert.IsFalse(modifier.HasValidScoreMultiplier());
        modifier.scoreMultiplier = 1.1f;
        Assert.IsFalse(modifier.HasValidScoreMultiplier());
        modifier.scoreMultiplier = float.NaN;
        Assert.IsFalse(modifier.HasValidScoreMultiplier());
        modifier.scoreMultiplier = float.PositiveInfinity;
        Assert.IsFalse(modifier.HasValidScoreMultiplier());

        Object.DestroyImmediate(modifier);
    }

    static ShiftModifierData MakeModifier(string id, string exclusiveGroup = "") {
        var modifier = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifier.modifierId = id;
        modifier.exclusiveGroup = exclusiveGroup;
        return modifier;
    }

    static bool ContainsId(IReadOnlyList<string> ids, string value) {
        for (int i = 0; i < ids.Count; i++) if (ids[i] == value) return true;
        return false;
    }

    /// <summary>Verifies empty, single, and multiple duplicate-free selections all resolve as expected.</summary>
    [Test]
    public void ModifierSelectionResolver_ResolvesEmptySingleAndMultipleSelections() {
        var a = MakeModifier("a");
        var b = MakeModifier("b");
        var c = MakeModifier("c");
        var catalog = new[] { a, b, c };

        var empty = ModifierSelectionResolver.Resolve(catalog, null);
        Assert.AreEqual(0, empty.resolvedModifiers.Count);
        Assert.AreEqual(0, empty.rejectedIds.Count);

        var single = ModifierSelectionResolver.Resolve(catalog, new[] { "b" });
        Assert.AreEqual(1, single.resolvedModifiers.Count);
        Assert.AreSame(b, single.resolvedModifiers[0]);

        var three = ModifierSelectionResolver.Resolve(catalog, new[] { "c", "a", "b" });
        Assert.AreEqual(3, three.resolvedModifiers.Count);
        Assert.AreSame(a, three.resolvedModifiers[0]);
        Assert.AreSame(b, three.resolvedModifiers[1]);
        Assert.AreSame(c, three.resolvedModifiers[2]);

        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
    }

    /// <summary>Verifies duplicate ids and unknown ids are rejected rather than silently accepted.</summary>
    [Test]
    public void ModifierSelectionResolver_RejectsDuplicateAndUnknownIds() {
        var a = MakeModifier("a");
        var catalog = new[] { a };

        var result = ModifierSelectionResolver.Resolve(catalog, new[] { "a", "a", "ghost" });
        Assert.AreEqual(1, result.resolvedModifiers.Count);
        Assert.AreSame(a, result.resolvedModifiers[0]);
        Assert.AreEqual(2, result.rejectedIds.Count);
        Assert.IsTrue(ContainsId(result.rejectedIds, "a"));
        Assert.IsTrue(ContainsId(result.rejectedIds, "ghost"));

        Object.DestroyImmediate(a);
    }

    /// <summary>Verifies that two modifiers sharing an exclusive group cannot both be selected, but different groups can combine.</summary>
    [Test]
    public void ModifierSelectionResolver_RejectsSecondModifierInSameExclusiveGroup() {
        var visualA = MakeModifier("visualA", "visual");
        var visualB = MakeModifier("visualB", "visual");
        var independent = MakeModifier("independent");
        var catalog = new[] { visualA, visualB, independent };

        var result = ModifierSelectionResolver.Resolve(catalog, new[] { "visualA", "visualB", "independent" });
        Assert.AreEqual(2, result.resolvedModifiers.Count);
        Assert.IsTrue(ContainsId(result.rejectedIds, "visualB"));
        bool hasVisualA = false;
        foreach (var m in result.resolvedModifiers) if (m == visualA) hasVisualA = true;
        Assert.IsTrue(hasVisualA);

        Object.DestroyImmediate(visualA); Object.DestroyImmediate(visualB); Object.DestroyImmediate(independent);
    }

    /// <summary>Verifies the resolved selection is canonical (sorted by id) regardless of requested order.</summary>
    [Test]
    public void ModifierSelectionResolver_ProducesOrderIndependentCanonicalResult() {
        var a = MakeModifier("a");
        var b = MakeModifier("b");
        var c = MakeModifier("c");
        var catalog = new[] { a, b, c };

        var orderOne = ModifierSelectionResolver.Resolve(catalog, new[] { "c", "b", "a" });
        var orderTwo = ModifierSelectionResolver.Resolve(catalog, new[] { "a", "c", "b" });

        Assert.AreEqual(orderOne.resolvedModifiers.Count, orderTwo.resolvedModifiers.Count);
        for (int i = 0; i < orderOne.resolvedModifiers.Count; i++)
            Assert.AreSame(orderOne.resolvedModifiers[i], orderTwo.resolvedModifiers[i]);

        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
    }

    /// <summary>Verifies that a single resolved modifier reproduces the exact pre-multi-select light/stat/score result.</summary>
    [Test]
    public void ModifierEffectResolver_SingleModifierMatchesLegacySingleModifierResult() {
        var modifier = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifier.modifierId = "single";
        modifier.speedDelta = 1.5f;
        modifier.lightColor = new Color(0.2f, 0.3f, 0.4f, 1f);
        modifier.lightBlend = 0.35f;
        modifier.lightIntensityMultiplier = 0.7f;
        modifier.scoreMultiplier = 0.6f;
        var list = new ShiftModifierData[] { modifier };
        var baseColor = Color.white;
        float baseIntensity = 1f;

        float delta = ModifierEffectResolver.GetCombinedStatDelta(list, VehicleStatId.Speed);
        Assert.AreEqual(1.5f, delta, 0.0001f);

        ModifierEffectResolver.GetCombinedLight(list, baseColor, baseIntensity, out Color resultColor, out float resultIntensity);
        Color expectedColor = Color.Lerp(baseColor, modifier.lightColor, Mathf.Clamp01(modifier.lightBlend));
        float expectedIntensity = baseIntensity * Mathf.Max(0.01f, modifier.lightIntensityMultiplier);
        Assert.AreEqual(expectedColor.r, resultColor.r, 0.0001f);
        Assert.AreEqual(expectedColor.g, resultColor.g, 0.0001f);
        Assert.AreEqual(expectedColor.b, resultColor.b, 0.0001f);
        Assert.AreEqual(expectedIntensity, resultIntensity, 0.0001f);

        Assert.AreEqual(0.6f, ModifierEffectResolver.GetCombinedScoreMultiplier(list), 0.0001f);

        Object.DestroyImmediate(modifier);
    }

    /// <summary>Verifies that an empty selection leaves light/stat/score exactly at scene-authored neutral values.</summary>
    [Test]
    public void ModifierEffectResolver_EmptySelectionIsNeutral() {
        var baseColor = new Color(0.1f, 0.2f, 0.3f, 1f);
        float baseIntensity = 1.2f;

        Assert.AreEqual(0f, ModifierEffectResolver.GetCombinedStatDelta(null, VehicleStatId.Speed), 0.0001f);
        Assert.AreEqual(1f, ModifierEffectResolver.GetCombinedScoreMultiplier(null), 0.0001f);
        Assert.IsFalse(ModifierEffectResolver.GetCombinedDisablesCivilianTraffic(null));
        Assert.IsFalse(ModifierEffectResolver.GetCombinedDisablesPolice(null));

        ModifierEffectResolver.GetCombinedLight(null, baseColor, baseIntensity, out Color resultColor, out float resultIntensity);
        Assert.AreEqual(baseColor, resultColor);
        Assert.AreEqual(baseIntensity, resultIntensity, 0.0001f);
    }

    /// <summary>Verifies stat/score combination is order-independent and disable flags combine with OR, not overwrite.</summary>
    [Test]
    public void ModifierEffectResolver_CombinesMultipleModifiersOrderIndependentlyWithOrFlags() {
        var civilianOff = ScriptableObject.CreateInstance<ShiftModifierData>();
        civilianOff.modifierId = "civOff";
        civilianOff.speedDelta = 1f;
        civilianOff.disablesCivilianTraffic = true;
        civilianOff.scoreMultiplier = 0.6f;

        var policeOff = ScriptableObject.CreateInstance<ShiftModifierData>();
        policeOff.modifierId = "polOff";
        policeOff.speedDelta = -0.5f;
        policeOff.disablesPolice = true;
        policeOff.scoreMultiplier = 0.5f;

        var forward = new[] { civilianOff, policeOff };
        var reverse = new[] { policeOff, civilianOff };

        Assert.AreEqual(0.5f, ModifierEffectResolver.GetCombinedStatDelta(forward, VehicleStatId.Speed), 0.0001f);
        Assert.AreEqual(0.5f, ModifierEffectResolver.GetCombinedStatDelta(reverse, VehicleStatId.Speed), 0.0001f);
        Assert.AreEqual(0.3f, ModifierEffectResolver.GetCombinedScoreMultiplier(forward), 0.0001f);
        Assert.AreEqual(0.3f, ModifierEffectResolver.GetCombinedScoreMultiplier(reverse), 0.0001f);
        Assert.IsTrue(ModifierEffectResolver.GetCombinedDisablesCivilianTraffic(forward));
        Assert.IsTrue(ModifierEffectResolver.GetCombinedDisablesPolice(forward));

        Object.DestroyImmediate(civilianOff); Object.DestroyImmediate(policeOff);
    }

    /// <summary>Verifies GameManager keeps the legacy single-selection field and the new multi-selection list synchronized.</summary>
    [Test]
    public void GameManager_SelectModifier_KeepsLegacyAndMultiSelectInSync() {
        var go = new GameObject("TestGameManager");
        go.SetActive(false); // keep Awake() (save I/O, singleton wiring) from running; SelectModifier needs none of it
        var manager = go.AddComponent<GameManager>();
        var modifierA = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifierA.modifierId = "syncTest";
        manager.allModifiers = new[] { modifierA };

        manager.SelectModifier(modifierA);
        Assert.AreSame(modifierA, manager.CurrentModifier);
        Assert.AreEqual(1, manager.SelectedModifierIds.Count);
        Assert.AreEqual("syncTest", manager.SelectedModifierIds[0]);

        manager.SelectModifier(null);
        Assert.IsNull(manager.CurrentModifier);
        Assert.AreEqual(0, manager.SelectedModifierIds.Count);

        Object.DestroyImmediate(modifierA);
        Object.DestroyImmediate(go);
    }

    /// <summary>Verifies the canonical worked example: 1000 raw times three coefficients (.6*.5*.5=.15) is 150, regardless of coefficient order.</summary>
    [Test]
    public void ModifierScoreRules_CanonicalTripleCoefficientExampleIs150() {
        Assert.AreEqual(150, ModifierScoreRules.ComputeFinalScore(1000, 0.6f * 0.5f * 0.5f));
        Assert.AreEqual(150, ModifierScoreRules.ComputeFinalScore(1000, 0.5f * 0.6f * 0.5f));
    }

    /// <summary>Verifies ten +1 events and one +10 event produce the same final score (multiplier applies once to the total, not per event).</summary>
    [Test]
    public void ModifierScoreRules_TenSmallEventsMatchOneLargeEventOfTheSameTotal() {
        int rawFromTenEvents = 0;
        for (int i = 0; i < 10; i++) rawFromTenEvents += 1;
        int rawFromOneEvent = 10;

        Assert.AreEqual(ModifierScoreRules.ComputeFinalScore(rawFromOneEvent, 0.5f), ModifierScoreRules.ComputeFinalScore(rawFromTenEvents, 0.5f));
        Assert.AreEqual(5, ModifierScoreRules.ComputeFinalScore(rawFromTenEvents, 0.5f));
    }

    /// <summary>Verifies a reward followed by a penalty nets to raw before the multiplier is applied: (100-20)*0.5=40.</summary>
    [Test]
    public void ModifierScoreRules_RewardThenPenaltyNetsBeforeMultiplier() {
        int raw = 100 + (-20);
        Assert.AreEqual(40, ModifierScoreRules.ComputeFinalScore(raw, 0.5f));
    }

    /// <summary>Verifies neutral/zero/negative-raw/overflow/invalid-multiplier edge cases never throw and never go negative.</summary>
    [Test]
    public void ModifierScoreRules_HandlesEmptyZeroNegativeOverflowAndInvalidMultiplier() {
        Assert.AreEqual(500, ModifierScoreRules.ComputeFinalScore(500, 1f)); // empty selection -> neutral 1
        Assert.AreEqual(0, ModifierScoreRules.ComputeFinalScore(500, 0f)); // zero coefficient
        Assert.AreEqual(0, ModifierScoreRules.ComputeFinalScore(-50, 1f)); // negative raw treated as zero
        Assert.AreEqual(int.MaxValue, ModifierScoreRules.ComputeFinalScore(int.MaxValue, 1f)); // overflow bound
        Assert.AreEqual(0, ModifierScoreRules.ComputeFinalScore(100, float.NaN));
        Assert.AreEqual(0, ModifierScoreRules.ComputeFinalScore(100, float.PositiveInfinity));
        Assert.AreEqual(0, ModifierScoreRules.ComputeFinalScore(100, -0.5f));
    }

    /// <summary>Verifies that SessionResult's raw/multiplier/final/modifier-id fields hold independent values with sane defaults.</summary>
    [Test]
    public void SessionResult_ScoreBreakdownFieldsAreIndependent() {
        var result = new SessionResult();
        Assert.AreEqual(0, result.rawScore);
        Assert.AreEqual(0f, result.scoreMultiplier);
        Assert.AreEqual(0, result.finalScore);
        Assert.IsNull(result.appliedModifierIds);

        result.rawScore = 1000;
        result.scoreMultiplier = 0.6f * 0.5f * 0.5f;
        result.finalScore = ModifierScoreRules.ComputeFinalScore(result.rawScore, result.scoreMultiplier);
        result.appliedModifierIds = new[] { "a", "b" };

        Assert.AreEqual(1000, result.rawScore);
        Assert.AreEqual(150, result.finalScore);
        Assert.AreEqual(2, result.appliedModifierIds.Length);
    }

    static bool InvokeIsSingleModifierSelectionApplied(GameManager manager, ShiftModifierData previewedModifier) {
        var method = typeof(MapSelectionPanel).GetMethod("IsSingleModifierSelectionApplied",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { manager, previewedModifier });
    }

    /// <summary>Verifies the old single-selection screen's applied-check compares by id set, not by object reference, against the authoritative multi-select state.</summary>
    [Test]
    public void MapSelectionPanel_IsSingleModifierSelectionApplied_ComparesByIdNotReference() {
        var go = new GameObject("TestGameManagerForPreview");
        go.SetActive(false); // keep Awake() (save I/O) from running
        var manager = go.AddComponent<GameManager>();
        var modifierA = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifierA.modifierId = "previewTest";
        manager.allModifiers = new[] { modifierA };

        // Nothing selected yet: previewing "none" (null) must read as applied, a real modifier must not.
        Assert.IsTrue(InvokeIsSingleModifierSelectionApplied(manager, null));
        Assert.IsFalse(InvokeIsSingleModifierSelectionApplied(manager, modifierA));

        manager.SelectModifier(modifierA);
        Assert.IsTrue(InvokeIsSingleModifierSelectionApplied(manager, modifierA));
        Assert.IsFalse(InvokeIsSingleModifierSelectionApplied(manager, null));

        // A different in-memory instance with the same permanent id must still compare as applied by id.
        var modifierACopy = ScriptableObject.CreateInstance<ShiftModifierData>();
        modifierACopy.modifierId = "previewTest";
        Assert.IsTrue(InvokeIsSingleModifierSelectionApplied(manager, modifierACopy));

        Object.DestroyImmediate(modifierA);
        Object.DestroyImmediate(modifierACopy);
        Object.DestroyImmediate(go);
    }

    /// <summary>Verifies world->local->world round-trips exactly for a real NarrowDistrict-style cell center, with no double half-cell offset.</summary>
    [Test]
    public void MapNavigationCoordinates_WorldLocalRoundTripIsExact() {
        // A real authored cell-center position from NarrowDistrict's supply-point placement (shop cell).
        Vector2 cellCenterWorld = new Vector2(38.5f, 14.5f);
        Vector2 mapOrigin = new Vector2(0f, 0f); // NarrowDistrict/Expressway/Riverside are authored centered on world origin.

        Vector2 local = MapNavigationCoordinates.WorldToLocal(cellCenterWorld, mapOrigin);
        Vector2 roundTripped = MapNavigationCoordinates.LocalToWorld(local, mapOrigin);
        Assert.AreEqual(cellCenterWorld.x, roundTripped.x, 0.0001f);
        Assert.AreEqual(cellCenterWorld.y, roundTripped.y, 0.0001f);

        // A non-zero map origin must round-trip too, and must not silently add a second half-cell shift.
        Vector2 offsetOrigin = new Vector2(-84f, -56f);
        Vector2 localWithOffset = MapNavigationCoordinates.WorldToLocal(cellCenterWorld, offsetOrigin);
        Assert.AreEqual(cellCenterWorld.x - offsetOrigin.x, localWithOffset.x, 0.0001f);
        Vector2 roundTrippedWithOffset = MapNavigationCoordinates.LocalToWorld(localWithOffset, offsetOrigin);
        Assert.AreEqual(cellCenterWorld.x, roundTrippedWithOffset.x, 0.0001f);
        Assert.AreEqual(cellCenterWorld.y, roundTrippedWithOffset.y, 0.0001f);
    }

    /// <summary>Verifies the heading<->direction conversion round-trips for the four cardinal directions.</summary>
    [Test]
    public void MapNavigationCoordinates_HeadingDirectionRoundTrip() {
        Assert.AreEqual(0f, MapNavigationCoordinates.DirectionToHeadingDegrees(new Vector2(0f, 1f)), 0.01f);
        Assert.AreEqual(90f, MapNavigationCoordinates.DirectionToHeadingDegrees(new Vector2(-1f, 0f)), 0.01f);
        Assert.AreEqual(-90f, MapNavigationCoordinates.DirectionToHeadingDegrees(new Vector2(1f, 0f)), 0.01f);

        Vector2 dirAt0 = MapNavigationCoordinates.HeadingDegreesToDirection(0f);
        Assert.AreEqual(0f, dirAt0.x, 0.0001f);
        Assert.AreEqual(1f, dirAt0.y, 0.0001f);

        float[] headings = { 0f, 37f, 90f, -45f, 179f };
        foreach (var h in headings) {
            var dir = MapNavigationCoordinates.HeadingDegreesToDirection(h);
            float back = MapNavigationCoordinates.DirectionToHeadingDegrees(dir);
            Assert.AreEqual(h, back, 0.01f, "heading=" + h);
        }
    }

    /// <summary>Verifies node/edge/junction ids and geometry survive a JSON round-trip unchanged, and that a two-way street stays two directed edges.</summary>
    [Test]
    public void MapNavigationDocument_JsonRoundTripPreservesIdsAndDirectedEdges() {
        var doc = new MapNavigationDocument();
        doc.mapId = "narrowDistrict";
        doc.documentId = "doc1";
        doc.localBounds = new Rect(-84f, -56f, 168f, 112f);
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n1", x = 10f, y = 20f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n2", x = 30f, y = 20f });

        var forward = new RoadEdgeRecord { edgeId = "e_n1_n2", fromNodeId = "n1", toNodeId = "n2", usableWidth = 5f, speedLimit = 8f };
        forward.allowedRoles.Add(VehicleRole.Civilian);
        var reverse = new RoadEdgeRecord { edgeId = "e_n2_n1", fromNodeId = "n2", toNodeId = "n1", usableWidth = 5f, speedLimit = 8f };
        reverse.allowedRoles.Add(VehicleRole.Civilian);
        doc.edges.Add(forward);
        doc.edges.Add(reverse);

        doc.junctions.Add(new JunctionRecord { junctionId = "j1", conflictZone = new Rect(9f, 19f, 2f, 2f) });

        string json = JsonUtility.ToJson(doc);
        var restored = JsonUtility.FromJson<MapNavigationDocument>(json);

        Assert.AreEqual(doc.mapId, restored.mapId);
        Assert.AreEqual(doc.documentId, restored.documentId);
        Assert.AreEqual(2, restored.nodes.Count);
        Assert.AreEqual("n1", restored.nodes[0].nodeId);
        Assert.AreEqual(10f, restored.nodes[0].x, 0.0001f);

        Assert.AreEqual(2, restored.edges.Count);
        var restoredForward = restored.edges[0];
        var restoredReverse = restored.edges[1];
        Assert.AreEqual("e_n1_n2", restoredForward.edgeId);
        Assert.AreEqual("n1", restoredForward.fromNodeId);
        Assert.AreEqual("n2", restoredForward.toNodeId);
        Assert.AreEqual("e_n2_n1", restoredReverse.edgeId);
        Assert.AreEqual("n2", restoredReverse.fromNodeId);
        Assert.AreEqual("n1", restoredReverse.toNodeId);
        // The two directions are genuinely separate records, not one bidirectional edge collapsed into one.
        Assert.AreNotEqual(restoredForward.edgeId, restoredReverse.edgeId);
        Assert.AreEqual(restoredForward.fromNodeId, restoredReverse.toNodeId);
        Assert.AreEqual(restoredForward.toNodeId, restoredReverse.fromNodeId);

        Assert.AreEqual(1, restored.junctions.Count);
        Assert.AreEqual("j1", restored.junctions[0].junctionId);
    }

    static MapNavigationDocument BuildTwoNodeLoopDocument() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n1", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n2", x = 10f, y = 0f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e1", fromNodeId = "n1", toNodeId = "n2" });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e2", fromNodeId = "n2", toNodeId = "n1" });
        return doc;
    }

    /// <summary>Verifies a closed loop route whose last edge connects back to its first is valid, and that a genuine gap is rejected.</summary>
    [Test]
    public void CivilianRouteValidator_AcceptsClosedLoopAndRejectsGap() {
        var doc = BuildTwoNodeLoopDocument();
        var loopRoute = new CivilianRouteRecord { routeId = "r1", loop = true, edgeIds = new List<string> { "e1", "e2" } };
        Assert.IsTrue(CivilianRouteValidator.IsValid(doc, loopRoute, out string issue1), issue1);

        var gappedRoute = new CivilianRouteRecord { routeId = "r2", loop = false, edgeIds = new List<string> { "e1", "e1" } };
        Assert.IsFalse(CivilianRouteValidator.IsValid(doc, gappedRoute, out string issue2));
        Assert.IsNotNull(issue2);

        var openNonLoop = new CivilianRouteRecord { routeId = "r3", loop = false, edgeIds = new List<string> { "e1" } };
        Assert.IsTrue(CivilianRouteValidator.IsValid(doc, openNonLoop, out string issue3), issue3);
    }

    /// <summary>Verifies weighted picking excludes zero/negative/NaN weight entries and ids the catalog cannot resolve, reporting each.</summary>
    [Test]
    public void VehiclePoolResolver_RejectsInvalidWeightsAndUnknownCatalogIds() {
        var profileA = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profileA.vehicleProfileId = "sedan";
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profileA });

        var pool = new VehiclePoolRecord();
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "sedan", weight = 1f });
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "zeroWeight", weight = 0f });
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "negativeWeight", weight = -1f });
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "ghostVehicle", weight = 5f });

        bool ok = VehiclePoolResolver.TryPickWeighted(pool, catalog, 0f, out string picked, out List<string> issues);
        Assert.IsTrue(ok);
        Assert.AreEqual("sedan", picked); // the only valid entry
        Assert.AreEqual(3, issues.Count);

        Object.DestroyImmediate(profileA);
    }

    /// <summary>Verifies the weighted pick is deterministic across the full [0,1) range and always lands on a valid entry.</summary>
    [Test]
    public void VehiclePoolResolver_WeightedPickCoversFullRangeDeterministically() {
        var pool = new VehiclePoolRecord();
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "a", weight = 1f });
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "b", weight = 3f });

        VehiclePoolResolver.TryPickWeighted(pool, null, 0f, out string pickAtZero, out _);
        Assert.AreEqual("a", pickAtZero);
        VehiclePoolResolver.TryPickWeighted(pool, null, 0.99f, out string pickNearOne, out _);
        Assert.AreEqual("b", pickNearOne);
    }

    /// <summary>Verifies a map document can describe one loop route with two different vehicles and an independent police entry — the canonical S02.3 data example — including a police-only navigation shape with zero civilian routes.</summary>
    [Test]
    public void MapNavigationDocument_SupportsOneLoopTwoVehiclesAndIndependentPoliceEntry() {
        var doc = BuildTwoNodeLoopDocument();

        var pool = new VehiclePoolRecord { poolId = "civilianPool" };
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "sedan", weight = 1f });
        pool.entries.Add(new VehiclePoolEntry { vehicleProfileId = "truck", weight = 1f });
        doc.vehiclePools.Add(pool);

        var route = new CivilianRouteRecord { routeId = "loopRoute", loop = true, vehiclePoolId = "civilianPool", targetCount = 2, edgeIds = new List<string> { "e1", "e2" } };
        doc.civilianRoutes.Add(route);
        Assert.IsTrue(CivilianRouteValidator.IsValid(doc, route, out string issue), issue);

        // Police entry does not require a civilian routeId.
        doc.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "policeSpawn1", edgeId = "e1", distanceAlongEdge = 2f, role = VehicleRole.Police, routeId = string.Empty });
        doc.policeEntries.Add(new PoliceEntryRecord { entryId = "policeEntry1", spawnId = "policeSpawn1" });

        Assert.AreEqual(2, doc.vehiclePools[0].entries.Count);
        Assert.AreEqual(1, doc.policeEntries.Count);
        Assert.IsTrue(string.IsNullOrEmpty(doc.spawnPoints[0].routeId));

        // Police-only navigation: a document can be fully valid with zero civilian routes as long as police entries exist.
        var policeOnlyDoc = BuildTwoNodeLoopDocument();
        policeOnlyDoc.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "onlyPolice", edgeId = "e1", role = VehicleRole.Police });
        policeOnlyDoc.policeEntries.Add(new PoliceEntryRecord { entryId = "onlyEntry", spawnId = "onlyPolice" });
        Assert.AreEqual(0, policeOnlyDoc.civilianRoutes.Count);
        Assert.AreEqual(1, policeOnlyDoc.policeEntries.Count);
    }

    static bool HasIssueCode(List<TrafficValidationIssue> issues, string code) {
        foreach (var issue in issues) if (issue.code == code) return true;
        return false;
    }

    /// <summary>Verifies a structurally valid document produces zero issues.</summary>
    [Test]
    public void TrafficMapValidator_AcceptsAValidDocument() {
        var doc = BuildTwoNodeLoopDocument();
        var issues = TrafficMapValidator.Validate(doc, null);
        Assert.AreEqual(0, issues.Count, string.Join("; ", issues.ConvertAll(i => i.code)));
    }

    /// <summary>Verifies duplicate node/edge ids are each caught with their own code, alongside a still-valid other record.</summary>
    [Test]
    public void TrafficMapValidator_CatchesDuplicateIds() {
        var doc = BuildTwoNodeLoopDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n1", x = 5f, y = 5f }); // duplicate of an existing node id
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e1", fromNodeId = "n1", toNodeId = "n2" }); // duplicate of an existing edge id

        var issues = TrafficMapValidator.Validate(doc, null);
        Assert.IsTrue(HasIssueCode(issues, "DuplicateNodeId"));
        Assert.IsTrue(HasIssueCode(issues, "DuplicateEdgeId"));
    }

    /// <summary>Verifies a dangling edge reference, a NaN node position, and an out-of-bounds node are each caught independently.</summary>
    [Test]
    public void TrafficMapValidator_CatchesDanglingReferenceNaNAndOutOfBounds() {
        var doc = BuildTwoNodeLoopDocument();
        doc.edges.Add(new RoadEdgeRecord { edgeId = "eGhost", fromNodeId = "n1", toNodeId = "ghostNode" });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "nNaN", x = float.NaN, y = 0f });
        doc.localBounds = new Rect(0f, 0f, 5f, 5f);
        doc.nodes.Add(new RoadNodeRecord { nodeId = "nFar", x = 500f, y = 500f });

        var issues = TrafficMapValidator.Validate(doc, null);
        Assert.IsTrue(HasIssueCode(issues, "DanglingEdgeToNode"));
        Assert.IsTrue(HasIssueCode(issues, "NonFiniteNodePosition"));
        Assert.IsTrue(HasIssueCode(issues, "NodeOutOfBounds"));
    }

    /// <summary>Verifies an oversized node list and a zero-edge document are each caught.</summary>
    [Test]
    public void TrafficMapValidator_CatchesOversizedListsAndZeroEdges() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "n1", x = 0f, y = 0f });
        var tinyLimits = new TrafficValidationLimits { maxNodes = 0 };

        var issues = TrafficMapValidator.Validate(doc, tinyLimits);
        Assert.IsTrue(HasIssueCode(issues, "TooManyNodes"));
        Assert.IsTrue(HasIssueCode(issues, "ZeroEdgeCount"));
    }

    /// <summary>Verifies an open (non-closing) loop route is caught, and that this one bad route does not stop validation of the rest of the document.</summary>
    [Test]
    public void TrafficMapValidator_CatchesOpenLoopWithoutStoppingOtherChecks() {
        var doc = BuildTwoNodeLoopDocument();
        doc.civilianRoutes.Add(new CivilianRouteRecord { routeId = "brokenLoop", loop = true, edgeIds = new List<string> { "e1" } }); // e1 alone: n1->n2, does not close back to n1
        doc.nodes.Add(new RoadNodeRecord { nodeId = "dup", x = 1f, y = 1f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "dup", x = 2f, y = 2f }); // a second, unrelated error in the same document

        var issues = TrafficMapValidator.Validate(doc, null);
        Assert.IsTrue(HasIssueCode(issues, "OpenOrDisconnectedRoute"));
        Assert.IsTrue(HasIssueCode(issues, "DuplicateNodeId")); // proves the bad route did not abort the rest of the checks
    }

    /// <summary>Verifies a spawn referencing an unknown edge, and a negative distanceAlongEdge, are each caught.</summary>
    [Test]
    public void TrafficMapValidator_CatchesBadSpawnAttachment() {
        var doc = BuildTwoNodeLoopDocument();
        doc.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s1", edgeId = "ghostEdge", distanceAlongEdge = 0f });
        doc.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s2", edgeId = "e1", distanceAlongEdge = -5f });

        var issues = TrafficMapValidator.Validate(doc, null);
        Assert.IsTrue(HasIssueCode(issues, "DanglingSpawnEdge"));
        Assert.IsTrue(HasIssueCode(issues, "InvalidSpawnDistance"));
    }

    /// <summary>Verifies a null document is reported, not thrown.</summary>
    [Test]
    public void TrafficMapValidator_NullDocumentIsReportedNotThrown() {
        List<TrafficValidationIssue> issues = null;
        Assert.DoesNotThrow(() => issues = TrafficMapValidator.Validate(null, null));
        Assert.IsTrue(HasIssueCode(issues, "NullDocument"));
    }

    /// <summary>Verifies a narrow passage is accepted for a small vehicle's width but rejected for a heavy jeep's width.</summary>
    [Test]
    public void RoadClearanceChecker_NarrowPassageAcceptsSmallVehicleRejectsHeavyJeep() {
        var narrowEdge = new RoadEdgeRecord { edgeId = "narrow", usableWidth = 2.2f };
        Assert.IsTrue(RoadClearanceChecker.FitsEdgeWidth(narrowEdge, vehicleClearanceWidth: 1.6f, safetyMargin: 0.2f));
        Assert.IsFalse(RoadClearanceChecker.FitsEdgeWidth(narrowEdge, vehicleClearanceWidth: 2.4f, safetyMargin: 0.2f));
    }

    /// <summary>Verifies a sharp corner rejects a vehicle whose minimum turning radius is too large, while a gentle/straight edge accepts it.</summary>
    [Test]
    public void RoadClearanceChecker_SharpCornerRejectsWideTurningRadius() {
        var fromNode = new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f };
        var toNode = new RoadNodeRecord { nodeId = "b", x = 1f, y = 5f };

        // A sharp near-90-degree corner via one interior point close to the from-node.
        var sharpEdge = new RoadEdgeRecord { edgeId = "sharp" };
        sharpEdge.orderedPoints.Add(new Vector2(0f, 1f));
        float sharpRadius = RoadClearanceChecker.EstimateSharpestCornerRadius(fromNode, sharpEdge, toNode);
        Assert.IsFalse(RoadClearanceChecker.FitsTurningRadius(fromNode, sharpEdge, toNode, vehicleMinimumTurningRadius: sharpRadius + 1f));
        Assert.IsTrue(RoadClearanceChecker.FitsTurningRadius(fromNode, sharpEdge, toNode, vehicleMinimumTurningRadius: 0.01f));

        // A dead-straight edge (from, to only) has no corner at all, so any turning radius fits.
        var straightEdge = new RoadEdgeRecord { edgeId = "straight" };
        var straightTo = new RoadNodeRecord { nodeId = "c", x = 0f, y = 10f };
        Assert.IsTrue(RoadClearanceChecker.FitsTurningRadius(fromNode, straightEdge, straightTo, vehicleMinimumTurningRadius: 100f));
    }

    /// <summary>Verifies a spawn's own footprint is checked against its edge independently of the general edge-width check.</summary>
    [Test]
    public void RoadClearanceChecker_SpawnFootprintChecksAgainstItsOwnEdge() {
        var edge = new RoadEdgeRecord { edgeId = "e1", usableWidth = 3f };
        var fittingSpawn = new VehicleSpawnRecord { spawnId = "s1", edgeId = "e1", clearanceWidth = 2f };
        var tooWideSpawn = new VehicleSpawnRecord { spawnId = "s2", edgeId = "e1", clearanceWidth = 3.5f };

        Assert.IsTrue(RoadClearanceChecker.FitsSpawnFootprint(fittingSpawn, edge, 0.2f));
        Assert.IsFalse(RoadClearanceChecker.FitsSpawnFootprint(tooWideSpawn, edge, 0.2f));
    }

    /// <summary>Verifies a one-way edge only permits travel in its authored direction, never the reverse.</summary>
    [Test]
    public void RoadPathQuery_OneWayEdgeBlocksReverseDirection() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "oneWay", fromNodeId = "a", toNodeId = "b", usableWidth = 5f });
        var graph = new RoadGraphRuntime(doc);

        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "b", VehicleRole.Civilian, 2f, out var forwardPath));
        Assert.AreEqual(1, forwardPath.Count);

        Assert.IsFalse(RoadPathQuery.TryFindPath(graph, "b", "a", VehicleRole.Civilian, 2f, out var reversePath));
        Assert.IsNull(reversePath);
    }

    /// <summary>Verifies a disconnected component and a genuinely missing target both return a safe false, never an exception.</summary>
    [Test]
    public void RoadPathQuery_DisconnectedComponentAndNoPathAreSafe() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "island", x = 999f, y = 999f }); // no edges at all
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e1", fromNodeId = "a", toNodeId = "b", usableWidth = 5f });
        var graph = new RoadGraphRuntime(doc);

        List<string> path = null;
        Assert.DoesNotThrow(() => RoadPathQuery.TryFindPath(graph, "a", "island", VehicleRole.Civilian, 2f, out path));
        Assert.IsNull(path);

        Assert.DoesNotThrow(() => RoadPathQuery.TryFindPath(graph, "a", "unknownNode", VehicleRole.Civilian, 2f, out path));
        Assert.IsNull(path);
    }

    /// <summary>Verifies an authored junction transition list forbids a specific turn while still allowing a listed one through the same node.</summary>
    [Test]
    public void RoadPathQuery_ProhibitedTurnIsRejectedWhileAllowedTurnSucceeds() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "west", x = -10f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "hub", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "north", x = 0f, y = 10f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "east", x = 10f, y = 0f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "in", fromNodeId = "west", toNodeId = "hub", usableWidth = 5f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "outNorth", fromNodeId = "hub", toNodeId = "north", usableWidth = 5f, startJunctionId = "j1" });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "outEast", fromNodeId = "hub", toNodeId = "east", usableWidth = 5f, startJunctionId = "j1" });
        var junction = new JunctionRecord { junctionId = "j1" };
        junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = "in", toEdgeId = "outNorth", priority = 0 }); // only straight-through is legal; the turn toward east is not listed
        doc.junctions.Add(junction);
        var graph = new RoadGraphRuntime(doc);

        Assert.IsFalse(RoadPathQuery.TryFindPath(graph, "west", "east", VehicleRole.Civilian, 2f, out var blockedPath));
        Assert.IsNull(blockedPath);

        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "west", "north", VehicleRole.Civilian, 2f, out var allowedPath));
        CollectionAssert.AreEqual(new[] { "in", "outNorth" }, allowedPath);
    }

    /// <summary>Verifies a heavy/wide vehicle is filtered out of a road too narrow for it, while a role restriction excludes the wrong role even on a wide road.</summary>
    [Test]
    public void RoadPathQuery_FiltersHeavyVehicleWidthAndDisallowedRole() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        var narrowEdge = new RoadEdgeRecord { edgeId = "narrow", fromNodeId = "a", toNodeId = "b", usableWidth = 2f };
        doc.edges.Add(narrowEdge);
        var graph = new RoadGraphRuntime(doc);

        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "b", VehicleRole.Civilian, 1.5f, out _));
        Assert.IsFalse(RoadPathQuery.TryFindPath(graph, "a", "b", VehicleRole.Civilian, 3.5f, out var tooWide)); // heavy jeep does not fit
        Assert.IsNull(tooWide);

        var doc2 = new MapNavigationDocument();
        doc2.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc2.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        var policeOnlyEdge = new RoadEdgeRecord { edgeId = "policeOnly", fromNodeId = "a", toNodeId = "b", usableWidth = 5f };
        policeOnlyEdge.allowedRoles.Add(VehicleRole.Police);
        doc2.edges.Add(policeOnlyEdge);
        var graph2 = new RoadGraphRuntime(doc2);

        Assert.IsFalse(RoadPathQuery.TryFindPath(graph2, "a", "b", VehicleRole.Civilian, 1f, out var civilianBlocked));
        Assert.IsNull(civilianBlocked);
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph2, "a", "b", VehicleRole.Police, 1f, out _));
    }

    /// <summary>Verifies moving a node atomically moves its connected edges' endpoints and renormalizes affected spawns' distanceAlongEdge.</summary>
    [Test]
    public void MapNavigationEditCommands_MoveNodeUpdatesConnectedEdgesAndRenormalizesSpawns() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e1", fromNodeId = "a", toNodeId = "b" });
        doc.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s1", edgeId = "e1", distanceAlongEdge = 5f }); // exactly halfway (u=0.5) on a length-10 edge

        bool moved = MapNavigationEditCommands.TryMoveNode(doc, "b", 20f, 0f); // edge e1 is now length 20
        Assert.IsTrue(moved);

        var edge = doc.edges[0];
        var toNode = doc.nodes[1];
        Assert.AreEqual(20f, toNode.x, 0.0001f);
        // u=0.5 preserved on the new length-20 edge -> distance should now be 10, not still 5.
        Assert.AreEqual(10f, doc.spawnPoints[0].distanceAlongEdge, 0.01f);
    }

    /// <summary>Verifies a node move that would collapse a connected edge to zero length is rejected atomically, leaving the node exactly where it was.</summary>
    [Test]
    public void MapNavigationEditCommands_RejectsMoveThatWouldCollapseAnEdge() {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        doc.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        doc.edges.Add(new RoadEdgeRecord { edgeId = "e1", fromNodeId = "a", toNodeId = "b" });

        bool moved = MapNavigationEditCommands.TryMoveNode(doc, "b", 0f, 0f); // would collapse e1 onto node a
        Assert.IsFalse(moved);
        Assert.AreEqual(10f, doc.nodes[1].x, 0.0001f); // put back exactly where it was
        Assert.AreEqual(0f, doc.nodes[1].y, 0.0001f);
    }

    /// <summary>Verifies create-node and connect-edge are each rejected atomically on a duplicate/dangling request, and accepted otherwise.</summary>
    [Test]
    public void MapNavigationEditCommands_CreateNodeAndConnectEdgeAreAtomic() {
        var doc = new MapNavigationDocument();
        Assert.IsTrue(MapNavigationEditCommands.TryCreateNode(doc, "a", 0f, 0f));
        Assert.IsFalse(MapNavigationEditCommands.TryCreateNode(doc, "a", 5f, 5f)); // duplicate id
        Assert.AreEqual(1, doc.nodes.Count);
        Assert.AreEqual(0f, doc.nodes[0].x, 0.0001f); // untouched by the rejected duplicate attempt

        Assert.IsTrue(MapNavigationEditCommands.TryCreateNode(doc, "b", 10f, 0f));
        Assert.IsFalse(MapNavigationEditCommands.TryConnectEdge(doc, "e1", "a", "ghost", 5f, 8f)); // dangling toNodeId
        Assert.AreEqual(0, doc.edges.Count);
        Assert.IsTrue(MapNavigationEditCommands.TryConnectEdge(doc, "e1", "a", "b", 5f, 8f));
        Assert.AreEqual(1, doc.edges.Count);
    }

    /// <summary>Verifies TrafficMapData.ResolveDocument returns a detached copy: mutating it never changes the asset's own serialized document.</summary>
    [Test]
    public void TrafficMapData_ResolveDocumentReturnsDetachedCopy() {
        var asset = ScriptableObject.CreateInstance<TrafficMapData>();
        var so = new UnityEditor.SerializedObject(asset);
        var documentProp = so.FindProperty("document");
        documentProp.FindPropertyRelative("mapId").stringValue = "originalMap";
        so.ApplyModifiedPropertiesWithoutUndo();

        var resolved = asset.ResolveDocument();
        Assert.AreEqual("originalMap", resolved.mapId);

        resolved.mapId = "mutatedByCaller";
        resolved.nodes.Add(new RoadNodeRecord { nodeId = "shouldNotLeak", x = 1f, y = 1f });

        var resolvedAgain = asset.ResolveDocument();
        Assert.AreEqual("originalMap", resolvedAgain.mapId); // the asset's own copy is untouched
        Assert.AreEqual(0, resolvedAgain.nodes.Count);

        Object.DestroyImmediate(asset);
    }

    /// <summary>Verifies an instance cannot be double-released, cannot activate without first being reserved, and death from crash recovery works.</summary>
    [Test]
    public void NpcVehicleInstance_DoubleReleaseAndUnpreparedActivationAreRejected() {
        var instance = new NpcVehicleInstance();
        var registry = new VehicleIdentityRegistry();

        Assert.IsFalse(instance.TryActivate()); // Pooled -> Active directly is illegal
        Assert.IsFalse(instance.TryReturnToPool()); // already Pooled: nothing to release

        Assert.IsTrue(instance.TryReserve(registry, VehicleRole.Civilian));
        Assert.IsTrue(instance.TryActivate());
        Assert.IsTrue(instance.TryEnterCrashRecovery());
        Assert.IsTrue(instance.TryMarkWrecked()); // death reachable from CrashRecovery, not only Active
        Assert.IsFalse(instance.TryMarkWrecked()); // double release of the same life's destruction event

        Assert.IsTrue(instance.TryReturnToPool());
        Assert.IsFalse(instance.TryReturnToPool()); // double release of the same pool slot
    }

    /// <summary>Verifies pool reuse always assigns a fresh lifeId, and that an old life's identity cannot be confused with the new one's.</summary>
    [Test]
    public void NpcVehicleInstance_PoolReuseAssignsFreshLifeIdEachTime() {
        var instance = new NpcVehicleInstance();
        var registry = new VehicleIdentityRegistry();

        instance.TryReserve(registry, VehicleRole.Police);
        int firstLifeId = instance.Identity.Value.lifeId;
        instance.TryActivate();
        instance.TryMarkWrecked();
        instance.TryReturnToPool();
        Assert.IsNull(instance.Identity); // old life's identity does not survive into the pooled state

        instance.TryReserve(registry, VehicleRole.Police);
        int secondLifeId = instance.Identity.Value.lifeId;
        Assert.AreNotEqual(firstLifeId, secondLifeId); // a fresh life never reuses a retired lifeId
    }

    static PopulationBudgetData MakeBudget(int maxCivilian, int maxPolice, int maxTotal, int maxWrecks) {
        var budget = ScriptableObject.CreateInstance<PopulationBudgetData>();
        budget.maxCivilianMoving = maxCivilian;
        budget.maxPoliceMoving = maxPolice;
        budget.maxTotalMoving = maxTotal;
        budget.maxWreckSlots = maxWrecks;
        return budget;
    }

    /// <summary>Verifies that when two requests compete for the single last slot in sequence, exactly one wins and counters never go negative on cancel/abort.</summary>
    [Test]
    public void VehiclePopulationService_LastSlotContentionHasExactlyOneWinner() {
        var budget = MakeBudget(maxCivilian: 1, maxPolice: 10, maxTotal: 10, maxWrecks: 10);
        var service = new VehiclePopulationService(budget);

        bool first = service.TryReserveSpawn(VehicleRole.Civilian);
        bool second = service.TryReserveSpawn(VehicleRole.Civilian); // same "frame": the last slot is already taken
        Assert.IsTrue(first);
        Assert.IsFalse(second);

        Assert.IsTrue(service.CancelReservation(VehicleRole.Civilian)); // abort returns it
        Assert.IsFalse(service.CancelReservation(VehicleRole.Civilian)); // nothing left to cancel: counter must not go negative
        Assert.AreEqual(0, service.PendingCivilianCount);

        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies a wreck releases its moving slot but holds a separate physical/wreck slot, never both, never neither.</summary>
    [Test]
    public void VehiclePopulationService_WreckHoldsPhysicalSlotNotMovingSlot() {
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 5);
        var service = new VehiclePopulationService(budget);

        service.TryReserveSpawn(VehicleRole.Civilian);
        service.CommitSpawn(VehicleRole.Civilian);
        Assert.AreEqual(1, service.ActiveCivilianCount);
        Assert.AreEqual(1, service.ReservedFutureWreckCount);
        Assert.AreEqual(0, service.OccupiedWreckCount);

        Assert.IsTrue(service.MarkActiveVehicleWrecked(VehicleRole.Civilian));
        Assert.AreEqual(0, service.ActiveCivilianCount); // moving slot released
        Assert.AreEqual(0, service.ReservedFutureWreckCount);
        Assert.AreEqual(1, service.OccupiedWreckCount); // physical/wreck slot held instead

        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies that even if every active vehicle dies on the same tick, wreck capacity is never exceeded, and a replacement spawn waits until a wreck is released.</summary>
    [Test]
    public void VehiclePopulationService_SimultaneousMassDeathNeverExceedsWreckCapacityAndReplacementWaits() {
        var budget = MakeBudget(maxCivilian: 3, maxPolice: 3, maxTotal: 6, maxWrecks: 3);
        var service = new VehiclePopulationService(budget);

        for (int i = 0; i < 3; i++) {
            Assert.IsTrue(service.TryReserveSpawn(VehicleRole.Civilian));
            Assert.IsTrue(service.CommitSpawn(VehicleRole.Civilian));
        }
        Assert.AreEqual(3, service.ReservedFutureWreckCount);

        // A fourth spawn is already rejected before anyone dies: the wreck-token budget is full.
        Assert.IsFalse(service.TryReserveSpawn(VehicleRole.Civilian));

        // All three die on the "same tick".
        for (int i = 0; i < 3; i++) Assert.IsTrue(service.MarkActiveVehicleWrecked(VehicleRole.Civilian));
        Assert.AreEqual(3, service.OccupiedWreckCount);
        Assert.AreEqual(0, service.ReservedFutureWreckCount);
        Assert.AreEqual(3, service.OccupiedWreckCount + service.ReservedFutureWreckCount); // never exceeded maxWreckSlots at any point

        // Replacement still waits: wreck slots are full even though no vehicle is moving anymore.
        Assert.IsFalse(service.TryReserveSpawn(VehicleRole.Civilian));

        // Only after a wreck is actually cleaned up does a slot free for a new spawn.
        Assert.IsTrue(service.ReleaseWreck());
        Assert.IsTrue(service.TryReserveSpawn(VehicleRole.Civilian));

        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies ResetAll clears every counter to zero, matching a clean new session.</summary>
    [Test]
    public void VehiclePopulationService_ResetAllClearsEveryCounter() {
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 5);
        var service = new VehiclePopulationService(budget);
        service.TryReserveSpawn(VehicleRole.Civilian);
        service.CommitSpawn(VehicleRole.Civilian);
        service.TryReserveSpawn(VehicleRole.Police);

        service.ResetAll();

        Assert.AreEqual(0, service.ActiveCivilianCount);
        Assert.AreEqual(0, service.PendingCivilianCount);
        Assert.AreEqual(0, service.PendingPoliceCount);
        Assert.AreEqual(0, service.OccupiedWreckCount);
        Assert.AreEqual(0, service.ReservedFutureWreckCount);

        Object.DestroyImmediate(budget);
    }

    static NpcVehicleProfile MakeTrafficProfile(string id, params VehicleRole[] roles) {
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.vehicleProfileId = id;
        profile.allowedRoles = new List<VehicleRole>(roles);
        return profile;
    }

    /// <summary>Verifies two acquires never hand out the same instance, and the pool rejects once every slot is on loan.</summary>
    [Test]
    public void NpcVehiclePool_TwoAcquiresNeverReturnSameInstanceAndExhaustionIsRejected() {
        var profile = MakeTrafficProfile("sedan", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var budget = MakeBudget(maxCivilian: 10, maxPolice: 10, maxTotal: 10, maxWrecks: 2);
        var pool = new NpcVehiclePool(budget, catalog, new VehicleIdentityRegistry());

        Assert.AreEqual(2, pool.Capacity);
        Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance first));
        Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance second));
        Assert.AreNotSame(first, second);

        Assert.IsFalse(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance third)); // exhausted
        Assert.IsNull(third);

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies an unknown profile id and a profile that disallows the requested role are both rejected atomically, without touching any slot.</summary>
    [Test]
    public void NpcVehiclePool_RejectsUnknownProfileAndDisallowedRoleAtomically() {
        var profile = MakeTrafficProfile("civilianOnly", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var budget = MakeBudget(maxCivilian: 10, maxPolice: 10, maxTotal: 10, maxWrecks: 3);
        var pool = new NpcVehiclePool(budget, catalog, new VehicleIdentityRegistry());

        Assert.IsFalse(pool.TryAcquire("ghostVehicle", VehicleRole.Civilian, out NpcVehicleInstance a));
        Assert.IsNull(a);
        Assert.AreEqual(3, pool.PooledCount); // nothing touched

        Assert.IsFalse(pool.TryAcquire("civilianOnly", VehicleRole.Police, out NpcVehicleInstance b));
        Assert.IsNull(b);
        Assert.AreEqual(3, pool.PooledCount); // still nothing touched

        Assert.IsTrue(pool.TryAcquire("civilianOnly", VehicleRole.Civilian, out NpcVehicleInstance c));
        Assert.IsNotNull(c);
        Assert.AreEqual(2, pool.PooledCount);

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies Release runs every registered reset hook and that the next acquire gets a fresh lifeId — no stale subscription/velocity/tint state survives across lives.</summary>
    [Test]
    public void NpcVehiclePool_ReleaseRunsResetHooksAndReacquireGetsFreshLifeId() {
        var profile = MakeTrafficProfile("sedan", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var budget = MakeBudget(maxCivilian: 10, maxPolice: 10, maxTotal: 10, maxWrecks: 1);
        var pool = new NpcVehiclePool(budget, catalog, new VehicleIdentityRegistry());

        bool hookRan = false;
        pool.RegisterResetHook(instance => hookRan = true);

        Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance first));
        int firstLifeId = first.Identity.Value.lifeId;

        Assert.IsTrue(pool.Release(first)); // returns straight from Reserved — no forced activation required
        Assert.IsTrue(hookRan);
        Assert.AreEqual(1, pool.PooledCount);
        Assert.IsFalse(pool.Release(first)); // already Pooled: rejected, hooks not re-run

        Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance second));
        Assert.AreNotEqual(firstLifeId, second.Identity.Value.lifeId);

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(budget);
    }

    sealed class FakeAreaClearanceQuery : IAreaClearanceQuery {
        public bool clear = true;
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => clear;
    }

    static RoadGraphRuntime MakeSingleNodeGraph(string nodeId) {
        var doc = new MapNavigationDocument();
        doc.nodes.Add(new RoadNodeRecord { nodeId = nodeId, x = 0f, y = 0f });
        return new RoadGraphRuntime(doc);
    }

    static SpawnQueryContext MakeSpawnContext(RoadGraphRuntime graph, Rect cameraBounds, float margin) {
        return new SpawnQueryContext {
            mode = SpawnPlacementMode.InitialPlacement,
            cameraBounds = cameraBounds,
            cameraMargin = margin,
            graph = graph,
            clearanceQuery = new FakeAreaClearanceQuery()
        };
    }

    /// <summary>Verifies a candidate inside the camera bounds+margin is rejected while one just outside is accepted.</summary>
    [Test]
    public void VehicleSpawnPolicy_RejectsCandidateInsideCameraBoundsPlusMargin() {
        var graph = MakeSingleNodeGraph("n1");
        var context = MakeSpawnContext(graph, new Rect(-10f, -10f, 20f, 20f), margin: 5f);

        var insideCandidate = new SpawnCandidate { position = new Vector2(0f, 0f), headingDegrees = 0f, graphNodeId = "n1" };
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(insideCandidate, context, out string insideReason));
        StringAssert.Contains("camera", insideReason);

        var outsideCandidate = new SpawnCandidate { position = new Vector2(20f, 0f), headingDegrees = 0f, graphNodeId = "n1" };
        Assert.IsTrue(VehicleSpawnPolicy.IsSafe(outsideCandidate, context, out string outsideReason));
        Assert.IsNull(outsideReason);
    }

    /// <summary>Verifies active-replacement mode rejects a candidate directly on the player's closing path within the minimum approach time, while accepting one the player is moving away from.</summary>
    [Test]
    public void VehicleSpawnPolicy_RejectsCandidateOnPlayersNearFuturePathOnlyInActiveReplacement() {
        var graph = MakeSingleNodeGraph("n1");
        var context = MakeSpawnContext(graph, new Rect(-1f, -1f, 2f, 2f), margin: 0f);
        context.mode = SpawnPlacementMode.ActiveReplacement;
        context.playerPosition = Vector2.zero;
        context.playerVelocity = new Vector2(10f, 0f); // closing fast toward +X
        context.minApproachTimeSeconds = 3f;

        var inPathCandidate = new SpawnCandidate { position = new Vector2(20f, 0f), headingDegrees = 0f, graphNodeId = "n1" }; // 2s away at speed 10
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(inPathCandidate, context, out string reason));
        StringAssert.Contains("near-future path", reason);

        var behindCandidate = new SpawnCandidate { position = new Vector2(-20f, 0f), headingDegrees = 0f, graphNodeId = "n1" }; // player moving away from it
        Assert.IsTrue(VehicleSpawnPolicy.IsSafe(behindCandidate, context, out string behindReason));
        Assert.IsNull(behindReason);

        // Same in-path position is fine for initial placement: nothing is moving with intent yet.
        context.mode = SpawnPlacementMode.InitialPlacement;
        Assert.IsTrue(VehicleSpawnPolicy.IsSafe(inPathCandidate, context, out string initialReason));
        Assert.IsNull(initialReason);
    }

    /// <summary>Verifies an unknown graph node, a no-spawn service region, and a blocked footprint are each rejected with a specific reason, while a fully valid candidate passes.</summary>
    [Test]
    public void VehicleSpawnPolicy_RejectsUnknownGraphNodeNoSpawnRegionAndBlockedFootprint() {
        var graph = MakeSingleNodeGraph("n1");
        var context = MakeSpawnContext(graph, new Rect(-1f, -1f, 2f, 2f), margin: 0f);
        context.noSpawnRegions = new List<NoSpawnRegion> {
            new NoSpawnRegion { area = new Rect(40f, -5f, 10f, 10f), reason = "extraction approach" }
        };

        var unknownNodeCandidate = new SpawnCandidate { position = new Vector2(20f, 0f), headingDegrees = 0f, graphNodeId = "ghostNode" };
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(unknownNodeCandidate, context, out string graphReason));
        StringAssert.Contains("road-graph", graphReason);

        var inRegionCandidate = new SpawnCandidate { position = new Vector2(45f, 0f), headingDegrees = 0f, graphNodeId = "n1" };
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(inRegionCandidate, context, out string regionReason));
        StringAssert.Contains("no-spawn region", regionReason);

        var blockedFootprintCandidate = new SpawnCandidate { position = new Vector2(20f, 0f), headingDegrees = 0f, graphNodeId = "n1" };
        ((FakeAreaClearanceQuery)context.clearanceQuery).clear = false;
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(blockedFootprintCandidate, context, out string footprintReason));
        StringAssert.Contains("footprint", footprintReason);

        ((FakeAreaClearanceQuery)context.clearanceQuery).clear = true;
        var validCandidate = new SpawnCandidate { position = new Vector2(20f, 0f), headingDegrees = 0f, graphNodeId = "n1" };
        Assert.IsTrue(VehicleSpawnPolicy.IsSafe(validCandidate, context, out string validReason));
        Assert.IsNull(validReason);
    }

    /// <summary>Verifies that when every candidate is unsafe (or the list is empty), the picker returns false with one reason per candidate and never loops or throws.</summary>
    [Test]
    public void VehicleSpawnPolicy_AllUnsafeCandidatesAndEmptyListNeverHangAndReportEveryReason() {
        var graph = MakeSingleNodeGraph("n1");
        var context = MakeSpawnContext(graph, new Rect(-100f, -100f, 200f, 200f), margin: 0f); // whole world is "visible"

        var candidates = new List<SpawnCandidate> {
            new SpawnCandidate { position = new Vector2(1f, 0f), headingDegrees = 0f, graphNodeId = "n1" },
            new SpawnCandidate { position = new Vector2(2f, 0f), headingDegrees = 0f, graphNodeId = "n1" },
            new SpawnCandidate { position = new Vector2(3f, 0f), headingDegrees = 0f, graphNodeId = "n1" },
        };

        bool found = VehicleSpawnPolicy.TryPickSafeCandidate(candidates, context, out SpawnCandidate chosen, out List<string> reasons);
        Assert.IsFalse(found);
        Assert.IsNull(chosen);
        Assert.AreEqual(3, reasons.Count);

        bool foundEmpty = VehicleSpawnPolicy.TryPickSafeCandidate(new List<SpawnCandidate>(), context, out SpawnCandidate emptyChosen, out List<string> emptyReasons);
        Assert.IsFalse(foundEmpty);
        Assert.IsNull(emptyChosen);
        Assert.AreEqual(1, emptyReasons.Count);
    }

    /// <summary>Verifies camera-bounds exclusion scales with whatever bounds+margin is actually passed in — a position excluded under an ultrawide/zoomed-out camera can still be valid under a narrower one, and vice versa.</summary>
    [Test]
    public void VehicleSpawnPolicy_CameraBoundsExclusionScalesWithActualCameraSizeNotHardcoded() {
        var graph = MakeSingleNodeGraph("n1");
        var candidate = new SpawnCandidate { position = new Vector2(15f, 0f), headingDegrees = 0f, graphNodeId = "n1" };

        var narrowContext = MakeSpawnContext(graph, new Rect(-8f, -4.5f, 16f, 9f), margin: 2f); // 16:9-like, position well outside
        Assert.IsTrue(VehicleSpawnPolicy.IsSafe(candidate, narrowContext, out string narrowReason));
        Assert.IsNull(narrowReason);

        var ultrawideContext = MakeSpawnContext(graph, new Rect(-21f, -4.5f, 42f, 9f), margin: 2f); // ultrawide, same position now inside
        Assert.IsFalse(VehicleSpawnPolicy.IsSafe(candidate, ultrawideContext, out string ultrawideReason));
        StringAssert.Contains("camera", ultrawideReason);
    }

    static RespawnTimingRules MakeRespawnRules(float minDelay, float minDistance, float deadline) {
        var rules = ScriptableObject.CreateInstance<RespawnTimingRules>();
        rules.minimumDelaySeconds = minDelay;
        rules.minimumPlayerDistance = minDistance;
        rules.replacementDeadlineSeconds = deadline;
        return rules;
    }

    /// <summary>Verifies a death and an independent timeout racing for the same lifeId in the same tick register only one deficit — never a second replacement vehicle.</summary>
    [Test]
    public void TrafficRespawnScheduler_DeathAndTimeoutSameTickNeverRegisterTwice() {
        var scheduler = new TrafficRespawnScheduler();
        Assert.IsTrue(scheduler.TryRegisterDeficit("life-7", sessionTime: 10f, incidentWorldPosition: Vector2.zero)); // death handler registers first
        Assert.IsFalse(scheduler.TryRegisterDeficit("life-7", sessionTime: 10f, incidentWorldPosition: new Vector2(5f, 5f))); // timeout handler races in the same tick
        Assert.AreEqual(1, scheduler.PendingCount);
        Assert.IsTrue(scheduler.IsPending("life-7"));
    }

    /// <summary>Verifies every timer/budget combination: delay gate first, then distance-OR-deadline, and that a deadline reached with no safe spot keeps the deficit pending without leaking a duplicate.</summary>
    [Test]
    public void TrafficRespawnScheduler_TimingCombinationsGateCorrectlyAndDeadlineWithoutSafeSpotDefersWithoutLeaking() {
        var rules = MakeRespawnRules(minDelay: 5f, minDistance: 20f, deadline: 12f);
        var scheduler = new TrafficRespawnScheduler();
        scheduler.TryRegisterDeficit("life-1", sessionTime: 0f, incidentWorldPosition: Vector2.zero);

        // Before minimum delay: never ready, even far away.
        Assert.IsFalse(scheduler.IsReadyToAttempt("life-1", sessionTime: 2f, playerPosition: new Vector2(100f, 0f), rules));

        // Delay elapsed, player still close, deadline not reached: not ready.
        Assert.IsFalse(scheduler.IsReadyToAttempt("life-1", sessionTime: 6f, playerPosition: Vector2.zero, rules));

        // Delay elapsed, player far enough, deadline not reached: ready via distance.
        Assert.IsTrue(scheduler.IsReadyToAttempt("life-1", sessionTime: 6f, playerPosition: new Vector2(25f, 0f), rules));

        // Delay elapsed, player still close, deadline reached: ready via deadline.
        Assert.IsTrue(scheduler.IsReadyToAttempt("life-1", sessionTime: 12f, playerPosition: Vector2.zero, rules));

        // Deadline reached but caller finds no safe spot this attempt: must NOT call CompleteRespawn.
        // The deficit stays pending, re-registering for the same id is still rejected (no leak/duplicate),
        // and the very next check still reports ready.
        Assert.IsFalse(scheduler.TryRegisterDeficit("life-1", sessionTime: 12f, Vector2.zero));
        Assert.AreEqual(1, scheduler.PendingCount);
        Assert.IsTrue(scheduler.IsReadyToAttempt("life-1", sessionTime: 13f, playerPosition: Vector2.zero, rules));

        // Only once a safe spot is actually found does the caller complete it.
        Assert.IsTrue(scheduler.CompleteRespawn("life-1"));
        Assert.AreEqual(0, scheduler.PendingCount);
        Assert.IsFalse(scheduler.IsPending("life-1"));

        Object.DestroyImmediate(rules);
    }

    /// <summary>Verifies the wreck budget only removes a wreck that has reached its minimum visible lifetime — a too-young wreck is never chosen even when it is the only one tracked.</summary>
    [Test]
    public void WreckCleanupScheduler_OnlyOldestEligibleWreckIsChosenAndTooYoungWrecksDefer() {
        var scheduler = new WreckCleanupScheduler();
        scheduler.RegisterWreck("wreck-young", sessionTime: 10f);

        Assert.IsFalse(scheduler.TryFindOldestEligibleWreck(sessionTime: 11f, minimumLifetimeSeconds: 5f, out string none));
        Assert.IsNull(none);

        scheduler.RegisterWreck("wreck-old", sessionTime: 0f);
        Assert.IsTrue(scheduler.TryFindOldestEligibleWreck(sessionTime: 11f, minimumLifetimeSeconds: 5f, out string oldest));
        Assert.AreEqual("wreck-old", oldest); // the eligible, oldest one — not the ineligible young one
    }

    /// <summary>Verifies that cleaning a wreck removes its collider/visual state and its scheduler record together, in one call, via the same NpcVehiclePool reset-hook path S03.3 established — never a partial cleanup.</summary>
    [Test]
    public void WreckCleanupScheduler_CleanupRemovesColliderVisualAndSchedulerRecordTogether() {
        var profile = MakeTrafficProfile("sedan", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 5);
        var pool = new NpcVehiclePool(budget, catalog, new VehicleIdentityRegistry());

        bool colliderCleared = false;
        bool visualCleared = false;
        pool.RegisterResetHook(instance => { colliderCleared = true; visualCleared = true; });

        Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance instance));
        Assert.IsTrue(instance.TryActivate());
        Assert.IsTrue(instance.TryMarkWrecked());

        var scheduler = new WreckCleanupScheduler();
        scheduler.RegisterWreck("wreck-1", sessionTime: 0f);
        Assert.IsTrue(scheduler.TryFindOldestEligibleWreck(sessionTime: 10f, minimumLifetimeSeconds: 5f, out string wreckId));

        // Single combined cleanup step: both collider/visual reset (via the pool's hook) and the
        // scheduler record removal happen together, matching the "together, never partial" contract.
        bool released = pool.Release(instance);
        bool schedulerCleared = scheduler.MarkCleaned(wreckId);

        Assert.IsTrue(released);
        Assert.IsTrue(schedulerCleared);
        Assert.IsTrue(colliderCleared);
        Assert.IsTrue(visualCleared);
        Assert.AreEqual(0, scheduler.Count);

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies pause freezes accumulation exactly, and that End permanently stops the clock even against a later SetPaused(false) call.</summary>
    [Test]
    public void SessionClock_PauseDoesNotConsumeTimeAndEndStopsPermanently() {
        var clock = new SessionClock();
        clock.Tick(1f);
        clock.Tick(1f);
        Assert.AreEqual(2f, clock.ElapsedActiveSeconds, 0.0001f);

        clock.SetPaused(true);
        clock.Tick(5f);
        clock.Tick(5f);
        Assert.AreEqual(2f, clock.ElapsedActiveSeconds, 0.0001f); // paused: zero consumed despite two ticks

        clock.SetPaused(false);
        clock.Tick(3f);
        Assert.AreEqual(5f, clock.ElapsedActiveSeconds, 0.0001f);

        clock.End();
        clock.Tick(100f);
        clock.SetPaused(false); // no-op after end
        Assert.AreEqual(5f, clock.ElapsedActiveSeconds, 0.0001f);
        Assert.IsTrue(clock.IsEnded);
    }

    static TrafficSessionCoordinator MakeActiveCoordinator(string sessionId) {
        var context = new TrafficSessionContext(new SessionSetupDraft(sessionId, "map1", "veh1", "diff1", 5, false, null));
        var coordinator = new TrafficSessionCoordinator(context);
        coordinator.NotifyMapReady();
        coordinator.NotifyPlayerReady();
        Assert.IsTrue(coordinator.IsInitialized);
        return coordinator;
    }

    /// <summary>Verifies the lifecycle gate rejects every spawn after session end and cancels every still-pending reservation as part of that same end, detaching its own subscription.</summary>
    [Test]
    public void TrafficSessionLifecycleGate_RejectsSpawnAfterEndAndCancelsPendingReservations() {
        var coordinator = MakeActiveCoordinator("s1");
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 10);
        var populationService = new VehiclePopulationService(budget);
        var gate = new TrafficSessionLifecycleGate(coordinator, populationService);

        Assert.IsTrue(gate.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsTrue(gate.TryReserveSpawn(VehicleRole.Civilian));
        Assert.AreEqual(2, populationService.PendingCivilianCount);
        Assert.IsTrue(gate.IsAttachedToSession);

        coordinator.NotifySessionEnded();

        Assert.AreEqual(0, populationService.PendingCivilianCount); // cancelled as part of ending, not left dangling
        Assert.IsFalse(gate.IsAttachedToSession); // subscription detached
        Assert.IsFalse(gate.TryReserveSpawn(VehicleRole.Civilian)); // no spawn accepted after end

        Object.DestroyImmediate(budget);
    }

    /// <summary>Verifies three consecutive fixture sessions, run back to back without a domain reload, never accumulate pending reservations, dangling subscriptions, or growing pool capacity — each session starts genuinely clean.</summary>
    [Test]
    public void TrafficSessionLifecycleGate_ThreeConsecutiveFixtureSessionsNeverAccumulateState() {
        var profile = MakeTrafficProfile("sedan", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 4);

        for (int session = 0; session < 3; session++) {
            var coordinator = MakeActiveCoordinator("session-" + session);
            var populationService = new VehiclePopulationService(budget);
            var pool = new NpcVehiclePool(budget, catalog, new VehicleIdentityRegistry());
            var gate = new TrafficSessionLifecycleGate(coordinator, populationService);

            Assert.AreEqual(0, populationService.PendingCivilianCount); // fresh, never carries over from a prior iteration
            Assert.AreEqual(4, pool.Capacity); // budget-derived, does not grow across sessions

            Assert.IsTrue(gate.TryReserveSpawn(VehicleRole.Civilian));
            Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance instance));
            Assert.IsNotNull(instance);

            coordinator.NotifySessionEnded();

            Assert.AreEqual(0, populationService.PendingCivilianCount);
            Assert.IsFalse(gate.IsAttachedToSession);
        }

        Object.DestroyImmediate(profile);
        Object.DestroyImmediate(budget);
    }

    static NpcMotorSettings MakeValidMotorSettings() {
        return new NpcMotorSettings {
            cruiseSpeed = 6f, maxSpeed = 9f, reverseSpeed = 3f, acceleration = 4f, brakeDeceleration = 8f,
            maxEngineForce = 12f, maxBrakeForce = 16f, turnRate = 120f, minimumTurningRadius = 3f,
            lateralGrip = 0.9f, sensorInterval = 0.2f, reactionTime = 0.3f, minimumGap = 2f
        };
    }

    /// <summary>Verifies every invalid motor tuning value (NaN/Infinity/non-positive, and max below cruise) is rejected, while a fully valid settings object passes.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RejectsInvalidMotorSettingsValues() {
        Assert.IsTrue(NpcVehicleBodyValidator.ValidateMotorSettings(MakeValidMotorSettings(), out string validReason));
        Assert.IsNull(validReason);

        var nanCruise = MakeValidMotorSettings(); nanCruise.cruiseSpeed = float.NaN;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(nanCruise, out _));

        var infiniteAccel = MakeValidMotorSettings(); infiniteAccel.acceleration = float.PositiveInfinity;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(infiniteAccel, out _));

        var zeroTurnRate = MakeValidMotorSettings(); zeroTurnRate.turnRate = 0f;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(zeroTurnRate, out _));

        var maxBelowCruise = MakeValidMotorSettings(); maxBelowCruise.maxSpeed = 2f; // cruiseSpeed is 6
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(maxBelowCruise, out string maxReason));
        StringAssert.Contains("maxSpeed", maxReason);

        var gripOutOfRange = MakeValidMotorSettings(); gripOutOfRange.lateralGrip = 1.5f;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(gripOutOfRange, out _));

        Assert.IsFalse(NpcVehicleBodyValidator.ValidateMotorSettings(null, out string nullReason));
        Assert.IsNotNull(nullReason);
    }

    /// <summary>Verifies an invalid baseMass or colliderSize on a whole profile is rejected, while a default-valued profile passes.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RejectsInvalidProfileBodyValues() {
        var validProfile = MakeTrafficProfile("sedan", VehicleRole.Civilian); // defaults: baseMass=1, colliderSize=(2,4), valid motor settings
        Assert.IsTrue(NpcVehicleBodyValidator.ValidateProfileBody(validProfile, out string validReason));
        Assert.IsNull(validReason);

        validProfile.baseMass = float.NaN;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateProfileBody(validProfile, out _));

        validProfile.baseMass = 1f;
        validProfile.colliderSize = new Vector2(0f, 4f);
        Assert.IsFalse(NpcVehicleBodyValidator.ValidateProfileBody(validProfile, out _));

        Object.DestroyImmediate(validProfile);
    }

    static GameObject MakeValidNpcBody(string name) {
        var go = new GameObject(name);
        go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
        var rb = go.GetComponent<Rigidbody2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        var renderer = go.AddComponent<SpriteRenderer>();
        var texture = new Texture2D(16, 16);
        renderer.sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(1f, 1f); // matches the 1x1 world-unit sprite above
        return go;
    }

    /// <summary>Verifies static decor (no Rigidbody2D) and a fully valid NPC body are told apart correctly.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RejectsStaticDecorMissingRigidbodyAndAcceptsValidBody() {
        var decor = new GameObject("StaticDecorCar");
        decor.AddComponent<SpriteRenderer>();
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(decor, out string decorReason));
        StringAssert.Contains("Rigidbody2D", decorReason);

        var validBody = MakeValidNpcBody("ValidNpcBody");
        Assert.IsTrue(NpcVehicleBodyValidator.ValidatePrefabStructure(validBody, out string validReason));
        Assert.IsNull(validReason);

        Object.DestroyImmediate(decor);
        Object.DestroyImmediate(validBody);
    }

    /// <summary>Verifies a prefab carrying player-only components (Driver/PlayerInput/Delivery) is rejected — the wrong prefab was bound instead of an NPC body.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RejectsPlayerOnlyComponentsOnWhatShouldBeAnNpcBody() {
        var playerLike = MakeValidNpcBody("PlayerLikeBody");
        playerLike.SetActive(false); // suppress Awake on Driver/Delivery while adding them (avoids any real save-file side effect)
        playerLike.AddComponent<Driver>();
        playerLike.AddComponent<Delivery>();

        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(playerLike, out string reason));
        StringAssert.Contains("player-only", reason);

        Object.DestroyImmediate(playerLike);
    }

    /// <summary>Verifies wrong Rigidbody2D configuration (Kinematic body, Discrete detection, no interpolation), a missing collider, a missing sprite, and a wildly mismatched footprint are each rejected.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RejectsWrongRigidbodyConfigMissingPartsAndMismatchedFootprint() {
        var kinematicBody = MakeValidNpcBody("KinematicBody");
        kinematicBody.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(kinematicBody, out string kinematicReason));
        StringAssert.Contains("Dynamic", kinematicReason);

        var discreteBody = MakeValidNpcBody("DiscreteBody");
        discreteBody.GetComponent<Rigidbody2D>().collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(discreteBody, out string discreteReason));
        StringAssert.Contains("Continuous", discreteReason);

        var noInterpolationBody = MakeValidNpcBody("NoInterpolationBody");
        noInterpolationBody.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(noInterpolationBody, out string interpolationReason));
        StringAssert.Contains("Interpolate", interpolationReason);

        var noColliderBody = MakeValidNpcBody("NoColliderBody");
        Object.DestroyImmediate(noColliderBody.GetComponent<BoxCollider2D>());
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(noColliderBody, out string colliderReason));
        StringAssert.Contains("Collider2D", colliderReason);

        var noSpriteBody = MakeValidNpcBody("NoSpriteBody");
        noSpriteBody.GetComponent<SpriteRenderer>().sprite = null;
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(noSpriteBody, out string spriteReason));
        StringAssert.Contains("sprite", spriteReason);

        var mismatchedBody = MakeValidNpcBody("MismatchedBody");
        mismatchedBody.GetComponent<BoxCollider2D>().size = new Vector2(50f, 50f); // wildly larger than the 1x1 sprite
        Assert.IsFalse(NpcVehicleBodyValidator.ValidatePrefabStructure(mismatchedBody, out string ratioReason));
        StringAssert.Contains("footprint", ratioReason);

        Object.DestroyImmediate(kinematicBody);
        Object.DestroyImmediate(discreteBody);
        Object.DestroyImmediate(noInterpolationBody);
        Object.DestroyImmediate(noColliderBody);
        Object.DestroyImmediate(noSpriteBody);
        Object.DestroyImmediate(mismatchedBody);
    }

    /// <summary>Verifies the real fixture NPC prefab created for S04.1 (Assets/Prefabs/Traffic/TestCivilianSedan.prefab) actually satisfies every structural check via real Unity component data. Instantiated as a temporary, never-saved copy first — a prefab asset that was never placed in a scene reports zero-size renderer/collider bounds, so bounds-dependent checks need a live instance.</summary>
    [Test]
    public void NpcVehicleBodyValidator_RealTestCivilianSedanPrefabPassesStructuralValidation() {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Traffic/TestCivilianSedan.prefab");
        Assert.IsNotNull(prefab, "Fixture prefab from S04.1 must exist on disk.");

        var instance = Object.Instantiate(prefab);
        try {
            Assert.IsTrue(NpcVehicleBodyValidator.ValidatePrefabStructure(instance, out string reason), reason);
        } finally {
            Object.DestroyImmediate(instance);
        }
    }

    TrafficPhysicsTestWorld trafficPhysicsWorld;

    (GameObject go, NpcVehicleMotor motor, Rigidbody2D rb) MakeMotorRig(
        NpcMotorSettings settings, float mass = 1f, Vector2? position = null) {
        return trafficPhysicsWorld.CreateMotor(settings, mass, position ?? Vector2.zero);
    }

    /// <summary>Runs a scenario in a disposable local physics scene without changing global simulation or time settings.</summary>
    void RunPhysicsScenario(float fixedDeltaTime, Action body) {
        using (var world = new TrafficPhysicsTestWorld(fixedDeltaTime)) {
            var previousWorld = trafficPhysicsWorld;
            trafficPhysicsWorld = world;
            try {
                body();
            } finally {
                trafficPhysicsWorld = previousWorld;
            }
        }
    }

    /// <summary>Verifies gradual acceleration on a straight track: speed rises monotonically over several steps, never overshoots targetSpeed, and never jumps straight to it in one step.</summary>
    [Test]
    public void NpcVehicleMotor_StraightTrackAccelerationIsGradualAndGovernedByTargetSpeed() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false)); // cruiseSpeed governor
                float previousSpeed = 0f;
                for (int i = 0; i < 30; i++) {
                    trafficPhysicsWorld.Step();
                    float speed = rb.linearVelocity.magnitude;
                    Assert.LessOrEqual(speed, 6.05f, "must never overshoot the commanded targetSpeed governor");
                    Assert.GreaterOrEqual(speed, previousSpeed - 0.0001f, "speed must rise monotonically under constant throttle");
                    previousSpeed = speed;
                }
                Assert.Greater(previousSpeed, 0.5f); // it actually moved
                Assert.Less(rb.linearVelocity.magnitude, 6.05f);
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies gradual braking from cruising speed comes to rest without ever reversing on its own.</summary>
    [Test]
    public void NpcVehicleMotor_BrakingIsGradualAndNeverAutoReverses() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                rb.linearVelocity = (Vector2)go.transform.up * 5f; // already cruising forward
                motor.SetCommand(NpcDriveCommand.Stopped);

                float previousForwardSpeed = 5f;
                for (int i = 0; i < 60; i++) {
                    trafficPhysicsWorld.Step();
                    float forwardSpeed = Vector2.Dot(rb.linearVelocity, (Vector2)go.transform.up);
                    Assert.LessOrEqual(forwardSpeed, previousForwardSpeed + 0.0001f, "forward speed must fall monotonically while braking");
                    Assert.GreaterOrEqual(forwardSpeed, -0.01f, "braking must never push the vehicle into reverse on its own");
                    previousForwardSpeed = forwardSpeed;
                }
                Assert.Less(Mathf.Abs(previousForwardSpeed), 0.5f); // it actually came to rest
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies reverse throttle is entirely ignored without explicit reverseAllowed permission.</summary>
    [Test]
    public void NpcVehicleMotor_ReverseThrottleIgnoredWithoutExplicitPermission() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                motor.SetCommand(new NpcDriveCommand(-1f, 0f, 0f, 3f, reverseAllowed: false));
                for (int i = 0; i < 10; i++) trafficPhysicsWorld.Step();
                Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.0001f);
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies a turn's angular rate is capped by minimumTurningRadius at low speed (a wide, profile-appropriate arc) rather than snapping to the raw authored turnRate regardless of speed.</summary>
    [Test]
    public void NpcVehicleMotor_TurnRateIsLimitedByMinimumTurningRadiusAtLowSpeed() {
        RunPhysicsScenario(0.02f, () => {
            var settings = MakeValidMotorSettings();
            settings.turnRate = 500f; // deliberately huge so the radius limit is what actually binds
            settings.minimumTurningRadius = 3f;
            var (go, motor, rb) = MakeMotorRig(settings);
            try {
                rb.linearVelocity = (Vector2)go.transform.up * 1f; // slow forward speed
                motor.SetCommand(new NpcDriveCommand(0f, 0f, 1f, 0f, false));
                float startRotation = rb.rotation;
                trafficPhysicsWorld.Step();
                float turnedDegrees = Mathf.Abs(rb.rotation - startRotation);
                float expectedMaxDegreesThisStep = (1f / 3f) * Mathf.Rad2Deg * trafficPhysicsWorld.DeltaTime;
                Assert.LessOrEqual(turnedDegrees, expectedMaxDegreesThisStep + 0.01f, "turn rate must be capped by speed/minimumTurningRadius, not the raw (huge) authored turnRate");
                Assert.Greater(turnedDegrees, 0f);
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies an external impact's momentum is never erased by a still-active follow command — it only decays through the authored lateralGrip, one step at a time.</summary>
    [Test]
    public void NpcVehicleMotor_ExternalImpactMomentumIsNotErasedByFollowCommand() {
        RunPhysicsScenario(0.02f, () => {
            var settings = MakeValidMotorSettings();
            settings.lateralGrip = 0.9f;
            var (go, motor, rb) = MakeMotorRig(settings);
            try {
                rb.linearVelocity = (Vector2)go.transform.up * 4f; // cruising
                motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false)); // still actively following a command

                Vector2 lateralImpulse = (Vector2)go.transform.right * 5f; // simulate a side impact
                rb.AddForce(lateralImpulse, ForceMode2D.Impulse);
                float lateralRightAfterImpact = Vector2.Dot(rb.linearVelocity, (Vector2)go.transform.right);
                Assert.Greater(Mathf.Abs(lateralRightAfterImpact), 1f, "the impact must actually be reflected in velocity before the motor runs again");

                trafficPhysicsWorld.Step(); // the follow command's own FixedUpdate runs once more
                float lateralAfterOneMotorStep = Vector2.Dot(rb.linearVelocity, (Vector2)go.transform.right);
                Assert.Greater(Mathf.Abs(lateralAfterOneMotorStep), 0.01f, "one active follow-command step must not erase the impact's momentum outright");
                Assert.Less(Mathf.Abs(lateralAfterOneMotorStep), Mathf.Abs(lateralRightAfterImpact), "grip damps it down, proportionally, not to zero in one step");
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies crash mode measurably reduces motor force versus normal mode under the identical command and step count.</summary>
    [Test]
    public void NpcVehicleMotor_CrashModeReducesAppliedForceVersusNormalMode() {
        RunPhysicsScenario(0.02f, () => {
            var (goNormal, motorNormal, rbNormal) = MakeMotorRig(MakeValidMotorSettings(), position: new Vector2(-10f, 0f));
            var (goCrash, motorCrash, rbCrash) = MakeMotorRig(MakeValidMotorSettings(), position: new Vector2(10f, 0f));
            try {
                motorNormal.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false));
                motorCrash.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false));
                motorCrash.SetCrashMode(true);

                for (int i = 0; i < 5; i++) {
                    trafficPhysicsWorld.Step();
                }

                Assert.Less(rbCrash.linearVelocity.magnitude, rbNormal.linearVelocity.magnitude, "crash mode must apply visibly less force than normal mode under the identical command");
            } finally {
                Object.DestroyImmediate(goNormal);
                Object.DestroyImmediate(goCrash);
            }
        });
    }

    /// <summary>Verifies ResetForNewLife clears velocity/angular velocity and driving/crash state for pool reuse.</summary>
    [Test]
    public void NpcVehicleMotor_ResetForNewLifeClearsVelocityAndDrivingState() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                rb.linearVelocity = new Vector2(3f, 4f);
                rb.angularVelocity = 90f;
                motor.SetCommand(new NpcDriveCommand(1f, 0f, 1f, 5f, true));
                motor.SetCrashMode(true);

                motor.ResetForNewLife();

                Assert.AreEqual(Vector2.zero, rb.linearVelocity);
                Assert.AreEqual(0f, rb.angularVelocity);
                Assert.IsFalse(motor.IsCrashMode);

                // Driving state cleared too: a step with no new SetCommand call must produce no motion.
                trafficPhysicsWorld.Step();
                Assert.AreEqual(Vector2.zero, rb.linearVelocity);
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies a long brake-to-stop (obstacle ahead) never dips into negative forward speed, and the vehicle drives again once throttle resumes (obstacle cleared).</summary>
    [Test]
    public void NpcVehicleMotor_LongBrakeNeverGoesNegativeAndResumesOnceObstacleClears() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                rb.linearVelocity = (Vector2)go.transform.up * 5f;
                motor.SetCommand(NpcDriveCommand.Stopped); // player stopped ahead: full brake hold
                for (int i = 0; i < 80; i++) {
                    trafficPhysicsWorld.Step();
                    float forwardSpeed = Vector2.Dot(rb.linearVelocity, (Vector2)go.transform.up);
                    Assert.GreaterOrEqual(forwardSpeed, -0.0001f, "must never go negative while braked to a stop");
                }
                Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.001f);

                motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false)); // obstacle clears
                for (int i = 0; i < 20; i++) trafficPhysicsWorld.Step();
                Assert.Greater(rb.linearVelocity.magnitude, 0.2f, "must actually drive again once released");
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies reverse only actually happens when BOTH the command's own reverseAllowed AND the motor-level reverse-maneuver permission (recovery state + rear clearance, S04.4+) are open — any single gate closed blocks it.</summary>
    [Test]
    public void NpcVehicleMotor_ReverseRequiresBothCommandPermissionAndMotorLevelManeuverGate() {
        RunPhysicsScenario(0.02f, () => {
            (GameObject go, NpcVehicleMotor motor, Rigidbody2D rb) MakeRig(bool commandAllows, bool motorPermits, float x) {
                var rig = MakeMotorRig(MakeValidMotorSettings(), position: new Vector2(x, 0f));
                rig.motor.SetReverseManeuverPermission(motorPermits);
                rig.motor.SetCommand(new NpcDriveCommand(-1f, 0f, 0f, 3f, commandAllows));
                return rig;
            }

            var neitherOpen = MakeRig(false, false, -15f);
            var onlyCommandOpen = MakeRig(true, false, -5f);
            var onlyMotorOpen = MakeRig(false, true, 5f);
            var bothOpen = MakeRig(true, true, 15f);
            try {
                for (int i = 0; i < 15; i++) trafficPhysicsWorld.Step();
                Assert.AreEqual(0f, neitherOpen.rb.linearVelocity.magnitude, 0.001f);
                Assert.AreEqual(0f, onlyCommandOpen.rb.linearVelocity.magnitude, 0.001f, "command alone must not be enough");
                Assert.AreEqual(0f, onlyMotorOpen.rb.linearVelocity.magnitude, 0.001f, "motor-level permission alone must not be enough");
                Assert.Greater(bothOpen.rb.linearVelocity.magnitude, 0.05f, "only both gates open together allow reverse");
            } finally {
                Object.DestroyImmediate(neitherOpen.go);
                Object.DestroyImmediate(onlyCommandOpen.go);
                Object.DestroyImmediate(onlyMotorOpen.go);
                Object.DestroyImmediate(bothOpen.go);
            }
        });
    }

    /// <summary>Verifies StopMovement (death/session-end) makes every later gas command a no-op, unlike a normal SetCommand(Stopped) which still allows resuming.</summary>
    [Test]
    public void NpcVehicleMotor_StopMovementMakesLaterGasCommandsIneffective() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                rb.linearVelocity = (Vector2)go.transform.up * 3f;
                motor.StopMovement();
                Assert.IsTrue(motor.IsStoppedPermanently);
                Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.0001f); // stopped immediately, not just braked gradually

                motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false)); // "gas after death" — must be ineffective
                for (int i = 0; i < 20; i++) trafficPhysicsWorld.Step();
                Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.0001f);
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies pool reuse (ResetForNewLife) clears the permanent-stopped flag — a fresh life can drive immediately, not pre-stopped from the previous one.</summary>
    [Test]
    public void NpcVehicleMotor_PoolReuseClearsPermanentStopFlag() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                motor.StopMovement();
                motor.ResetForNewLife();
                Assert.IsFalse(motor.IsStoppedPermanently, "next life must not start pre-stopped");

                motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false));
                for (int i = 0; i < 20; i++) trafficPhysicsWorld.Step();
                Assert.Greater(rb.linearVelocity.magnitude, 0.1f, "a fresh life must actually be able to drive again");
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies pool reuse (ResetForNewLife) closes reverse-maneuver permission again — the previous life's open gate never leaks into the next one.</summary>
    [Test]
    public void NpcVehicleMotor_PoolReuseClosesReverseManeuverPermissionAgain() {
        RunPhysicsScenario(0.02f, () => {
            var (go, motor, rb) = MakeMotorRig(MakeValidMotorSettings());
            try {
                motor.SetReverseManeuverPermission(true);
                motor.ResetForNewLife();

                motor.SetCommand(new NpcDriveCommand(-1f, 0f, 0f, 3f, reverseAllowed: true));
                for (int i = 0; i < 15; i++) trafficPhysicsWorld.Step();
                Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.001f, "the previous life's open reverse-maneuver gate must not survive into the next life");
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    (GameObject go, VehicleObstacleSensor sensor, Rigidbody2D rb) MakeSensorRig(NpcMotorSettings settings, int bufferSize = 8) {
        var rb = trafficPhysicsWorld.CreateBody("SensorRig", Vector2.zero);
        var sensor = trafficPhysicsWorld.AddSensor(rb, settings, bufferSize);
        return (rb.gameObject, sensor, rb);
    }

    GameObject MakeObstacleCollider(string name, Vector2 position, bool isTrigger = false) {
        var rb = trafficPhysicsWorld.CreateBody(name, position, bodyType: RigidbodyType2D.Static);
        rb.GetComponent<Collider2D>().isTrigger = isTrigger;
        return rb.gameObject;
    }

    /// <summary>Verifies a real forward obstacle on layer 0 (Default — where the project's actual player prefabs sit) is detected; no dedicated "Player layer" assumption is made.</summary>
    [Test]
    public void VehicleObstacleSensor_DetectsForwardObstacleOnDefaultLayer() {
        RunPhysicsScenario(0.02f, () => {
            var (go, sensor, rb) = MakeSensorRig(MakeValidMotorSettings());
            var obstacle = MakeObstacleCollider("ForwardObstacle", new Vector2(0f, 1.5f));
            try {
                Assert.AreEqual(0, obstacle.layer, "fixture must actually sit on Default, matching the real project's player prefabs");
                trafficPhysicsWorld.Step();
                Assert.IsTrue(sensor.TryDetectForwardObstacle(out float distance, out Collider2D hit));
                Assert.IsNotNull(hit);
                Assert.Greater(distance, 0f);
            } finally {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(obstacle);
            }
        });
    }

    /// <summary>Verifies a vehicle in the side lane (offset sideways, same forward distance) is never counted as a forward blocker.</summary>
    [Test]
    public void VehicleObstacleSensor_SideLaneVehicleIsNotCountedAsForwardBlocker() {
        RunPhysicsScenario(0.02f, () => {
            var (go, sensor, rb) = MakeSensorRig(MakeValidMotorSettings());
            var sideObstacle = MakeObstacleCollider("SideLaneVehicle", new Vector2(2f, 1.5f));
            try {
                trafficPhysicsWorld.Step();
                Assert.IsFalse(sensor.TryDetectForwardObstacle(out _, out _));
            } finally {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(sideObstacle);
            }
        });
    }

    /// <summary>Verifies a trigger collider directly ahead is never reported as a physical obstacle.</summary>
    [Test]
    public void VehicleObstacleSensor_TriggerColliderAheadIsFiltered() {
        RunPhysicsScenario(0.02f, () => {
            var (go, sensor, rb) = MakeSensorRig(MakeValidMotorSettings());
            var triggerAhead = MakeObstacleCollider("TriggerAhead", new Vector2(0f, 1.5f), isTrigger: true);
            try {
                trafficPhysicsWorld.Step();
                Assert.IsFalse(sensor.TryDetectForwardObstacle(out _, out _));
            } finally {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(triggerAhead);
            }
        });
    }

    /// <summary>Verifies the sensor's own collider is never mistaken for a forward obstacle when nothing else is present.</summary>
    [Test]
    public void VehicleObstacleSensor_NeverDetectsItsOwnCollider() {
        RunPhysicsScenario(0.02f, () => {
            var (go, sensor, rb) = MakeSensorRig(MakeValidMotorSettings());
            try {
                trafficPhysicsWorld.Step();
                Assert.IsFalse(sensor.TryDetectForwardObstacle(out _, out _));
            } finally {
                Object.DestroyImmediate(go);
            }
        });
    }

    /// <summary>Verifies buffer saturation is reported rather than silently dropped when more colliders are in range than the buffer can hold.</summary>
    [Test]
    public void VehicleObstacleSensor_ReportsBufferSaturationRatherThanSilentlyDropping() {
        RunPhysicsScenario(0.02f, () => {
            var (go, sensor, rb) = MakeSensorRig(MakeValidMotorSettings(), bufferSize: 1);
            var obstacleA = MakeObstacleCollider("StackedA", new Vector2(0f, 1.2f));
            var obstacleB = MakeObstacleCollider("StackedB", new Vector2(0f, 1.6f));
            try {
                trafficPhysicsWorld.Step();
                sensor.TryDetectForwardObstacle(out _, out _);
                Assert.IsTrue(sensor.LastQuerySaturated, "two colliders in range against a 1-slot buffer must be reported as saturated, never silently ignored");
            } finally {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(obstacleA);
                Object.DestroyImmediate(obstacleB);
            }
        });
    }

    /// <summary>Verifies the sweep range actually derives from current speed+stopping-distance+reaction-margin rather than a fixed hardcoded distance — a faster vehicle detects a farther obstacle the same slow one misses entirely. Run sequentially (not both rigs alive at once) so the two fixtures never sit on top of each other and detect one another instead of the intended obstacle.</summary>
    [Test]
    public void VehicleObstacleSensor_SweepRangeScalesWithSpeedNotFixedDistance() {
        RunPhysicsScenario(0.02f, () => {
            var settings = MakeValidMotorSettings(); // brakeDeceleration=8, reactionTime=0.3, minimumGap=2

            var (slowGo, slowSensor, slowRb) = MakeSensorRig(settings);
            var slowFarObstacle = MakeObstacleCollider("SlowFarObstacle", new Vector2(0f, 4.5f)); // distance ~4.0 from origin
            try {
                trafficPhysicsWorld.Step();
                Assert.IsFalse(slowSensor.TryDetectForwardObstacle(out _, out _), "stationary sensor's short (minimumGap-only) sweep must miss a far obstacle");
            } finally {
                Object.DestroyImmediate(slowGo);
                Object.DestroyImmediate(slowFarObstacle);
            }

            var (fastGo, fastSensor, fastRb) = MakeSensorRig(settings);
            fastRb.linearVelocity = (Vector2)fastGo.transform.up * 6f; // stoppingDistance=2.25 + reaction=1.8 + gap=2 = ~6.05 sweep
            var fastFarObstacle = MakeObstacleCollider("FastFarObstacle", new Vector2(0f, 4.5f));
            try {
                trafficPhysicsWorld.Step();
                Assert.IsTrue(fastSensor.TryDetectForwardObstacle(out float distance, out _), "fast-moving sensor's speed-scaled sweep must reach the same-shaped obstacle the stationary one missed");
                Assert.Greater(distance, 0f);
            } finally {
                Object.DestroyImmediate(fastGo);
                Object.DestroyImmediate(fastFarObstacle);
            }
        });
    }

    /// <summary>Verifies ResolveMass uses baseMass when the table is empty/out of range/invalid, and a per-level override when it's a valid positive finite value.</summary>
    [Test]
    public void VehicleBodySettings_ResolveMassFallsBackToBaseMassWhenTableEntryInvalid() {
        var settings = new VehicleBodySettings { baseMass = 2f, massByHealthLevel = new float[0] };
        Assert.AreEqual(2f, settings.ResolveMass(0), 0.0001f); // empty table
        Assert.AreEqual(2f, settings.ResolveMass(5), 0.0001f); // out of range

        settings.massByHealthLevel = new float[] { 2.5f, float.NaN, 0f, -1f, 4f };
        Assert.AreEqual(2.5f, settings.ResolveMass(0), 0.0001f); // valid override
        Assert.AreEqual(2f, settings.ResolveMass(1), 0.0001f); // NaN falls back to baseMass
        Assert.AreEqual(2f, settings.ResolveMass(2), 0.0001f); // zero falls back
        Assert.AreEqual(2f, settings.ResolveMass(3), 0.0001f); // negative falls back
        Assert.AreEqual(4f, settings.ResolveMass(4), 0.0001f); // valid override further in the table

        var invalidBase = new VehicleBodySettings { baseMass = float.NaN, massByHealthLevel = new float[0] };
        Assert.AreEqual(1f, invalidBase.ResolveMass(0), 0.0001f); // ultimate fallback when even baseMass is invalid
    }

    (float targetDisplacement, float targetSpeedAfter) RunPushScenario(float pusherMass, float targetMass, float approachSpeed) {
        var pusher = trafficPhysicsWorld.CreateBody("PushScenarioPusher", new Vector2(0f, -3f)).gameObject;
        var target = trafficPhysicsWorld.CreateBody("PushScenarioTarget", Vector2.zero).gameObject;
        try {
            var pusherRb = pusher.GetComponent<Rigidbody2D>();
            var targetRb = target.GetComponent<Rigidbody2D>();
            pusherRb.mass = pusherMass;
            targetRb.mass = targetMass;
            pusherRb.linearVelocity = new Vector2(0f, approachSpeed);
            targetRb.linearVelocity = Vector2.zero;

            Vector2 targetStart = targetRb.position;
            for (int i = 0; i < 40; i++) trafficPhysicsWorld.Step(); // local contact physics only; no motors in this fixture
            float displacement = Vector2.Distance(targetStart, targetRb.position);
            return (displacement, targetRb.linearVelocity.magnitude);
        } finally {
            Object.DestroyImmediate(pusher);
            Object.DestroyImmediate(target);
        }
    }

    /// <summary>Verifies a heavy target is not dragged around as easily as an equal-mass one under the identical impact — real Physics2D collision resolution, not a hand-authored formula.</summary>
    [Test]
    public void PushPhysics_HeavyTargetMovesLessThanEqualMassTargetUnderIdenticalImpact() {
        RunPhysicsScenario(0.02f, () => {
            var equalMassResult = RunPushScenario(pusherMass: 1f, targetMass: 1f, approachSpeed: 8f);
            var heavyTargetResult = RunPushScenario(pusherMass: 1f, targetMass: 10f, approachSpeed: 8f);
            Assert.Greater(equalMassResult.targetDisplacement, heavyTargetResult.targetDisplacement * 2f,
                "a heavy target (e.g. a police jeep) must not be pushed around by a light vehicle nearly as much as an equal-mass one is");
        });
    }

    /// <summary>Verifies a sufficiently heavy/upgraded pusher meaningfully pushes a heavy target farther than a light pusher does — mass alone in the Inspector is not the acceptance criterion, this is real displacement/velocity evidence.</summary>
    [Test]
    public void PushPhysics_SufficientlyHeavyPusherMovesHeavyTargetMeaningfullyMoreThanLightPusherDoes() {
        RunPhysicsScenario(0.02f, () => {
            var lightPusherResult = RunPushScenario(pusherMass: 1f, targetMass: 10f, approachSpeed: 8f);
            var heavyPusherResult = RunPushScenario(pusherMass: 8f, targetMass: 10f, approachSpeed: 8f);
            Assert.Greater(heavyPusherResult.targetDisplacement, lightPusherResult.targetDisplacement * 1.5f,
                "a heavy/upgraded enough vehicle must meaningfully push a heavy target farther than a light one manages");
            Assert.Greater(heavyPusherResult.targetSpeedAfter, lightPusherResult.targetSpeedAfter * 1.5f);
        });
    }

    static DamageContext MakeDamageContext(int sourceLifeId, InstigatorKind instigator, int instigatorLifeId, DamageKind kind = DamageKind.Collision) {
        return new DamageContext("evt-1", sourceLifeId, instigator, instigatorLifeId, kind, "incident-1");
    }

    /// <summary>Verifies two lethal hits arriving in the same frame produce exactly one destruction event — the second is a no-op, not a double-kill.</summary>
    [Test]
    public void NpcVehicleHealth_TwoLethalHitsSameFrameProduceExactlyOneDestruction() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 50f, newLifeId: 7);

        bool firstKilled = health.ApplyDamage(7, 100f, MakeDamageContext(7, InstigatorKind.Player, 1), VehicleRole.Civilian, 10f, out var firstEvent);
        bool secondKilled = health.ApplyDamage(7, 100f, MakeDamageContext(7, InstigatorKind.Player, 1), VehicleRole.Civilian, 10f, out var secondEvent);

        Assert.IsTrue(firstKilled);
        Assert.AreEqual(7, firstEvent.victimLifeId);
        Assert.IsFalse(secondKilled, "a second lethal hit against an already-destroyed life must be a no-op");
        Assert.AreEqual(0f, health.CurrentHealth);
        Assert.IsTrue(health.IsDestroyed);
    }

    /// <summary>Verifies a stale lifeId (from before a pool reuse) can never damage or kill the reused instance's new life.</summary>
    [Test]
    public void NpcVehicleHealth_StaleLifeIdFromPreviousLifeNeverAppliesToReusedInstance() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 50f, newLifeId: 1); // old life
        health.InitializeForNewLife(authoredMaxHealth: 80f, newLifeId: 2); // pool reuse: fresh life, fresh lifeId

        bool killed = health.ApplyDamage(1, 1000f, MakeDamageContext(1, InstigatorKind.Player, 1), VehicleRole.Civilian, 5f, out _);
        Assert.IsFalse(killed, "damage addressed to the old lifeId must never touch the new life");
        Assert.AreEqual(80f, health.CurrentHealth, "the new life's full health must be untouched");
        Assert.IsFalse(health.IsDestroyed);
    }

    /// <summary>Verifies health clamps to [0, maxHealth] and never goes negative under overkill damage.</summary>
    [Test]
    public void NpcVehicleHealth_HealthClampsAndNeverGoesNegative() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 30f, newLifeId: 3);
        health.ApplyDamage(3, 99999f, MakeDamageContext(3, InstigatorKind.Environment, -1), VehicleRole.Police, 1f, out _);
        Assert.AreEqual(0f, health.CurrentHealth);
        Assert.GreaterOrEqual(health.CurrentHealth, 0f);
    }

    /// <summary>Verifies a session marked inactive never accepts damage, even a normally-lethal amount.</summary>
    [Test]
    public void NpcVehicleHealth_InactiveSessionRejectsAllDamage() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 40f, newLifeId: 4);
        health.SetSessionActive(false);

        bool killed = health.ApplyDamage(4, 999f, MakeDamageContext(4, InstigatorKind.Police, 9), VehicleRole.Civilian, 2f, out _);
        Assert.IsFalse(killed);
        Assert.AreEqual(40f, health.CurrentHealth);
        Assert.IsFalse(health.IsDestroyed);
    }

    /// <summary>Verifies InitializeForNewLife never mutates a shared authored value — each life reads its own runtime copy, so re-initializing with a different value cannot leak the old value forward.</summary>
    [Test]
    public void NpcVehicleHealth_InitializeForNewLifeNeverLeaksPreviousLifesRuntimeValue() {
        float authoredMaxHealth = 60f;
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth, newLifeId: 1);
        health.ApplyDamage(1, 40f, MakeDamageContext(1, InstigatorKind.Civilian, 1), VehicleRole.Civilian, 1f, out _);
        Assert.AreEqual(20f, health.CurrentHealth);

        health.InitializeForNewLife(authoredMaxHealth, newLifeId: 2); // pool reuse with the SAME authored SO value
        Assert.AreEqual(60f, health.CurrentHealth, "the authored value itself must be untouched by the previous life's damage");
        Assert.AreEqual(60f, authoredMaxHealth); // the caller's own authored float, unmutated by construction (a float is passed by value; this documents the guarantee explicitly)
    }

    static NpcCollisionDamageProfile MakeCollisionDamageProfile() {
        return new NpcCollisionDamageProfile {
            minimumImpactSpeed = 1.5f, damageCooldownSeconds = 0.5f, damageBase = 3f, damageFactor = 0.85f, damageExponent = 2f
        };
    }

    /// <summary>Verifies a stopped/parked contact (zero impact speed) is below the dead-zone and never damages.</summary>
    [Test]
    public void NpcCollisionDamage_StoppedContactBelowDeadZoneNeverDamages() {
        var profile = MakeCollisionDamageProfile();
        bool applied = NpcCollisionDamage.TryCalculateDamage(profile, impactSpeed: 0f, armorPercent: 0f,
            lastDamageSessionTime: -999f, sessionTime: 0f, out float damage);
        Assert.IsFalse(applied);
        Assert.AreEqual(0f, damage);
    }

    /// <summary>Verifies a fast side-scrape (low contact-normal closing speed) and a head-on hit (high closing speed) — computed via the same shared VehicleDrivingMath.CalculateClosingSpeed the player uses — are treated differently: the scrape stays under the dead-zone while the head-on hit deals real damage.</summary>
    [Test]
    public void NpcCollisionDamage_FastSideScrapeAndHeadOnHitAreTreatedDifferently() {
        var profile = MakeCollisionDamageProfile();

        // A fast side-scrape: relative velocity runs mostly parallel to the contact normal.
        Vector2 scrapeRelativeVelocity = new Vector2(10f, 0.2f);
        Vector2 scrapeNormal = new Vector2(0f, 1f);
        float scrapeImpactSpeed = VehicleDrivingMath.CalculateClosingSpeed(scrapeRelativeVelocity, scrapeNormal);
        bool scrapeApplied = NpcCollisionDamage.TryCalculateDamage(profile, scrapeImpactSpeed, 0f, -999f, 0f, out float scrapeDamage);

        // A head-on hit: relative velocity runs straight into the contact normal.
        Vector2 headOnRelativeVelocity = new Vector2(0f, 10f);
        Vector2 headOnNormal = new Vector2(0f, 1f);
        float headOnImpactSpeed = VehicleDrivingMath.CalculateClosingSpeed(headOnRelativeVelocity, headOnNormal);
        bool headOnApplied = NpcCollisionDamage.TryCalculateDamage(profile, headOnImpactSpeed, 0f, -999f, 0f, out float headOnDamage);

        Assert.Less(scrapeImpactSpeed, headOnImpactSpeed, "a parallel scrape must yield a lower contact-normal closing speed than a head-on hit at the same raw speed");
        Assert.IsFalse(scrapeApplied, "the scrape's low closing speed must stay under the dead-zone");
        Assert.IsTrue(headOnApplied);
        Assert.Greater(headOnDamage, 0f);
    }

    /// <summary>Verifies a sustained CollisionStay never spams damage — only the first hit in a cooldown window applies, and separating (cooldown elapsing) allows a genuinely new hit.</summary>
    [Test]
    public void NpcCollisionDamage_SustainedContactNeverSpamsAndSeparatingAllowsNewHit() {
        var profile = MakeCollisionDamageProfile();
        float lastDamageTime = -999f;

        bool first = NpcCollisionDamage.TryCalculateDamage(profile, 10f, 0f, lastDamageTime, sessionTime: 0f, out float firstDamage);
        Assert.IsTrue(first);
        lastDamageTime = 0f;

        // Same contact keeps calling every physics tick while cooldown hasn't elapsed.
        bool secondSameTick = NpcCollisionDamage.TryCalculateDamage(profile, 10f, 0f, lastDamageTime, sessionTime: 0.1f, out _);
        bool thirdStillWithinCooldown = NpcCollisionDamage.TryCalculateDamage(profile, 10f, 0f, lastDamageTime, sessionTime: 0.4f, out _);
        Assert.IsFalse(secondSameTick);
        Assert.IsFalse(thirdStillWithinCooldown);

        // Cooldown elapsed: a genuinely new hit applies again.
        bool fourthAfterCooldown = NpcCollisionDamage.TryCalculateDamage(profile, 10f, 0f, lastDamageTime, sessionTime: 0.6f, out float fourthDamage);
        Assert.IsTrue(fourthAfterCooldown);
        Assert.AreEqual(firstDamage, fourthDamage, 0.0001f);
    }

    /// <summary>Verifies the collision-damage-to-health pipeline: a survivable hit leaves the vehicle alive (free to signal recovery via NpcVehicleInstance), while a severe enough hit kills in one shot — and neither path ever touches EndLevel/ScoreHandler (grep-verified: no reference exists in either source file).</summary>
    [Test]
    public void NpcCollisionDamage_SurvivableHitLeavesAliveAndSevereHitKillsInOneShotNeverCallingEndLevel() {
        var profile = MakeCollisionDamageProfile();
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 100f, newLifeId: 1);

        NpcCollisionDamage.TryCalculateDamage(profile, impactSpeed: 4f, armorPercent: 0f, -999f, 0f, out float mildDamage);
        bool killedByMildHit = health.ApplyDamage(1, mildDamage, MakeDamageContext(1, InstigatorKind.Environment, -1), VehicleRole.Civilian, 0f, out _);
        Assert.IsFalse(killedByMildHit);
        Assert.Greater(health.CurrentHealth, 0f);
        Assert.Less(health.CurrentHealth, 100f);

        NpcCollisionDamage.TryCalculateDamage(profile, impactSpeed: 20f, armorPercent: 0f, -999f, 1f, out float severeDamage);
        bool killedBySevereHit = health.ApplyDamage(1, severeDamage, MakeDamageContext(1, InstigatorKind.Player, 2), VehicleRole.Civilian, 1f, out var destroyedEvent);
        Assert.IsTrue(killedBySevereHit, "a sufficiently severe crash must be able to kill in one hit");
        Assert.AreEqual(0f, health.CurrentHealth);
        Assert.AreEqual(1, destroyedEvent.victimLifeId);
    }

    static DamageAttributionRules MakeAttributionRules() {
        return new DamageAttributionRules { minimumApproachThreshold = 0.5f, attributionDeltaTolerance = 0.5f };
    }

    /// <summary>Verifies the four worked examples from contracts §6 exactly: a moving rammer vs a stationary victim is blamed (Player or Police), an equal head-on is Environment, and a stationary Player next to a reversing Police is Police, not Player.</summary>
    [Test]
    public void DamageAttributionResolver_MatchesContractWorkedExamples() {
        var rules = MakeAttributionRules();
        Vector2 point = Vector2.zero;
        Vector2 normal = Vector2.right; // n = +X

        var playerRamming = new ContactParticipant(1, InstigatorKind.Player, new Vector2(6f, 0f), 0f, Vector2.zero);
        var stationaryCivilianB = new ContactParticipant(2, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f));
        Assert.AreEqual(InstigatorKind.Player, DamageAttributionResolver.ResolveFault(playerRamming, stationaryCivilianB, point, normal, rules));

        var policeRamming = new ContactParticipant(3, InstigatorKind.Police, new Vector2(6f, 0f), 0f, Vector2.zero);
        var stationaryCivilianB2 = new ContactParticipant(4, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f));
        Assert.AreEqual(InstigatorKind.Police, DamageAttributionResolver.ResolveFault(policeRamming, stationaryCivilianB2, point, normal, rules));

        var aApproaching = new ContactParticipant(5, InstigatorKind.Civilian, new Vector2(6f, 0f), 0f, Vector2.zero);
        var bApproaching = new ContactParticipant(6, InstigatorKind.Civilian, new Vector2(-6f, 0f), 0f, new Vector2(1f, 0f));
        Assert.AreEqual(InstigatorKind.Environment, DamageAttributionResolver.ResolveFault(aApproaching, bApproaching, point, normal, rules), "an equal head-on approach must be ruled Environment, not blamed on whoever's callback fires first");

        var stationaryPlayer = new ContactParticipant(7, InstigatorKind.Player, Vector2.zero, 0f, Vector2.zero);
        var reversingPolice = new ContactParticipant(8, InstigatorKind.Police, new Vector2(-6f, 0f), 0f, new Vector2(1f, 0f));
        Assert.AreEqual(InstigatorKind.Police, DamageAttributionResolver.ResolveFault(stationaryPlayer, reversingPolice, point, normal, rules), "a stationary Player next to a reversing Police must never be blamed");
    }

    /// <summary>Verifies the result never depends on which side is passed as X vs Y (callback order) — swapping the arguments and negating the normal accordingly yields the identical fault.</summary>
    [Test]
    public void DamageAttributionResolver_NeverDependsOnCallbackArgumentOrder() {
        var rules = MakeAttributionRules();
        var ramming = new ContactParticipant(10, InstigatorKind.Police, new Vector2(8f, 0f), 0f, Vector2.zero);
        var victim = new ContactParticipant(20, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f));

        InstigatorKind normalOrder = DamageAttributionResolver.ResolveFault(ramming, victim, Vector2.zero, Vector2.right, rules);
        InstigatorKind swappedOrder = DamageAttributionResolver.ResolveFault(victim, ramming, Vector2.zero, Vector2.left, rules); // swapped args AND swapped normal, as a real engine callback would report

        Assert.AreEqual(InstigatorKind.Police, normalOrder);
        Assert.AreEqual(normalOrder, swappedOrder);
    }

    /// <summary>Verifies a police vehicle hitting a static wall on its own is blamed on Police (or Environment if truly ambiguous) — never on the Player, who is not even a contact participant here, regardless of any pursuit context.</summary>
    [Test]
    public void DamageAttributionResolver_PoliceHittingStaticWallIsNeverBlamedOnAbsentPlayer() {
        var rules = MakeAttributionRules();
        var policeChasing = new ContactParticipant(30, InstigatorKind.Police, new Vector2(0f, 7f), 0f, Vector2.zero);
        var staticWall = new ContactParticipant(31, InstigatorKind.Environment, Vector2.zero, 0f, new Vector2(0f, 1f), isStaticWorld: true);

        InstigatorKind fault = DamageAttributionResolver.ResolveFault(policeChasing, staticWall, Vector2.zero, Vector2.up, rules);
        Assert.AreEqual(InstigatorKind.Police, fault);
        Assert.AreNotEqual(InstigatorKind.Player, fault);
    }

    /// <summary>Verifies two contacts are resolved fully independently — an earlier non-lethal Player touch on a civilian is never carried forward to overwrite a later, separate, actually-lethal Police collision's own fault.</summary>
    [Test]
    public void DamageAttributionResolver_LaterLethalPoliceCollisionIsNeverOverwrittenByEarlierPlayerTouch() {
        var rules = MakeAttributionRules();

        var earlierPlayerTouch = new ContactParticipant(40, InstigatorKind.Player, new Vector2(2f, 0f), 0f, Vector2.zero);
        var civilianDuringTouch = new ContactParticipant(41, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f));
        InstigatorKind earlierFault = DamageAttributionResolver.ResolveFault(earlierPlayerTouch, civilianDuringTouch, Vector2.zero, Vector2.right, rules);
        Assert.AreEqual(InstigatorKind.Player, earlierFault); // this touch alone was Player's fault, but it was non-lethal and discarded

        var laterPolice = new ContactParticipant(42, InstigatorKind.Police, new Vector2(9f, 0f), 0f, Vector2.zero);
        var civilianAtDeath = new ContactParticipant(41, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f)); // same victim lifeId, fresh contact
        InstigatorKind killingFault = DamageAttributionResolver.ResolveFault(laterPolice, civilianAtDeath, Vector2.zero, Vector2.right, rules);
        Assert.AreEqual(InstigatorKind.Police, killingFault, "the killing blow's own fault must be used, never the earlier discarded touch");
    }

    /// <summary>Verifies two moving vehicles with unequal contact-point contributions correctly blame the clearly-closing side, not just "someone was moving".</summary>
    [Test]
    public void DamageAttributionResolver_TwoMovingVehiclesWithUnequalContributionsBlamesTheStrongerCloser() {
        var rules = MakeAttributionRules();
        var fastCloser = new ContactParticipant(50, InstigatorKind.Civilian, new Vector2(8f, 0f), 0f, Vector2.zero);
        var slowCloser = new ContactParticipant(51, InstigatorKind.Police, new Vector2(-1f, 0f), 0f, new Vector2(1f, 0f));

        InstigatorKind fault = DamageAttributionResolver.ResolveFault(fastCloser, slowCloser, Vector2.zero, Vector2.right, rules);
        Assert.AreEqual(InstigatorKind.Civilian, fault);
    }

    sealed class FakeBlastVictimQuery : IBlastVictimQuery {
        public List<BlastVictim> victims = new List<BlastVictim>();
        public int FindVictimsInRadius(Vector2 origin, float radius, BlastVictim[] buffer) {
            int count = Mathf.Min(victims.Count, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = victims[i];
            return count;
        }
    }

    /// <summary>Verifies distance falloff (0 at/beyond radius), full resistance zeroing damage, and the player's separate low multiplier producing less damage than a civilian under identical conditions.</summary>
    [Test]
    public void BlastDamageMath_DistanceLimitResistanceAndPlayerMultiplierAreCorrect() {
        Assert.AreEqual(1f, BlastDamageMath.ComputeDistanceFalloff(0f, 10f), 0.0001f);
        Assert.AreEqual(0f, BlastDamageMath.ComputeDistanceFalloff(10f, 10f), 0.0001f);
        Assert.AreEqual(0f, BlastDamageMath.ComputeDistanceFalloff(50f, 10f), 0.0001f); // beyond radius clamps, never negative

        float fullResistanceDamage = BlastDamageMath.CalculateBlastDamage(100f, 0f, 10f, 1f, explosionResistance: 1f);
        Assert.AreEqual(0f, fullResistanceDamage, 0.0001f);

        var multipliers = new BlastRoleMultipliers { civilianMultiplier = 1f, playerMultiplier = 0.3f };
        float civilianDamage = BlastDamageMath.CalculateBlastDamage(100f, 0f, 10f, multipliers.Resolve(VehicleRole.Civilian), 0f);
        float playerDamage = BlastDamageMath.CalculateBlastDamage(100f, 0f, 10f, multipliers.Resolve(VehicleRole.Player), 0f);
        Assert.Less(playerDamage, civilianDamage);
        Assert.AreEqual(30f, playerDamage, 0.0001f);
    }

    /// <summary>Verifies a vehicle reported via three colliders (three BlastVictim entries sharing one lifeId) takes exactly one hit from a single blast.</summary>
    [Test]
    public void VehicleExplosionService_OneVehicleWithThreeCollidersTakesExactlyOneHit() {
        var query = new FakeBlastVictimQuery();
        query.victims.Add(new BlastVictim(5, VehicleRole.Civilian, Vector2.zero, 0f));
        query.victims.Add(new BlastVictim(5, VehicleRole.Civilian, Vector2.zero, 0f));
        query.victims.Add(new BlastVictim(5, VehicleRole.Civilian, Vector2.zero, 0f));
        var service = new VehicleExplosionService(query, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 10);
        service.EnqueueBlast(new PendingBlast("blast-1", Vector2.zero, 100f, 10f, MakeDamageContext(5, InstigatorKind.Player, 1)));

        var applications = service.ProcessTick(new BlastVictim[8]);
        Assert.AreEqual(1, applications.Count);
        Assert.AreEqual(5, applications[0].victimLifeId);
    }

    /// <summary>Verifies two separate blasts against the same victim are two separate events, each applying its own damage — dedupe is scoped to one blast, never shared across blasts.</summary>
    [Test]
    public void VehicleExplosionService_TwoSeparateBlastsAgainstSameVictimBothApply() {
        var query = new FakeBlastVictimQuery();
        query.victims.Add(new BlastVictim(7, VehicleRole.Civilian, Vector2.zero, 0f));
        var service = new VehicleExplosionService(query, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 10);

        service.EnqueueBlast(new PendingBlast("blast-A", Vector2.zero, 50f, 10f, MakeDamageContext(7, InstigatorKind.Player, 1)));
        var firstApplications = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(1, firstApplications.Count);
        Assert.AreEqual("blast-A", firstApplications[0].blastId);

        service.EnqueueBlast(new PendingBlast("blast-B", Vector2.zero, 50f, 10f, MakeDamageContext(7, InstigatorKind.Player, 1)));
        var secondApplications = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(1, secondApplications.Count);
        Assert.AreEqual("blast-B", secondApplications[0].blastId);
    }

    /// <summary>Verifies per-tick work is bounded by the authored budget, and anything beyond it stays queued for a later tick rather than being dropped.</summary>
    [Test]
    public void VehicleExplosionService_BoundedPerTickWorkNeverDropsExcessQueuedBlasts() {
        var query = new FakeBlastVictimQuery();
        query.victims.Add(new BlastVictim(1, VehicleRole.Civilian, Vector2.zero, 0f));
        var service = new VehicleExplosionService(query, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 2);

        for (int i = 0; i < 5; i++) service.EnqueueBlast(new PendingBlast("blast-" + i, Vector2.zero, 10f, 10f, MakeDamageContext(1, InstigatorKind.Environment, -1)));
        Assert.AreEqual(5, service.PendingCount);

        var firstTick = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(2, firstTick.Count, "only the authored per-tick budget may process in one tick");
        Assert.AreEqual(3, service.PendingCount, "the rest must stay queued, never dropped");

        var secondTick = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(2, secondTick.Count);
        Assert.AreEqual(1, service.PendingCount);

        var thirdTick = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(1, thirdTick.Count);
        Assert.AreEqual(0, service.PendingCount);
    }

    /// <summary>Verifies a long blast chain (each processed blast's result driving the next blast, via an explicit loop of EnqueueBlast/ProcessTick calls — never direct recursion) completes without a stack overflow and applies exactly one hit per link.</summary>
    [Test]
    public void VehicleExplosionService_LongChainNeverRecursesAndCompletesSafely() {
        var query = new FakeBlastVictimQuery();
        query.victims.Add(new BlastVictim(1, VehicleRole.Civilian, Vector2.zero, 0f));
        var service = new VehicleExplosionService(query, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 1000);

        int totalApplications = 0;
        for (int i = 0; i < 500; i++) {
            // Each "link" enqueues the next blast in the chain itself, exactly as a real death->new-explosion chain would — driven by this loop, never by a recursive method call.
            service.EnqueueBlast(new PendingBlast("chain-" + i, Vector2.zero, 10f, 10f, MakeDamageContext(1, InstigatorKind.Player, 1)));
            var applications = service.ProcessTick(new BlastVictim[4]);
            totalApplications += applications.Count;
        }

        Assert.AreEqual(500, totalApplications);
        Assert.AreEqual(0, service.PendingCount);
    }

    Driver MakeInertDriver() {
        // SetActive(false) before AddComponent suppresses Awake/Start entirely (established BF-021
        // precaution) — ApplyBlastDamage touches none of Start's GameManager-dependent fields, so
        // this is safe: no real save file, no GameManager singleton access.
        var go = new GameObject("BlastTestDriver");
        go.SetActive(false);
        go.AddComponent<Rigidbody2D>();
        var driver = go.AddComponent<Driver>();
        return driver;
    }

    static void SetPrivateField(object target, string fieldName, object value) {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(target, value);
    }

    static T GetPrivateField<T>(object target, string fieldName) {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field.GetValue(target);
    }

    /// <summary>Verifies a survivable blast reduces HP by exactly the given (already-resistance-adjusted) amount without disabling the driver, while a lethal one triggers HandleDeath exactly once (isDisabled becomes true, HP clamped to zero) — never applying collision armor a second time since ApplyBlastDamage never reads armorPercent at all.</summary>
    [Test]
    public void Driver_ApplyBlastDamage_SurvivableReducesHpAndLethalTriggersDeathOnce() {
        var driver = MakeInertDriver();
        try {
            driver.currentHealth = 100f;
            driver.maxHealth = 100f;

            bool acceptedMild = driver.ApplyBlastDamage(30f);
            Assert.IsTrue(acceptedMild);
            Assert.AreEqual(70f, driver.currentHealth, 0.0001f);
            Assert.IsFalse(driver.IsDisabled);

            SetPrivateField(driver, "lastBlastDamageTime", -999f); // simulate cooldown already elapsed
            bool acceptedLethal = driver.ApplyBlastDamage(999f);
            Assert.IsTrue(acceptedLethal);
            Assert.AreEqual(0f, driver.currentHealth);
            Assert.IsTrue(driver.IsDisabled);

            // A second blast after death must be rejected outright (already disabled).
            SetPrivateField(driver, "lastBlastDamageTime", -999f);
            bool acceptedAfterDeath = driver.ApplyBlastDamage(10f);
            Assert.IsFalse(acceptedAfterDeath);
        } finally {
            Object.DestroyImmediate(driver.gameObject);
        }
    }

    /// <summary>Verifies blast damage has its own cooldown, entirely separate from the collision invulnerability window — a second blast immediately after the first is rejected until that cooldown (simulated by directly advancing lastBlastDamageTime) has actually elapsed.</summary>
    [Test]
    public void Driver_ApplyBlastDamage_HasOwnCooldownSeparateFromCollisionInvulnerability() {
        var driver = MakeInertDriver();
        try {
            driver.currentHealth = 100f;
            driver.maxHealth = 100f;

            Assert.IsTrue(driver.ApplyBlastDamage(10f));
            Assert.IsFalse(driver.ApplyBlastDamage(10f), "a second blast within the blast cooldown window must be rejected");
            Assert.AreEqual(90f, driver.currentHealth, 0.0001f, "the rejected blast must not have applied any damage");

            SetPrivateField(driver, "lastBlastDamageTime", -999f); // simulate the blast cooldown having elapsed
            Assert.IsTrue(driver.ApplyBlastDamage(10f), "once the blast cooldown has elapsed, a new blast applies normally");
            Assert.AreEqual(80f, driver.currentHealth, 0.0001f);
        } finally {
            Object.DestroyImmediate(driver.gameObject);
        }
    }

    static NpcVehicleProfile MakeFeedbackProfile(float smokeFraction, float criticalFraction) {
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.smokeHealthFraction01 = smokeFraction;
        profile.criticalHealthFraction01 = criticalFraction;
        return profile;
    }

    /// <summary>Verifies the feedback tier escalates correctly across full/mid/low health, and degenerate inputs (no profile, zero maxHealth) safely resolve to None rather than throwing.</summary>
    [Test]
    public void NpcDamageFeedbackResolver_EscalatesAcrossThresholdsAndHandlesDegenerateInputs() {
        var profile = MakeFeedbackProfile(smokeFraction: 0.5f, criticalFraction: 0.2f);
        try {
            Assert.AreEqual(NpcDamageFeedbackLevel.None, NpcDamageFeedbackResolver.Resolve(100f, 100f, profile));
            Assert.AreEqual(NpcDamageFeedbackLevel.Smoking, NpcDamageFeedbackResolver.Resolve(40f, 100f, profile));
            Assert.AreEqual(NpcDamageFeedbackLevel.Critical, NpcDamageFeedbackResolver.Resolve(10f, 100f, profile));
            Assert.AreEqual(NpcDamageFeedbackLevel.Critical, NpcDamageFeedbackResolver.Resolve(0f, 100f, profile));

            Assert.AreEqual(NpcDamageFeedbackLevel.None, NpcDamageFeedbackResolver.Resolve(0f, 0f, profile), "zero maxHealth must not throw or misclassify");
            Assert.AreEqual(NpcDamageFeedbackLevel.None, NpcDamageFeedbackResolver.Resolve(10f, 100f, null), "no profile must safely resolve to None");
        } finally {
            Object.DestroyImmediate(profile);
        }
    }

    /// <summary>Verifies destroyed wreck "debris" can never take a second explosion's damage — reusing NpcVehicleHealth's single-death guarantee from S05.1, framed here explicitly around a later, genuinely separate blast rather than a same-frame double-hit.</summary>
    [Test]
    public void NpcVehicleHealth_DestroyedWreckNeverTakesDamageFromALaterSeparateExplosion() {
        var health = new NpcVehicleHealth();
        health.InitializeForNewLife(authoredMaxHealth: 50f, newLifeId: 9);
        bool firstKilled = health.ApplyDamage(9, 999f, MakeDamageContext(9, InstigatorKind.Player, 1, DamageKind.Collision), VehicleRole.Civilian, 0f, out _);
        Assert.IsTrue(firstKilled);

        // A completely separate, later explosion (different eventId/rootIncidentId/damageKind) targeting the same now-wrecked lifeId.
        bool secondBlastKilled = health.ApplyDamage(9, 999f, MakeDamageContext(9, InstigatorKind.Environment, -1, DamageKind.Explosion), VehicleRole.Civilian, 5f, out var secondEvent);
        Assert.IsFalse(secondBlastKilled, "already-wrecked debris must never produce a second explosion/destruction event");
        Assert.AreEqual(default(VehicleDestroyedEvent).victimLifeId, secondEvent.victimLifeId);
    }

    /// <summary>Verifies a pool-reused instance never comes back already smoking/critical from its previous life — InitializeForNewLife's fresh full health resets the feedback tier to None.</summary>
    [Test]
    public void NpcDamageFeedbackResolver_PoolReusedInstanceNeverComesBackAlreadyDamaged() {
        var profile = MakeFeedbackProfile(smokeFraction: 0.5f, criticalFraction: 0.2f);
        var health = new NpcVehicleHealth();
        try {
            health.InitializeForNewLife(100f, newLifeId: 1);
            health.ApplyDamage(1, 95f, MakeDamageContext(1, InstigatorKind.Civilian, 1), VehicleRole.Civilian, 0f, out _);
            Assert.AreEqual(NpcDamageFeedbackLevel.Critical, NpcDamageFeedbackResolver.Resolve(health.CurrentHealth, health.MaxHealth, profile));

            health.InitializeForNewLife(100f, newLifeId: 2); // pool reuse: fresh life
            Assert.AreEqual(NpcDamageFeedbackLevel.None, NpcDamageFeedbackResolver.Resolve(health.CurrentHealth, health.MaxHealth, profile),
                "a freshly reused instance must never come back already smoking/critical from the previous life");
        } finally {
            Object.DestroyImmediate(profile);
        }
    }

    static HeatAwardRules MakeHeatRules() {
        return new HeatAwardRules { civilianVictimHeat = 10f, policeVictimHeat = 25f };
    }

    /// <summary>Verifies the heat truth table: every police victim awards heat, while civilian victims require a Player root attribution.</summary>
    [Test]
    public void HeatPayloadResolver_MatchesContractTruthTable() {
        var rules = MakeHeatRules();

        var nullRulesEvent = new VehicleDestroyedEvent(0, VehicleRole.Police, MakeDamageContext(0, InstigatorKind.Environment, -1), 0f);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(nullRulesEvent, null, out float nullRulesHeat));
        Assert.AreEqual(0f, nullRulesHeat);

        var playerKilledCivilian = new VehicleDestroyedEvent(1, VehicleRole.Civilian, MakeDamageContext(1, InstigatorKind.Player, 99), 0f);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(playerKilledCivilian, rules, out float civilianHeat));
        Assert.AreEqual(10f, civilianHeat);

        var playerKilledPolice = new VehicleDestroyedEvent(2, VehicleRole.Police, MakeDamageContext(2, InstigatorKind.Player, 99), 0f);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(playerKilledPolice, rules, out float policeHeat));
        Assert.AreEqual(25f, policeHeat);

        var policeKilledCivilian = new VehicleDestroyedEvent(3, VehicleRole.Civilian, MakeDamageContext(3, InstigatorKind.Police, 50), 0f);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(policeKilledCivilian, rules, out float noHeatFromPolice));
        Assert.AreEqual(0f, noHeatFromPolice);

        var civilianSelfAccident = new VehicleDestroyedEvent(4, VehicleRole.Civilian, MakeDamageContext(4, InstigatorKind.Civilian, 4), 0f);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(civilianSelfAccident, rules, out _));

        var environmentCause = new VehicleDestroyedEvent(5, VehicleRole.Civilian, MakeDamageContext(5, InstigatorKind.Environment, -1), 0f);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(environmentCause, rules, out _));

        var policeKilledByPolice = new VehicleDestroyedEvent(6, VehicleRole.Police, MakeDamageContext(6, InstigatorKind.Police, 50), 0f);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(policeKilledByPolice, rules, out float policeCauseHeat));
        Assert.AreEqual(25f, policeCauseHeat);

        var policeKilledByEnvironment = new VehicleDestroyedEvent(7, VehicleRole.Police, MakeDamageContext(7, InstigatorKind.Environment, -1), 0f);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(policeKilledByEnvironment, rules, out float environmentHeat));
        Assert.AreEqual(25f, environmentHeat);

        var policeKilledByCivilian = new VehicleDestroyedEvent(8, VehicleRole.Police, MakeDamageContext(8, InstigatorKind.Civilian, 60), 0f);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(policeKilledByCivilian, rules, out float civilianCauseHeat));
        Assert.AreEqual(25f, civilianCauseHeat);

        var playerVictim = new VehicleDestroyedEvent(9, VehicleRole.Player, MakeDamageContext(9, InstigatorKind.Player, 99), 0f);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(playerVictim, rules, out _));
    }

    /// <summary>Verifies one victim life is deduplicated even when its first destruction cause carries no heat.</summary>
    [Test]
    public void PoliceIncidentLedger_DeduplicatesEveryVictimLifeIncludingZeroHeatEvents() {
        var ledger = new PoliceIncidentLedger();
        var rules = MakeHeatRules();
        var first = new VehicleDestroyedEvent(301, VehicleRole.Civilian, MakeDamageContext(301, InstigatorKind.Environment, -1), 0f);
        var duplicateWithPlayerCause = new VehicleDestroyedEvent(301, VehicleRole.Civilian, MakeDamageContext(301, InstigatorKind.Player, 99), 1f);

        Assert.IsFalse(ledger.TryRecord(first, rules, out float firstHeat));
        Assert.AreEqual(0f, firstHeat);
        Assert.IsFalse(ledger.TryRecord(duplicateWithPlayerCause, rules, out float duplicateHeat));
        Assert.AreEqual(0f, duplicateHeat);
        Assert.AreEqual(1, ledger.RecordedIncidentCount);

        var policeDeath = new VehicleDestroyedEvent(302, VehicleRole.Police, MakeDamageContext(302, InstigatorKind.Environment, -1), 2f);
        Assert.IsTrue(ledger.TryRecord(policeDeath, rules, out float policeHeat));
        Assert.AreEqual(25f, policeHeat);
        Assert.IsFalse(ledger.TryRecord(policeDeath, rules, out float duplicatePoliceHeat));
        Assert.AreEqual(0f, duplicatePoliceHeat);
        Assert.AreEqual(2, ledger.RecordedIncidentCount);
    }

    /// <summary>Verifies NarrowDistrict keeps small active-time heat growth and the first request remains gated at 15 seconds.</summary>
    [Test]
    public void PoliceDirectorRuntime_NarrowDistrictKeepsTimeGrowthAndSafeFirstSpawnGate() {
        var data = UnityEditor.AssetDatabase.LoadAssetAtPath<PoliceDirectorData>("Assets/ScriptableObjects/Police/PoliceDirector_NarrowDistrict.asset");
        Assert.IsNotNull(data);
        Assert.AreEqual(0.1f, data.baseHeatPerActiveSecond, 0.0001f);
        var runtime = new PoliceDirectorRuntime(data, policeIsEnabled: true, seed: 17);

        runtime.Tick(14.9f);
        Assert.AreEqual(1.49f, runtime.BaseHeat, 0.0001f);
        Assert.AreEqual(0, runtime.PendingCount);
        runtime.Tick(15f);
        Assert.AreEqual(1, runtime.PendingCount);
        Assert.IsTrue(runtime.TryDequeue(out var request));
        Assert.AreEqual(15f, request.requestedAtSeconds, 0.0001f);
    }

    /// <summary>Verifies the retained pursuit-loss compatibility API never reports relief or decays heat.</summary>
    [Test]
    public void PolicePursuitLossRules_RetainedReliefScaffoldNeverChangesPersistentHeat() {
        var settings = new PolicePursuitLossSettings {
            minimumLostDistance = 1f,
            lostDurationSeconds = 1f,
            incidentHeatDecayPerSecond = 100f
        };

        Assert.IsFalse(PolicePursuitLossRules.IsReliefReady(PolicePursuitLossPolicy.ReliefAfterEscape, settings, 100f, 100f));
        Assert.AreEqual(25f, PolicePursuitLossRules.DecayIncidentHeat(25f, settings, 100f), 0.0001f);
    }

    /// <summary>Verifies a Player→Police→Civilian chain keeps its Player root through every hop (DamageContextFactory propagation) and awards heat for every new victim — the headline "player chain" scenario from S05.7's own doğrula text.</summary>
    [Test]
    public void HeatPayloadResolver_PlayerToPoliceToCivilianChainKeepsPlayerRootAndAwardsHeatEachHop() {
        var rules = MakeHeatRules();
        var attributionRules = MakeAttributionRules();

        // Player rams Police: fault resolves to Player via the same S05.3 mechanism.
        var playerRam = new ContactParticipant(100, InstigatorKind.Player, new Vector2(6f, 0f), 0f, Vector2.zero);
        var policeVictim = new ContactParticipant(101, InstigatorKind.Police, Vector2.zero, 0f, new Vector2(1f, 0f));
        InstigatorKind firstFault = DamageAttributionResolver.ResolveFault(playerRam, policeVictim, Vector2.zero, Vector2.right, attributionRules);
        Assert.AreEqual(InstigatorKind.Player, firstFault);

        var context1 = new DamageContext("evt-1", 101, firstFault, 100, DamageKind.Collision, "incident-A");
        var policeHealth = new NpcVehicleHealth();
        policeHealth.InitializeForNewLife(50f, newLifeId: 101);
        bool policeDied = policeHealth.ApplyDamage(101, 999f, context1, VehicleRole.Police, 0f, out var policeDestroyed);
        Assert.IsTrue(policeDied);
        Assert.AreEqual(InstigatorKind.Player, policeDestroyed.killingContext.instigatorKind);
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(policeDestroyed, rules, out float policeHeat));
        Assert.AreEqual(25f, policeHeat);

        // The police's death chain-triggers a second explosion; the new context propagates the SAME root incident and instigator.
        var context2 = DamageContextFactory.CreateChainedContext(policeDestroyed.killingContext, "evt-2", 101);
        Assert.AreEqual("incident-A", context2.rootIncidentId);
        Assert.AreEqual(InstigatorKind.Player, context2.instigatorKind);

        var civilianHealth = new NpcVehicleHealth();
        civilianHealth.InitializeForNewLife(50f, newLifeId: 102);
        bool civilianDied = civilianHealth.ApplyDamage(102, 999f, context2, VehicleRole.Civilian, 1f, out var civilianDestroyed);
        Assert.IsTrue(civilianDied);
        Assert.AreEqual(InstigatorKind.Player, civilianDestroyed.killingContext.instigatorKind, "the chain's root cause (Player) must still be traceable two hops later");
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(civilianDestroyed, rules, out float civilianHeat));
        Assert.AreEqual(10f, civilianHeat);
    }

    /// <summary>Verifies a Police→Civilian→Police chain keeps its Police root: the civilian is ignored but every police victim awards heat.</summary>
    [Test]
    public void HeatPayloadResolver_PoliceToCivilianToPoliceChainKeepsPoliceRootAndAwardsPoliceHeat() {
        var rules = MakeHeatRules();
        var attributionRules = MakeAttributionRules();

        var policeRam = new ContactParticipant(200, InstigatorKind.Police, new Vector2(6f, 0f), 0f, Vector2.zero);
        var civilianVictim = new ContactParticipant(201, InstigatorKind.Civilian, Vector2.zero, 0f, new Vector2(1f, 0f));
        InstigatorKind firstFault = DamageAttributionResolver.ResolveFault(policeRam, civilianVictim, Vector2.zero, Vector2.right, attributionRules);
        Assert.AreEqual(InstigatorKind.Police, firstFault);

        var context1 = new DamageContext("evt-1", 201, firstFault, 200, DamageKind.Collision, "incident-B");
        var civilianHealth = new NpcVehicleHealth();
        civilianHealth.InitializeForNewLife(50f, newLifeId: 201);
        civilianHealth.ApplyDamage(201, 999f, context1, VehicleRole.Civilian, 0f, out var civilianDestroyed);
        Assert.AreEqual(InstigatorKind.Police, civilianDestroyed.killingContext.instigatorKind);
        Assert.IsFalse(HeatPayloadResolver.TryResolveHeat(civilianDestroyed, rules, out _));

        var context2 = DamageContextFactory.CreateChainedContext(civilianDestroyed.killingContext, "evt-2", 201);
        Assert.AreEqual("incident-B", context2.rootIncidentId);
        Assert.AreEqual(InstigatorKind.Police, context2.instigatorKind);

        var secondPoliceHealth = new NpcVehicleHealth();
        secondPoliceHealth.InitializeForNewLife(50f, newLifeId: 202);
        secondPoliceHealth.ApplyDamage(202, 999f, context2, VehicleRole.Police, 1f, out var secondPoliceDestroyed);
        Assert.AreEqual(InstigatorKind.Police, secondPoliceDestroyed.killingContext.instigatorKind, "the chain's root cause (Police) must still be traceable two hops later");
        Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(secondPoliceDestroyed, rules, out float secondPoliceHeat));
        Assert.AreEqual(25f, secondPoliceHeat);
    }

    /// <summary>Verifies ending the session mid-queue cancels every still-pending blast — nothing queued before end resolves after it, and nothing new can be queued once inactive.</summary>
    [Test]
    public void VehicleExplosionService_SessionEndCancelsPendingQueueAndRejectsNewBlasts() {
        var query = new FakeBlastVictimQuery();
        query.victims.Add(new BlastVictim(1, VehicleRole.Civilian, Vector2.zero, 0f));
        var service = new VehicleExplosionService(query, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 10);

        service.EnqueueBlast(new PendingBlast("b1", Vector2.zero, 10f, 10f, MakeDamageContext(1, InstigatorKind.Player, 1)));
        service.EnqueueBlast(new PendingBlast("b2", Vector2.zero, 10f, 10f, MakeDamageContext(1, InstigatorKind.Player, 1)));
        Assert.AreEqual(2, service.PendingCount);

        service.SetSessionActive(false);
        Assert.AreEqual(0, service.PendingCount, "ending the session must cancel every still-pending blast");

        service.EnqueueBlast(new PendingBlast("b3", Vector2.zero, 10f, 10f, MakeDamageContext(1, InstigatorKind.Player, 1)));
        Assert.AreEqual(0, service.PendingCount, "no new blast may be queued once the session is inactive");

        var applications = service.ProcessTick(new BlastVictim[4]);
        Assert.AreEqual(0, applications.Count);
    }

    /// <summary>Verifies a real timer built on this session's SessionClock respects pause end to end: TrafficRespawnScheduler's readiness check, fed SessionClock.ElapsedActiveSeconds, never advances while the clock is paused.</summary>
    [Test]
    public void SessionClockAndTrafficRespawnScheduler_TimerNeverAdvancesWhilePaused() {
        var clock = new SessionClock();
        var scheduler = new TrafficRespawnScheduler();
        var rules = MakeRespawnRules(minDelay: 5f, minDistance: 100f, deadline: 10f);

        clock.Tick(1f); // t=1
        scheduler.TryRegisterDeficit("life-1", clock.ElapsedActiveSeconds, Vector2.zero);

        clock.SetPaused(true);
        clock.Tick(50f); clock.Tick(50f); // must not move while paused
        Assert.IsFalse(scheduler.IsReadyToAttempt("life-1", clock.ElapsedActiveSeconds, Vector2.zero, rules), "the deadline must not be considered reached while the session clock is paused");

        clock.SetPaused(false);
        clock.Tick(10f); // t=11: 10s elapsed since the deficit was registered at t=1
        Assert.IsTrue(scheduler.IsReadyToAttempt("life-1", clock.ElapsedActiveSeconds, Vector2.zero, rules), "once actually unpaused and enough active time has passed, the deadline gate opens normally");
    }

    /// <summary>
    /// S05 closing gate: a full collision→recovery→death→explosion→wreck→release→respawn lifecycle,
    /// composed entirely from S03+S05's pure fixtures, run three times in a row within one session.
    /// Every counter that should return to its baseline after a complete cycle actually does —
    /// health/event/queue/subscription state never accumulates across repeated deaths.
    /// </summary>
    [Test]
    public void S05_FullDamageLifecycleGate_RepeatedCyclesNeverLeakHealthEventQueueOrSubscriptionState() {
        var budget = MakeBudget(maxCivilian: 5, maxPolice: 5, maxTotal: 10, maxWrecks: 5);
        var profile = MakeTrafficProfile("sedan", VehicleRole.Civilian);
        var catalog = new BuiltInTrafficProfileCatalog(new[] { profile });
        var identityRegistry = new VehicleIdentityRegistry();
        var pool = new NpcVehiclePool(budget, catalog, identityRegistry);
        var populationService = new VehiclePopulationService(budget);
        var respawnScheduler = new TrafficRespawnScheduler();
        var wreckScheduler = new WreckCleanupScheduler();
        var explosionQuery = new FakeBlastVictimQuery();
        var explosionService = new VehicleExplosionService(explosionQuery, new BlastRoleMultipliers(), maxBlastsProcessedPerTick: 10);
        var heatRules = MakeHeatRules();

        int hookRunCount = 0;
        pool.RegisterResetHook(_ => hookRunCount++);

        try {
            for (int cycle = 0; cycle < 3; cycle++) {
                Assert.IsTrue(populationService.TryReserveSpawn(VehicleRole.Civilian));
                Assert.IsTrue(pool.TryAcquire("sedan", VehicleRole.Civilian, out NpcVehicleInstance instance));
                Assert.IsTrue(populationService.CommitSpawn(VehicleRole.Civilian));
                Assert.IsTrue(instance.TryActivate());
                int lifeId = instance.Identity.Value.lifeId;

                var health = new NpcVehicleHealth();
                health.InitializeForNewLife(40f, lifeId);

                // Collision -> recovery (survives a non-lethal hit).
                Assert.IsTrue(instance.TryEnterCrashRecovery());
                bool survived = health.ApplyDamage(lifeId, 20f, MakeDamageContext(lifeId, InstigatorKind.Player, 1), VehicleRole.Civilian, cycle, out _);
                Assert.IsFalse(survived);
                Assert.IsTrue(instance.TryRecoverToActive());

                // Lethal collision -> death.
                bool died = health.ApplyDamage(lifeId, 999f, MakeDamageContext(lifeId, InstigatorKind.Player, 1), VehicleRole.Civilian, cycle, out var destroyedEvent);
                Assert.IsTrue(died);
                Assert.IsTrue(HeatPayloadResolver.TryResolveHeat(destroyedEvent, heatRules, out _));

                // Explosion: its own separate blast against the same victim.
                explosionQuery.victims.Clear();
                explosionQuery.victims.Add(new BlastVictim(lifeId, VehicleRole.Civilian, Vector2.zero, 0f));
                explosionService.EnqueueBlast(new PendingBlast("blast-" + cycle, Vector2.zero, 50f, 10f, destroyedEvent.killingContext));
                var applications = explosionService.ProcessTick(new BlastVictim[4]);
                Assert.AreEqual(1, applications.Count);

                // Wreck.
                Assert.IsTrue(instance.TryMarkWrecked());
                Assert.IsTrue(populationService.MarkActiveVehicleWrecked(VehicleRole.Civilian));
                wreckScheduler.RegisterWreck("wreck-" + cycle, sessionTime: cycle);

                // Release: reset hook fires, wreck cleaned up, wreck slot freed.
                Assert.IsTrue(wreckScheduler.TryFindOldestEligibleWreck(cycle + 10f, minimumLifetimeSeconds: 5f, out string wreckId));
                Assert.IsTrue(pool.Release(instance));
                Assert.IsTrue(wreckScheduler.MarkCleaned(wreckId));
                Assert.IsTrue(populationService.ReleaseWreck());

                // Respawn: population deficit registered and, once cleared, a fresh acquire gets a new lifeId.
                string deficitId = "life-" + lifeId;
                Assert.IsTrue(respawnScheduler.TryRegisterDeficit(deficitId, cycle, Vector2.zero));
                Assert.IsTrue(respawnScheduler.CompleteRespawn(deficitId));

                // Leak checks: every per-cycle counter must be back at its baseline before the next cycle starts.
                Assert.AreEqual(0, populationService.PendingCivilianCount);
                Assert.AreEqual(0, populationService.ActiveCivilianCount);
                Assert.AreEqual(0, populationService.OccupiedWreckCount);
                Assert.AreEqual(0, populationService.ReservedFutureWreckCount);
                Assert.AreEqual(0, respawnScheduler.PendingCount);
                Assert.AreEqual(0, wreckScheduler.Count);
                Assert.AreEqual(0, explosionService.PendingCount);
                Assert.AreEqual(5, pool.Capacity, "pool capacity must stay budget-derived, never grow across cycles");
                Assert.AreEqual(cycle + 1, hookRunCount, "the reset hook must fire exactly once per completed cycle, never zero, never twice");
            }
        } finally {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(budget);
        }
    }

    /// <summary>Verifies that strong service performance produces a positive rating delta.</summary>
    [Test]
    public void CareerManager_StrongShiftProducesPositiveRatingDelta() {
        var data = ScriptableObject.CreateInstance<CareerData>();
        try {
            var career = new CareerManager(data);
            int delta = career.ComputeRatingDelta(180f, 8, 8, 0, 1f, EndReason.TimeUp);

            Assert.Greater(delta, 0);
        }
        finally {
            Object.DestroyImmediate(data);
        }
    }

    /// <summary>Verifies that a poor shift can reduce the courier rating.</summary>
    [Test]
    public void CareerManager_PoorShiftProducesNegativeRatingDelta() {
        var data = ScriptableObject.CreateInstance<CareerData>();
        try {
            var career = new CareerManager(data);
            int delta = career.ComputeRatingDelta(30f, 0, 6, 6, 0f, EndReason.Wrecked);

            Assert.Less(delta, 0);
        }
        finally {
            Object.DestroyImmediate(data);
        }
    }

    /// <summary>Verifies that rank is a visual calculation and does not alter the authored rating.</summary>
    [Test]
    public void CareerData_RankUsesThresholdsWithoutUnlockSideEffects() {
        var data = ScriptableObject.CreateInstance<CareerData>();
        try {
            bool reachedEnding;
            int rank = data.ComputeRank(data.rankThresholds[1], out reachedEnding);

            Assert.AreEqual(2, rank);
            Assert.IsFalse(reachedEnding);
            Assert.AreEqual(1, data.ComputeUnlockedRegionTier(1));
        }
        finally {
            Object.DestroyImmediate(data);
        }
    }

    /// <summary>Verifies that migration creates ownership storage and preserves the selected map.</summary>
    [Test]
    public void SaveMigration_SelectedMapBecomesOwnedDuringV3ToV4() {
        var data = new GameSaveData {
            saveVersion = 3,
            currentMapId = "map.test",
            ownedMapIds = new List<string>()
        };

        SaveMigration.Migrate(data, name => name);

        CollectionAssert.Contains(data.ownedMapIds, "map.test");
        Assert.AreEqual(SaveMigration.CurrentVersion, data.saveVersion);
    }

    /// <summary>Verifies that migration tolerates missing ownership data on an old profile.</summary>
    [Test]
    public void SaveMigration_MissingOwnershipListIsCreatedSafely() {
        var data = new GameSaveData {
            saveVersion = 3,
            currentMapId = string.Empty,
            ownedMapIds = null
        };

        SaveMigration.Migrate(data, name => name);

        Assert.IsNotNull(data.ownedMapIds);
        Assert.AreEqual(SaveMigration.CurrentVersion, data.saveVersion);
    }

    /// <summary>Verifies that the rebalanced vehicle defaults expose the intended speed curve.</summary>
    [Test]
    public void VehicleData_DefaultsUseTheRebalancedSpeedCurve() {
        var vehicle = ScriptableObject.CreateInstance<VehicleData>();
        try {
            Assert.AreEqual(0.1f, vehicle.speedStep, 0.0001f);
            Assert.AreEqual(25, vehicle.maxSpeedLevel);
            Assert.Greater(vehicle.minimumTuningSpeed, 0f);
        }
        finally {
            Object.DestroyImmediate(vehicle);
        }
    }

    /// <summary>Verifies that the Advanced Tuning speed ceiling follows purchased Speed levels.</summary>
    [Test]
    public void VehicleTuningRules_SpeedCeilingUsesPurchasedLevel() {
        Assert.AreEqual(4.9f, VehicleTuningRules.GetUnlockedMaxSpeed(4.4f, 0.1f, 5, 25), 0.0001f);
        Assert.AreEqual(6.9f, VehicleTuningRules.GetUnlockedMaxSpeed(4.4f, 0.1f, 999, 25), 0.0001f);
    }

    /// <summary>Verifies that custom speed cannot cross the authored floor or purchased ceiling.</summary>
    [Test]
    public void VehicleTuningRules_SpeedSelectionIsClamped() {
        Assert.AreEqual(2.5f, VehicleTuningRules.ClampSpeed(2.5f, 4.9f, -10f), 0.0001f);
        Assert.AreEqual(4.9f, VehicleTuningRules.ClampSpeed(2.5f, 4.9f, 99f), 0.0001f);
        Assert.AreEqual(4.9f, VehicleTuningRules.ResolveSpeed(true, false, 2.5f, 2.5f, 4.9f), 0.0001f);
    }

    /// <summary>Verifies that resolving a tuning value never mutates authored vehicle data.</summary>
    [Test]
    public void VehicleTuningRules_ResolveDoesNotMutateAuthoredVehicle() {
        var vehicle = ScriptableObject.CreateInstance<VehicleData>();
        try {
            float originalSpeed = vehicle.baseSpeed;
            float originalGrip = vehicle.drivingSettings.driftGrip;

            VehicleTuningRules.ResolveSpeed(true, true, 0f, vehicle.minimumTuningSpeed, vehicle.baseSpeed);
            VehicleTuningRules.ResolvePlayerValue(true, true, 99f, vehicle.drivingSettings.driftGrip,
                vehicle.drivingSettings.playerDriftGripMin, vehicle.drivingSettings.playerDriftGripMax);

            Assert.AreEqual(originalSpeed, vehicle.baseSpeed, 0.0001f);
            Assert.AreEqual(originalGrip, vehicle.drivingSettings.driftGrip, 0.0001f);
        }
        finally {
            Object.DestroyImmediate(vehicle);
        }
    }

    /// <summary>Verifies that invalid speed inputs resolve to safe finite values.</summary>
    [Test]
    public void VehicleTuningRules_InvalidSpeedInputsRemainFinite() {
        Assert.AreEqual(0f, VehicleTuningRules.GetUnlockedMaxSpeed(float.NaN, float.PositiveInfinity, 1, 1), 0.0001f);
        Assert.AreEqual(0f, VehicleTuningRules.ResolveSpeed(true, true, float.NaN, float.NaN,
            float.PositiveInfinity), 0.0001f);
    }

    /// <summary>Verifies that optional drift values preserve Classic fallback and clamp custom values.</summary>
    [Test]
    public void VehicleTuningRules_DriftValuesPreserveFallbackAndBounds() {
        Assert.AreEqual(1.7f, VehicleTuningRules.ResolvePlayerValue(false, true, 0.8f, 1.7f, 0.8f, 3f), 0.0001f);
        Assert.AreEqual(3f, VehicleTuningRules.ResolvePlayerValue(true, true, 9f, 1.7f, 0.8f, 3f), 0.0001f);
        Assert.AreEqual(0f, VehicleTuningRules.ResolvePlayerValue(true, true, -2f, 0.08f, 0f, 0.5f), 0.0001f);
        Assert.AreEqual(0.8f, VehicleTuningRules.ClampPlayerValue(float.NaN, 0.8f, 3f), 0.0001f);
        Assert.AreEqual(3f, VehicleTuningRules.ClampPlayerValue(float.PositiveInfinity, 0.8f, 3f), 0.0001f);
        Assert.AreEqual(0.8f, VehicleTuningRules.ClampPlayerValue(float.NegativeInfinity, 0.8f, 3f), 0.0001f);
        Assert.AreEqual(2f, VehicleTuningRules.ClampPlayerValue(1f, 3f, 2f), 0.0001f);
    }

    /// <summary>Verifies that authored difficulty order ranges are inclusive at both ends.</summary>
    [Test]
    public void MapDifficulty_OrderRangeIsInclusive() {
        var difficulty = new MapDifficultyData {
            orderMin = 3,
            orderMax = 3
        };

        Assert.AreEqual(3, difficulty.RollOrderAmount());
        Assert.AreEqual(3, difficulty.GetSafeOrderMin());
        Assert.AreEqual(3, difficulty.GetSafeOrderMax());
    }

    /// <summary>Verifies that loss budgets above one hundred guarantee whole losses and roll one extra.</summary>
    [Test]
    public void MapDifficulty_LossBudgetSupportsGuaranteedAndFractionalLosses() {
        Assert.AreEqual(1, MapDifficultyRules.CalculatePizzaLossCount(120f, 0f, 80f, 4));
        Assert.AreEqual(2, MapDifficultyRules.CalculatePizzaLossCount(120f, 0f, 10f, 4));
        Assert.AreEqual(2, MapDifficultyRules.CalculatePizzaLossCount(250f, 0f, 99f, 4));
        Assert.AreEqual(3, MapDifficultyRules.CalculatePizzaLossCount(250f, 0f, 10f, 4));
    }

    /// <summary>Verifies that Stabilizer reduces both guaranteed and fractional pizza losses.</summary>
    [Test]
    public void MapDifficulty_StabilizerReducesLossBudget() {
        Assert.AreEqual(0, MapDifficultyRules.CalculatePizzaLossCount(120f, 0.5f, 99f, 4));
        Assert.AreEqual(0, MapDifficultyRules.CalculatePizzaLossCount(20f, 1f, 0f, 4));
    }

    /// <summary>Verifies that map ownership and the previous tier score both gate later tiers.</summary>
    [Test]
    public void MapDifficulty_TierUnlockRequiresPreviousTarget() {
        var previous = new MapDifficultyData { unlockTargetScoreForNext = 800 };

        Assert.IsFalse(MapDifficultyRules.IsTierUnlocked(false, 0, null, 0));
        Assert.IsTrue(MapDifficultyRules.IsTierUnlocked(true, 0, null, 0));
        Assert.IsFalse(MapDifficultyRules.IsTierUnlocked(true, 1, previous, 799));
        Assert.IsTrue(MapDifficultyRules.IsTierUnlocked(true, 1, previous, 800));
        Assert.IsFalse(MapDifficultyRules.IsTierUnlocked(true, 1,
            new MapDifficultyData { unlockTargetScoreForNext = 0 }, 9999));
    }

    /// <summary>Verifies that difficulty rewards scale without allowing negative authored values.</summary>
    [Test]
    public void MapDifficulty_RewardMultiplierScalesPositiveRewards() {
        var difficulty = new MapDifficultyData { rewardMultiplier = 1.5f };

        Assert.AreEqual(15, difficulty.ApplyRewardMultiplier(10f));
        difficulty.rewardMultiplier = -2f;
        Assert.AreEqual(0, difficulty.ApplyRewardMultiplier(10f));
    }

    /// <summary>Verifies that version five saves receive initialized difficulty progress storage.</summary>
    [Test]
    public void SaveMigration_V5InitializesDifficultyProgress() {
        var data = new GameSaveData {
            saveVersion = 5,
            currentDifficultyId = "old-tier",
            mapDifficultyProgress = null
        };

        SaveMigration.Migrate(data, name => name);

        Assert.AreEqual(SaveMigration.CurrentVersion, data.saveVersion);
        Assert.IsNotNull(data.mapDifficultyProgress);
        Assert.IsEmpty(data.mapDifficultyProgress);
        Assert.IsEmpty(data.currentDifficultyId);
    }

    /// <summary>Verifies that old saves opt out of custom tuning during schema migration.</summary>
    [Test]
    public void SaveMigration_V6InitializesAdvancedTuningFields() {
        var record = new VehicleSaveData("vehicle.test", true) {
            hasCustomTuning = true,
            tunedSpeed = 99f,
            tunedDriftGrip = 99f,
            tunedDriftSteeringMultiplier = 99f,
            tunedGripEnterTime = 99f
        };
        var data = new GameSaveData {
            saveVersion = 6,
            vehicleSaveList = new List<VehicleSaveData> { record }
        };

        SaveMigration.Migrate(data, name => name);

        Assert.AreEqual(SaveMigration.CurrentVersion, data.saveVersion);
        Assert.IsFalse(record.hasCustomTuning);
        Assert.AreEqual(0f, record.tunedSpeed);
        Assert.AreEqual(0f, record.tunedDriftGrip);
        Assert.AreEqual(0f, record.tunedDriftSteeringMultiplier);
        Assert.AreEqual(0f, record.tunedGripEnterTime);
    }

    /// <summary>Verifies that time-up and extraction keep all non-negative earnings.</summary>
    [Test]
    public void SessionSettlement_TimeUpAndExtractionKeepEarnings() {
        Assert.AreEqual(120, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.TimeUp, 0.5f));
        Assert.AreEqual(120, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.Extracted, 0.5f));
    }

    /// <summary>Verifies that wrecked and abandoned sessions apply their distinct retention rules.</summary>
    [Test]
    public void SessionSettlement_WreckedAndAbandonedApplyLossRules() {
        Assert.AreEqual(60, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.Wrecked, 0.5f));
        Assert.AreEqual(0, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.Abandoned, 0.5f));
        Assert.AreEqual(0, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.Interrupted, 0.5f));
    }

    /// <summary>Arrest retains the authored 75 percent share and charges repair from actual remaining health.</summary>
    [Test]
    public void SessionSettlement_ArrestedKeepsQ05ShareAndUsesRemainingHealth() {
        Assert.AreEqual(90, SessionSettlementMath.CalculateKeptEarnings(120, EndReason.Arrested, 0.5f, 0.75f));
        int repair = SessionSettlementMath.CalculateRepairCost(75f, 100f, false, 1000, 0.7f, 0.03f);
        Assert.AreEqual(25, repair);
        Assert.AreEqual(90, SessionSettlementMath.ClampRepairCost(200, 90, 0, 0.5f));
        Assert.AreEqual(0, SessionSettlementMath.CalculateBankAfter(0, 90, 90));
    }

    /// <summary>Arrest is appended without changing legacy ordinals and cannot qualify for the standard board.</summary>
    [Test]
    public void Arrested_EndReasonPreservesOrdinalsAndCompetitiveRulesRejectIt() {
        Assert.AreEqual(0, (int)EndReason.TimeUp);
        Assert.AreEqual(1, (int)EndReason.Extracted);
        Assert.AreEqual(2, (int)EndReason.Wrecked);
        Assert.AreEqual(3, (int)EndReason.Abandoned);
        Assert.AreEqual(4, (int)EndReason.Interrupted);
        Assert.AreEqual(5, (int)EndReason.Arrested);
        Assert.AreEqual(CompetitiveEligibilityStatus.Unfinished,
            CompetitiveRunRules.Evaluate(false, true, true, false, 5, 5, EndReason.Arrested));
    }

    /// <summary>Verifies that repair cost is linear in health loss and bills full health after death.</summary>
    [Test]
    public void SessionSettlement_RepairUsesLinearDamageAndDeathFullBill() {
        int quarterDamage = SessionSettlementMath.CalculateRepairCost(75f, 100f, false, 1000, 0.7f, 0.03f);
        int halfDamage = SessionSettlementMath.CalculateRepairCost(50f, 100f, false, 1000, 0.7f, 0.03f);
        int deathDamage = SessionSettlementMath.CalculateRepairCost(1f, 100f, true, 1000, 0.7f, 0.03f);

        Assert.AreEqual(25, quarterDamage);
        Assert.AreEqual(50, halfDamage);
        Assert.AreEqual(100, deathDamage);
    }

    /// <summary>Verifies that repair affordability preserves the bank safety reserve and zero floor.</summary>
    [Test]
    public void SessionSettlement_RepairClampProtectsBankAndBankFloor() {
        Assert.AreEqual(65, SessionSettlementMath.ClampRepairCost(200, 15, 100, 0.5f));
        Assert.AreEqual(0, SessionSettlementMath.ClampRepairCost(200, 0, 0, 0.5f));
        Assert.AreEqual(0, SessionSettlementMath.CalculateBankAfter(10, 0, 20));
        Assert.AreEqual(55, SessionSettlementMath.CalculateBankAfter(40, 30, 15));
    }

    /// <summary>Verifies that saving one profile does not overwrite a different profile slot.</summary>
    [Test]
    public void SaveSlots_KeepProfilesIndependent() {
        var config = CreateTestConfig();
        var service = new SaveSlotService(config, name => name);
        try {
            Assert.IsTrue(service.Save(0, CreateSave(125)));
            Assert.IsTrue(service.Save(1, CreateSave(875)));

            var first = service.Load(0, out var firstStatus);
            var second = service.Load(1, out var secondStatus);

            Assert.AreEqual(SaveLoadStatus.Loaded, firstStatus);
            Assert.AreEqual(SaveLoadStatus.Loaded, secondStatus);
            Assert.AreEqual(125, first.totalMoney);
            Assert.AreEqual(875, second.totalMoney);
        }
        finally {
            CleanupTestFiles(config);
            Object.DestroyImmediate(config);
        }
    }

    /// <summary>Verifies that the pre-slot save file is adopted without losing its contents.</summary>
    [Test]
    public void SaveSlots_AdoptLegacySaveIntoSlotZero() {
        var config = CreateTestConfig();
        var service = new SaveSlotService(config, name => name);
        string legacyPath = Path.Combine(Application.persistentDataPath, TestId + "_legacy.json");
        try {
            File.WriteAllText(legacyPath, JsonUtility.ToJson(CreateSave(321)));

            Assert.IsTrue(service.AdoptLegacySave(TestId + "_legacy.json", 0));
            var loaded = service.Load(0, out var status);

            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            Assert.AreEqual(321, loaded.totalMoney);
        }
        finally {
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            CleanupTestFiles(config);
            Object.DestroyImmediate(config);
        }
    }

    /// <summary>Verifies that an unreadable main file can recover from its atomic-write backup.</summary>
    [Test]
    public void SaveSlots_RecoverUnreadableMainFileFromBackup() {
        var config = CreateTestConfig();
        var service = new SaveSlotService(config, name => name);
        try {
            Assert.IsTrue(service.Save(0, CreateSave(100)));
            Assert.IsTrue(service.Save(0, CreateSave(200)));
            File.WriteAllText(service.GetPath(0), "not valid json");

            var loaded = service.Load(0, out var status);

            Assert.AreEqual(SaveLoadStatus.RecoveredFromBackup, status);
            Assert.AreEqual(100, loaded.totalMoney);
        }
        finally {
            CleanupTestFiles(config);
            Object.DestroyImmediate(config);
        }
    }

    /// <summary>Verifies that two unreadable files become a visible damaged slot instead of a reset career.</summary>
    [Test]
    public void SaveSlots_ReportCorruptWhenMainAndBackupAreUnreadable() {
        var config = CreateTestConfig();
        var service = new SaveSlotService(config, name => name);
        try {
            Assert.IsTrue(service.Save(0, CreateSave(100)));
            Assert.IsTrue(service.Save(0, CreateSave(200)));
            File.WriteAllText(service.GetPath(0), "not valid json");
            File.WriteAllText(service.GetPath(0) + config.backupSuffix, "also not valid json");
            LogAssert.Expect(LogType.Error, new Regex("\\[Save\\] Slot 0 could not be read and was set aside as '.*'. Nothing was deleted\\."));

            var loaded = service.Load(0, out var status);

            Assert.IsNull(loaded);
            Assert.AreEqual(SaveLoadStatus.Corrupt, status);
            Assert.IsFalse(File.Exists(service.GetPath(0)));
            Assert.IsFalse(File.Exists(service.GetPath(0) + config.backupSuffix));
        }
        finally {
            CleanupTestFiles(config);
            Object.DestroyImmediate(config);
        }
    }

    /// <summary>Verifies that collision damage uses normal closing speed instead of tangential speed.</summary>
    [Test]
    public void VehicleDrivingMath_ClosingSpeedIgnoresTangentialScraping() {
        float directImpact = VehicleDrivingMath.CalculateClosingSpeed(new Vector2(0f, 10f), Vector2.up);
        float sideScrape = VehicleDrivingMath.CalculateClosingSpeed(new Vector2(10f, 0f), Vector2.up);

        Assert.AreEqual(10f, directImpact, 0.0001f);
        Assert.AreEqual(0f, sideScrape, 0.0001f);
    }

    /// <summary>Verifies that driving math remains stable for zero and negative authored inputs.</summary>
    [Test]
    public void VehicleDrivingMath_ClampsUnsafeInputsWithoutProducingInvalidValues() {
        Assert.AreEqual(0f, VehicleDrivingMath.AccelerationForTime(-10f, 0f), 0.0001f);
        Assert.AreEqual(10f, VehicleDrivingMath.ClampForwardSpeed(20f, 10f, 3f), 0.0001f);
        Assert.AreEqual(-3f, VehicleDrivingMath.ClampForwardSpeed(-20f, 10f, 3f), 0.0001f);
        Assert.AreEqual(10f, VehicleDrivingMath.Damp(10f, 0f, 1f), 0.0001f);
    }

    /// <summary>Verifies that authored damping survives the runtime settings snapshot.</summary>
    [Test]
    public void VehicleDrivingSettings_ClonePreservesLinearDamping() {
        var authored = new VehicleDrivingSettings {
            linearDamping = 0.15f,
            driftRequiresHandbrake = true
        };
        VehicleDrivingSettings clone = authored.Clone();
        Assert.AreEqual(authored.linearDamping, clone.linearDamping, 0.0001f);
        Assert.AreEqual(authored.driftRequiresHandbrake, clone.driftRequiresHandbrake);
    }

    /// <summary>Verifies that zero dwell times do not bypass the handbrake drift gate.</summary>
    [Test]
    public void VehicleMovement_ZeroDwellStillRequiresHandbrakeAndSteering() {
        GameObject vehicle = new GameObject("DriftStateTestVehicle");
        Rigidbody2D body = vehicle.AddComponent<Rigidbody2D>();
        VehicleInput input = vehicle.AddComponent<VehicleInput>();
        VehicleMovement movement = vehicle.AddComponent<VehicleMovement>();
        var settings = new VehicleDrivingSettings {
            driftRequiresHandbrake = true,
            driftMinimumSpeedFraction = 0.25f,
            driftEnterAngle = 12f,
            driftExitAngle = 8f,
            driftMaximumAngle = 75f,
            driftEnterDwell = 0f,
            driftExitDwell = 0f
        };

        try {
            movement.Initialize(input, body, 10f, 180f, settings);
            body.linearVelocity = Vector2.up * 5f;

            MethodInfo updateDriftState = typeof(VehicleMovement).GetMethod(
                "UpdateDriftState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updateDriftState);

            updateDriftState.Invoke(movement, new object[] { 5f, 0f, 10f, 0f, false, 0.02f });
            Assert.IsFalse(movement.IsDrifting, "Normal steering must not enter drift without handbrake.");

            updateDriftState.Invoke(movement, new object[] { 5f, 0f, 10f, 1f, true, 0.02f });
            Assert.IsTrue(movement.IsDrifting, "Handbrake plus steering must enter drift immediately.");

            updateDriftState.Invoke(movement, new object[] { 5f, 0f, 10f, 1f, false, 0.02f });
            Assert.IsFalse(movement.IsDrifting, "Releasing handbrake must exit drift immediately at zero dwell.");
        }
        finally {
            Object.DestroyImmediate(vehicle);
        }
    }

    /// <summary>Verifies that normalized input construction clamps every public command.</summary>
    [Test]
    public void VehicleInputSnapshot_ClampsCommandRanges() {
        var snapshot = new VehicleInputSnapshot(5f, 2f, -1f, true);

        Assert.AreEqual(1f, snapshot.steering, 0.0001f);
        Assert.AreEqual(1f, snapshot.throttle, 0.0001f);
        Assert.AreEqual(0f, snapshot.brake, 0.0001f);
        Assert.IsTrue(snapshot.handbrake);
    }

    static readonly string TestId = "codex_save_test_" + Guid.NewGuid().ToString("N");

    static GameConfig CreateTestConfig() {
        var config = ScriptableObject.CreateInstance<GameConfig>();
        config.saveSlotCount = 3;
        config.saveFilePattern = TestId + "_{0}.json";
        config.backupSuffix = ".bak";
        config.corruptSuffix = ".corrupt_{0}.json";
        return config;
    }

    static GameSaveData CreateSave(int money) {
        return new GameSaveData {
            totalMoney = money,
            currentVehicleId = "vehicle.test",
            vehicleSaveList = new List<VehicleSaveData> {
                new VehicleSaveData("vehicle.test", true)
            }
        };
    }

    static void CleanupTestFiles(GameConfig config) {
        if (config == null) return;
        for (int i = 0; i < config.saveSlotCount; i++) {
            string path = Path.Combine(Application.persistentDataPath, config.GetSaveFileName(i));
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + config.backupSuffix)) File.Delete(path + config.backupSuffix);
            string directory = Path.GetDirectoryName(path);
            string prefix = Path.GetFileName(path) + ".corrupt_";
            if (!Directory.Exists(directory)) continue;
            foreach (string corrupt in Directory.GetFiles(directory, prefix + "*.json")) File.Delete(corrupt);
        }
    }

    /// <summary>Verifies that a temporary speed debuff multiplies the resolved speed ceiling.</summary>
    [Test]
    public void VehicleMovement_TemporarySpeedMultiplierPreservesResolvedCeiling() {
        GameObject vehicle = new GameObject("SpeedMultiplierTestVehicle");
        Rigidbody2D body = vehicle.AddComponent<Rigidbody2D>();
        VehicleInput input = vehicle.AddComponent<VehicleInput>();
        VehicleMovement movement = vehicle.AddComponent<VehicleMovement>();

        try {
            movement.Initialize(input, body, 4.9f, 210f, new VehicleDrivingSettings());
            movement.SetSpeedMultiplier(0.6f);
            Assert.AreEqual(2.94f, movement.GetEffectiveMaximumSpeed(), 0.0001f);
            movement.SetSpeedMultiplier(1.5f);
            Assert.AreEqual(4.9f, movement.GetEffectiveMaximumSpeed(), 0.0001f);
        }
        finally {
            Object.DestroyImmediate(vehicle);
        }
    }
}
