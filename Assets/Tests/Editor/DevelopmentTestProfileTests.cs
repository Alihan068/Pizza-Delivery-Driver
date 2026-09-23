using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure S12 resolver and isolated save-service tests; no Unity scene or Play Mode use.</summary>
public sealed class DevelopmentTestProfileTests {
    string temporaryRoot;

    /// <summary>Creates the uniquely named temporary root used by this fixture.</summary>
    [SetUp]
    public void SetUp() {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "PizzaDeliveryS12_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
    }

    /// <summary>Removes only the verified fixture root created by <see cref="SetUp"/>.</summary>
    [TearDown]
    public void TearDown() {
        string fullRoot = Path.GetFullPath(temporaryRoot);
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool exactFixture = Path.GetDirectoryName(fullRoot) == tempParent &&
                            Path.GetFileName(fullRoot).StartsWith("PizzaDeliveryS12_", StringComparison.Ordinal);
        if (exactFixture && Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
    }

    /// <summary>Confirms that no command-line opt-in preserves inactive resolver state.</summary>
    [Test]
    public void NoFlagRemainsInactive() {
        DevelopmentTestProfile result = DevelopmentTestProfile.ResolveArguments(
            new[] { "game.exe" }, temporaryRoot);

        Assert.That(result.IsRequested, Is.False);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.RootPath, Is.Empty);
    }

    /// <summary>Confirms that a valid 32-hex identifier maps to the bounded normalized root.</summary>
    [Test]
    public void ValidGuidProducesBoundedNormalizedRoot() {
        DevelopmentTestProfile result = DevelopmentTestProfile.ResolveArguments(
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName,
                "0123456789ABCDEF0123456789ABCDEF" }, temporaryRoot);

        string expected = Path.Combine(temporaryRoot, DevelopmentTestProfile.ProfileDirectoryName,
            "0123456789abcdef0123456789abcdef");
        Assert.That(result.IsRequested, Is.True);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.RootPath, Is.EqualTo(Path.GetFullPath(expected)));
        Assert.That(DevelopmentTestProfile.IsContainedPath(temporaryRoot, result.RootPath), Is.True);
    }

    /// <summary>Confirms malformed, duplicate, empty-root, and traversal requests fail closed.</summary>
    [Test]
    public void MalformedDuplicateEmptyAndTraversalRequestsFailClosed() {
        var cases = new[] {
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName },
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName, "" },
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName, "../save.json" },
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName,
                "0123456789abcdef0123456789abcdef", DevelopmentTestProfile.ArgumentName,
                "0123456789abcdef0123456789abcdef" }
        };

        foreach (string[] arguments in cases) {
            DevelopmentTestProfile result = DevelopmentTestProfile.ResolveArguments(arguments, temporaryRoot);
            Assert.That(result.IsRequested, Is.True, string.Join(" ", arguments));
            Assert.That(result.IsValid, Is.False, string.Join(" ", arguments));
            Assert.That(result.RootPath, Is.Empty, string.Join(" ", arguments));
        }

        DevelopmentTestProfile emptyRoot = DevelopmentTestProfile.ResolveArguments(
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName,
                "0123456789abcdef0123456789abcdef" }, string.Empty);
        Assert.That(emptyRoot.IsRequested, Is.True);
        Assert.That(emptyRoot.IsValid, Is.False);
    }

    /// <summary>Confirms isolated save/load works while legacy adoption and traversal are blocked.</summary>
    [Test]
    public void IsolatedSaveServiceSavesLoadsAndRefusesLegacyAdoption() {
        const string profileGuid = "0123456789abcdef0123456789abcdef";
        DevelopmentTestProfile profile = DevelopmentTestProfile.ResolveArguments(
            new[] { "game.exe", DevelopmentTestProfile.ArgumentName, profileGuid }, temporaryRoot);
        GameConfig config = ScriptableObject.CreateInstance<GameConfig>();
        string legacyPath = Path.Combine(temporaryRoot, "save.json");
        const string legacyBytes = "legacy-career-fixture";
        File.WriteAllText(legacyPath, legacyBytes);

        try {
            SaveSlotService service = new SaveSlotService(config, _ => string.Empty, profile, temporaryRoot);
            GameSaveData expected = new GameSaveData {
                totalMoney = 321,
                currentVehicleId = "vehicle.test",
                vehicleSaveList = new System.Collections.Generic.List<VehicleSaveData> {
                    new VehicleSaveData("vehicle.test", true)
                }
            };

            Assert.That(service.AdoptLegacySave("save.json", 0), Is.False);
            Assert.That(File.ReadAllText(legacyPath), Is.EqualTo(legacyBytes));

            Assert.That(service.Save(0, expected), Is.True);
            string isolatedPath = service.GetPath(0);
            Assert.That(isolatedPath, Does.StartWith(Path.GetFullPath(profile.RootPath)));
            Assert.That(File.Exists(isolatedPath), Is.True);

            GameSaveData loaded = service.Load(0, out SaveLoadStatus status);
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.totalMoney, Is.EqualTo(expected.totalMoney));
            Assert.That(loaded.currentVehicleId, Is.EqualTo(expected.currentVehicleId));

            config.saveFilePattern = "../escape_{0}.json";
            Assert.Throws<InvalidOperationException>(() => service.GetPath(0));
        }
        finally {
            UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
