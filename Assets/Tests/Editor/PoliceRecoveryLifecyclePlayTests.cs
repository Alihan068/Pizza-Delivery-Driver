using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Isolated Play-mode lifecycle negatives for the bounded police recovery integration.
/// Every episode is opened by a genuine physical collision; this bridge never injects
/// damage, callbacks, poses, or velocities.
/// </summary>
public sealed class PoliceRecoveryLifecyclePlayTests {
    /// <summary>Runs the bounded target, graph, pause, session, reuse, work, and disable cases.</summary>
    [UnityTest]
    public IEnumerator RecoveryLifecycleMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene,
            "The isolated recovery bridge must not launch a saved gameplay scene.");
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The recovery bridge must not initialize career services.");

        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=target-unbind-account");
        RunWithFixture(0.02f, TargetUnbind_AccountsAndExhausts);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=graph-rebuild-stale-evidence");
        RunWithFixture(0.02f, GraphRebuild_StopsAndRejectsStaleRelease);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=pause-freezes-accounting");
        RunWithFixture(0.03f, Pause_StopsWithoutAdvancingWorldThenResumesAccounting);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=session-end-closes-binding");
        RunWithFixture(0.02f, SessionEnd_ClosesActiveRecoveryBinding);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=pool-reuse-fresh-life");
        RunWithFixture(0.03f, PoolReuse_CreatesFreshRecoveryWithoutStaleEpisode);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=cursor-work-exhaustion");
        RunWithFixture(0.02f, CursorWorkExhaustion_StopsAndCannotRenewEpisode);
        TestContext.Progress.WriteLine("RECOVERY_LIFECYCLE case=real-component-disable");
        RunWithFixture(0.03f, ComponentDisable_ClosesBindingWithoutPhysicsStep);
    }

    /// <summary>Runs fresh-route cadence, replacement-target, and raw sensor saturation guards.</summary>
    [UnityTest]
    public IEnumerator RecoveryFreshSafetyMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene,
            "The isolated recovery bridge must not launch a saved gameplay scene.");
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance, "The recovery bridge must not initialize career services.");

        TestContext.Progress.WriteLine("RECOVERY_FRESH case=invalidated-route-cadence");
        RunWithFixture(0.02f, InvalidatedRoute_RespectsHardMinimumAndRefreshCadence);
        TestContext.Progress.WriteLine("RECOVERY_FRESH case=replaced-blocking-target");
        RunWithFixture(0.03f, ReplacedTarget_PreservesOriginalEpisodeAndCadence);
        TestContext.Progress.WriteLine("RECOVERY_FRESH case=raw-target-sensor-saturation");
        RunWithFixture(0.02f, SaturatedTargetColliders_StopWithoutReverseFallback);
    }

    /// <summary>Always restores Edit Mode after a failed or interrupted Play-mode bridge.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void TargetUnbind_AccountsAndExhausts(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        int attempts = fixture.Controller.PlannerAttemptCount;

        fixture.Player.Unbind();
        StepUntilExhausted(fixture);

        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
    }

    static void GraphRebuild_StopsAndRejectsStaleRelease(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        int attempts = fixture.Controller.PlannerAttemptCount;
        float deadline = fixture.Controller.RamCooldownUntil;

        fixture.InvalidateGraph();
        fixture.Step(1);

        Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.IsFalse(fixture.Recovery.ResumeReady, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));

        StepUntilExhausted(fixture);
        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.IsFalse(fixture.Recovery.ResumeReady, Facts(fixture));
    }

    static void Pause_StopsWithoutAdvancingWorldThenResumesAccounting(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        float clock = fixture.World.SessionTime;
        int attempts = fixture.Controller.PlannerAttemptCount;
        var phase = fixture.Controller.RecoveryPhase;

        fixture.Controller.Tick(fixture.StepDelta, true);

        Assert.AreEqual(clock, fixture.World.SessionTime, Facts(fixture));
        Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount, Facts(fixture));
        Assert.AreEqual(phase, fixture.Controller.RecoveryPhase, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));

        fixture.Step(1);
        Assert.Greater(fixture.World.SessionTime, clock, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
    }

    static void SessionEnd_ClosesActiveRecoveryBinding(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        fixture.Coordinator.NotifySessionEnded();

        Assert.IsFalse(fixture.Controller.IsBound, Facts(fixture));
        Assert.IsFalse(fixture.Controller.IsRecovering, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
    }

    static void PoolReuse_CreatesFreshRecoveryWithoutStaleEpisode(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        var oldRecovery = fixture.Recovery;
        int oldLife = fixture.PoliceReceiver.Identity.lifeId;
        int oldControllerSubscribers = CountControllerImpactSubscribers(fixture.PoliceReceiver);

        int returnedOldLife = fixture.RebindPoliceToFreshPoolLife();
        Assert.AreEqual(oldLife, returnedOldLife);
        Assert.Greater(fixture.PoliceReceiver.Identity.lifeId, oldLife, Facts(fixture));

        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
        int newControllerSubscribers = CountControllerImpactSubscribers(fixture.PoliceReceiver);
        Assert.AreNotSame(oldRecovery, fixture.Recovery, Facts(fixture));
        Assert.AreEqual(1, oldControllerSubscribers, Facts(fixture, "expected exactly one original controller subscriber"));
        Assert.AreEqual(1, newControllerSubscribers, Facts(fixture, "expected exactly one rebound controller subscriber"));
        Assert.IsFalse(fixture.Controller.IsRecovering, Facts(fixture));
        Assert.IsFalse(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, fixture.Controller.RecoveryPhase, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
    }

    static void CursorWorkExhaustion_StopsAndCannotRenewEpisode(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        int attempts = fixture.Controller.PlannerAttemptCount;
        int originalWork = fixture.ControllerSettings.cursorWork;
        fixture.SetRuntimeCursorWork(0);

        StepUntilExhausted(fixture);

        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));

        fixture.SetRuntimeCursorWork(originalWork);
        fixture.Step(1);
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
    }

    static void ComponentDisable_ClosesBindingWithoutPhysicsStep(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        Vector2 position = fixture.PolicePosition;
        Vector2 velocity = fixture.PoliceVelocity;
        Assert.Greater(velocity.magnitude, 0f, Facts(fixture, "real pre-disable momentum"));

        fixture.Controller.enabled = true;
        fixture.Controller.enabled = false;

        Assert.IsFalse(fixture.Controller.IsBound, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        Assert.AreEqual(position, fixture.PolicePosition, Facts(fixture));
        Assert.AreEqual(velocity, fixture.PoliceVelocity, Facts(fixture));
    }

    static void InvalidatedRoute_RespectsHardMinimumAndRefreshCadence(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        var planner = fixture.Planner;
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        int firstRouteAttempt = -1;
        float firstRouteClock = 0f;
        int firstRouteBudget = Mathf.CeilToInt(1.1f / fixture.StepDelta) + 4;
        for (int step = 0; step < firstRouteBudget && firstRouteAttempt < 0; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            int delta = planner.AttemptCount - before;
            Assert.LessOrEqual(delta, 1, Facts(fixture, "planner attempted more than once in one step"));
            if (delta > 0 && planner.CurrentResult != null &&
                planner.CurrentResult.status == PoliceRoadTargetQuery.Status.Route) {
                firstRouteAttempt = planner.AttemptCount;
                firstRouteClock = fixture.LastControllerClock;
            }
        }
        Assert.GreaterOrEqual(firstRouteAttempt, 0, Facts(fixture, "no first valid recovery route"));
        Assert.IsTrue(fixture.Controller.IsRecovering, Facts(fixture, "recovery must preempt future Ram cooldown"));
        Assert.Less(fixture.LastControllerClock, deadline, Facts(fixture, "recovery was not observed before Ram deadline"));

        int invalidationDelayBudget = Mathf.CeilToInt(Mathf.Max(0f, (firstRouteClock + 0.1f - fixture.World.SessionTime) / fixture.StepDelta)) + 2;
        for (int step = 0; step < invalidationDelayBudget && fixture.World.SessionTime - firstRouteClock < 0.1f; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
        }
        Assert.GreaterOrEqual(fixture.World.SessionTime - firstRouteClock, 0.1f, Facts(fixture, "bounded invalidation delay did not elapse"));
        planner.InvalidateRoute();
        int invalidatedAttempt = planner.AttemptCount;
        int preMinimumSteps = Mathf.CeilToInt(Mathf.Max(0f, (firstRouteClock + 0.25f - fixture.World.SessionTime) / fixture.StepDelta)) + 1;
        for (int step = 0; step < preMinimumSteps && fixture.LastControllerClock < firstRouteClock + 0.25f; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            if (fixture.LastControllerClock < firstRouteClock + 0.25f)
                Assert.AreEqual(invalidatedAttempt, planner.AttemptCount, Facts(fixture, "route query before hard minimum"));
        }
        int eligibleDeadline = Mathf.CeilToInt((firstRouteClock + 0.25f + 2f * fixture.StepDelta - fixture.World.SessionTime) / fixture.StepDelta) + 3;
        bool sawFreshRoute = false;
        float freshRouteClock = 0f;
        for (int step = 0; step < eligibleDeadline && !sawFreshRoute; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            if (planner.AttemptCount > invalidatedAttempt && IsRoute(planner.CurrentResult)) {
                sawFreshRoute = true;
                freshRouteClock = PlannerLastAttemptSeconds(planner);
            }
        }
        Assert.IsTrue(sawFreshRoute, Facts(fixture, "invalidated route did not refresh after hard minimum"));
        Assert.LessOrEqual(freshRouteClock, firstRouteClock + 0.25f + 2f * fixture.StepDelta,
            Facts(fixture, "invalidated route refreshed after bounded hard-minimum window"));
        Assert.AreEqual(invalidatedAttempt + 1, planner.AttemptCount, Facts(fixture));
        int refreshBaseAttempt = planner.AttemptCount;
        float refreshBaseClock = freshRouteClock;
        int refreshDelayBudget = Mathf.CeilToInt(Mathf.Max(0f, (refreshBaseClock + 0.75f - fixture.World.SessionTime) / fixture.StepDelta)) + 2;
        for (int step = 0; step < refreshDelayBudget && fixture.World.SessionTime - refreshBaseClock < 0.75f; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            if (fixture.LastControllerClock < refreshBaseClock + 0.75f)
                Assert.AreEqual(refreshBaseAttempt, planner.AttemptCount, Facts(fixture, "stable cached route refreshed too early"));
        }
        Assert.GreaterOrEqual(fixture.World.SessionTime - refreshBaseClock, 0.75f, Facts(fixture, "bounded refresh delay did not elapse"));
        int refreshBudget = Mathf.CeilToInt(2f * fixture.StepDelta / fixture.StepDelta) + 2;
        bool refreshed = false;
        for (int step = 0; step < refreshBudget && !refreshed; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            refreshed = planner.AttemptCount > refreshBaseAttempt;
        }
        Assert.IsTrue(refreshed, Facts(fixture, "stable cached route did not refresh by refresh interval"));
        Assert.AreEqual(refreshBaseAttempt + 1, planner.AttemptCount, Facts(fixture));
        Assert.LessOrEqual(PlannerLastAttemptSeconds(planner), refreshBaseClock + 0.75f + 2f * fixture.StepDelta, Facts(fixture));
        Assert.AreSame(planner, fixture.Planner, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
    }

    static void ReplacedTarget_PreservesOriginalEpisodeAndCadence(PursuitFixture fixture) {
        float originalImpactClock = EstablishHeavyRecovery(fixture);
        var planner = fixture.Planner;
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        float firstRecoveryRouteClock = 0f;
        bool firstRecoveryRouteObserved = false;
        int routeWaitBudget = Mathf.CeilToInt(1.4f / fixture.StepDelta) + 4;
        for (int step = 0; step < routeWaitBudget && fixture.World.SessionTime - originalImpactClock < 1f; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            if (planner.AttemptCount > before && IsRoute(planner.CurrentResult)) {
                firstRecoveryRouteObserved = true;
                firstRecoveryRouteClock = fixture.LastControllerClock;
            }
        }
        Assert.GreaterOrEqual(fixture.World.SessionTime - originalImpactClock, 1f,
            Facts(fixture, "replacement was not delayed into the original episode"));
        Assert.IsTrue(firstRecoveryRouteObserved, Facts(fixture, "no valid recovery route clock was observed"));

        float lastAttemptClock = PlannerLastAttemptSeconds(planner);
        Assert.AreEqual(firstRecoveryRouteClock, lastAttemptClock,
            Facts(fixture, "readonly planner clock did not match the observed route-attempt tick"));
        Vector2 oldTargetPosition = fixture.PlayerPosition;
        var replacement = fixture.ReplacePlayer(oldTargetPosition);
        var replacementBody = replacement.GetComponent<Rigidbody2D>();
        replacementBody.mass = 1000f;
        replacementBody.linearDamping = 10f;
        Assert.IsTrue(fixture.Controller.TrySetTarget(replacement, out string reason), reason);
        Assert.LessOrEqual(fixture.ColliderGap, fixture.ControllerSettings.acquisitionTolerance + 2f,
            Facts(fixture, "replacement target must remain physically blocking at the old position"));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));

        int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta) + 2;
        Assert.LessOrEqual(budget * fixture.StepDelta, 10f, Facts(fixture, "bounded replacement-target budget"));
        for (int step = 0; step < budget && !fixture.Controller.IsRecoveryExhausted; step++) {
            int before = planner.AttemptCount;
            fixture.Step(1);
            Assert.LessOrEqual(planner.AttemptCount - before, 1, Facts(fixture));
            if (fixture.LastControllerClock < lastAttemptClock + 0.25f)
                Assert.AreEqual(before, planner.AttemptCount, Facts(fixture, "replacement target queried before hard minimum"));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        }

        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.LessOrEqual(fixture.World.SessionTime - originalImpactClock,
            fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
        Assert.AreSame(planner, fixture.Planner, Facts(fixture));
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
        Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
    }

    static void SaturatedTargetColliders_StopWithoutReverseFallback(PursuitFixture fixture) {
        EstablishHeavyRecovery(fixture);
        int starts = fixture.Controller.RamStarts;
        float deadline = fixture.Controller.RamCooldownUntil;
        int colliderCount = fixture.ControllerSettings.sensorBuffer;
        var source = fixture.Player.GetComponent<BoxCollider2D>();
        for (int index = 0; index < colliderCount; index++) {
            var duplicate = fixture.Player.gameObject.AddComponent<BoxCollider2D>();
            duplicate.size = source.size;
            duplicate.offset = source.offset;
        }

        var rawStatus = fixture.Sensor.QuerySweep(fixture.PoliceForward, fixture.ControllerSettings.maxSweepDistance,
            out _, out _);
        bool saturated = rawStatus == VehicleObstacleSensor.SweepStatus.Saturated;
        Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Saturated, rawStatus,
            Facts(fixture, "raw ordinary sensor did not saturate on exact target-body collider buffer"));
        int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta) + 2;
        Assert.LessOrEqual(budget * fixture.StepDelta, 10f, Facts(fixture, "bounded saturation budget"));
        for (int step = 0; step < budget && !fixture.Controller.IsRecoveryExhausted; step++) {
            fixture.Step(1);
            saturated |= fixture.Sensor.LastQuerySaturated;
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture));
            Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, Facts(fixture));
            Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture));
        }
        Assert.IsTrue(saturated, Facts(fixture, "ordinary sensor never reported raw target saturation"));
        Assert.IsTrue(fixture.Controller.IsRecoveryExhausted, Facts(fixture));
        Assert.AreEqual(starts, fixture.Controller.RamStarts, Facts(fixture));
        Assert.AreEqual(deadline, fixture.Controller.RamCooldownUntil, Facts(fixture));
    }

    static float EstablishHeavyRecovery(PursuitFixture fixture) {
        fixture.ConfigureRamRoute();
        fixture.ControllerSettings.recovery.heavyImpactSpeed = 2.5f;
        var targetBody = fixture.Player.GetComponent<Rigidbody2D>();
        targetBody.mass = 1000f;
        targetBody.linearDamping = 10f;
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);

        bool impactObserved = false;
        fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
        int contactBudget = Mathf.CeilToInt(3f / fixture.StepDelta);
        for (int step = 0; step < contactBudget && !impactObserved; step++) fixture.Step(1);
        Assert.IsTrue(impactObserved, Facts(fixture, "no real ImpactObserved callback within contact budget"));
        Assert.IsTrue(fixture.Controller.IsRecovering, Facts(fixture));
        Assert.Greater(fixture.Controller.RamStarts, 0, Facts(fixture));
        Assert.Greater(fixture.Player.CurrentHealth, 0f, Facts(fixture));
        Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, Facts(fixture));
        return fixture.World.SessionTime;
    }

    static void StepUntilExhausted(PursuitFixture fixture) {
        int budget = Mathf.CeilToInt((fixture.ControllerSettings.recovery.maximumActiveSeconds + 2f * fixture.StepDelta) / fixture.StepDelta) + 2;
        Assert.LessOrEqual(budget * fixture.StepDelta, 10f, Facts(fixture, "bounded recovery budget"));
        for (int step = 0; step < budget && !fixture.Controller.IsRecoveryExhausted; step++) fixture.Step(1);
    }

    static void RunWithFixture(float deltaTime, Action<PursuitFixture> scenario) {
        PursuitFixture fixture = null;
        try {
            fixture = new PursuitFixture(deltaTime);
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

    static string Facts(PursuitFixture fixture) {
        return Facts(fixture, null);
    }

    static string Facts(PursuitFixture fixture, string detail) {
        var command = fixture.Controller.LastCommand;
        float reverseTravel = fixture.Recovery != null ? fixture.Recovery.ReverseTravel : 0f;
        return "case=" + (detail ?? "lifecycle") +
            ", phase=" + fixture.Controller.RecoveryPhase +
            ", recovering=" + fixture.Controller.IsRecovering +
            ", exhausted=" + fixture.Controller.IsRecoveryExhausted +
            ", bound=" + fixture.Controller.IsBound +
            ", plannerAttempts=" + fixture.Controller.PlannerAttemptCount +
            ", ramStarts=" + fixture.Controller.RamStarts +
            ", ramDeadline=" + fixture.Controller.RamCooldownUntil +
            ", reverseGate=" + fixture.MotorReversePermitted +
            ", command=" + command.throttle + "/" + command.brake + "/rev=" + command.reverseAllowed +
            ", reverseTravel=" + reverseTravel +
            ", clock=" + fixture.World.SessionTime +
            ", policePosition=" + fixture.PolicePosition +
            ", policeVelocity=" + fixture.PoliceVelocity +
            ", playerHealth=" + fixture.Player.CurrentHealth +
            ", policeHealth=" + fixture.PoliceReceiver.CurrentHealth;
    }

    static int CountControllerImpactSubscribers(VehicleDamageReceiver receiver) {
        var field = typeof(VehicleDamageReceiver).GetField("ImpactObserved",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) return -1;
        var callbacks = field.GetValue(receiver) as Delegate;
        if (callbacks == null) return 0;
        int count = 0;
        foreach (var callback in callbacks.GetInvocationList())
            if (callback.Method.DeclaringType == typeof(PolicePursuitController)) count++;
        return count;
    }

    static bool IsRoute(PoliceRoadTargetQuery.Result result) {
        return result != null && result.status == PoliceRoadTargetQuery.Status.Route;
    }

    static float PlannerLastAttemptSeconds(PolicePursuitTargetPlanner planner) {
        var field = typeof(PolicePursuitTargetPlanner).GetField("lastAttemptSeconds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Planner last-attempt clock is required for readonly cadence diagnostics.");
        return (float)field.GetValue(planner);
    }
}
