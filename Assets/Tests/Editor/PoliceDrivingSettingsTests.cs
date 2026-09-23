using NUnit.Framework;
using UnityEngine;

/// <summary>Verifies the detached, finite serialized limits consumed by committed police turns.</summary>
public sealed class PoliceDrivingSettingsTests {
    /// <summary>Nondefault turn limits survive clone and Unity JSON, and changing the source cannot mutate either snapshot.</summary>
    [Test]
    public void TurnLimits_NondefaultCloneAndJsonRoundTrip() {
        var source = new PoliceDrivingSettings {
            turnExitDistance = 3.25f, turnExitAlignmentDegrees = 11f,
            maximumTurnTravel = 25f, maximumTurnActiveSeconds = 6.5f
        };
        Assert.IsTrue(source.IsValid(out string reason), reason);
        var clone = source.Clone();
        var restored = JsonUtility.FromJson<PoliceDrivingSettings>(JsonUtility.ToJson(source));
        source.turnExitDistance = 1f; source.turnExitAlignmentDegrees = 2f;
        source.maximumTurnTravel = 3f; source.maximumTurnActiveSeconds = 4f;
        foreach (var snapshot in new[] { clone, restored }) {
            Assert.AreEqual(3.25f, snapshot.turnExitDistance);
            Assert.AreEqual(11f, snapshot.turnExitAlignmentDegrees);
            Assert.AreEqual(25f, snapshot.maximumTurnTravel);
            Assert.AreEqual(6.5f, snapshot.maximumTurnActiveSeconds);
            Assert.IsTrue(snapshot.IsValid(out reason), reason);
        }
    }

    /// <summary>Each authored turn limit independently rejects zero, negative and non-finite values.</summary>
    [Test]
    public void TurnLimits_RejectInvalidValues(
        [Values(nameof(PoliceDrivingSettings.turnExitDistance), nameof(PoliceDrivingSettings.turnExitAlignmentDegrees),
            nameof(PoliceDrivingSettings.maximumTurnTravel), nameof(PoliceDrivingSettings.maximumTurnActiveSeconds))] string field,
        [Values(0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity)] float value) {
        var settings = new PoliceDrivingSettings();
        typeof(PoliceDrivingSettings).GetField(field).SetValue(settings, value);
        Assert.IsFalse(settings.IsValid(out string reason));
        Assert.IsNotEmpty(reason);
    }

    /// <summary>Release angle remains bounded and travel cannot expire before the minimum requested exit distance.</summary>
    [Test]
    public void TurnLimits_RejectInconsistentReleaseBounds() {
        var settings = new PoliceDrivingSettings { turnExitAlignmentDegrees = 181f };
        Assert.IsFalse(settings.IsValid(out _));
        settings.turnExitAlignmentDegrees = 8f;
        settings.maximumTurnTravel = settings.turnExitDistance - 0.1f;
        Assert.IsFalse(settings.IsValid(out _));
        settings.maximumTurnTravel = settings.turnExitDistance;
        Assert.IsTrue(settings.IsValid(out string reason), reason);
    }

    /// <summary>All four nondefault ram values survive detached cloning and Unity JSON serialization.</summary>
    [Test]
    public void RamSettings_NondefaultCloneAndJsonRoundTrip() {
        var source = new PoliceDrivingSettings {
            ramConeDegrees = 18f, ramMaximumSurfaceGap = 5f,
            ramMaximumActiveSeconds = 1.75f, ramCooldownSeconds = 3.5f
        };
        Assert.IsTrue(source.IsValid(out string reason), reason);
        var clone = source.Clone();
        var restored = JsonUtility.FromJson<PoliceDrivingSettings>(JsonUtility.ToJson(source));
        source.ramConeDegrees = 12f; source.ramMaximumSurfaceGap = 4f;
        source.ramMaximumActiveSeconds = 1f; source.ramCooldownSeconds = 2f;
        foreach (var snapshot in new[] { clone, restored }) {
            Assert.AreEqual(18f, snapshot.ramConeDegrees);
            Assert.AreEqual(5f, snapshot.ramMaximumSurfaceGap);
            Assert.AreEqual(1.75f, snapshot.ramMaximumActiveSeconds);
            Assert.AreEqual(3.5f, snapshot.ramCooldownSeconds);
            Assert.IsTrue(snapshot.IsValid(out reason), reason);
        }
    }

    /// <summary>Each ram setting independently rejects its authored invalid range or nonfinite value.</summary>
    [Test]
    public void RamSettings_RejectInvalidValues(
        [Values(nameof(PoliceDrivingSettings.ramConeDegrees), nameof(PoliceDrivingSettings.ramMaximumSurfaceGap),
            nameof(PoliceDrivingSettings.ramMaximumActiveSeconds), nameof(PoliceDrivingSettings.ramCooldownSeconds))] string field,
        [Values(0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity)] float value) {
        var settings = new PoliceDrivingSettings();
        typeof(PoliceDrivingSettings).GetField(field).SetValue(settings, value);
        Assert.IsFalse(settings.IsValid(out string reason));
        Assert.IsNotEmpty(reason);
    }

    /// <summary>Ram surface gap cannot exceed the existing caller sweep maximum.</summary>
    [Test]
    public void RamSettings_RejectSurfaceGapBeyondSweepRange() {
        var settings = new PoliceDrivingSettings { ramMaximumSurfaceGap = 64.01f };
        Assert.IsFalse(settings.IsValid(out _));
    }
}
