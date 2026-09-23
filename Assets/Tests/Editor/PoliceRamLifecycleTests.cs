using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Bounded EditMode coverage for Ram timeout, safety-failure, cadence, and lifecycle gates.
/// These tests validate controller state and commands; they do not claim EditMode collision
/// callback delivery, which is covered by the Play-mode contact bridge.
/// </summary>
public sealed class PoliceRamLifecycleTests {
    /// <summary>
    /// A failed active safety check stops immediately, cannot fall through to another Ram
    /// admission, and cannot renew its cancellation deadline while the fault remains.
    /// </summary>
    [TestCase(0.02f, 0)]
    [TestCase(0.03f, 0)]
    [TestCase(0.02f, 1)]
    [TestCase(0.03f, 1)]
    [TestCase(0.02f, 2)]
    [TestCase(0.03f, 2)]
    [TestCase(0.02f, 3)]
    [TestCase(0.03f, 3)]
    public void ActiveSafetyFailure_DoesNotFallbackOrRenew(float deltaTime, int fault) {
        using (var fixture = ActiveRamFixture(deltaTime)) {
            int startsBeforeFault = fixture.Controller.RamStarts;
            GameObject blocker = null;
            switch (fault) {
                case 0:
                    blocker = fixture.CreateBlocker(new Vector2(0f, 25f), false, Vector2.one);
                    break;
                case 1:
                    blocker = fixture.CreateBlocker(new Vector2(0f, 25f), true, Vector2.one);
                    break;
                case 2:
                    for (int index = 0; index < 16; index++) fixture.Player.gameObject.AddComponent<BoxCollider2D>();
                    break;
                case 3:
                    fixture.SetRuntimeCursorWork(0);
                    break;
                default:
                    Assert.Fail("Unknown bounded safety fault: " + fault);
                    break;
            }

            fixture.Step(1);
            string immediateFacts = FaultFacts(fixture, fault, blocker);
            Assert.IsFalse(fixture.Controller.IsRamming, immediateFacts);
            Assert.IsTrue(fixture.Controller.IsBound, immediateFacts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, immediateFacts);
            Assert.AreEqual(startsBeforeFault, fixture.Controller.RamStarts, immediateFacts);
            float cancellationDeadline = fixture.Controller.RamCooldownUntil;

            int blockedSteps = Mathf.CeilToInt(1.2f / deltaTime);
            for (int step = 0; step < blockedSteps; step++) fixture.Step(1);
            string continuedFacts = FaultFacts(fixture, fault, blocker);
            Assert.IsFalse(fixture.Controller.IsRamming, continuedFacts);
            Assert.AreEqual(startsBeforeFault, fixture.Controller.RamStarts, continuedFacts);
            Assert.AreEqual(cancellationDeadline, fixture.Controller.RamCooldownUntil, continuedFacts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, continuedFacts);
        }
    }

    /// <summary>
    /// A reachable Ram at a four-unit surface gap remains active through the normal planner
    /// refresh, then expires at its original one-second bound without renewal or damage.
    /// </summary>
    [TestCase(0.02f)]
    [TestCase(0.03f)]
    public void ActiveRam_TimeoutPreservesCadenceAndDoesNotRenew(float deltaTime) {
        using (var fixture = ActiveRamFixture(deltaTime, 8f, 14f)) {
            int starts = fixture.Controller.RamStarts;
            int initialPlannerAttempts = fixture.Controller.PlannerAttemptCount;
            float initialHealth = fixture.Player.CurrentHealth;
            float admissionClock = fixture.World.SessionTime;
            bool activeAfterPlannerRefresh = false;
            bool plannerRefreshed = false;

            for (int step = 0; step < 60; step++) {
                fixture.Step(1);
                if (fixture.World.SessionTime >= admissionClock + 0.75f &&
                    fixture.Controller.PlannerAttemptCount > initialPlannerAttempts) {
                    plannerRefreshed = true;
                    activeAfterPlannerRefresh |= fixture.Controller.IsRamming;
                }
                if (fixture.World.SessionTime >= admissionClock + 1.02f) break;
            }

            string refreshFacts = TimeoutFacts(fixture, admissionClock, initialPlannerAttempts);
            Assert.IsTrue(plannerRefreshed, refreshFacts);
            Assert.IsTrue(activeAfterPlannerRefresh, refreshFacts);
            Assert.AreEqual(starts, fixture.Controller.RamStarts, refreshFacts);

            bool reachedOriginalTimeout = fixture.World.SessionTime >= admissionClock + 1.02f;
            for (int step = 0; step < 10 && !reachedOriginalTimeout; step++) {
                fixture.Step(1);
                reachedOriginalTimeout = fixture.World.SessionTime >= admissionClock + 1.02f;
            }
            Assert.IsTrue(reachedOriginalTimeout, TimeoutFacts(fixture, admissionClock, initialPlannerAttempts));
            fixture.Step(1);
            string expiryFacts = TimeoutFacts(fixture, admissionClock, initialPlannerAttempts);
            Assert.IsFalse(fixture.Controller.IsRamming, expiryFacts);
            Assert.AreEqual(starts, fixture.Controller.RamStarts, expiryFacts);
            Assert.AreEqual(initialHealth, fixture.Player.CurrentHealth, expiryFacts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, expiryFacts);
        }
    }

    /// <summary>Pause cancels the active command while preserving the healthy binding.</summary>
    [Test]
    public void ActiveRam_PauseCancelsAndPreservesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Controller.Tick(fixture.StepDelta, true);
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "pause"));
            Assert.IsTrue(fixture.Controller.IsBound, GateFacts(fixture, "pause"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "pause"));
        }
    }

    /// <summary>Invalid delta stops and cancels the active Ram without a physics step.</summary>
    [Test]
    public void ActiveRam_InvalidDeltaCancels() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Controller.Tick(0f, false);
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "invalid-delta"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "invalid-delta"));
        }
    }

    /// <summary>A regressing session clock closes the invalid binding and cannot keep driving.</summary>
    [Test]
    public void ActiveRam_RegressingClockClosesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Step(1); // The first admission sampled zero; first observe a strictly later clock.
            fixture.SetActiveClock(0f);
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsFalse(fixture.Controller.IsBound, GateFacts(fixture, "regressing-clock"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "regressing-clock"));
        }
    }

    /// <summary>A graph revision cancels the certificate but preserves the valid life binding.</summary>
    [Test]
    public void ActiveRam_GraphInvalidationPreservesBindingButCancels() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.InvalidateGraph();
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsTrue(fixture.Controller.IsBound, GateFacts(fixture, "graph"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "graph"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "graph"));
        }
    }

    /// <summary>Unbinding the target cancels the Ram without manufacturing an impact event.</summary>
    [Test]
    public void ActiveRam_TargetUnbindCancels() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Player.Unbind();
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "target-unbind"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "target-unbind"));
        }
    }

    /// <summary>Unbinding the police receiver closes the controller's captured life.</summary>
    [Test]
    public void ActiveRam_PoliceUnbindClosesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.PoliceReceiver.Unbind();
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsFalse(fixture.Controller.IsBound, GateFacts(fixture, "police-unbind"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "police-unbind"));
        }
    }

    /// <summary>Changing the captured tactical intent invalidates the binding on the next tick.</summary>
    [Test]
    public void ActiveRam_IntentMutationClosesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Binding.behavior.tacticalRole = PoliceTacticalRole.Pursue;
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsFalse(fixture.Controller.IsBound, GateFacts(fixture, "intent-mutation"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "intent-mutation"));
        }
    }

    /// <summary>Receiver-owned crash mode cancels Ram and gives bounded recovery priority without losing the binding.</summary>
    [Test]
    public void ActiveRam_CrashModeCancelsAndPreservesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Binding.body.Motor.SetCrashMode(true);
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.IsTrue(fixture.Controller.IsBound, GateFacts(fixture, "crash-mode"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "crash-mode"));
            Assert.IsTrue(fixture.Controller.IsRecovering, GateFacts(fixture, "crash-mode"));
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "crash-mode"));
        }
    }

    /// <summary>Explicit disable cleanup revokes the binding and leaves a stopped command.</summary>
    [Test]
    public void ActiveRam_DisableClosesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Controller.enabled = false;
            fixture.InvokeControllerDisable();
            Assert.IsFalse(fixture.Controller.IsBound, GateFacts(fixture, "disable"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "disable"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, GateFacts(fixture, "disable"));
        }
    }

    /// <summary>Session end revokes the captured life and prevents further Ram state.</summary>
    [Test]
    public void ActiveRam_SessionEndClosesBinding() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            fixture.Coordinator.NotifySessionEnded();
            Assert.IsFalse(fixture.Controller.IsBound, GateFacts(fixture, "session-end"));
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "session-end"));
        }
    }

    /// <summary>
    /// Replacing the target cancels the old Ram immediately, preserves its cooldown latch,
    /// and retains the planner's minimum refresh cadence rather than forcing a fresh attempt.
    /// </summary>
    [Test]
    public void ActiveRam_TargetReplacementPreservesCooldownAndPlannerMinimum() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            int starts = fixture.Controller.RamStarts;
            int attemptsBeforeReplacement = fixture.Controller.PlannerAttemptCount;
            var replacement = fixture.ReplacePlayer(new Vector2(0f, 22f));
            Assert.IsTrue(fixture.Controller.TrySetTarget(replacement, out string reason), reason);
            fixture.Controller.Tick(fixture.StepDelta, false);
            float cancellationDeadline = fixture.Controller.RamCooldownUntil;
            Assert.IsFalse(fixture.Controller.IsRamming, GateFacts(fixture, "target-replacement"));
            Assert.AreEqual(starts, fixture.Controller.RamStarts, GateFacts(fixture, "target-replacement"));
            Assert.AreEqual(attemptsBeforeReplacement, fixture.Controller.PlannerAttemptCount,
                GateFacts(fixture, "target-replacement"));

            fixture.Step(1);
            Assert.AreEqual(cancellationDeadline, fixture.Controller.RamCooldownUntil,
                GateFacts(fixture, "target-replacement-cooldown"));
            Assert.AreEqual(starts, fixture.Controller.RamStarts,
                GateFacts(fixture, "target-replacement-cooldown"));
        }
    }

    /// <summary>High mass and weak braking make the complete post-contact reserve exceed the sweep bound.</summary>
    [Test]
    public void ReserveGate_LowBrakeForceHighMassDeniesRam() {
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.Binding.body.Body.mass = 10f;
            fixture.Profile.baseMass = 10f;
            fixture.Profile.motorSettings.maxBrakeForce = 0.2f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Assert.IsTrue(fixture.Controller.IsBound, ReserveFacts(fixture, "low-brake-high-mass"));
            Assert.IsTrue(fixture.TryQueryCurrent(out PoliceRoadTargetQuery.Result query), ReserveFacts(fixture, "low-brake-high-mass"));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, query.status, ReserveFacts(fixture, "low-brake-high-mass"));

            fixture.Step(2);
            Assert.IsFalse(fixture.Controller.IsRamming, ReserveFacts(fixture, "low-brake-high-mass"));
            Assert.AreEqual(0, fixture.Controller.RamStarts, ReserveFacts(fixture, "low-brake-high-mass"));
            Assert.IsTrue(fixture.Controller.IsBound, ReserveFacts(fixture, "low-brake-high-mass"));
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, ReserveFacts(fixture, "low-brake-high-mass"));
        }
    }

    /// <summary>A very weak captured crash multiplier makes the post-contact reserve exceed the sweep bound.</summary>
    [Test]
    public void ReserveGate_LowCrashMultiplierDeniesRam() {
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.Binding.body.Motor.crashModeForceMultiplier = 0.05f;
            Assert.IsFalse(fixture.Binding.body.Motor.IsCrashMode);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Assert.IsTrue(fixture.Controller.IsBound, ReserveFacts(fixture, "low-crash-multiplier"));
            fixture.Step(2);

            Assert.IsFalse(fixture.Controller.IsRamming, ReserveFacts(fixture, "low-crash-multiplier"));
            Assert.AreEqual(0, fixture.Controller.RamStarts, ReserveFacts(fixture, "low-crash-multiplier"));
            Assert.IsTrue(fixture.Controller.IsBound, ReserveFacts(fixture, "low-crash-multiplier"));
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, ReserveFacts(fixture, "low-crash-multiplier"));
        }
    }

    /// <summary>Admission uses the measured real sideways impulse and the captured lateral-grip envelope.</summary>
    [TestCase(0f, false)]
    [TestCase(0.5f, true)]
    public void LateralGripAdmission_UsesPhysicalImpulse(float lateralGrip, bool expectedAdmission) {
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute(roadWidth: 4f);
            fixture.Profile.motorSettings.lateralGrip = lateralGrip;
            fixture.Binding.body.Body.AddForce(Vector2.right * 0.2f, ForceMode2D.Impulse);
            fixture.Step(1);
            Assert.Greater(Mathf.Abs(fixture.PoliceVelocity.x), 0.0001f,
                ReserveFacts(fixture, "lateral-grip=" + lateralGrip + " physical impulse"));

            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Controller.Tick(fixture.StepDelta, false);

            string facts = ReserveFacts(fixture, "lateral-grip=" + lateralGrip);
            Assert.AreEqual(expectedAdmission, fixture.Controller.IsRamming, facts);
            Assert.IsTrue(fixture.Controller.IsBound, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
        }
    }

    /// <summary>A larger active step than the admitted .02-second envelope cancels immediately without renewal.</summary>
    [Test]
    public void ActiveRam_LargerDeltaCancelsAndPreservesCooldown() {
        using (var fixture = ActiveRamFixture(0.02f)) {
            int starts = fixture.Controller.RamStarts;
            fixture.Controller.Tick(0.03f, false);
            float cancellationDeadline = fixture.Controller.RamCooldownUntil;
            string immediateFacts = ReserveFacts(fixture, "larger-delta");
            Assert.IsFalse(fixture.Controller.IsRamming, immediateFacts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, immediateFacts);
            Assert.AreEqual(starts, fixture.Controller.RamStarts, immediateFacts);
            Assert.Greater(cancellationDeadline, fixture.World.SessionTime, immediateFacts);

            fixture.Step(1);
            string continuedFacts = ReserveFacts(fixture, "larger-delta-follow-up");
            Assert.AreEqual(cancellationDeadline, fixture.Controller.RamCooldownUntil, continuedFacts);
            Assert.AreEqual(starts, fixture.Controller.RamStarts, continuedFacts);
            Assert.IsFalse(fixture.Controller.IsRamming, continuedFacts);
        }
    }

    static PursuitFixture ActiveRamFixture(float deltaTime, float policeY = 11.3f, float targetY = 14f) {
        var fixture = new PursuitFixture(deltaTime);
        try {
            fixture.ConfigureRamRoute(policeY: policeY, targetY: targetY);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            for (int step = 0; step < 100 && !fixture.Controller.IsRamming; step++) fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsRamming, "Ram admission was not established; " + GateFacts(fixture, "admission"));
            Assert.AreEqual(1, fixture.Controller.RamStarts, GateFacts(fixture, "admission"));
            return fixture;
        } catch {
            fixture.Dispose();
            throw;
        }
    }

    static string FaultFacts(PursuitFixture fixture, int fault, GameObject blocker) {
        return "Safety fault=" + fault + ", blocker=" + (blocker != null) + ", phase=" + fixture.Controller.CurrentPhase +
            ", bound=" + fixture.Controller.IsBound + ", active=" + fixture.Controller.IsRamming +
            ", starts=" + fixture.Controller.RamStarts + ", deadline=" + fixture.Controller.RamCooldownUntil +
            ", position=" + fixture.PolicePosition + ", target=" + fixture.PlayerPosition +
            ", gap=" + fixture.ColliderGap + ", brake=" + fixture.Controller.LastCommand.brake;
    }

    static string TimeoutFacts(PursuitFixture fixture, float admissionClock, int initialPlannerAttempts) {
        return "Timeout diagnostics: clock=" + fixture.World.SessionTime + ", admissionClock=" + admissionClock +
            ", phase=" + fixture.Controller.CurrentPhase + ", active=" + fixture.Controller.IsRamming +
            ", starts=" + fixture.Controller.RamStarts + ", attempts=" + fixture.Controller.PlannerAttemptCount +
            ", initialAttempts=" + initialPlannerAttempts + ", position=" + fixture.PolicePosition +
            ", target=" + fixture.PlayerPosition + ", gap=" + fixture.ColliderGap +
            ", brake=" + fixture.Controller.LastCommand.brake;
    }

    static string GateFacts(PursuitFixture fixture, string gate) {
        return "Gate=" + gate + ", phase=" + fixture.Controller.CurrentPhase + ", bound=" + fixture.Controller.IsBound +
            ", active=" + fixture.Controller.IsRamming + ", starts=" + fixture.Controller.RamStarts +
            ", deadline=" + fixture.Controller.RamCooldownUntil + ", attempts=" + fixture.Controller.PlannerAttemptCount +
            ", position=" + fixture.PolicePosition + ", target=" + fixture.PlayerPosition +
            ", brake=" + fixture.Controller.LastCommand.brake;
    }

    static string ReserveFacts(PursuitFixture fixture, string caseName) {
        return "Reserve gate=" + caseName + ", phase=" + fixture.Controller.CurrentPhase +
            ", bound=" + fixture.Controller.IsBound + ", active=" + fixture.Controller.IsRamming +
            ", starts=" + fixture.Controller.RamStarts + ", deadline=" + fixture.Controller.RamCooldownUntil +
            ", policePosition=" + fixture.PolicePosition + ", policeVelocity=" + fixture.PoliceVelocity +
            ", targetPosition=" + fixture.PlayerPosition + ", gap=" + fixture.ColliderGap +
            ", playerHealth=" + fixture.Player.CurrentHealth + ", brake=" + fixture.Controller.LastCommand.brake;
    }
}
