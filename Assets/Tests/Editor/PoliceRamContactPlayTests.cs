using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode bridge for seven physical Ram contact scenarios. The synchronous scenario
/// helpers remain in the foundation test class where applicable; this entry point only
/// supplies Unity's callback-enabled runtime.
/// </summary>
public sealed class PoliceRamContactPlayTests {
    /// <summary>
    /// Runs the four contact variants, final Holding, initial-overlap refusal, and sustained
    /// contact scenarios under Play Mode with callback-enabled simulated physics.
    /// </summary>
    [UnityTest]
    public IEnumerator ContactMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene,
            "A configured Play Mode start scene must not launch saved gameplay during this isolated test.");
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The isolated bootstrap must not initialize career or save services.");

        var foundation = new PoliceRamPursuitTests();
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=0.02 reverseCreationOrder=false");
        foundation.ActualContact_HealthMomentumAndImmediateCancel(0.02f, false);
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=0.02 reverseCreationOrder=true");
        foundation.ActualContact_HealthMomentumAndImmediateCancel(0.02f, true);
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=0.03 reverseCreationOrder=false");
        foundation.ActualContact_HealthMomentumAndImmediateCancel(0.03f, false);
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=0.03 reverseCreationOrder=true");
        foundation.ActualContact_HealthMomentumAndImmediateCancel(0.03f, true);
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=final-holding");
        foundation.FinalHoldingRam_RemovesReserveBlockerAndMakesRealContact();
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=initial-overlap-refusal");
        InitialOverlap_RefusesRamAdmission();
        TestContext.Progress.WriteLine("RAM_CONTACT scenario=sustained-contact-no-rearm");
        SustainedContact_DoesNotRearmAfterRealImpact();
    }

    /// <summary>Always restores Edit Mode after a failed or interrupted Play-mode bridge.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    /// <summary>Initial overlap is a real contact state, not a valid separated Ram admission.</summary>
    internal void InitialOverlap_RefusesRamAdmission() {
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute(policeY: 13f, targetY: 14f);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(1);

            Assert.IsTrue(fixture.PoliceCollider.IsTouching(fixture.Player.GetComponent<Collider2D>()));
            Assert.AreEqual(0, fixture.Controller.RamStarts);
            Assert.IsFalse(fixture.Controller.IsRamming);
        }
    }

    /// <summary>A genuine Ram impact remains physically touching through cooldown and never rearms.</summary>
    internal void SustainedContact_DoesNotRearmAfterRealImpact() {
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 1000f;
            targetBody.linearDamping = 10f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 120 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved);

            var policeBody = fixture.PoliceReceiver.GetComponent<Rigidbody2D>();
            var policeCollider = fixture.PoliceReceiver.GetComponent<Collider2D>();
            var targetCollider = fixture.Player.GetComponent<Collider2D>();
            int startsAtImpact = fixture.Controller.RamStarts;
            float cooldownAtImpact = fixture.Controller.RamCooldownUntil;
            Assert.AreEqual(1, startsAtImpact);
            Assert.Greater(cooldownAtImpact, 0f);
            Assert.Greater(fixture.Player.CurrentHealth, 0f);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f);

            if (!policeCollider.IsTouching(targetCollider)) {
                var joint = policeBody.gameObject.AddComponent<FixedJoint2D>();
                joint.connectedBody = targetBody;
                joint.enableCollision = true;
                joint.autoConfigureConnectedAnchor = true;
                fixture.Step(2);
            }

            Assert.IsTrue(policeCollider.IsTouching(targetCollider));
            double remainingCooldown = (double)cooldownAtImpact - fixture.World.SessionTime;
            double recoverySeconds = fixture.World.Settings.recoverySeconds;
            Assert.IsFalse(float.IsNaN(cooldownAtImpact) || float.IsInfinity(cooldownAtImpact));
            Assert.IsFalse(double.IsNaN(remainingCooldown) || double.IsInfinity(remainingCooldown));
            Assert.IsFalse(double.IsNaN(recoverySeconds) || double.IsInfinity(recoverySeconds));
            double sustainedSeconds = System.Math.Max(remainingCooldown, recoverySeconds) + 0.5d;
            Assert.IsFalse(double.IsNaN(sustainedSeconds) || double.IsInfinity(sustainedSeconds));
            Assert.GreaterOrEqual(sustainedSeconds, 0.5d);
            Assert.Less(sustainedSeconds, 60d);
            Assert.IsFalse(float.IsNaN(fixture.StepDelta) || float.IsInfinity(fixture.StepDelta) || fixture.StepDelta <= 0f);
            int sustainedSteps = Mathf.CeilToInt((float)(sustainedSeconds / fixture.StepDelta)) + 1;
            for (int step = 0; step < sustainedSteps; step++) {
                fixture.Step(1);
                Assert.IsTrue(policeCollider.IsTouching(targetCollider));
                Assert.IsFalse(fixture.Controller.IsRamming);
                Assert.AreEqual(startsAtImpact, fixture.Controller.RamStarts);
                Assert.AreEqual(cooldownAtImpact, fixture.Controller.RamCooldownUntil);
            }
            Assert.GreaterOrEqual(fixture.World.SessionTime, cooldownAtImpact);
            Assert.IsFalse(policeBody.GetComponent<NpcVehicleMotor>().IsCrashMode);
            Assert.LessOrEqual(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold);
            Assert.IsTrue(policeCollider.IsTouching(targetCollider));
            Assert.AreEqual(startsAtImpact, fixture.Controller.RamStarts);
            Assert.AreEqual(cooldownAtImpact, fixture.Controller.RamCooldownUntil);
            Assert.Greater(fixture.Player.CurrentHealth, 0f);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f);
        }
    }
}
