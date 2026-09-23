using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Callback-enabled Play-mode disturbances for bounded police recovery safety gates.</summary>
public sealed class PoliceRecoveryDisturbancePlayTests {
    /// <summary>Runs real yaw, padded-corridor, and powered-reverse second-contact scenarios.</summary>
    [UnityTest]
    public IEnumerator RecoveryDisturbanceMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene,
            "A configured Play Mode start scene must not launch saved gameplay during this isolated test.");
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The isolated bootstrap must not initialize career or save services.");

        TestContext.Progress.WriteLine("RECOVERY_DISTURBANCE case=yaw-invalidates-reverse");
        RunWithFixture(0.02f, YawDisturbance_StopsWithoutNewReverseCredit);
        TestContext.Progress.WriteLine("RECOVERY_DISTURBANCE case=padded-corridor-denial");
        RunWithFixture(0.02f, PaddedCorridorBlocker_DeniesReverseAfterRawClear);
        TestContext.Progress.WriteLine("RECOVERY_DISTURBANCE case=second-powered-reverse-callback");
        RunWithFixture(0.03f, SecondImpactDuringPoweredReverse_ClosesGatesSynchronously, 0.05f);
    }

    /// <summary>Always restores Edit Mode after a failed or interrupted Play-mode bridge.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void YawDisturbance_StopsWithoutNewReverseCredit(PursuitFixture fixture) {
        ConfigureHeavyFixture(fixture, 2f, 1f);
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
        bool impactObserved = false;
        fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
        StepUntil(fixture, () => impactObserved, 3f, "first physical impact");

        float impactClock = fixture.World.SessionTime;
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        bool poweredReverse = false;
        for (int step = 0; step < RecoveryStepBudget(fixture) && !poweredReverse; step++) {
            fixture.Step(1);
            poweredReverse = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed &&
                Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < -0.01f;
        }
        Assert.IsTrue(poweredReverse, Facts(fixture, "no powered reverse with both gates and negative velocity"));

        Vector2 gate = fixture.Recovery.Gate;
        var policeBody = fixture.PoliceReceiver.GetComponent<Rigidbody2D>();
        policeBody.AddTorque(0.01f, ForceMode2D.Impulse);
        fixture.Step(1);
        Assert.Greater(Mathf.Abs(policeBody.angularVelocity), 0f, Facts(fixture, "torque did not produce real angular velocity"));
        fixture.Step(1);

        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture));
        Assert.AreEqual(gate, fixture.Recovery.Gate, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));

        RunUntilExhausted(fixture, impactClock, starts, deadline);
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
    }

    static void PaddedCorridorBlocker_DeniesReverseAfterRawClear(PursuitFixture fixture) {
        fixture.ConfigureRamRoute(targetY: 14.35f);
        fixture.SetPoliceColliderOffset(new Vector2(0f, 0.35f));
        fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
        var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
        targetBody.mass = 2f;
        targetBody.linearDamping = 1f;

        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

        bool impactObserved = false;
        fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
        StepUntil(fixture, () => impactObserved, 3f, "padded-corridor physical impact");
        float impactClock = fixture.World.SessionTime;
        float playerHealth = fixture.Player.CurrentHealth;
        float policeHealth = fixture.PoliceReceiver.CurrentHealth;
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        bool poweredReverse = false;
        for (int step = 0; step < RecoveryStepBudget(fixture) && !poweredReverse; step++) {
            fixture.Step(1);
            poweredReverse = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed &&
                Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < -0.01f;
        }
        Assert.IsTrue(poweredReverse, Facts(fixture, "padded-corridor setup never reached powered reverse"));
        Vector2 right = new Vector2(fixture.PoliceForward.y, -fixture.PoliceForward.x);
        fixture.CreateBlocker(fixture.PolicePosition + right * 0.775f - fixture.PoliceForward * 3f,
            true, new Vector2(0.05f, 0.1f));
        var rawRearStatus = fixture.Sensor.QuerySweep(-fixture.PoliceForward, fixture.ControllerSettings.maxSweepDistance,
            out _, out _);
        Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Clear, rawRearStatus,
            Facts(fixture, "raw rear cast must miss the laterally padded blocker"));

        fixture.Step(1);
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.AreEqual(playerHealth, fixture.Player.CurrentHealth, Facts(fixture));
        Assert.AreEqual(policeHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture));
        RunUntilExhausted(fixture, impactClock, starts, deadline);
        Assert.AreEqual(playerHealth, fixture.Player.CurrentHealth, Facts(fixture));
        Assert.AreEqual(policeHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture));
    }

    static void SecondImpactDuringPoweredReverse_ClosesGatesSynchronously(PursuitFixture fixture) {
        fixture.ConfigureRamRoute();
        fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
        var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
        targetBody.mass = 2f;
        targetBody.linearDamping = 1f;
        int preCallbacks = 0;
        int postCallbacks = 0;
        bool firstImpactObserved = false;
        bool secondPreSawBothGates = false;
        bool secondPostSawStopped = false;
        fixture.PoliceReceiver.ImpactObserved += _ => {
            preCallbacks++;
            if (preCallbacks == 1) firstImpactObserved = true;
            if (preCallbacks == 2)
                secondPreSawBothGates = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed &&
                    fixture.Controller.LastCommand.throttle < 0f;
        };
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
        fixture.PoliceReceiver.ImpactObserved += _ => {
            postCallbacks++;
            if (postCallbacks == 2)
                secondPostSawStopped = fixture.Controller.LastCommand.brake == 1f &&
                    fixture.Controller.LastCommand.throttle == 0f && !fixture.Controller.LastCommand.reverseAllowed && !fixture.MotorReversePermitted;
        };
        StepUntil(fixture, () => firstImpactObserved, 3f, "first physical impact");
        Assert.AreEqual(1, preCallbacks, Facts(fixture, "unexpected first-impact callback count"));
        Assert.AreEqual(1, postCallbacks, Facts(fixture, "post observer missed first callback"));

        float impactClock = fixture.World.SessionTime;
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        bool poweredReverse = false;
        for (int step = 0; step < RecoveryStepBudget(fixture) && !poweredReverse; step++) {
            fixture.Step(1);
            poweredReverse = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed &&
                Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < -0.01f;
        }
        Assert.IsTrue(poweredReverse, Facts(fixture, "second callback setup never reached powered reverse"));

        Vector2 fixedGate = fixture.Recovery.Gate;
        Vector2 forward = fixture.PoliceForward;
        Vector2 right = new Vector2(forward.y, -forward.x);
        const float strikerDistance = 1.85f;
        const float strikerSpeed = 16f;
        var striker = fixture.CreateTrafficBlocker(fixture.PolicePosition + right * strikerDistance, VehicleRole.Civilian);
        var strikerBody = striker.GetComponent<Rigidbody2D>();
        strikerBody.position = fixture.PolicePosition + right * strikerDistance;
        strikerBody.rotation = 90f;
        strikerBody.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        strikerBody.linearDamping = 0f;
        var policeCollider = fixture.PoliceReceiver.GetComponent<Collider2D>();
        var strikerCollider = striker.GetComponent<Collider2D>();
        var initialDistance = policeCollider.Distance(strikerCollider);
        float initialGap = initialDistance.distance;
        Assert.IsTrue(initialDistance.isValid, Facts(fixture, "striker distance query is invalid"));
        Assert.IsFalse(initialDistance.isOverlapped, Facts(fixture, "striker starts overlapped"));
        Assert.IsFalse(float.IsNaN(initialGap) || float.IsInfinity(initialGap), Facts(fixture, "striker distance is nonfinite"));
        Assert.AreEqual(1f, strikerCollider.bounds.extents.x, 0.0001f, Facts(fixture, "rotated striker half-extent changed"));
        float nearFace = Vector2.Dot(strikerBody.position - fixture.PolicePosition, right) - strikerCollider.bounds.extents.x;
        float paddedHalfWidth = (fixture.Profile.colliderSize.x + fixture.Binding.navigationSettings.clearanceMargin) * 0.5f +
            fixture.ControllerSettings.acquisitionTolerance;
        Assert.AreEqual(strikerDistance - 1f, nearFace, 0.0001f, Facts(fixture, "striker near-face coordinate changed"));
        Assert.Greater(nearFace, paddedHalfWidth, Facts(fixture, "striker near face entered the padded corridor"));
        // Collider.Distance measures the actual collision shapes, not a difference of AABBs.
        // Prove separation and one-step reach independently of the engine's shape skin.
        float strikerBrake = Mathf.Min(fixture.Profile.motorSettings.brakeDeceleration,
            fixture.Profile.motorSettings.maxBrakeForce / strikerBody.mass);
        float minimumStepTravel = (strikerSpeed - strikerBrake * fixture.StepDelta) * fixture.StepDelta;
        Assert.Greater(initialGap, 0.32f, Facts(fixture, "striker lacks the required initial physical separation"));
        Assert.Less(initialGap + 0.05f, minimumStepTravel, Facts(fixture, "one physics step lacks contact margin"));
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Vector2 rearStart = (Vector2)typeof(PoliceRecoveryIntegration).GetField("reverseStart", fields).GetValue(fixture.Recovery);
        Vector2 rearEnd = (Vector2)typeof(PoliceRecoveryIntegration).GetField("reverseEnd", fields).GetValue(fixture.Recovery);
        float rearLateral = (float)typeof(PoliceRecoveryIntegration).GetField("reverseLateral", fields).GetValue(fixture.Recovery);
        Vector2 rearDelta = rearEnd - rearStart;
        Vector2 corridorSize = fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin +
            new Vector2(2f * rearLateral + Mathf.Abs(Vector2.Dot(rearDelta, right)), Mathf.Abs(Vector2.Dot(rearDelta, forward)));
        var overlaps = new Collider2D[fixture.ControllerSettings.sensorBuffer];
        int count = fixture.Physics.OverlapBox((rearStart + rearEnd) * 0.5f,
            corridorSize, fixture.Binding.body.Body.rotation, new ContactFilter2D { useTriggers = false }, overlaps);
        Assert.Less(count, overlaps.Length, Facts(fixture, "striker preflight overlap saturated"));
        for (int index = 0; index < count; index++)
            Assert.AreEqual(fixture.Binding.body.Body, overlaps[index].attachedRigidbody, Facts(fixture, "solid overlaps the certified padded rear envelope"));
        strikerBody.AddForce(-right * strikerSpeed * strikerBody.mass, ForceMode2D.Impulse);

        fixture.Step(1);
        Assert.AreEqual(2, preCallbacks, Facts(fixture, "second physical callback was not observed on police receiver"));
        Assert.AreEqual(2, postCallbacks, Facts(fixture, "post observer missed second callback"));
        Assert.IsTrue(secondPreSawBothGates, Facts(fixture, "second callback did not arrive while reverse was powered"));
        Assert.IsTrue(secondPostSawStopped, Facts(fixture, "controller did not synchronously close reverse gates"));
        Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture));
        Assert.AreEqual(fixedGate, fixture.Recovery.Gate, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));

        RunUntilExhausted(fixture, impactClock, starts, deadline);
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.Greater(fixture.Player.CurrentHealth, 0f, Facts(fixture));
        Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, Facts(fixture));
    }

    static void ConfigureHeavyFixture(PursuitFixture fixture, float mass, float damping) {
        fixture.ConfigureRamRoute();
        fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
        var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
        targetBody.mass = mass;
        targetBody.linearDamping = damping;
    }

    static void StepUntil(PursuitFixture fixture, Func<bool> condition, float seconds, string description) {
        int budget = Mathf.CeilToInt(seconds / fixture.StepDelta) + 2;
        Assert.LessOrEqual(budget * fixture.StepDelta, 10f, Facts(fixture, description + " budget"));
        for (int step = 0; step < budget && !condition(); step++) fixture.Step(1);
        Assert.IsTrue(condition(), Facts(fixture, description));
    }

    static int RecoveryStepBudget(PursuitFixture fixture) {
        int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta) + 2;
        Assert.LessOrEqual(budget * fixture.StepDelta, 10f, Facts(fixture, "recovery budget"));
        return budget;
    }

    static void RunUntilExhausted(PursuitFixture fixture, float impactClock, int starts, float deadline) {
        for (int step = 0; step < RecoveryStepBudget(fixture) && !fixture.Controller.IsRecoveryExhausted; step++) {
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
            Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture));
            Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
            Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
            Assert.LessOrEqual(fixture.World.SessionTime,
                impactClock + fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta,
                Facts(fixture, "recovery deadline renewed or exceeded"));
        }
        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture, "original recovery episode did not exhaust"));
    }

    static void RunWithFixture(float deltaTime, Action<PursuitFixture> scenario, float? collisionDamageFactor = null) {
        PursuitFixture fixture = null;
        try {
            fixture = new PursuitFixture(deltaTime, collisionDamageFactor: collisionDamageFactor);
            scenario(fixture);
        } catch {
            if (fixture != null) {
                fixture.Dispose();
                fixture = null;
            }
            throw;
        } finally {
            if (fixture != null) fixture.Dispose();
        }
    }

    static string Facts(PursuitFixture fixture, string detail = "disturbance") {
        var command = fixture.Controller.LastCommand;
        return "case=" + detail + ", phase=" + fixture.Controller.RecoveryPhase +
            ", recovering=" + fixture.Controller.IsRecovering + ", exhausted=" + fixture.Controller.IsRecoveryExhausted +
            ", bound=" + fixture.Controller.IsBound + ", ramStarts=" + fixture.Controller.RamStarts +
            ", ramDeadline=" + fixture.Controller.RamCooldownUntil + ", motorReverse=" + fixture.MotorReversePermitted +
            ", command=" + command.throttle + "/" + command.brake + "/rev=" + command.reverseAllowed +
            ", policePosition=" + fixture.PolicePosition + ", policeVelocity=" + fixture.PoliceVelocity +
            ", playerHealth=" + fixture.Player.CurrentHealth + ", policeHealth=" + fixture.PoliceReceiver.CurrentHealth;
    }
}
