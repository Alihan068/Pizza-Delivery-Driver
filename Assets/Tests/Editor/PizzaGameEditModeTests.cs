using System;
using System.Collections.Generic;
using System.IO;
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
