using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Foundation-level physical coverage for the bounded straight Ram contract.
/// Every case uses the shared registered-life fixture and observes the complete
/// controller, motor, damage-capture, isolated-physics, and damage-tick sequence.
/// </summary>
public sealed class PoliceRamPursuitTests {
    /// <summary>
    /// Proves that an admitted Ram reaches a real contact, damages both vehicles,
    /// transfers physical momentum, and cancels immediately on the observed impact.
    /// </summary>
    internal void ActualContact_HealthMomentumAndImmediateCancel(float deltaTime, bool reverseCreationOrder) {
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureRamRoute();
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            float initialPlayerHealth = fixture.Player.CurrentHealth;
            float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
            Vector2 initialPlayerPosition = fixture.PlayerPosition;
            bool wasActiveBeforeContact = false;
            bool impactObserved = false;
            bool callbackCancelledSynchronously = false;
            float observedImpact = 0f;
            Vector2 playerVelocityAtContact = Vector2.zero;
            Vector2 policeVelocityAtContact = Vector2.zero;
            Vector2 policeVelocityAfterContact = Vector2.zero;
            Vector2 playerDisplacementAfterContact = Vector2.zero;
            fixture.PoliceReceiver.ImpactObserved += impact => {
                impactObserved = true;
                observedImpact = impact;
                callbackCancelledSynchronously = !fixture.Controller.IsRamming && fixture.Controller.LastCommand.brake == 1f;
                playerVelocityAtContact = fixture.PlayerVelocity;
                policeVelocityAtContact = fixture.PoliceVelocity;
            };

            for (int step = 0; step < 100 && !impactObserved; step++) {
                wasActiveBeforeContact |= fixture.Controller.IsRamming;
                fixture.Step(1);
                if (impactObserved) {
                    policeVelocityAfterContact = fixture.PoliceVelocity;
                    playerDisplacementAfterContact = fixture.PlayerPosition - initialPlayerPosition;
                }
            }

            string facts = ContactFacts(fixture, wasActiveBeforeContact, observedImpact);
            Assert.IsTrue(wasActiveBeforeContact, facts);
            Assert.IsTrue(impactObserved, facts);
            Assert.IsTrue(callbackCancelledSynchronously, facts);
            Assert.Greater(observedImpact, 0f, facts);
            Assert.Less(fixture.Player.CurrentHealth, initialPlayerHealth, facts);
            Assert.Less(fixture.PoliceReceiver.CurrentHealth, initialPoliceHealth, facts);
            Assert.Greater(playerVelocityAtContact.magnitude, 0.001f, facts);
            Assert.Greater(policeVelocityAtContact.magnitude, 0.001f, facts);
            Assert.Greater(policeVelocityAfterContact.magnitude, 0.01f, facts);
            fixture.Step(3);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            playerDisplacementAfterContact = fixture.PlayerPosition - initialPlayerPosition;
            Assert.Greater(playerDisplacementAfterContact.magnitude, 0.001f, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
        }
    }

    /// <summary>Confirms that ordinary target blocking remains unchanged for Pursue and stops the police short.</summary>
    [TestCase(0.02f)]
    [TestCase(0.03f)]
    public void Pursue_NoExclusion(float deltaTime) {
        using (var fixture = new PursuitFixture(deltaTime)) {
            fixture.ConfigureRamRoute(policeY: 10f, ramIntent: false);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(100);

            string facts = PursuitFacts(fixture);
            Assert.AreEqual(0, fixture.Controller.RamStarts, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.GreaterOrEqual(fixture.ColliderGap, fixture.ControllerSettings.stopGap - 0.15f, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth, facts);
        }
    }

    /// <summary>Each real blocker behind the target must deny the complete Ram reserve.</summary>
    [TestCase(VehicleRole.Civilian)]
    [TestCase(VehicleRole.Police)]
    public void BeyondContactBlockers_RealTrafficReserveDeniesRam(VehicleRole blockerRole) {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute();
            fixture.CreateTrafficBlocker(new Vector2(0f, 24f), blockerRole);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            AssertTargetIsTheOrdinaryShortSweepHit(fixture);
            fixture.Step(100);

            string facts = PursuitFacts(fixture);
            Assert.AreEqual(0, fixture.Controller.RamStarts, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth, facts);
        }
    }

    /// <summary>A static wall behind the target denies Ram even when ordinary pursuit can still hold safely.</summary>
    [Test]
    public void BeyondContactBlockers_StaticWallReserveDeniesRam() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute();
            fixture.CreateBlocker(new Vector2(0f, 24f), false, Vector2.one);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            AssertTargetIsTheOrdinaryShortSweepHit(fixture);
            fixture.Step(100);

            string facts = PursuitFacts(fixture);
            Assert.AreEqual(0, fixture.Controller.RamStarts, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
        }
    }

    /// <summary>A blocker inside only the padded occupancy corridor denies Ram despite missing the raw forward cast.</summary>
    [Test]
    public void BeyondContactBlockers_PaddedCorridorDynamicDeniesRam() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute();
            GameObject paddedBlocker = fixture.CreateBlocker(new Vector2(1.2f, 20f), true, new Vector2(0.2f, 0.2f));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Rigidbody2D targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            VehicleObstacleSensor.SweepStatus rawStatus = fixture.Sensor.QuerySweep(
                fixture.PoliceForward, fixture.ControllerSettings.maxSweepDistance, targetBody,
                out float rawGap, out Collider2D rawHit);
            float actualBlockerDistance = fixture.PoliceCollider.Distance(paddedBlocker.GetComponent<Collider2D>()).distance;
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Clear, rawStatus,
                "Padded blocker raw sweep was not clear; gap=" + rawGap + ", hit=" + rawHit);
            Assert.IsNull(rawHit, "Padded blocker entered the raw cast unexpectedly; gap=" + rawGap);
            Assert.Greater(actualBlockerDistance, 0f, "Padded blocker collider distance was not measured.");
            fixture.Step(100);

            string facts = PursuitFacts(fixture);
            Assert.AreEqual(0, fixture.Controller.RamStarts, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
        }
    }

    /// <summary>Separately verifies that a short road, insufficient bounds, or narrow width denies the reserve.</summary>
    [TestCase(18f, 80f, 4f)]
    [TestCase(60f, 22f, 4f)]
    [TestCase(60f, 80f, 2f)]
    public void StructuralReserveLimits_DenyRam(float roadLength, float boundsMaxY, float roadWidth) {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute(roadLength: roadLength, boundsMaxY: boundsMaxY, roadWidth: roadWidth);
            Assert.IsTrue(fixture.TryQueryCurrent(out PoliceRoadTargetQuery.Result initialQuery),
                "Initial target query failed before binding; " + PursuitFacts(fixture));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, initialQuery.status,
                "Initial target query was not a Route before binding; status=" + initialQuery.status + "; " + PursuitFacts(fixture));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(100);

            string facts = PursuitFacts(fixture);
            Assert.AreEqual(0, fixture.Controller.RamStarts, facts);
            Assert.IsFalse(fixture.Controller.IsRamming, facts);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth, facts);
        }
    }

    /// <summary>
    /// Reaches genuine Holding with a reserve blocker, removes only that temporary fixture,
    /// and proves the unchanged Ram intent can then obtain a full reserve and make contact.
    /// </summary>
    internal void FinalHoldingRam_RemovesReserveBlockerAndMakesRealContact() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute(policeY: 0f, targetY: 14f, roadLength: 10f, boundsMaxY: 80f);
            GameObject reserveWall = fixture.CreateBlocker(new Vector2(0f, 25f), false, Vector2.one);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            for (int step = 0; step < 500 && fixture.Controller.CurrentPhase != PolicePursuitController.TraversalPhase.Holding; step++)
                fixture.Step(1);
            string holdingFacts = PursuitFacts(fixture);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Holding, fixture.Controller.CurrentPhase, holdingFacts);
            Assert.AreEqual(0, fixture.Controller.RamStarts, holdingFacts);

            Object.DestroyImmediate(reserveWall);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 100 && !impactObserved; step++) fixture.Step(1);

            string contactFacts = PursuitFacts(fixture);
            Assert.GreaterOrEqual(fixture.Controller.RamStarts, 1, contactFacts);
            Assert.IsTrue(impactObserved, contactFacts);
            Assert.Less(fixture.Player.CurrentHealth, 100f, contactFacts);
            Assert.IsFalse(fixture.Controller.IsRamming, contactFacts);
        }
    }

    /// <summary>Target displacement after admission cancels the active Ram without preserving its old command.</summary>
    [TestCase(0.02f)]
    [TestCase(0.03f)]
    public void ActiveRam_TargetPushCancelsWithoutOldLine(float deltaTime) {
        using (var fixture = new PursuitFixture(deltaTime)) {
            fixture.ConfigureRamRoute();
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            for (int step = 0; step < 100 && !fixture.Controller.IsRamming; step++) fixture.Step(1);
            string activeFacts = PursuitFacts(fixture);
            Assert.IsTrue(fixture.Controller.IsRamming, activeFacts);
            int starts = fixture.Controller.RamStarts;
            float initialPlayerHealth = fixture.Player.CurrentHealth;
            Vector2 beforePush = fixture.PlayerPosition;
            fixture.PushPlayer(Vector2.up * 0.5f);
            bool targetMoved = false;
            for (int step = 0; step < 30 && !targetMoved; step++) {
                fixture.Step(1);
                targetMoved = Vector2.Distance(fixture.PlayerPosition, beforePush) > fixture.ControllerSettings.acquisitionTolerance;
            }
            Assert.IsTrue(targetMoved, "Target did not move beyond acquisition tolerance; " + PursuitFacts(fixture));
            fixture.Step(1);

            string cancelFacts = PursuitFacts(fixture);
            Assert.IsFalse(fixture.Controller.IsRamming, cancelFacts);
            Assert.AreEqual(starts, fixture.Controller.RamStarts, cancelFacts);
            Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth, cancelFacts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, cancelFacts);
            fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsRamming, PursuitFacts(fixture));
            Assert.AreEqual(starts, fixture.Controller.RamStarts, PursuitFacts(fixture));
        }
    }

    static string ContactFacts(PursuitFixture fixture, bool activeBeforeContact, float impact) {
        return "Ram contact diagnostics: activeBeforeContact=" + activeBeforeContact +
            ", impact=" + impact + ", phase=" + fixture.Controller.CurrentPhase +
            ", starts=" + fixture.Controller.RamStarts + ", cooldownUntil=" + fixture.Controller.RamCooldownUntil +
            ", bound=" + fixture.Controller.IsBound + ", policePosition=" + fixture.PolicePosition +
            ", playerPosition=" + fixture.PlayerPosition + ", gap=" + fixture.ColliderGap +
            ", policeVelocity=" + fixture.PoliceVelocity + ", playerVelocity=" + fixture.PlayerVelocity +
            ", commandBrake=" + fixture.Controller.LastCommand.brake;
    }

    static void AssertTargetIsTheOrdinaryShortSweepHit(PursuitFixture fixture) {
        Collider2D targetCollider = fixture.Player.GetComponent<Collider2D>();
        VehicleObstacleSensor.SweepStatus status = fixture.Sensor.QuerySweep(
            fixture.PoliceForward, 4f, out float gap, out Collider2D obstacle);
        string facts = PursuitFacts(fixture) + ", ordinaryShortSweepStatus=" + status + ", gap=" + gap + ", obstacle=" + obstacle;
        Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked, status, facts);
        Assert.AreEqual(targetCollider, obstacle, facts);
    }

    static string PursuitFacts(PursuitFixture fixture) {
        return "Ram safety diagnostics: phase=" + fixture.Controller.CurrentPhase +
            ", starts=" + fixture.Controller.RamStarts + ", active=" + fixture.Controller.IsRamming +
            ", bound=" + fixture.Controller.IsBound + ", gap=" + fixture.ColliderGap +
            ", policePosition=" + fixture.PolicePosition + ", playerPosition=" + fixture.PlayerPosition +
            ", policeVelocity=" + fixture.PoliceVelocity + ", playerVelocity=" + fixture.PlayerVelocity +
            ", commandBrake=" + fixture.Controller.LastCommand.brake +
            ", plannerAttempts=" + fixture.Controller.PlannerAttemptCount;
    }
}
