using NUnit.Framework;
using UnityEngine;

/// <summary>Regression coverage for stationary heading correction on the real pursuit fixture.</summary>
public sealed class PoliceStationarySteeringTests {
    /// <summary>A small lateral road offset reacquires without teleporting and resumes measured pursuit.</summary>
    [TestCase(0.02f, 0.08f)]
    [TestCase(0.03f, -0.08f)]
    public void LateralRoadAcquisition_ResumesMeasuredPursuit(float deltaTime, float offset) {
        using (var fixture = new PursuitFixture(deltaTime)) {
            fixture.ConfigureStraightInitialRoute();
            fixture.SetPolicePosition(new Vector2(offset, 2f));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Vector2 start = fixture.PolicePosition;
            fixture.Controller.Tick(deltaTime, false);
            Assert.AreEqual(start, fixture.PolicePosition, "Route acquisition must not write the physical pose.");
            Assert.IsTrue(fixture.Cursor.IsBound);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Greater(fixture.Controller.LastCommand.throttle, 0f);
            fixture.Step(150);
            Assert.Greater(fixture.PolicePosition.y, start.y + 2f);
            Assert.Less(Mathf.Abs(fixture.PolicePosition.x), Mathf.Abs(offset));
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>Road acquisition cannot bypass authored deviation, lane width, or static collision clearance.</summary>
    [TestCase(2.1f, false)]
    [TestCase(1.5f, false)]
    [TestCase(0.08f, true)]
    public void LateralRoadAcquisition_RejectsOutsideCorridorOrBlockedPose(float offset, bool blocked) {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureStraightInitialRoute();
            fixture.SetPolicePosition(new Vector2(offset, 2f));
            if (blocked) fixture.CreateBlocker(new Vector2(0.65f, 2f), false, Vector2.one * 0.1f);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Vector2 start = fixture.PolicePosition;
            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.AreEqual(start, fixture.PolicePosition);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>
    /// Confirms a stationary police body keeps steering out of the first command while retaining
    /// geometric intent for the next moving tick, without allowing the controller to write pose.
    /// Zero-degree alignment remains straight; a six-degree offset receives corrective steering
    /// only after the motor has produced forward motion.
    /// </summary>
    [TestCase(6f, true)]
    [TestCase(0f, false)]
    public void StationaryHeadingCorrection_DefersCommandSteeringUntilMoving(float initialHeading, bool expectCorrection) {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureStraightInitialRoute();
            fixture.SetPoliceRotation(0f);
            Assert.AreEqual(Vector2.zero, fixture.PoliceVelocity);

            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            fixture.Controller.Tick(fixture.StepDelta, false);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.IsTrue(fixture.Cursor.IsBound);

            // Existing-road pose setup occurs before physics; the controller must not write driving pose.
            fixture.SetPoliceRotation(initialHeading);
            Assert.AreEqual(Vector2.zero, fixture.PoliceVelocity);

            Vector2 initialPosition = fixture.PolicePosition;
            float initialRotation = fixture.Binding.body.Body.rotation;
            float initialHealth = fixture.PoliceReceiver.CurrentHealth;

            fixture.Controller.Tick(fixture.StepDelta, false);

            Assert.AreEqual(0f, fixture.Controller.LastCommand.steering);
            Assert.Greater(fixture.Controller.LastCommand.throttle, 0f);
            Assert.AreEqual(initialPosition, fixture.PolicePosition);
            Assert.AreEqual(initialRotation, fixture.Binding.body.Body.rotation);

            bool sawMovingCorrection = false;
            float maximumSteering = 0f;
            for (int step = 0; step < 120; step++) {
                fixture.Step(1);
                float steering = Mathf.Abs(fixture.Controller.LastCommand.steering);
                maximumSteering = Mathf.Max(maximumSteering, steering);
                sawMovingCorrection |= fixture.ForwardSpeed > fixture.ControllerSettings.stoppedSpeedThreshold && steering > 0f;
            }

            Assert.Greater(fixture.Controller.CursorProgress, 1f);
            Assert.AreEqual(initialHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
            if (expectCorrection) Assert.IsTrue(sawMovingCorrection);
            else Assert.AreEqual(0f, maximumSteering);
        }
    }
}
