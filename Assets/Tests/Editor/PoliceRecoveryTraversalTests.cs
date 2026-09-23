using NUnit.Framework;
using UnityEngine;

/// <summary>Pure bounded tests for the police recovery traversal wrapper and shared policy phases.</summary>
public sealed class PoliceRecoveryTraversalTests {
    /// <summary>Measured positions before and after a repeated impact must remain part of reverse travel.</summary>
    [Test]
    public void Account_UsesPreviousPositionAndRepeatImpactPreservesTravel() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());

        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.5f), 1f, false));
        Assert.IsFalse(traversal.NotifyImpact(4f, 0.05f, new Vector2(0f, -1f)));
        Assert.IsTrue(traversal.Account(0.06f, 0.01f, new Vector2(0f, -1.5f), 0.5f, false));

        Assert.Greater(traversal.ReverseTravel, 1.4f);
        Assert.Less(traversal.ReverseTravel, 1.6f);
        Assert.IsFalse(traversal.IsReversing);
    }

    /// <summary>Reverse duration uses the larger clock delta and accumulated dt, then expires without ObservePolicy.</summary>
    [Test]
    public void Account_ReverseClockExpiryCancelsWithoutObservation() {
        var settings = Settings();
        settings.reverseMaxSeconds = 0.2f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());

        Assert.IsTrue(traversal.Account(0.31f, 0.01f, new Vector2(0f, -0.1f), 1f, false));
        Assert.GreaterOrEqual(traversal.ReverseSeconds, 0.29f);
        Assert.IsFalse(traversal.IsReversing);
        Assert.IsFalse(traversal.CanStartReverse);
    }

    /// <summary>Rear clearance remains admissible during the currently active reverse credit.</summary>
    [Test]
    public void ObservePolicy_ActiveReverseRetainsRearClearance() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());

        traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.1f), 0.5f, false);
        traversal.ObservePolicy(0.5f, BlockedRearClear(), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, traversal.Phase);
        Assert.IsTrue(traversal.IsReversing);
    }

    /// <summary>Leaving Reversing cancels the command but preserves measured braking-tail travel until stopped.</summary>
    [Test]
    public void ObservePolicy_ReverseExitTracksBrakingTail() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.4f), 1f, false));

        traversal.ObservePolicy(1f, new CrashRecoveryPolicy.RejoinObservation(9f, false, true, false), true);
        Assert.IsFalse(traversal.IsReversing);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, traversal.Phase);
        Assert.IsTrue(traversal.Account(0.06f, 0.02f, new Vector2(0f, -0.8f), 0.5f, false));
        Assert.IsTrue(traversal.Account(0.08f, 0.02f, new Vector2(0f, -1.0f), 0f, false));

        Assert.Greater(traversal.ReverseTravel, 0.9f);
        Assert.Less(traversal.ReverseTravel, 1.1f);
        float stoppedTravel = traversal.ReverseTravel;
        Assert.IsTrue(traversal.Account(0.10f, 0.02f, new Vector2(0f, 0.5f), 1f, false));
        Assert.AreEqual(stoppedTravel, traversal.ReverseTravel);
    }

    /// <summary>Invalid active impact input fails closed instead of leaving a powered reverse alive.</summary>
    [Test]
    public void NotifyImpact_InvalidActiveInputFailsClosed() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());

        Assert.IsFalse(traversal.NotifyImpact(float.NaN, 0.03f, Vector2.zero));
        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
        Assert.IsFalse(traversal.CanStartReverse);
    }

    /// <summary>Account rejects zero, negative, nonfinite, and regressing time inputs without renewing state.</summary>
    [Test]
    public void Account_InvalidDeltaAndClockFailClosed() {
        var zero = StartHeavyEpisode(out _);
        Assert.IsFalse(zero.Account(0.02f, 0f, Vector2.zero, 0f, false));
        Assert.IsTrue(zero.IsExhausted);

        var negative = StartHeavyEpisode(out _);
        Assert.IsFalse(negative.Account(0.02f, -0.01f, Vector2.zero, 0f, false));
        Assert.IsTrue(negative.IsExhausted);

        var nonfinite = StartHeavyEpisode(out _);
        Assert.IsFalse(nonfinite.Account(0.02f, float.PositiveInfinity, Vector2.zero, 0f, false));
        Assert.IsTrue(nonfinite.IsExhausted);

        var regressing = StartHeavyEpisode(out _);
        Assert.IsTrue(regressing.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsFalse(regressing.Account(0.01f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsTrue(regressing.IsExhausted);
    }

    /// <summary>Invalid observations consume the single pending Account slot and become finite denied input.</summary>
    [Test]
    public void ObservePolicy_InvalidObservationIsDeniedAndConsumedOnce() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 2f, false));
        traversal.ObservePolicy(float.NaN, new CrashRecoveryPolicy.RejoinObservation(float.NaN, true, false, true), false);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, traversal.Phase);
        Assert.IsFalse(traversal.IsReversing);

        traversal.ObservePolicy(0f, new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, traversal.Phase);
    }

    /// <summary>Geometry wait can accumulate policy time without observation, then consume it once at a fresh retry.</summary>
    [Test]
    public void ObservePolicy_WaitAccumulatesUntilFreshObservationAndDoesNotDoubleTick() {
        var settings = Settings();
        settings.rejoinRetrySeconds = 0.03f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, new CrashRecoveryPolicy.RejoinObservation(9f, false, true, false), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, traversal.Phase);

        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.1f), 0f, false));
        Assert.IsTrue(traversal.Account(0.06f, 0.02f, new Vector2(0f, -0.2f), 0f, false));
        Assert.AreEqual(CrashRecoveryPolicy.Phase.WaitingForRejoin, traversal.Phase);

        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, traversal.Phase);
        traversal.ObservePolicy(0f, new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Reversing, traversal.Phase);
    }

    /// <summary>The one reverse credit cannot be restarted after cancellation, even while policy remains reversing.</summary>
    [Test]
    public void ReverseCredit_CancelAndPauseCannotRestart() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        traversal.Pause();
        Assert.IsFalse(traversal.IsReversing);
        Assert.IsFalse(traversal.CanStartReverse);
        Assert.IsFalse(traversal.TryStartReverse());
        traversal.CancelMovement();
        Assert.IsFalse(traversal.TryStartReverse());
    }

    /// <summary>The whole episode expires once, then only a proven stopped fresh gate may release it.</summary>
    [Test]
    public void EpisodeTimeout_ExhaustsAndRequiresProvenRelease() {
        var settings = Settings();
        settings.lightHoldSeconds = 0.05f;
        settings.settleTimeoutSeconds = 0.05f;
        settings.reverseMaxSeconds = 0.05f;
        settings.maximumActiveSeconds = 0.1f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.Account(0.12f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsTrue(traversal.IsRecovering);
        Assert.IsFalse(traversal.TryRelease(false, false, 0f));
        Assert.IsFalse(traversal.TryRelease(true, true, 0f));
        Assert.IsFalse(traversal.TryRelease(true, false, 0.2f));
        Assert.IsTrue(traversal.TryRelease(true, false, 0f));
        Assert.IsFalse(traversal.IsRecovering);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, traversal.Phase);
    }

    /// <summary>Nondefault thresholds and reverse limits demonstrably alter wrapper decisions.</summary>
    [Test]
    public void NondefaultSettings_ChangeImpactAndReverseBehavior() {
        var settings = Settings();
        settings.lightImpactSpeed = 2f;
        settings.heavyImpactSpeed = 4f;
        settings.reverseMaxSeconds = 0.1f;
        var traversal = new PoliceRecoveryTraversal(settings, 0.1f);
        Assert.IsFalse(traversal.NotifyImpact(1.5f, 0f, Vector2.zero));
        Assert.IsTrue(traversal.NotifyImpact(4f, 0f, Vector2.zero));
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.2f, 0.01f, new Vector2(0f, -0.1f), 1f, false));
        Assert.IsFalse(traversal.IsReversing);
        Assert.GreaterOrEqual(traversal.ReverseSeconds, 0.18f);
    }

    /// <summary>Healthy idle accounting does not leak its old clock span into a newly opened light episode.</summary>
    [Test]
    public void IdleTimeBeforeImpact_DoesNotSkipLightHold() {
        var traversal = new PoliceRecoveryTraversal(Settings(), 0.1f);
        Assert.IsTrue(traversal.Account(100f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.NotifyImpact(1.5f, 100f, Vector2.zero));
        Assert.IsTrue(traversal.Account(100.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true), true);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.HoldingAfterContact, traversal.Phase);
        Assert.Less(traversal.ActiveSeconds, 0.1f);
    }

    /// <summary>Only one heavy escalation is admitted and later impacts cannot renew episode elapsed time.</summary>
    [Test]
    public void ImpactEscalation_IsSingleAndDoesNotRenewEpisode() {
        var traversal = new PoliceRecoveryTraversal(Settings(), 0.1f);
        Assert.IsTrue(traversal.NotifyImpact(1.5f, 0f, Vector2.zero));
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsFalse(traversal.NotifyImpact(4f, 0.03f, Vector2.zero));
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, Vector2.zero, 0f, false));
        float elapsedAfterFirstEscalation = traversal.ActiveSeconds;
        Assert.IsFalse(traversal.NotifyImpact(4f, 0.05f, Vector2.zero));
        Assert.IsTrue(traversal.Account(0.06f, 0.02f, Vector2.zero, 0f, false));
        Assert.Greater(traversal.ActiveSeconds, elapsedAfterFirstEscalation);
        Assert.Less(traversal.ActiveSeconds, 8f);
        Assert.IsFalse(traversal.IsExhausted);
    }

    /// <summary>Reverse travel over its finite authored limit fails closed without truncating the measurement.</summary>
    [Test]
    public void ReverseTravelBeyondLimit_FailsClosed() {
        var settings = Settings();
        settings.maximumReverseTravel = 0.5f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.4f), 1f, false));
        Assert.IsFalse(traversal.Account(0.06f, 0.02f, new Vector2(0f, -0.7f), 1f, false));
        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
    }

    /// <summary>Cancelled reverse continues measuring the braking tail below the stop threshold, then ignores forward travel after a real stop.</summary>
    [Test]
    public void CancelledReverse_TracksBelowThresholdUntilActualStopThenFreezes() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.4f), 1f, false));

        traversal.CancelMovement();
        Assert.IsTrue(traversal.Account(0.06f, 0.02f, new Vector2(0f, -0.8f), 0.075f, false));
        float beforeStop = traversal.ReverseTravel;
        Assert.Greater(beforeStop, 0.7f);
        Assert.IsTrue(traversal.Account(0.08f, 0.02f, new Vector2(0f, -1.0f), 0f, false));
        float stoppedTravel = traversal.ReverseTravel;
        Assert.Greater(stoppedTravel, beforeStop);

        Assert.IsTrue(traversal.Account(0.10f, 0.02f, new Vector2(0f, 0.5f), 1f, false));
        Assert.AreEqual(stoppedTravel, traversal.ReverseTravel);
    }

    /// <summary>Whole-episode expiry accounts the final displaced zero-speed interval before preserving settled travel.</summary>
    [Test]
    public void EpisodeExpiry_FinalDisplacedStoppedAccountRetainsLastTravelInterval() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.4f), 1f, false));

        Assert.IsTrue(traversal.Account(8.01f, 0.02f, new Vector2(0f, -1.0f), 0f, false));
        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
        Assert.AreEqual(1f, traversal.ReverseTravel, 0.0001f);
        Assert.AreEqual(8f, traversal.ActiveSeconds, 0.0001f);
    }

    /// <summary>Reverse overshoot remains observable at the measured value while the policy fails closed.</summary>
    [Test]
    public void ReverseTravelOvershoot_PreservesMeasuredDiagnosticBeforeFailClosed() {
        var settings = Settings();
        settings.maximumReverseTravel = 0.5f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.4f), 1f, false));
        Assert.IsFalse(traversal.Account(0.06f, 0.02f, new Vector2(0f, -0.7f), 1f, false));

        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
        Assert.AreEqual(0.7f, traversal.ReverseTravel, 0.0001f);
    }

    /// <summary>Equal-clock accumulated dt still reaches the whole-episode bound.</summary>
    [Test]
    public void EqualClockAccumulatedDelta_ExpiresEpisode() {
        var settings = Settings();
        settings.lightHoldSeconds = 0.05f;
        settings.settleTimeoutSeconds = 0.05f;
        settings.reverseMaxSeconds = 0.05f;
        settings.maximumActiveSeconds = 0.05f;
        var traversal = StartHeavyEpisode(settings);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.Account(0.02f, 0.04f, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.IsExhausted);
    }

    /// <summary>Pause freezes current episode time while ending any active reverse command.</summary>
    [Test]
    public void Pause_DoesNotAdvanceActiveClock() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        float beforePause = traversal.ActiveSeconds;
        traversal.Pause();
        Assert.AreEqual(beforePause, traversal.ActiveSeconds);
        Assert.IsFalse(traversal.IsReversing);
    }

    /// <summary>A null settings source creates a harmless unconfigured wrapper and never dereferences it.</summary>
    [Test]
    public void UnconfiguredTraversal_FailsClosedWithoutSettingsAccess() {
        var traversal = new PoliceRecoveryTraversal(null, 0.1f);
        Assert.IsFalse(traversal.NotifyImpact(4f, 0f, Vector2.zero));
        Assert.IsFalse(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, true));
        traversal.ObservePolicy(0f, new CrashRecoveryPolicy.RejoinObservation(0f, true, false, true), true);
        Assert.IsFalse(traversal.IsRecovering);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, traversal.Phase);
    }

    /// <summary>Crash-mode rising opens one episode, while continued crash mode cannot restart its clock.</summary>
    [Test]
    public void CrashRise_OpensOnceWithoutRenewal() {
        var traversal = new PoliceRecoveryTraversal(Settings(), 0.1f);
        Assert.IsTrue(traversal.Account(10f, 0.02f, Vector2.zero, 0f, true));
        float firstElapsed = traversal.ActiveSeconds;
        Assert.IsTrue(traversal.IsRecovering);
        Assert.IsTrue(traversal.Account(10.02f, 0.02f, Vector2.zero, 0f, true));
        Assert.Greater(traversal.ActiveSeconds, firstElapsed);
        Assert.Less(traversal.ActiveSeconds, 0.1f);
    }

    /// <summary>Pending policy delta overflow fails closed instead of producing an infinite accounting value.</summary>
    [Test]
    public void Account_PendingDeltaOverflowFailsClosed() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, float.MaxValue, Vector2.zero, 0f, false));
        Assert.IsFalse(traversal.Account(0.03f, float.MaxValue, Vector2.zero, 0f, false));
        Assert.IsTrue(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
    }

    /// <summary>Reset clears episode timing, reverse credit, travel, exhaustion, and the shared policy phase.</summary>
    [Test]
    public void ResetForNewLife_ClearsFullEpisodeState() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsTrue(traversal.Account(0.02f, 0.02f, Vector2.zero, 0f, false));
        traversal.ObservePolicy(0f, BlockedRearClear(), true);
        Assert.IsTrue(traversal.TryStartReverse());
        Assert.IsTrue(traversal.Account(0.04f, 0.02f, new Vector2(0f, -0.2f), 1f, false));

        traversal.ResetForNewLife();
        Assert.IsFalse(traversal.IsRecovering);
        Assert.IsFalse(traversal.IsExhausted);
        Assert.IsFalse(traversal.IsReversing);
        Assert.IsFalse(traversal.CanStartReverse);
        Assert.AreEqual(0f, traversal.ActiveSeconds);
        Assert.AreEqual(0f, traversal.ReverseSeconds);
        Assert.AreEqual(0f, traversal.ReverseTravel);
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, traversal.Phase);
        Assert.IsTrue(traversal.NotifyImpact(1.5f, 0f, Vector2.zero));
    }

    /// <summary>Release rejects nonfinite or moving inputs and accepts an already proven stopped gate.</summary>
    [Test]
    public void TryRelease_RejectsNonfiniteAndMovingInputs() {
        var traversal = StartHeavyEpisode(out _);
        Assert.IsFalse(traversal.TryRelease(true, false, float.NaN));
        Assert.IsFalse(traversal.TryRelease(true, false, float.PositiveInfinity));
        Assert.IsFalse(traversal.TryRelease(true, false, 0.2f));
        Assert.IsTrue(traversal.TryRelease(true, false, 0.1f));
        Assert.IsFalse(traversal.IsRecovering);
    }

    static PoliceRecoveryTraversal StartHeavyEpisode(out PoliceRecoverySettings settings) {
        settings = Settings();
        return StartHeavyEpisode(settings);
    }

    static PoliceRecoveryTraversal StartHeavyEpisode(PoliceRecoverySettings settings) {
        var traversal = new PoliceRecoveryTraversal(settings, 0.1f);
        Assert.IsTrue(traversal.NotifyImpact(4f, 0f, Vector2.zero));
        Assert.IsTrue(traversal.IsRecovering);
        return traversal;
    }

    static CrashRecoveryPolicy.RejoinObservation BlockedRearClear() =>
        new CrashRecoveryPolicy.RejoinObservation(9f, false, true, true);

    static PoliceRecoverySettings Settings() => new PoliceRecoverySettings {
        lightImpactSpeed = 1f,
        heavyImpactSpeed = 3f,
        lightHoldSeconds = 0.6f,
        settleSpeed = 1f,
        settleTimeoutSeconds = 2f,
        settleBrake = 0.3f,
        rejoinRetrySeconds = 1f,
        maxRejoinDistance = 8f,
        reverseMaxSeconds = 1.5f,
        maneuverSpeed = 1.5f,
        maximumReverseTravel = 3f,
        maximumActiveSeconds = 8f
    };
}
