using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Physical force-envelope coverage for bounded police recovery.</summary>
public sealed class PoliceRecoveryForcePlayTests {
    /// <summary>Runs three isolated real-callback force and braking cases.</summary>
    [UnityTest]
    public IEnumerator RecoveryForceMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);
        RunMassLimitedTinyReverse();
        RunCapturedCrashBrakeReserve();
        RunFiniteGripRejoin();
    }

    /// <summary>Always exits Play Mode after the isolated force matrix.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunMassLimitedTinyReverse() {
        TestContext.Progress.WriteLine("Recovery force mass-limited tiny reverse case");
        using (var fixture = new PursuitFixture(0.03f)) {
            fixture.ConfigureRamRoute();
            fixture.Profile.baseMass = 4f;
            fixture.Profile.motorSettings.acceleration = 12f;
            fixture.Profile.motorSettings.maxEngineForce = 24f;
            fixture.Profile.motorSettings.brakeDeceleration = 8f;
            fixture.Profile.motorSettings.maxBrakeForce = 4f;
            fixture.ControllerSettings.comfort = 1f;
            fixture.ControllerSettings.maxSweepDistance = 64f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 1.5f;
            fixture.ControllerSettings.recovery.maximumReverseTravel = 0.015f;
            fixture.Binding.body.Motor.SetMass(4f);
            fixture.Binding.body.Motor.crashModeForceMultiplier = 1f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 8f;
            targetBody.linearDamping = 1f;
            bool impactPreControllerRecorded = false;
            bool impactPreControllerValid = false;
            fixture.PoliceReceiver.ImpactObserved += _ => {
                if (impactPreControllerRecorded) return;
                impactPreControllerRecorded = true;
                impactPreControllerValid = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Road && fixture.Cursor.IsBound;
            };
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool recoveryEvidenceRecorded = false;
            bool recoveryEvidenceWasTrue = false;
            fixture.PoliceReceiver.ImpactObserved += _ => {
                if (recoveryEvidenceRecorded) return;
                recoveryEvidenceRecorded = true;
                recoveryEvidenceWasTrue = ReadField<bool>(typeof(PoliceRecoveryIntegration), fixture.Recovery, "hasEvidence");
            };
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 240 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            Assert.IsTrue(impactPreControllerRecorded && impactPreControllerValid, Facts(fixture));
            Assert.IsTrue(recoveryEvidenceRecorded && recoveryEvidenceWasTrue, Facts(fixture));

            int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta);
            bool firstReverse = false;
            bool firstNegativeVelocity = false;
            bool previousReverseGate = false;
            int reverseRisingEdges = 0;
            bool stopped = false;
            float measuredTravel = 0f;
            float reportedTravel = 0f;
            float firstThrottle = 0f;
            float firstPreTickSpeed = 0f;
            Vector2 previousPosition = Vector2.zero;
            var trace = new List<string>();
            var diagnosticSamples = new List<string>();
            string lastKey = null;
            float impactClock = fixture.World.SessionTime;
            bool sampledOneSecond = false;
            bool sampledTwoSeconds = false;
            for (int step = 0; step < budget && !stopped; step++) {
                fixture.Step(1);
                var command = fixture.Controller.LastCommand;
                bool reverseGate = fixture.MotorReversePermitted && command.reverseAllowed;
                if (reverseGate && !previousReverseGate) reverseRisingEdges++;
                previousReverseGate = reverseGate;
                reportedTravel = Mathf.Max(reportedTravel, fixture.Recovery.ReverseTravel);
                if (!firstReverse && command.reverseAllowed && command.throttle < 0f) {
                    firstReverse = true;
                    firstThrottle = command.throttle;
                    firstPreTickSpeed = Mathf.Abs(Vector2.Dot(fixture.LastControllerVelocity, fixture.LastControllerForward));
                    firstNegativeVelocity = Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < 0f;
                    previousPosition = fixture.LastControllerPosition;
                }
                if (firstReverse) {
                    measuredTravel += Vector2.Distance(previousPosition, fixture.PolicePosition);
                    previousPosition = fixture.PolicePosition;
                }
                stopped = firstReverse && !reverseGate && !fixture.MotorReversePermitted && fixture.PoliceVelocity.magnitude <= 0.00001f;
                string state = Facts(fixture) + ", measuredTravel=" + measuredTravel + ", reportedTravel=" + reportedTravel;
                string key = TraceKey(fixture) + ", stopped=" + stopped;
                if ((step < 6 || !key.Equals(lastKey)) && trace.Count < 24) trace.Add(state);
                lastKey = key;
                float elapsed = fixture.World.SessionTime - impactClock;
                if (!sampledOneSecond && elapsed >= 1f) {
                    diagnosticSamples.Add("t+1=" + RecoveryDiagnostics(fixture));
                    sampledOneSecond = true;
                }
                if (!sampledTwoSeconds && elapsed >= 2f) {
                    diagnosticSamples.Add("t+2=" + RecoveryDiagnostics(fixture));
                    sampledTwoSeconds = true;
                }
            }
            string facts = Facts(fixture) + ", trace:\n" + string.Join("\n", trace);
            facts += "\ndiagnostics:\n" + string.Join("\n", diagnosticSamples) +
                "\nfinal=" + RecoveryDiagnostics(fixture);
            float actualFraction = Mathf.Abs(firstThrottle);
            double acceleration = Mathf.Min(fixture.Profile.motorSettings.acceleration,
                fixture.Profile.motorSettings.maxEngineForce / 4f);
            double braking = Mathf.Min(fixture.Profile.motorSettings.brakeDeceleration,
                fixture.Profile.motorSettings.maxBrakeForce / 4f);
            double response = fixture.Profile.motorSettings.reactionTime + 2d * fixture.StepDelta;
            double room = fixture.ControllerSettings.recovery.maximumReverseTravel;
            double root = 2d * room / (System.Math.Sqrt(response * response + 2d * room / braking) + response);
            double expectedFraction = (root - firstPreTickSpeed) / (acceleration * fixture.StepDelta);
            float fractionTolerance = 0.00001f + 4f / 8388608f;
            Assert.AreEqual(4f, fixture.Binding.body.Body.mass, 0.000001f, facts);
            Assert.AreEqual(6d, acceleration, 0.000001d, facts);
            Assert.AreEqual(1d, braking, 0.000001d, facts);
            Assert.IsTrue(firstReverse, facts);
            Assert.IsTrue(firstThrottle > -1f && firstThrottle < 0f, facts);
            Assert.IsTrue(firstNegativeVelocity, facts);
            Assert.IsTrue(stopped, facts);
            Assert.AreEqual(1, reverseRisingEdges, facts);
            Assert.Greater(expectedFraction, 0d, facts);
            Assert.LessOrEqual(Mathf.Abs(actualFraction - (float)expectedFraction), fractionTolerance, facts);
            Assert.Greater(measuredTravel, 0f, facts);
            Assert.LessOrEqual(measuredTravel, 0.01501f, facts);
            Assert.LessOrEqual(reportedTravel, 0.01501f, facts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    static void RunCapturedCrashBrakeReserve() {
        TestContext.Progress.WriteLine("Recovery force captured crash-brake reserve case");
        using (var fixture = new PursuitFixture(0.03f)) {
            fixture.ConfigureRamRoute(11.3f, 14f, 100f, 120f);
            fixture.ControllerSettings.maxSweepDistance = 64f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            fixture.Binding.body.Motor.crashModeForceMultiplier = 0.1f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            bool crashModeObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 240 && !impactObserved; step++) {
                fixture.Step(1);
                crashModeObserved |= fixture.Binding.body.Motor.IsCrashMode;
            }
            Assert.IsTrue(impactObserved, Facts(fixture));
            float impactClock = fixture.World.SessionTime;
            int originalRamStarts = fixture.Controller.RamStarts;
            float policeHealth = fixture.PoliceReceiver.CurrentHealth;
            float playerHealth = fixture.Player.CurrentHealth;
            int settleSteps = 0;
            int settleBudget = Mathf.CeilToInt(2f / fixture.StepDelta);
            crashModeObserved |= fixture.Binding.body.Motor.IsCrashMode;
            while (settleSteps < settleBudget && fixture.PoliceVelocity.magnitude > 0.00001f) {
                fixture.Step(1);
                settleSteps++;
                crashModeObserved |= fixture.Binding.body.Motor.IsCrashMode;
            }
            Assert.IsTrue(crashModeObserved, Facts(fixture));
            Assert.LessOrEqual(fixture.PoliceVelocity.magnitude, 0.00001f, Facts(fixture));

            NpcMotorSettings motor = fixture.Profile.motorSettings;
            float mass = fixture.Binding.body.Body.mass;
            Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, mass, false, 1f, fixture.ControllerSettings.comfort, out float normalBrake));
            Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, mass, true, 0.1f, fixture.ControllerSettings.comfort, out float crashBrake));
            float acceleration = Mathf.Min(motor.acceleration, motor.maxEngineForce / mass);
            float cap = Mathf.Min(fixture.ControllerSettings.recovery.maneuverSpeed, Mathf.Min(motor.reverseSpeed, 5f));
            float peak = cap + acceleration * fixture.StepDelta;
            float response = motor.reactionTime + fixture.StepDelta;
            float normalReserve = peak * (fixture.ControllerSettings.recovery.reverseMaxSeconds + response) +
                peak * peak / (2f * normalBrake) + peak * fixture.StepDelta + fixture.ControllerSettings.stopGap;
            float crashReserve = peak * (fixture.ControllerSettings.recovery.reverseMaxSeconds + response) +
                peak * peak / (2f * crashBrake) + peak * fixture.StepDelta + fixture.ControllerSettings.stopGap;
            Vector2 paddedFootprint = fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin;
            float paddedHalfLength = paddedFootprint.y * 0.5f;
            float wallHalfThickness = 0.05f;
            float wallDistance = (normalReserve + crashReserve) * 0.5f + paddedHalfLength + wallHalfThickness;
            float effectiveWallDistance = wallDistance - paddedHalfLength - wallHalfThickness;
            Assert.Greater(crashReserve, normalReserve, Facts(fixture));
            Assert.Greater(effectiveWallDistance, normalReserve, Facts(fixture));
            Assert.Less(effectiveWallDistance, crashReserve, Facts(fixture));
            Vector2 wallPosition = fixture.PolicePosition - fixture.PoliceForward * wallDistance;
            float wallHeading = Mathf.Atan2(-fixture.PoliceForward.x, fixture.PoliceForward.y) * Mathf.Rad2Deg;
            GameObject wall = fixture.CreateBlocker(wallPosition, false, new Vector2(2f, 0.1f), wallHeading);
            try {
                Vector2 normalEndpoint = fixture.PolicePosition - fixture.PoliceForward * normalReserve;
                Vector2 crashEndpoint = fixture.PolicePosition - fixture.PoliceForward * crashReserve;
                Assert.IsTrue(RoadFootprintClearance.IsSweepClear(fixture.PolicePosition, normalEndpoint, paddedFootprint,
                    Vector2.zero, fixture.Binding.body.Body.rotation, fixture.Binding.body.Body.rotation,
                    fixture.Binding.navigation.localBounds, null, fixture.Binding.staticClearance), Facts(fixture));
                Assert.IsFalse(RoadFootprintClearance.IsSweepClear(fixture.PolicePosition, crashEndpoint, paddedFootprint,
                    Vector2.zero, fixture.Binding.body.Body.rotation, fixture.Binding.body.Body.rotation,
                    fixture.Binding.navigation.localBounds, null, fixture.Binding.staticClearance), Facts(fixture));
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
                fixture.Step(1);
                Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
                int episodeBudget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta);
                for (int step = settleSteps + 1; step < episodeBudget && !fixture.Controller.IsRecoveryExhausted; step++) {
                    fixture.Step(1);
                    Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                    Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
                    Assert.AreEqual(originalRamStarts, fixture.Controller.RamStarts, Facts(fixture));
                    Assert.AreEqual(policeHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture));
                    Assert.AreEqual(playerHealth, fixture.Player.CurrentHealth, Facts(fixture));
                }
                Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
                Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
                Assert.LessOrEqual(fixture.World.SessionTime - impactClock,
                    fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta + 0.001f, Facts(fixture));
            } finally {
                Object.DestroyImmediate(wall);
            }
        }
    }

    static void RunFiniteGripRejoin() {
        TestContext.Progress.WriteLine("Recovery force finite-grip rejoin case");
        using (var fixture = new PursuitFixture(0.03f)) {
            fixture.ConfigureRamRoute();
            fixture.Profile.motorSettings.lateralGrip = 0.1f;
            fixture.ControllerSettings.maxSweepDistance = 64f;
            fixture.ControllerSettings.recovery.lightImpactSpeed = 1f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            // This test isolates the zero-yaw lateral consumer; it does not claim arbitrary yaw recovery.
            fixture.Binding.body.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            targetBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            Assert.AreEqual(RigidbodyType2D.Dynamic, fixture.Binding.body.Body.bodyType);
            Assert.AreEqual(RigidbodyType2D.Dynamic, targetBody.bodyType);
            Assert.IsTrue(fixture.Binding.body.Body.simulated);
            Assert.IsTrue(targetBody.simulated);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeRotation, fixture.Binding.body.Body.constraints);
            Assert.AreEqual(RigidbodyConstraints2D.FreezeRotation, targetBody.constraints);
            Assert.AreEqual(0f, fixture.Binding.body.Body.rotation);
            Assert.AreEqual(0f, targetBody.rotation);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            var planner = fixture.Planner;
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 240 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            float impactClock = fixture.World.SessionTime;
            float originalDeadline = fixture.Controller.RamCooldownUntil;
            Vector2 targetAtImpact = fixture.PlayerPosition;
            bool reverseBegan = false;
            bool reverseExited = false;
            bool impulseApplied = false;
            bool playerEscaped = false;
            bool lateralPushed = false;
            bool positiveRejoin = false;
            bool lateralObserved = false;
            bool behindAtDisturbance = false;
            bool previousReverseGate = false;
            int reverseRisingEdges = 0;
            float maximumReverseTravel = 0f;
            Vector2 gateAtReverse = Vector2.zero;
            Vector2 forwardAtReverse = Vector2.zero;
            bool gateStable = true;
            bool deadlineStable = true;
            bool positiveRoute = false;
            float sideSpeedObserved = 0f;
            var trace = new List<string>();
            string lastKey = null;
            int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta);
            for (int step = 0; step < budget && !lateralObserved; step++) {
                fixture.Step(1);
                var command = fixture.Controller.LastCommand;
                bool reverseGate = fixture.MotorReversePermitted && command.reverseAllowed;
                if (reverseGate && !previousReverseGate) {
                    reverseRisingEdges++;
                    if (reverseRisingEdges == 1) { gateAtReverse = fixture.Recovery.Gate; forwardAtReverse = fixture.PoliceForward; }
                }
                previousReverseGate = reverseGate;
                reverseBegan |= reverseGate;
                if (reverseBegan && !reverseGate) reverseExited = true;
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                gateStable &= !reverseBegan || Vector2.Distance(fixture.Recovery.Gate, gateAtReverse) <= 0.0001f;
                deadlineStable &= Mathf.Abs(fixture.Controller.RamCooldownUntil - originalDeadline) <= 0.0001f;
                if (!impulseApplied && reverseExited && maximumReverseTravel >= 0.2f) {
                    fixture.PushPlayer(Vector2.up * 6f);
                    impulseApplied = true;
                }
                positiveRejoin |= playerEscaped && fixture.Controller.IsRecovering && command.throttle > 0f && command.brake == 0f && !command.reverseAllowed &&
                    Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) > 0f;
                positiveRoute |= positiveRejoin && fixture.Planner.CurrentResult != null &&
                    fixture.Planner.CurrentResult.status == PoliceRoadTargetQuery.Status.Route;
                playerEscaped |= Vector2.Distance(fixture.PlayerPosition, targetAtImpact) > 0.05f;
                if (!lateralPushed && positiveRejoin) {
                    behindAtDisturbance = Vector2.Dot(fixture.LastControllerPosition - gateAtReverse, forwardAtReverse) < 0f;
                    Vector2 side = new Vector2(-fixture.PoliceForward.y, fixture.PoliceForward.x);
                    fixture.Binding.body.Body.AddForce(side * (fixture.Binding.body.Body.mass * 0.25f), ForceMode2D.Impulse);
                    lateralPushed = true;
                }
                if (lateralPushed) {
                    Vector2 observedSide = new Vector2(-fixture.LastControllerForward.y, fixture.LastControllerForward.x);
                    sideSpeedObserved = Mathf.Abs(Vector2.Dot(fixture.LastControllerVelocity, observedSide));
                    float allowance = sideSpeedObserved * 0.9f * fixture.StepDelta / 0.1f;
                    lateralObserved = allowance > 0.05f && fixture.Binding.body.Body.angularVelocity == 0f;
                }
                string state = Facts(fixture) + ", reverseTravel=" + maximumReverseTravel + ", playerEscaped=" + playerEscaped +
                    ", lateralPushed=" + lateralPushed + ", lateralObserved=" + lateralObserved;
                string key = TraceKey(fixture) + ", playerEscaped=" + playerEscaped + ", lateralPushed=" + lateralPushed + ", positive=" + positiveRejoin;
                if ((step < 6 || !key.Equals(lastKey)) && trace.Count < 24) trace.Add(state);
                lastKey = key;
            }
            string facts = Facts(fixture) + ", reverseTravel=" + maximumReverseTravel + ", sideSpeed=" + sideSpeedObserved +
                "\ntrace:\n" + string.Join("\n", trace) + "\nfinal=" + RecoveryDiagnostics(fixture);
            Assert.IsTrue(reverseBegan, facts);
            Assert.IsTrue(reverseExited, facts);
            Assert.IsTrue(impulseApplied, facts);
            Assert.IsTrue(playerEscaped, facts);
            Assert.IsTrue(positiveRejoin, facts);
            Assert.IsTrue(positiveRoute, facts);
            Assert.IsTrue(lateralPushed, facts);
            Assert.IsTrue(lateralObserved, facts);
            Assert.IsTrue(behindAtDisturbance, facts);
            Assert.AreEqual(1, reverseRisingEdges, facts);
            Assert.GreaterOrEqual(maximumReverseTravel, 0.2f, facts);
            Assert.IsTrue(gateStable, facts);
            Assert.IsTrue(deadlineStable, facts);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle, facts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
            Assert.IsFalse(fixture.Recovery.ResumeReady, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.LessOrEqual(fixture.World.SessionTime - impactClock,
                fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta + 0.001f, facts);
            Assert.AreSame(planner, fixture.Planner, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    static string Facts(PursuitFixture fixture) {
        return "Force facts: phase=" + fixture.Controller.RecoveryPhase + ", recovering=" + fixture.Controller.IsRecovering +
            ", exhausted=" + fixture.Controller.IsRecoveryExhausted + ", command=" + fixture.Controller.LastCommand.throttle +
            "/" + fixture.Controller.LastCommand.brake + "/rev=" + fixture.Controller.LastCommand.reverseAllowed +
            ", motorReverse=" + fixture.MotorReversePermitted + ", ramStarts=" + fixture.Controller.RamStarts +
            ", ramDeadline=" + fixture.Controller.RamCooldownUntil + ", policePosition=" + fixture.PolicePosition +
            ", policeVelocity=" + fixture.PoliceVelocity + ", targetPosition=" + fixture.PlayerPosition +
            ", policeHP=" + fixture.PoliceReceiver.CurrentHealth + ", targetHP=" + fixture.Player.CurrentHealth;
    }

    static string TraceKey(PursuitFixture fixture) {
        return fixture.Controller.RecoveryPhase + "/" + fixture.Controller.LastCommand.throttle + "/" +
            fixture.Controller.LastCommand.brake + "/rev=" + fixture.Controller.LastCommand.reverseAllowed +
            "/motorReverse=" + fixture.MotorReversePermitted + "/ram=" + fixture.Controller.RamStarts +
            "/planner=" + fixture.Controller.PlannerAttemptCount;
    }

    static string RecoveryDiagnostics(PursuitFixture fixture) {
        var recovery = fixture.Recovery;
        var recoveryType = typeof(PoliceRecoveryIntegration);
        bool hasEvidence = ReadField<bool>(recoveryType, recovery, "hasEvidence");
        bool hasGate = ReadField<bool>(recoveryType, recovery, "hasGate");
        var floor = ReadField<RoadPathQuery.EdgeAnchor>(recoveryType, recovery, "floor");
        long version = ReadField<long>(recoveryType, recovery, "version");
        var retainedTarget = ReadField<Rigidbody2D>(recoveryType, recovery, "retainedTarget");
        float retryAt = ReadField<float>(recoveryType, recovery, "retryAt");
        bool reverseCertified = ReadField<bool>(recoveryType, recovery, "reverseCertified");
        object traversal = recoveryType.GetField("traversal", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(recovery);
        System.Type traversalType = traversal.GetType();
        bool isReversing = (bool)traversalType.GetProperty("IsReversing", BindingFlags.Instance | BindingFlags.Public).GetValue(traversal);
        bool canStartReverse = (bool)traversalType.GetProperty("CanStartReverse", BindingFlags.Instance | BindingFlags.Public).GetValue(traversal);
        CrashRecoveryPolicy.Phase phase = (CrashRecoveryPolicy.Phase)traversalType.GetProperty("Phase", BindingFlags.Instance | BindingFlags.Public).GetValue(traversal);
        float activeSeconds = (float)traversalType.GetProperty("ActiveSeconds", BindingFlags.Instance | BindingFlags.Public).GetValue(traversal);
        bool reverseAttempted = ReadField<bool>(traversalType, traversal, "reverseAttempted");
        var result = fixture.Planner.CurrentResult;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Vector2 velocity = fixture.Binding.body.Body.linearVelocity;
        return "bound=" + fixture.Controller.IsBound + ", clock=" + fixture.World.SessionTime +
            ", pos=" + fixture.Binding.body.Body.position + ", v=" + velocity.x.ToString("R", culture) + "/" + velocity.y.ToString("R", culture) +
            ", angular=" + fixture.Binding.body.Body.angularVelocity.ToString("R", culture) + ", rotation=" + fixture.Binding.body.Body.rotation.ToString("R", culture) +
            ", hasEvidence=" + hasEvidence + ", hasGate=" + hasGate + ", floor=" + floor.edgeId + "@" + floor.distanceAlongEdge +
            ", version=" + version + ", retainedTarget=" + (retainedTarget != null) + ", retryAt=" + retryAt +
            ", reverseCertified=" + reverseCertified + ", gate=" + recovery.Gate +
            ", attempts=" + fixture.Planner.AttemptCount + ", result=" + (result == null ? "null" : result.status + "/" + result.waitReason + "/" + result.startAnchor.edgeId + "@" + result.startAnchor.distanceAlongEdge) +
            ", traversal=" + phase + "/active=" + activeSeconds + "/reversing=" + isReversing + "/canReverse=" + canStartReverse +
            "/attempted=" + reverseAttempted;
    }

    static T ReadField<T>(System.Type type, object instance, string name) {
        return (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
}
