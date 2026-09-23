using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Play-mode callback coverage for bounded police recovery and its physical gates.</summary>
public sealed class PoliceRecoveryContactPlayTests {
    /// <summary>Runs the bounded light matrix and independent negative and positive heavy recovery cases.</summary>
    [UnityTest]
    public IEnumerator RecoveryFoundationMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        RunLightRecovery(0.02f, false);
        RunLightRecovery(0.02f, true);
        RunLightRecovery(0.03f, false);
        RunLightRecovery(0.03f, true);
        RunRearBlockedRecovery();
        RunSustainedContactExhaustion();
        RunHeavyRecoveryStationary();
        RunHeavyRecoveryPositiveEscape(0.02f, false);
    }

    /// <summary>Runs the remaining creation-order and cadence variants in an isolated Play session.</summary>
    [UnityTest]
    public IEnumerator RecoveryHeavyVariantsMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);
        RunHeavyRecoveryPositiveEscape(0.02f, true);
        RunHeavyRecoveryPositiveEscape(0.03f, false);
        RunHeavyRecoveryPositiveEscape(0.03f, true);
    }

    /// <summary>Exercises rear-clear freshness after a genuine heavy reverse is physically blocked.</summary>
    [UnityTest]
    public IEnumerator RecoveryRearFreshnessMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);
        RunRearFreshnessBlockedRecovery();
    }

    /// <summary>Proves a force-limited tiny reverse remains within its authored travel envelope.</summary>
    [UnityTest]
    public IEnumerator RecoveryTinyTravelMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);
        RunTinyTravelRecovery();
    }

    /// <summary>Always returns the editor to Edit Mode after the isolated recovery matrix.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunLightRecovery(float deltaTime, bool reverseCreationOrder) {
        TestContext.Progress.WriteLine("Recovery light case: delta=" + deltaTime + ", reverseCreationOrder=" + reverseCreationOrder);
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureRamRoute();
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            var planner = fixture.Planner;
            bool impactObserved = false;
            bool immediateCancel = false;
            fixture.PoliceReceiver.ImpactObserved += _ => {
                impactObserved = true;
                immediateCancel = !fixture.Controller.IsRamming && fixture.Controller.LastCommand.brake == 1f;
            };
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            Assert.IsTrue(immediateCancel, Facts(fixture));

            bool sawRecovery = false;
            bool releaseObserved = false;
            bool releaseGateProof = false;
            int releasePlannerAttempt = -1;
            int releaseArrivalAttempt = -1;
            Vector2 gateAtRelease = Vector2.zero;
            Vector2 forwardAtRelease = Vector2.zero;
            Vector2 impactPosition = fixture.PolicePosition;
            float impactClock = fixture.World.SessionTime;
            for (int step = 0; step < 500 && !releaseObserved; step++) {
                fixture.Step(1);
                sawRecovery |= fixture.Controller.IsRecovering;
                if (fixture.Recovery.ResumeReady) {
                    releaseObserved = true;
                    releasePlannerAttempt = fixture.Controller.PlannerAttemptCount;
                    releaseArrivalAttempt = fixture.RecoveryArrivalAttempt;
                    gateAtRelease = fixture.Recovery.Gate;
                    forwardAtRelease = fixture.PoliceForward;
                    releaseGateProof = releaseArrivalAttempt >= 0 && releasePlannerAttempt > releaseArrivalAttempt &&
                        Vector2.Distance(fixture.LastControllerPosition, gateAtRelease) <= fixture.ControllerSettings.acquisitionTolerance &&
                        Vector2.Dot(fixture.LastControllerPosition - gateAtRelease, forwardAtRelease) >= 0f;
                }
            }
            Assert.IsTrue(sawRecovery, Facts(fixture));
            Assert.IsTrue(releaseObserved, Facts(fixture));
            Assert.AreSame(planner, fixture.Planner, Facts(fixture));
            Assert.GreaterOrEqual(releaseArrivalAttempt, 0, Facts(fixture));
            Assert.Greater(releasePlannerAttempt, releaseArrivalAttempt, Facts(fixture));
            Assert.IsTrue(releaseGateProof, Facts(fixture));
            Assert.LessOrEqual(fixture.World.SessionTime - impactClock, 8f);
            Assert.Greater(Vector2.Distance(impactPosition, fixture.PolicePosition), 0.01f, Facts(fixture));
            Assert.IsFalse(fixture.Controller.IsRecovering, Facts(fixture));
            Assert.IsFalse(fixture.Controller.IsRamming, Facts(fixture));
            Assert.LessOrEqual(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold + 0.001f,
                Facts(fixture, "light deltaTime=" + deltaTime + ", reverseCreationOrder=" + reverseCreationOrder));
            Vector2 releasePosition = fixture.PolicePosition;
            bool sawOrdinaryDrive = false;
            var postReleaseTrace = new List<string>();
            string lastTraceKey = null;
            float maximumPostReleaseTravel = 0f;
            int postReleaseSteps = PostReleaseStepBudget(fixture);
            for (int step = 0; step < postReleaseSteps; step++) {
                fixture.Step(1);
                maximumPostReleaseTravel = Mathf.Max(maximumPostReleaseTravel, Vector2.Distance(releasePosition, fixture.PolicePosition));
                bool ordinaryDrive = !fixture.Controller.IsRecovering && fixture.Controller.LastCommand.throttle > 0f &&
                    fixture.Controller.LastCommand.brake < 1f && Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) > 0.1f;
                sawOrdinaryDrive |= maximumPostReleaseTravel > 0.1f && ordinaryDrive;
                string trace = RecoveryTrace(fixture, step) + ", maxTravel=" + maximumPostReleaseTravel;
                string traceKey = RecoveryTraceKey(fixture);
                bool changed = lastTraceKey == null || !traceKey.Equals(lastTraceKey);
                if ((step < 12 || changed) && postReleaseTrace.Count < 24) postReleaseTrace.Add(trace);
                lastTraceKey = traceKey;
                if (sawOrdinaryDrive) break;
            }
            string postReleaseFacts = Facts(fixture, "light deltaTime=" + deltaTime + ", reverseCreationOrder=" + reverseCreationOrder) +
                ", maxPostReleaseTravel=" + maximumPostReleaseTravel + ", postReleaseSteps=" + postReleaseSteps +
                "\npostReleaseTrace:\n" + string.Join("\n", postReleaseTrace);
            Assert.IsTrue(sawOrdinaryDrive, postReleaseFacts);
            Assert.Greater(maximumPostReleaseTravel, 0.1f, postReleaseFacts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f);
        }
    }

    /// <summary>Real heavy impact reverses but remains safely stationary until the bounded episode exhausts.</summary>
    static void RunHeavyRecoveryStationary() {
        TestContext.Progress.WriteLine("Recovery heavy stationary negative case");
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.ControllerSettings.recovery.lightImpactSpeed = 1f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));

            bool reverseBegan = false;
            bool negativeVelocity = false;
            bool reverseExited = false;
            float maximumReverseTravel = 0f;
            Vector2 start = fixture.PolicePosition;
            var recoveryTrace = new List<string>();
            string lastTraceKey = null;
            int recoverySteps = Mathf.CeilToInt(8f / fixture.StepDelta);
            for (int step = 0; step < recoverySteps && !fixture.Controller.IsRecoveryExhausted; step++) {
                fixture.Step(1);
                bool reverseGate = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
                reverseBegan |= reverseGate;
                negativeVelocity |= Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < -0.01f;
                if (reverseBegan && !reverseGate) reverseExited = true;
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                string trace = RecoveryTrace(fixture, step) + ", maxReverseTravel=" + maximumReverseTravel;
                string traceKey = RecoveryTraceKey(fixture) + ", exhausted=" + fixture.Controller.IsRecoveryExhausted;
                if ((step < 4 || !traceKey.Equals(lastTraceKey)) && recoveryTrace.Count < 16) recoveryTrace.Add(trace);
                lastTraceKey = traceKey;
            }
            string facts = Facts(fixture, "heavy stationary recoverySteps=" + recoverySteps) +
                ", maxReverseTravel=" + maximumReverseTravel +
                "\nrecoveryTrace:\n" + string.Join("\n", recoveryTrace);
            Assert.IsTrue(reverseBegan, facts);
            Assert.IsTrue(negativeVelocity, facts);
            Assert.IsTrue(reverseExited, facts);
            Assert.Greater(maximumReverseTravel, 0f, facts);
            Assert.LessOrEqual(maximumReverseTravel, fixture.ControllerSettings.recovery.maximumReverseTravel + 0.001f, facts);
            Assert.Greater(Vector2.Distance(start, fixture.PolicePosition), 0.01f, facts);
            Assert.IsFalse(fixture.Recovery.ResumeReady, facts);
            Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    /// <summary>One real target escape permits one bounded heavy reverse and a fresh forward rejoin.</summary>
    static void RunHeavyRecoveryPositiveEscape(float deltaTime, bool reverseCreationOrder) {
        TestContext.Progress.WriteLine("Recovery heavy positive escape case: delta=" + deltaTime + ", reverseCreationOrder=" + reverseCreationOrder);
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureRamRoute();
            fixture.ControllerSettings.recovery.lightImpactSpeed = 1f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            var planner = fixture.Planner;
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            float impactClock = fixture.World.SessionTime;

            bool reverseBegan = false;
            bool negativeVelocity = false;
            bool reverseExited = false;
            bool impulseApplied = false;
            bool releaseObserved = false;
            bool releaseGateProof = false;
            int releasePlannerAttempt = -1;
            int releaseArrivalAttempt = -1;
            float maximumReverseTravel = 0f;
            int reverseGateRisingEdges = 0;
            bool previousReverseGate = false;
            bool gateStable = true;
            bool deadlineStable = true;
            Vector2 gateAtReverse = Vector2.zero;
            Vector2 forwardAtReverse = Vector2.zero;
            float originalRamDeadline = fixture.Controller.RamCooldownUntil;
            float firstReverseClock = float.NaN;
            bool releaseRouteProof = false;
            Vector2 targetAtImpact = fixture.PlayerPosition;
            var recoveryTrace = new List<string>();
            string lastTraceKey = null;
            int recoverySteps = Mathf.CeilToInt(8f / fixture.StepDelta);
            for (int step = 0; step < recoverySteps && !releaseObserved; step++) {
                fixture.Step(1);
                bool reverseGate = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
                reverseBegan |= reverseGate;
                if (reverseGate && !previousReverseGate) {
                    reverseGateRisingEdges++;
                    if (reverseGateRisingEdges == 1) {
                        gateAtReverse = fixture.Recovery.Gate;
                        forwardAtReverse = fixture.PoliceForward;
                        firstReverseClock = fixture.World.SessionTime;
                    }
                }
                previousReverseGate = reverseGate;
                negativeVelocity |= Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < -0.01f;
                if (reverseBegan && !reverseGate) reverseExited = true;
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                gateStable &= reverseGateRisingEdges == 0 || Vector2.Distance(gateAtReverse, fixture.Recovery.Gate) <= 0.0001f;
                deadlineStable &= Mathf.Abs(fixture.Controller.RamCooldownUntil - originalRamDeadline) <= 0.0001f;
                if (!impulseApplied && !fixture.Controller.IsRecoveryExhausted && reverseExited && maximumReverseTravel >= 0.2f) {
                    fixture.PushPlayer(Vector2.up * 6f);
                    impulseApplied = true;
                }
                if (fixture.Recovery.ResumeReady) {
                    releaseObserved = true;
                    releasePlannerAttempt = fixture.Controller.PlannerAttemptCount;
                    releaseArrivalAttempt = fixture.RecoveryArrivalAttempt;
                    releaseRouteProof = fixture.Planner.CurrentResult != null &&
                        fixture.Planner.CurrentResult.status == PoliceRoadTargetQuery.Status.Route;
                    releaseGateProof = releaseArrivalAttempt >= 0 && releasePlannerAttempt > releaseArrivalAttempt &&
                        Vector2.Distance(fixture.LastControllerPosition, gateAtReverse) <= fixture.ControllerSettings.acquisitionTolerance &&
                        Vector2.Dot(fixture.LastControllerPosition - gateAtReverse, forwardAtReverse) >= 0f;
                }
                string trace = RecoveryTrace(fixture, step) + ", maxReverseTravel=" + maximumReverseTravel + ", impulseApplied=" + impulseApplied +
                    ", gateStable=" + gateStable + ", deadlineStable=" + deadlineStable;
                string traceKey = RecoveryTraceKey(fixture) + ", impulse=" + impulseApplied + ", release=" + releaseObserved;
                if ((step < 4 || !traceKey.Equals(lastTraceKey)) && recoveryTrace.Count < 16) recoveryTrace.Add(trace);
                lastTraceKey = traceKey;
            }
            string facts = Facts(fixture, "heavy positive recoverySteps=" + recoverySteps) +
                ", maxReverseTravel=" + maximumReverseTravel + ", impulseApplied=" + impulseApplied +
                "\nrecoveryTrace:\n" + string.Join("\n", recoveryTrace);
            Assert.IsTrue(reverseBegan, facts);
            Assert.IsTrue(negativeVelocity, facts);
            Assert.IsTrue(reverseExited, facts);
            Assert.IsTrue(impulseApplied, facts);
            Assert.AreEqual(1, reverseGateRisingEdges, facts);
            Assert.IsTrue(gateStable, facts);
            Assert.IsTrue(deadlineStable, facts);
            Assert.Less(firstReverseClock, originalRamDeadline, facts);
            Assert.GreaterOrEqual(maximumReverseTravel, 0.2f, facts);
            Assert.LessOrEqual(maximumReverseTravel, fixture.ControllerSettings.recovery.maximumReverseTravel + 0.001f, facts);
            Assert.Greater(Vector2.Distance(targetAtImpact, fixture.PlayerPosition), 0.05f, facts);
            Assert.IsTrue(releaseObserved, facts);
            Assert.AreSame(planner, fixture.Planner, facts);
            Assert.GreaterOrEqual(releaseArrivalAttempt, 0, facts);
            Assert.Greater(releasePlannerAttempt, releaseArrivalAttempt, facts);
            Assert.IsTrue(releaseRouteProof, facts);
            Assert.IsTrue(releaseGateProof, facts);
            Assert.LessOrEqual(fixture.World.SessionTime - impactClock, 8f + 0.001f, facts);
            Assert.IsFalse(fixture.Controller.IsRecovering, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    /// <summary>Fresh rear clearance is proven after a real temporary reverse blocker is removed.</summary>
    static void RunRearFreshnessBlockedRecovery() {
        TestContext.Progress.WriteLine("Recovery rear freshness blocked case");
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.ControllerSettings.recovery.lightImpactSpeed = 1f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            float impactClock = fixture.World.SessionTime;
            float originalRamDeadline = fixture.Controller.RamCooldownUntil;
            int originalRamStarts = fixture.Controller.RamStarts;
            float originalPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
            float originalPlayerHealth = fixture.Player.CurrentHealth;
            int episodeSteps = 0;
            int episodeBudget = Mathf.CeilToInt(8f / fixture.StepDelta);
            bool reverseObserved = false;
            bool certifiedNegativeVelocity = false;
            float maximumReverseTravel = 0f;
            bool previousReverseGate = false;
            int reverseGateRisingEdges = 0;
            while (episodeSteps < episodeBudget && !(reverseObserved && certifiedNegativeVelocity && maximumReverseTravel > 0.02f)) {
                fixture.Step(1);
                episodeSteps++;
                bool reverseGate = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
                if (reverseGate && !previousReverseGate) reverseGateRisingEdges++;
                previousReverseGate = reverseGate;
                reverseObserved |= reverseGate;
                certifiedNegativeVelocity |= Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) <= -0.6f;
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
            }
            Assert.IsTrue(reverseObserved && certifiedNegativeVelocity && maximumReverseTravel > 0.02f, Facts(fixture));
            Vector2 blockedPosition = fixture.PolicePosition;
            Vector2 wallPosition = blockedPosition - fixture.PoliceForward * 2f;
            float wallHeading = Mathf.Atan2(-fixture.PoliceForward.x, fixture.PoliceForward.y) * Mathf.Rad2Deg;
            GameObject wall = fixture.CreateBlocker(wallPosition, false, new Vector2(2f, 0.1f), wallHeading);
            Assert.Greater(Vector2.Distance(blockedPosition, wallPosition), 1.5f);
            Assert.Less(Vector2.Distance(blockedPosition, wallPosition), 4f);

            fixture.Step(1);
            episodeSteps++;
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
            Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
            float maximumBrakingTravel = Vector2.Distance(blockedPosition, fixture.PolicePosition);
            bool actualStopped = fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold + 0.001f;
            while (episodeSteps < episodeBudget && !actualStopped) {
                fixture.Step(1);
                episodeSteps++;
                maximumBrakingTravel = Mathf.Max(maximumBrakingTravel, Vector2.Distance(blockedPosition, fixture.PolicePosition));
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
                Assert.AreEqual(originalRamStarts, fixture.Controller.RamStarts, Facts(fixture));
                Assert.AreEqual(originalPoliceHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture));
                Assert.AreEqual(originalPlayerHealth, fixture.Player.CurrentHealth, Facts(fixture));
                Assert.LessOrEqual(Mathf.Abs(fixture.Controller.RamCooldownUntil - originalRamDeadline), 0.0001f, Facts(fixture));
                actualStopped = fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold + 0.001f;
            }
            Assert.IsTrue(actualStopped, Facts(fixture));
            Object.DestroyImmediate(wall);
            while (episodeSteps < episodeBudget && !fixture.Controller.IsRecoveryExhausted) {
                fixture.Step(1);
                episodeSteps++;
                bool reverseGate = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
                if (reverseGate && !previousReverseGate) reverseGateRisingEdges++;
                previousReverseGate = reverseGate;
                maximumBrakingTravel = Mathf.Max(maximumBrakingTravel, Vector2.Distance(blockedPosition, fixture.PolicePosition));
                maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                Assert.IsFalse(reverseGate, Facts(fixture));
                Assert.AreEqual(originalRamStarts, fixture.Controller.RamStarts, Facts(fixture));
                Assert.AreEqual(originalPoliceHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture));
                Assert.AreEqual(originalPlayerHealth, fixture.Player.CurrentHealth, Facts(fixture));
                Assert.LessOrEqual(Mathf.Abs(fixture.Controller.RamCooldownUntil - originalRamDeadline), 0.0001f, Facts(fixture));
            }
            string facts = Facts(fixture, "rear freshness episodeSteps=" + episodeSteps) +
                ", maxReverseTravel=" + maximumReverseTravel + ", maxBrakingTravel=" + maximumBrakingTravel +
                ", reverseGateRisingEdges=" + reverseGateRisingEdges + ", impactClock=" + impactClock;
            Assert.AreEqual(1, reverseGateRisingEdges, facts);
            Assert.Greater(maximumReverseTravel, 0f, facts);
            Assert.Greater(maximumBrakingTravel, 0.01f, facts);
            Assert.AreEqual(originalRamStarts, fixture.Controller.RamStarts, facts);
            Assert.LessOrEqual(fixture.World.SessionTime - impactClock, 8f + 0.001f, facts);
            Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, facts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    /// <summary>Uses real motor acceleration and braking to measure a bounded tiny reverse.</summary>
    static void RunTinyTravelRecovery() {
        TestContext.Progress.WriteLine("Recovery tiny travel case");
        using (var fixture = new PursuitFixture(0.03f)) {
            fixture.ConfigureRamRoute();
            fixture.Profile.motorSettings.brakeDeceleration = 1f;
            fixture.Profile.motorSettings.maxBrakeForce = 1f;
            fixture.ControllerSettings.comfort = 1f;
            fixture.ControllerSettings.maxSweepDistance = 64f;
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            fixture.ControllerSettings.recovery.maximumReverseTravel = 0.015f;
            fixture.Binding.body.Motor.crashModeForceMultiplier = 1f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));

            int stepBudget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta);
            bool firstNegativeCommand = false;
            bool firstNegativeVelocity = false;
            bool previousReverseGate = false;
            int reverseGateRisingEdges = 0;
            bool measuringTravel = false;
            bool gatesClosedAndStopped = false;
            Vector2 previousPhysicalPosition = Vector2.zero;
            Vector2 firstNegativeStart = Vector2.zero;
            float firstNegativeThrottle = 0f;
            float measuredTravel = 0f;
            float reportedMaximumReverseTravel = 0f;
            var trace = new List<string>();
            string lastTraceKey = null;
            for (int step = 0; step < stepBudget && !gatesClosedAndStopped; step++) {
                fixture.Step(1);
                var command = fixture.Controller.LastCommand;
                bool reverseGate = fixture.MotorReversePermitted && command.reverseAllowed;
                if (reverseGate && !previousReverseGate) reverseGateRisingEdges++;
                previousReverseGate = reverseGate;
                reportedMaximumReverseTravel = Mathf.Max(reportedMaximumReverseTravel, fixture.Recovery.ReverseTravel);
                bool negativeCommand = command.reverseAllowed && command.throttle > -1f && command.throttle < 0f;
                bool negativeVelocity = Vector2.Dot(fixture.PoliceVelocity, fixture.PoliceForward) < 0f;
                if (!firstNegativeCommand && negativeCommand) {
                    firstNegativeCommand = true;
                    firstNegativeVelocity = negativeVelocity;
                    firstNegativeThrottle = command.throttle;
                    measuringTravel = true;
                    firstNegativeStart = fixture.LastControllerPosition;
                    previousPhysicalPosition = fixture.LastControllerPosition;
                }
                if (measuringTravel) {
                    measuredTravel += Vector2.Distance(previousPhysicalPosition, fixture.PolicePosition);
                    previousPhysicalPosition = fixture.PolicePosition;
                }
                gatesClosedAndStopped = firstNegativeCommand && !reverseGate && !fixture.MotorReversePermitted &&
                    fixture.PoliceVelocity.magnitude <= 0.00001f;
                string currentTrace = RecoveryTrace(fixture, step) + ", actualTravel=" + measuredTravel +
                    ", reportedReverseTravel=" + reportedMaximumReverseTravel + ", firstStart=" + firstNegativeStart;
                string traceKey = RecoveryTraceKey(fixture) + ", tinyStopped=" + gatesClosedAndStopped;
                if ((step < 6 || !traceKey.Equals(lastTraceKey)) && trace.Count < 24) trace.Add(currentTrace);
                lastTraceKey = traceKey;
            }
            string facts = Facts(fixture, "tiny travel stepBudget=" + stepBudget) +
                ", actualTravel=" + measuredTravel + ", reportedReverseTravel=" + reportedMaximumReverseTravel +
                ", firstNegativeThrottle=" + firstNegativeThrottle +
                ", reverseGateRisingEdges=" + reverseGateRisingEdges + ", trace:\n" + string.Join("\n", trace);
            Assert.IsTrue(firstNegativeCommand, facts);
            Assert.IsTrue(firstNegativeVelocity, facts);
            Assert.Less(Mathf.Abs(firstNegativeThrottle), 0.99f, facts);
            Assert.IsTrue(gatesClosedAndStopped, facts);
            Assert.AreEqual(1, reverseGateRisingEdges, facts);
            Assert.Greater(measuredTravel, 0f, facts);
            Assert.LessOrEqual(measuredTravel, fixture.ControllerSettings.recovery.maximumReverseTravel + 0.00001f, facts);
            Assert.LessOrEqual(reportedMaximumReverseTravel, fixture.ControllerSettings.recovery.maximumReverseTravel + 0.00001f, facts);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
            Assert.IsFalse(fixture.MotorReversePermitted, facts);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, facts);
            Assert.Greater(fixture.Player.CurrentHealth, 0f, facts);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, facts);
        }
    }

    /// <summary>A real rear wall inside the heavy reverse reserve denies reverse while recovery remains alive.</summary>
    static void RunRearBlockedRecovery() {
        TestContext.Progress.WriteLine("Recovery rear-blocked case");
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            fixture.CreateBlocker(new Vector2(0f, 7f), false, Vector2.one);
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 2f;
            targetBody.linearDamping = 1f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            bool sawRecovery = false;
            for (int step = 0; step < 500 && !fixture.Controller.IsRecoveryExhausted; step++) {
                fixture.Step(1);
                sawRecovery |= fixture.Controller.IsRecovering;
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            }
            Assert.IsTrue(sawRecovery, Facts(fixture));
            Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
            Assert.AreEqual(0f, fixture.Recovery.ReverseTravel, Facts(fixture));
            Assert.Greater(fixture.Player.CurrentHealth, 0f);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f);
        }
    }

    /// <summary>Real sustained heavy contact reaches episode exhaustion without a second Ram or reverse.</summary>
    static void RunSustainedContactExhaustion() {
        TestContext.Progress.WriteLine("Recovery sustained-contact exhaustion case");
        using (var fixture = new PursuitFixture(0.02f)) {
            fixture.ConfigureRamRoute();
            fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
            var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
            targetBody.mass = 1000f;
            targetBody.linearDamping = 10f;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture));
            var policeBody = fixture.PoliceReceiver.GetComponent<Rigidbody2D>();
            var policeCollider = fixture.PoliceReceiver.GetComponent<Collider2D>();
            var targetCollider = fixture.Player.GetComponent<Collider2D>();
            if (!policeCollider.IsTouching(targetCollider)) {
                var joint = policeBody.gameObject.AddComponent<FixedJoint2D>();
                joint.connectedBody = targetBody;
                joint.enableCollision = true;
                joint.autoConfigureConnectedAnchor = true;
                fixture.Step(2);
            }
            Assert.IsTrue(policeCollider.IsTouching(targetCollider), Facts(fixture));
            int ramStarts = fixture.Controller.RamStarts;
            for (int step = 0; step < 600 && !fixture.Controller.IsRecoveryExhausted; step++) {
                fixture.Step(1);
                Assert.IsTrue(policeCollider.IsTouching(targetCollider), Facts(fixture));
                Assert.AreEqual(ramStarts, fixture.Controller.RamStarts, Facts(fixture));
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
                Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            }
            Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
            Assert.AreEqual(1, fixture.Controller.RamStarts, Facts(fixture));
            Assert.IsFalse(fixture.MotorReversePermitted);
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed);
            Assert.IsTrue(policeCollider.IsTouching(targetCollider));
            Assert.Greater(fixture.Player.CurrentHealth, 0f);
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f);
        }
    }

    static string Facts(PursuitFixture fixture) {
        return "Recovery facts: phase=" + fixture.Controller.RecoveryPhase +
            ", recovering=" + fixture.Controller.IsRecovering + ", exhausted=" + fixture.Controller.IsRecoveryExhausted +
            ", ramStarts=" + fixture.Controller.RamStarts + ", reverseGate=" + fixture.MotorReversePermitted +
            ", commandReverse=" + fixture.Controller.LastCommand.reverseAllowed +
            ", reverseTravel=" + fixture.Recovery.ReverseTravel + ", session=" + fixture.World.SessionTime +
            ", policePosition=" + fixture.PolicePosition + ", policeVelocity=" + fixture.PoliceVelocity +
            ", playerPosition=" + fixture.PlayerPosition + ", playerHP=" + fixture.Player.CurrentHealth +
            ", policeHP=" + fixture.PoliceReceiver.CurrentHealth;
    }

    static string Facts(PursuitFixture fixture, string caseDetails) {
        return Facts(fixture) + ", " + caseDetails;
    }

    static int PostReleaseStepBudget(PursuitFixture fixture) {
        float seconds = 2f * fixture.ControllerSettings.ramCooldownSeconds +
            fixture.ControllerSettings.ramMaximumActiveSeconds + fixture.Binding.navigationSettings.refreshInterval + 1f;
        return Mathf.Max(1, Mathf.CeilToInt(seconds / fixture.StepDelta));
    }

    static string RecoveryTrace(PursuitFixture fixture, int step) {
        var result = fixture.Planner.CurrentResult;
        string anchor = fixture.Cursor.IsBound ? fixture.Cursor.CurrentAnchor.edgeId + "@" + fixture.Cursor.CurrentAnchor.distanceAlongEdge : "unbound";
        string resultDetails = result == null ? "null" : result.status + "/" + result.waitReason;
        var command = fixture.Controller.LastCommand;
        return "step=" + step + ", pos=" + fixture.PolicePosition + ", v=" + fixture.PoliceVelocity +
            ", cmd=" + command.throttle + "/" + command.brake + "/" + command.steering + "/" + command.targetSpeed + "/rev=" + command.reverseAllowed +
            ", phase=" + fixture.Controller.RecoveryPhase + ", cursor=" + fixture.Cursor.IsBound + "/" + fixture.Controller.CursorProgress +
            "/" + anchor + ", planner=" + fixture.Controller.PlannerAttemptCount + "/" + resultDetails +
            ", gate=" + fixture.Recovery.Gate + ", ramStarts=" + fixture.Controller.RamStarts + ", motorReverse=" + fixture.MotorReversePermitted +
            ", reverseTravel=" + fixture.Recovery.ReverseTravel + ", ramCooldownUntil=" + fixture.Controller.RamCooldownUntil +
            ", clock=" + fixture.World.SessionTime + ", target=" + fixture.PlayerPosition + ", aim=" + fixture.Controller.LastAimPoint;
    }

    static string RecoveryTraceKey(PursuitFixture fixture) {
        var command = fixture.Controller.LastCommand;
        return command.throttle + "/" + command.brake + "/" + command.steering + "/" + command.targetSpeed +
            "/rev=" + command.reverseAllowed + ", phase=" + fixture.Controller.RecoveryPhase +
            ", ramStarts=" + fixture.Controller.RamStarts + ", planner=" + fixture.Controller.PlannerAttemptCount;
    }
}
