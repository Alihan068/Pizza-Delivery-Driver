using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure EditMode gate for the R03 free-drive command and recovery policies.</summary>
public sealed class PoliceFreeDrivingTests {
    /// <summary>Reaching the measured endpoint must not leave forward throttle latched.</summary>
    [Test]
    public void EndpointAppliesFiniteBrake() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var command = driver.Tick(new PoliceFreePursuitDriver.Pose(Vector2.up * 20f, Vector2.up, Vector2.up * 4f), 0.02f);
        Assert.AreEqual(0f, command.throttle);
        Assert.Greater(command.brake, 0f);
        Assert.AreEqual(0f, command.targetSpeed);
    }

    /// <summary>Continuing contact and a blocked rear cannot hold a recovery clock forever.</summary>
    [Test]
    public void RecoveryRepeatedImpactsAndBlockedRearEventuallyReplan() {
        var recovery = new PoliceFreeRecovery(Driving());
        recovery.ReportImpact();
        for (int index = 0; index < 25; index++) {
            if (recovery.CurrentState == PoliceFreeRecovery.State.CrashWait) recovery.ReportImpact();
            recovery.Step(0.1f, 10f, 0f, false, true, false);
        }
        Assert.AreNotEqual(PoliceFreeRecovery.State.CrashWait, recovery.CurrentState);
        for (int index = 0; index < 25; index++) recovery.Step(0.1f, 10f, 0f, false, true, false);
        Assert.AreEqual(PoliceFreeRecovery.State.Replanning, recovery.CurrentState);
    }

    /// <summary>The real obstacle sensor uses positive infinity for a clear sweep.</summary>
    [Test]
    public void UnobstructedSensorClearanceDoesNotBrake() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var command = driver.Tick(Pose(4f), 0.02f, new ClearSensorQuery());
        Assert.IsFalse(driver.NeedsReplan);
        Assert.Greater(command.throttle, 0f);
        Assert.AreEqual(0f, command.brake);
    }

    /// <summary>Lookahead follows measured path progress instead of pointing back at the start.</summary>
    [Test]
    public void LookaheadAdvancesToTheNextPrimitive() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight(), new PoliceFreePathPlanner.Primitive(
            PoliceFreePathPlanner.PrimitiveKind.Straight, Vector2.up * 20f, -90f,
            new Vector2(20f, 20f), -90f, 0f, 0f, 20f) });
        var command = driver.Tick(new PoliceFreePursuitDriver.Pose(Vector2.up * 20f, Vector2.up, Vector2.up * 4f), 0.02f);
        Assert.Less(command.steering, 0f);
    }

    sealed class ClearSensorQuery : PoliceFreePursuitDriver.IAvoidanceQuery {
        public PoliceFreePursuitDriver.AvoidanceResult Check(Vector2 start, Vector2 end, float headingDegrees, float horizonSeconds, float lateralOffset) =>
            new PoliceFreePursuitDriver.AvoidanceResult(true, float.PositiveInfinity);
    }

    static NpcMotorSettings Motor() {
        return new NpcMotorSettings { cruiseSpeed = 8f, maxSpeed = 10f, reverseSpeed = 3f, acceleration = 4f,
            brakeDeceleration = 8f, maxEngineForce = 12f, maxBrakeForce = 16f, turnRate = 120f, minimumTurningRadius = 3f };
    }

    static PoliceDrivingSettings Driving() {
        var value = new PoliceDrivingSettings();
        Assert.IsTrue(value.IsValid(out string reason), reason);
        return value;
    }

    static PoliceFreePathPlanner.Primitive Straight(float length = 20f) {
        return new PoliceFreePathPlanner.Primitive(PoliceFreePathPlanner.PrimitiveKind.Straight,
            Vector2.zero, 0f, Vector2.up * length, 0f, 0f, 0f, length);
    }

    static PoliceFreePursuitDriver.Pose Pose(float speed = 0f) {
        return new PoliceFreePursuitDriver.Pose(Vector2.zero, Vector2.up, Vector2.up * speed);
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void CurvedPath_UsesTransformUpAndClampedCounterClockwiseSteering(float turnSign) {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { new PoliceFreePathPlanner.Primitive(PoliceFreePathPlanner.PrimitiveKind.Arc,
            Vector2.zero, 0f, new Vector2(-0.8786797f * turnSign, 2.1213203f), 45f * turnSign, 3f, 45f * turnSign, 2.3561945f) });
        var command = driver.Tick(Pose(4f), 0.02f);
        Assert.Greater(command.steering * turnSign, 0f);
        Assert.LessOrEqual(Mathf.Abs(command.steering), 1f);
    }

    [Test]
    public void ZeroSpeed_CommandsZeroSteering_AndClearPathThrottles() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var command = driver.Tick(Pose(), 0.02f);
        Assert.Greater(command.throttle, 0f);
        Assert.AreEqual(0f, command.steering);
        Assert.AreEqual(0f, command.brake);
    }

    [Test]
    public void ExcessSpeed_ProducesFiniteBrakeWithoutVelocityMutation() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var before = Pose(20f);
        var command = driver.Tick(before, 0.02f);
        Assert.AreEqual(0f, command.throttle);
        Assert.Greater(command.brake, 0f);
        Assert.IsFalse(float.IsNaN(command.brake) || float.IsInfinity(command.brake));
        Assert.AreEqual(Vector2.up * 20f, before.velocity);
    }

    [Test]
    public void Avoidance_UsesStableTieBreakAndCommitment() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var query = new TieQuery();
        var first = driver.Tick(Pose(4f), 0.02f, query);
        var firstIndex = driver.SelectedAvoidanceIndex;
        var second = driver.Tick(Pose(4f), 0.02f, query);
        Assert.AreEqual(2, firstIndex);
        Assert.AreEqual(firstIndex, driver.SelectedAvoidanceIndex);
        Assert.AreEqual(first.steering, second.steering);
    }

    [Test]
    public void Avoidance_NoClearCandidate_BrakesAndRequestsReplan() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        var command = driver.Tick(Pose(4f), 0.02f, new BlockedQuery());
        Assert.IsTrue(driver.NeedsReplan);
        Assert.AreEqual(0f, command.throttle);
        Assert.Greater(command.brake, 0f);
    }

    [Test]
    public void Recovery_PendingAloneDoesNotReverse_ButCrashWaitAndRearClearDo() {
        var recovery = new PoliceFreeRecovery(Driving());
        Assert.AreEqual(NpcDriveCommand.Stopped.targetSpeed, recovery.Step(0.1f, 0f, 0f, true, false, true).targetSpeed);
        recovery.ReportImpact();
        Assert.AreEqual(PoliceFreeRecovery.State.CrashWait, recovery.CurrentState);
        recovery.Step(Driving().recovery.lightHoldSeconds + 0.01f, 0f, 0f, true, false, false);
        Assert.AreEqual(PoliceFreeRecovery.State.Braking, recovery.CurrentState);
        var blocked = recovery.Step(0.1f, 0f, 0f, false, true, false);
        Assert.AreEqual(0f, blocked.throttle);
        var reverse = recovery.Step(0.1f, 0f, 0f, true, true, false);
        Assert.IsTrue(reverse.reverseAllowed);
        Assert.Less(reverse.throttle, 0f);
    }

    /// <summary>Obstacle escape may begin immediately without weakening the player-contact rule.</summary>
    [Test]
    public void Recovery_ImmediateObstacleEscapeStartsBoundedReverse() {
        var recovery = new PoliceFreeRecovery(Driving());
        recovery.BeginImmediateEscape();
        Assert.AreEqual(PoliceFreeRecovery.State.Braking, recovery.CurrentState);
        var reverse = recovery.Step(0.1f, 0f, 0f, true, true, false);
        Assert.IsTrue(reverse.reverseAllowed);
        Assert.Less(reverse.throttle, 0f);
    }

    [Test]
    public void Recovery_ReverseDistanceAndAttemptsAreBounded() {
        var recovery = new PoliceFreeRecovery(Driving());
        recovery.ReportImpact();
        recovery.Step(Driving().recovery.lightHoldSeconds + 0.01f, 0f, 0f, true, false, false);
        recovery.Step(0.1f, 0f, 0f, true, true, false);
        recovery.Step(0.1f, 0f, Driving().reverseMaxDistance, true, true, false);
        Assert.IsTrue(recovery.NeedsReplan);
        Assert.AreEqual(PoliceFreeRecovery.State.Replanning, recovery.CurrentState);
    }

    [Test]
    public void Reset_ClearsPathCommitmentRecoveryAndCommandIntent() {
        var driver = new PoliceFreePursuitDriver(Motor(), Driving());
        driver.SetPath(new[] { Straight() });
        driver.Tick(Pose(4f), 0.02f, new TieQuery());
        driver.Reset();
        var command = driver.Tick(Pose(4f), 0.02f);
        Assert.AreEqual(Driving().stoppedSpeedThreshold, command.targetSpeed);
        Assert.AreEqual(-1, driver.SelectedAvoidanceIndex);
        var recovery = new PoliceFreeRecovery(Driving());
        recovery.ReportImpact();
        recovery.Reset();
        Assert.AreEqual(PoliceFreeRecovery.State.Pursuing, recovery.CurrentState);
        Assert.IsFalse(recovery.NeedsReplan);
    }

    sealed class TieQuery : PoliceFreePursuitDriver.IAvoidanceQuery {
        public PoliceFreePursuitDriver.AvoidanceResult Check(Vector2 start, Vector2 end, float headingDegrees, float horizonSeconds, float lateralOffset) {
            return new PoliceFreePursuitDriver.AvoidanceResult(true, 10f);
        }
    }

    sealed class BlockedQuery : PoliceFreePursuitDriver.IAvoidanceQuery {
        public PoliceFreePursuitDriver.AvoidanceResult Check(Vector2 start, Vector2 end, float headingDegrees, float horizonSeconds, float lateralOffset) {
            return new PoliceFreePursuitDriver.AvoidanceResult(false, 0f);
        }
    }
}
