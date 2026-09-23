using NUnit.Framework;
using UnityEngine;

/// <summary>Pure bounded state and reserve-math coverage for the police ram decision.</summary>
public sealed class PoliceRamDecisionTests {
    /// <summary>Eligibility accepts a finite ahead target inside the cone and rejects lateral, rear, and gap failures.</summary>
    [Test]
    public void RamDecision_EligibilityUsesAheadConeAndSurfaceGap() {
        var decision = Configured();
        Assert.IsTrue(decision.IsEligible(Vector2.up, new Vector2(0f, 2f), 2f, 0.05f));
        Assert.IsFalse(decision.IsEligible(Vector2.up, new Vector2(0.8f, 2f), 2f, 0.05f));
        Assert.IsFalse(decision.IsEligible(Vector2.up, Vector2.down, 2f, 0.05f));
        Assert.IsFalse(decision.IsEligible(Vector2.up, Vector2.up, 0.05f, 0.05f));
        Assert.IsFalse(decision.IsEligible(Vector2.up, Vector2.up, 4.01f, 0.05f));
        Assert.IsFalse(decision.IsEligible(Vector2.zero, Vector2.up, 2f, 0.05f));
    }

    /// <summary>Reserve math matches the double-intermediate certificate without truncation.</summary>
    [Test]
    public void RamDecision_ReserveMatchesFullDriveAndStopFormula() {
        Assert.IsTrue(PoliceRamDecision.TryReserve(2f, 3f, 4f, 1f, 0.2f, 0.02f, 1f, 2f, 20f,
            out float length, out float peakSpeed));
        Assert.AreEqual(12.02285f, length, 0.0001f);
        Assert.AreEqual(5.66f, peakSpeed, 0.0001f);
        Assert.IsFalse(PoliceRamDecision.TryReserve(2f, 3f, 4f, 1f, 0.2f, 0.02f, 1f, 2f, 12f,
            out _, out _));
        Assert.IsFalse(PoliceRamDecision.TryReserve(float.MaxValue, float.MaxValue, 1f, 1f, 0f, 0.02f, 1f, 1f,
            float.MaxValue, out _, out _));
        Assert.IsFalse(PoliceRamDecision.TryReserve(1f, 0f, 4f, 1f, 0f, 0.02f, 1f, 1f, 20f,
            out _, out _));
    }

    /// <summary>Lateral allowance matches the authored grip formula and fails closed at zero grip or overflow.</summary>
    [Test]
    public void RamDecision_LateralAllowanceMatchesGripFormula() {
        Assert.IsTrue(PoliceRamDecision.TryLateralAllowance(4f, 0.5f, 0.2f, out float distance));
        Assert.AreEqual(0.8f, distance, 0.0001f);
        Assert.IsTrue(PoliceRamDecision.TryLateralAllowance(-4f, 1f, 0.2f, out distance));
        Assert.AreEqual(0f, distance);
        Assert.IsTrue(PoliceRamDecision.TryLateralAllowance(0f, 0f, 0.2f, out distance));
        Assert.AreEqual(0f, distance);
        Assert.IsFalse(PoliceRamDecision.TryLateralAllowance(1f, 0f, 0.2f, out _));
        Assert.IsFalse(PoliceRamDecision.TryLateralAllowance(float.MaxValue, float.Epsilon, 1f, out _));
    }

    /// <summary>Equal-clock cadence accumulates active dt once while the original interval remains authoritative.</summary>
    [Test]
    public void RamDecision_EqualClockCadenceDoesNotRenewOriginalDuration() {
        var decision = Configured();
        Assert.IsTrue(decision.TryBegin(10f, 0.1f, 5f, 0.1f, true, false));
        Assert.AreEqual(0.9f, decision.RemainingActiveSeconds, 0.0001f);
        Assert.IsTrue(decision.Advance(10f, 0.1f));
        Assert.AreEqual(0.8f, decision.RemainingActiveSeconds, 0.0001f);
        Assert.IsTrue(decision.Advance(10.1f, 0.1f));
        Assert.AreEqual(0.7f, decision.RemainingActiveSeconds, 0.0001f);
        Assert.IsTrue(decision.Advance(10.85f, 0.01f));
        Assert.AreEqual(0.15f, decision.RemainingActiveSeconds, 0.0001f);
        Assert.IsFalse(decision.Advance(11f, 0.01f));
        Assert.IsFalse(decision.IsActive);
        Assert.IsTrue(decision.SeparationRequired);
    }

    /// <summary>Invalid and regressing active time cancels safely without accruing paused time.</summary>
    [Test]
    public void RamDecision_InvalidTimeCancelsWithoutPausedAccrual() {
        var decision = Configured();
        Assert.IsTrue(decision.TryBegin(2f, 0.02f, 0f, 0.1f, true, false));
        Assert.IsFalse(decision.Advance(1.9f, 0.02f));
        float deadline = decision.CooldownUntil;
        Assert.Greater(deadline, 2f);
        Assert.IsFalse(decision.Advance(float.NaN, 0.02f));
        Assert.AreEqual(deadline, decision.CooldownUntil);
        decision.ResetForNewLife();
        Assert.IsTrue(decision.TryBegin(5f, 0.02f, 0f, 0.1f, true, false));
        Assert.IsFalse(decision.Advance(5f, float.NaN));
        Assert.IsFalse(decision.IsActive);
    }

    /// <summary>Cooldown is idempotent, requires stopped speed, and separates recovery from admission.</summary>
    [Test]
    public void RamDecision_CooldownSeparationAndRecoveryGates() {
        var decision = Configured();
        Assert.IsTrue(decision.Cancel(1f));
        float deadline = decision.CooldownUntil;
        Assert.IsFalse(decision.Cancel(1.5f));
        Assert.AreEqual(deadline, decision.CooldownUntil);
        Assert.IsFalse(decision.CanAttempt(deadline, 0.2f, 0.1f, false));
        Assert.IsTrue(decision.CanAttempt(deadline, 0.1f, 0.1f, false));
        Assert.IsFalse(decision.TryBegin(deadline, 0.02f, 0f, 0.1f, false, false));
        Assert.IsTrue(decision.TryBegin(deadline, 0.02f, 0f, 0.1f, true, false));
        Assert.IsTrue(decision.IsActive);
        decision.Cancel(deadline + 0.02f);
        Assert.IsFalse(decision.TryBegin(decision.CooldownUntil, 0.02f, 0f, 0.1f, true, true));
    }

    /// <summary>Reset clears lifecycle latches while the detached nondefault configuration remains effective.</summary>
    [Test]
    public void RamDecision_ResetPreservesDetachedConfiguration() {
        var settings = new PoliceDrivingSettings { ramConeDegrees = 20f, ramMaximumSurfaceGap = 3f,
            ramMaximumActiveSeconds = 1.5f, ramCooldownSeconds = 4f };
        var decision = new PoliceRamDecision();
        Assert.IsTrue(decision.TryConfigure(settings));
        settings.ramConeDegrees = 1f;
        Assert.IsTrue(decision.IsEligible(Vector2.up, new Vector2(0.3f, 1f), 2f, 0.1f));
        Assert.IsTrue(decision.TryBegin(0f, 0.02f, 0f, 0.1f, true, false));
        Assert.IsTrue(decision.Cancel(0.02f));
        decision.ResetForNewLife();
        Assert.IsFalse(decision.IsActive);
        Assert.IsFalse(decision.SeparationRequired);
        Assert.AreEqual(0f, decision.CooldownUntil);
        Assert.IsTrue(decision.IsEligible(Vector2.up, new Vector2(0.3f, 1f), 2f, 0.1f));
    }

    /// <summary>Initial and post-cancel attempts reject negative or regressing retained clocks.</summary>
    [Test]
    public void RamDecision_RejectsNegativeAndRegressingClocks() {
        var decision = Configured();
        Assert.IsFalse(decision.CanAttempt(-0.01f, 0f, 0.1f, false));
        Assert.IsTrue(decision.TryBegin(2f, 0.02f, 0f, 0.1f, true, false));
        Assert.IsTrue(decision.Cancel(2.02f));
        Assert.IsFalse(decision.CanAttempt(1.99f, 0f, 0.1f, false));
        Assert.IsFalse(decision.CanAttempt(-1f, 0f, 0.1f, false));
    }

    /// <summary>Eligibility uses the exact configured cone boundary without an angular fudge allowance.</summary>
    [Test]
    public void RamDecision_ConeBoundaryUsesExactFiniteComparison() {
        var settings = new PoliceDrivingSettings { ramConeDegrees = 20f };
        var decision = new PoliceRamDecision();
        Assert.IsTrue(decision.TryConfigure(settings));
        Vector2 inside = new Vector2(Mathf.Sin(19.9f * Mathf.Deg2Rad), Mathf.Cos(19.9f * Mathf.Deg2Rad));
        Vector2 outside = new Vector2(Mathf.Sin(20.1f * Mathf.Deg2Rad), Mathf.Cos(20.1f * Mathf.Deg2Rad));
        Assert.IsTrue(decision.IsEligible(Vector2.up, inside, 2f, 0.1f));
        Assert.IsFalse(decision.IsEligible(Vector2.up, outside, 2f, 0.1f));
    }

    /// <summary>An unrepresentable cooldown deadline remains latched and cannot rearm at float maximum.</summary>
    [Test]
    public void RamDecision_UnrepresentableCooldownDeadlineFailsClosed() {
        var decision = Configured();
        Assert.IsTrue(decision.Cancel(float.MaxValue));
        Assert.IsTrue(decision.SeparationRequired);
        Assert.AreEqual(float.MaxValue, decision.CooldownUntil);
        Assert.IsFalse(decision.CanAttempt(float.MaxValue, 0f, 0.1f, false));
        Assert.IsFalse(decision.TryBegin(float.MaxValue, 0.02f, 0f, 0.1f, true, false));
    }

    /// <summary>A positive cooldown that rounds back to its retained clock remains fail-closed.</summary>
    [Test]
    public void RamDecision_RoundedCooldownDeadlineFailsClosed() {
        var decision = Configured();
        Assert.IsTrue(decision.Cancel(100000000f));
        Assert.AreEqual(float.MaxValue, decision.CooldownUntil);
        Assert.IsFalse(decision.CanAttempt(100000000f, 0f, 0.1f, false));
    }

    /// <summary>An invalid replacement clears a previously active detached configuration and state.</summary>
    [Test]
    public void RamDecision_InvalidReconfigureClearsActiveConfiguration() {
        var decision = Configured();
        Assert.IsTrue(decision.TryBegin(0f, 0.02f, 0f, 0.1f, true, false));
        var invalid = new PoliceDrivingSettings { ramConeDegrees = 0f };
        Assert.IsFalse(decision.TryConfigure(invalid));
        Assert.IsFalse(decision.IsActive);
        Assert.IsFalse(decision.SeparationRequired);
        Assert.IsFalse(decision.IsEligible(Vector2.up, Vector2.up, 2f, 0.1f));
        Assert.IsFalse(decision.CanAttempt(0f, 0f, 0.1f, false));
    }

    static PoliceRamDecision Configured() {
        var decision = new PoliceRamDecision();
        Assert.IsTrue(decision.TryConfigure(new PoliceDrivingSettings()));
        return decision;
    }
}
