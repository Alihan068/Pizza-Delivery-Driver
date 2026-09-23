using NUnit.Framework;
using UnityEngine;

/// <summary>Focused authoring, validation, clone, and JSON coverage for bounded police recovery settings.</summary>
public sealed class PoliceRecoverySettingsTests {
    /// <summary>Default recovery values match the approved bounded contract and validate against normal driving.</summary>
    [Test]
    public void RecoverySettings_DefaultsMatchContract() {
        var settings = new PoliceRecoverySettings();
        Assert.AreEqual(1f, settings.lightImpactSpeed);
        Assert.AreEqual(3f, settings.heavyImpactSpeed);
        Assert.AreEqual(0.6f, settings.lightHoldSeconds);
        Assert.AreEqual(1f, settings.settleSpeed);
        Assert.AreEqual(2f, settings.settleTimeoutSeconds);
        Assert.AreEqual(0.3f, settings.settleBrake);
        Assert.AreEqual(1f, settings.rejoinRetrySeconds);
        Assert.AreEqual(8f, settings.maxRejoinDistance);
        Assert.AreEqual(1.5f, settings.reverseMaxSeconds);
        Assert.AreEqual(1.5f, settings.maneuverSpeed);
        Assert.AreEqual(3f, settings.maximumReverseTravel);
        Assert.AreEqual(8f, settings.maximumActiveSeconds);
        Assert.IsTrue(settings.IsValid(0.1f, out string reason), reason);
    }

    /// <summary>Every recovery field independently rejects a nonfinite authored value.</summary>
    [Test]
    public void RecoverySettings_EachFieldRejectsNonfiniteValues(
        [Values(nameof(PoliceRecoverySettings.lightImpactSpeed), nameof(PoliceRecoverySettings.heavyImpactSpeed),
            nameof(PoliceRecoverySettings.lightHoldSeconds), nameof(PoliceRecoverySettings.settleSpeed),
            nameof(PoliceRecoverySettings.settleTimeoutSeconds), nameof(PoliceRecoverySettings.settleBrake),
            nameof(PoliceRecoverySettings.rejoinRetrySeconds), nameof(PoliceRecoverySettings.maxRejoinDistance),
            nameof(PoliceRecoverySettings.reverseMaxSeconds), nameof(PoliceRecoverySettings.maneuverSpeed),
            nameof(PoliceRecoverySettings.maximumReverseTravel), nameof(PoliceRecoverySettings.maximumActiveSeconds))] string field,
        [Values(float.NaN, float.PositiveInfinity, float.NegativeInfinity)] float value) {
        var settings = new PoliceRecoverySettings();
        typeof(PoliceRecoverySettings).GetField(field).SetValue(settings, value);
        Assert.IsFalse(settings.IsValid(0.1f, out string reason));
        Assert.IsNotEmpty(reason);
    }

    /// <summary>Each positive or nonnegative range constraint rejects its own invalid value.</summary>
    [TestCase(nameof(PoliceRecoverySettings.lightImpactSpeed), -1f)]
    [TestCase(nameof(PoliceRecoverySettings.heavyImpactSpeed), -1f)]
    [TestCase(nameof(PoliceRecoverySettings.lightHoldSeconds), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.settleSpeed), -1f)]
    [TestCase(nameof(PoliceRecoverySettings.settleTimeoutSeconds), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.settleBrake), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.settleBrake), 1.01f)]
    [TestCase(nameof(PoliceRecoverySettings.rejoinRetrySeconds), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.maxRejoinDistance), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.reverseMaxSeconds), -1f)]
    [TestCase(nameof(PoliceRecoverySettings.maneuverSpeed), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.maximumReverseTravel), 0f)]
    [TestCase(nameof(PoliceRecoverySettings.maximumActiveSeconds), 0f)]
    public void RecoverySettings_EachRangeRejectsInvalidValue(string field, float value) {
        var settings = new PoliceRecoverySettings();
        typeof(PoliceRecoverySettings).GetField(field).SetValue(settings, value);
        Assert.IsFalse(settings.IsValid(0.1f, out string reason));
        Assert.IsNotEmpty(reason);
    }

    /// <summary>Cross-field recovery constraints reject ordering, threshold, duration, and reverse-cap violations.</summary>
    [Test]
    public void RecoverySettings_RejectsCrossFieldViolations() {
        var settings = new PoliceRecoverySettings { lightImpactSpeed = 3f, heavyImpactSpeed = 2f };
        Assert.IsFalse(settings.IsValid(0.1f, out _));
        settings = new PoliceRecoverySettings { settleSpeed = 0.5f };
        Assert.IsFalse(settings.IsValid(1f, out _));
        settings = new PoliceRecoverySettings { maximumActiveSeconds = 0.5f };
        Assert.IsFalse(settings.IsValid(0.1f, out _));
        settings = new PoliceRecoverySettings { maximumActiveSeconds = 1.5f, settleTimeoutSeconds = 2f };
        Assert.IsFalse(settings.IsValid(0.1f, out _));
        settings = new PoliceRecoverySettings { reverseMaxSeconds = 9f, maximumActiveSeconds = 8f };
        Assert.IsFalse(settings.IsValid(0.1f, out _));
    }

    /// <summary>The existing stopped threshold input rejects negative and nonfinite values.</summary>
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RecoverySettings_RejectsInvalidStoppedThreshold(float stoppedSpeedThreshold) {
        Assert.IsFalse(new PoliceRecoverySettings().IsValid(stoppedSpeedThreshold, out string reason));
        Assert.IsNotEmpty(reason);
    }

    /// <summary>Driving validation rejects a missing nested object and delegates the stopped-threshold constraint.</summary>
    [Test]
    public void DrivingSettings_RecoveryNullAndStoppedThresholdAreInvalid() {
        var settings = new PoliceDrivingSettings { recovery = null };
        Assert.IsFalse(settings.IsValid(out _));
        settings = new PoliceDrivingSettings { stoppedSpeedThreshold = 2f };
        Assert.IsFalse(settings.IsValid(out _));
    }

    /// <summary>All nondefault nested recovery values survive detached clone and Unity JSON round-trip.</summary>
    [Test]
    public void DrivingSettings_RecoveryDeepCloneAndJsonRoundTrip() {
        var source = new PoliceDrivingSettings {
            stoppedSpeedThreshold = 0.2f,
            recovery = new PoliceRecoverySettings {
                lightImpactSpeed = 2f, heavyImpactSpeed = 4f, lightHoldSeconds = 1.2f,
                settleSpeed = 2f, settleTimeoutSeconds = 3f, settleBrake = 0.8f,
                rejoinRetrySeconds = 1.5f, maxRejoinDistance = 7f, reverseMaxSeconds = 0.5f,
                maneuverSpeed = 2.2f, maximumReverseTravel = 4f, maximumActiveSeconds = 9f
            }
        };
        Assert.IsTrue(source.IsValid(out string reason), reason);
        var clone = source.Clone();
        var restored = JsonUtility.FromJson<PoliceDrivingSettings>(JsonUtility.ToJson(source));
        source.recovery.lightImpactSpeed = 99f;
        source.recovery = null;

        Assert.IsNotNull(clone.recovery);
        Assert.IsNotNull(restored.recovery);
        Assert.AreNotSame(clone.recovery, restored.recovery);
        Assert.AreEqual(2f, clone.recovery.lightImpactSpeed);
        Assert.AreEqual(4f, clone.recovery.heavyImpactSpeed);
        Assert.AreEqual(1.2f, clone.recovery.lightHoldSeconds);
        Assert.AreEqual(2f, clone.recovery.settleSpeed);
        Assert.AreEqual(3f, clone.recovery.settleTimeoutSeconds);
        Assert.AreEqual(0.8f, clone.recovery.settleBrake);
        Assert.AreEqual(1.5f, clone.recovery.rejoinRetrySeconds);
        Assert.AreEqual(7f, clone.recovery.maxRejoinDistance);
        Assert.AreEqual(0.5f, clone.recovery.reverseMaxSeconds);
        Assert.AreEqual(2.2f, clone.recovery.maneuverSpeed);
        Assert.AreEqual(4f, clone.recovery.maximumReverseTravel);
        Assert.AreEqual(9f, clone.recovery.maximumActiveSeconds);
        Assert.AreEqual(JsonUtility.ToJson(clone.recovery), JsonUtility.ToJson(restored.recovery));
        Assert.IsTrue(clone.IsValid(out reason), reason);
        Assert.IsTrue(restored.IsValid(out reason), reason);
    }

    /// <summary>Deep clone remains null-safe when an invalid authored object omits recovery settings.</summary>
    [Test]
    public void DrivingSettings_CloneIsNullSafeForMissingRecovery() {
        var source = new PoliceDrivingSettings { recovery = null };
        var clone = source.Clone();
        Assert.IsNull(clone.recovery);
        Assert.IsFalse(clone.IsValid(out _));
    }
}
