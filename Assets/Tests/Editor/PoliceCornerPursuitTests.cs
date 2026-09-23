using NUnit.Framework;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Isolated-physics acceptance for one bounded certified turn and its independent safety gates.</summary>
public sealed class PoliceCornerPursuitTests {
    /// <summary>Accepts measured derived transform roundoff while rejecting sign, zero, nonfinite, and large deviations.</summary>
    [Test]
    public void DerivedGeometryMatches_UsesBoundedRoundoffOnly() {
        MethodInfo method = typeof(PolicePursuitController).GetMethod("DerivedGeometryMatches",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(float), typeof(float) }, null);
        Assert.IsNotNull(method);

        bool Matches(float actual, float expected) => (bool)method.Invoke(null, new object[] { actual, expected });

        Assert.IsTrue(Matches(0.9000004f, 0.9f));
        Assert.IsTrue(Matches(1.700001f, 1.7f));
        Assert.IsFalse(Matches(-0.9f, 0.9f));
        Assert.IsTrue(Matches(0f, 0f));
        Assert.IsFalse(Matches(0.000001f, 0f));
        Assert.IsFalse(Matches(float.NaN, 0.9f));
        Assert.IsFalse(Matches(float.PositiveInfinity, 0.9f));
        Assert.IsFalse(Matches(0.901f, 0.9f));
    }

    /// <summary>Physically exits both bend representations, outlives refresh, and resumes only after a fresh default-cadence query.</summary>
    [TestCase(0.02f, false, true)]
    [TestCase(0.02f, true, false)]
    [TestCase(0.03f, false, false)]
    [TestCase(0.03f, true, true)]
    public void CertifiedCorner_ExitsAndResumesAfterFreshPlan(float deltaTime, bool reverseCreationOrder, bool rightSeam) {
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureCornerRoute(rightSeam, !rightSeam);
            fixture.CaptureBindingDiagnostics = true;
            Bind(fixture);
            Assert.AreEqual(0.75f, fixture.Binding.navigationSettings.refreshInterval);
            float startGap = fixture.Gap;
            float commitClock = 0f, exitClock = 0f, cap = 0f, arc = 0f, exitProgress = 0f;
            int commitAttempts = -1;
            bool committed = false, exited = false, resumed = false, rejectedFreshStart = false, postVertexMotion = false;
            for (int i = 0; i < 700; i++) {
                fixture.Step(1);
                if (fixture.Controller.IsTurnCommitted) {
                    Assert.IsFalse(exited, "a completed certificate cannot be relatched");
                    if (!committed) {
                        committed = true;
                        commitClock = fixture.LastControllerClock;
                        commitAttempts = fixture.Controller.PlannerAttemptCount;
                        cap = fixture.Controller.ActiveTurnCap;
                        arc = fixture.Turn.AbsoluteArc;
                        Assert.GreaterOrEqual(arc - fixture.Controller.CursorProgress, fixture.Profile.motorSettings.minimumTurningRadius);
                    }
                    Assert.AreEqual(commitAttempts, fixture.Controller.PlannerAttemptCount);
                    Assert.AreEqual(cap, fixture.Controller.ActiveTurnCap);
                    Assert.LessOrEqual(fixture.Controller.LastPlannedSpeed, cap);
                    float remainder = arc - fixture.Controller.CursorProgress;
                    if (!rejectedFreshStart && remainder > 0f && remainder < fixture.Profile.motorSettings.minimumTurningRadius * 0.8f) {
                        AssertFreshTurnRejected(fixture, rightSeam);
                        rejectedFreshStart = true;
                    }
                    if (remainder < 0f && fixture.Controller.LastCommand.throttle > 0f) postVertexMotion = true;
                } else if (committed && !exited) {
                    Assert.IsFalse(fixture.Controller.IsTurnExpired, "stopping at an expired mid-turn pose is not an exit");
                    exited = true;
                    exitClock = fixture.LastControllerClock;
                    exitProgress = fixture.Controller.CursorProgress;
                    Assert.GreaterOrEqual(exitProgress - arc, fixture.ControllerSettings.turnExitDistance,
                        fixture.FirstBindingLossDiagnostic ?? fixture.LastBindingDiagnostic);
                    Assert.Greater(rightSeam ? fixture.LastControllerPosition.x : -fixture.LastControllerPosition.x,
                        fixture.ControllerSettings.turnExitDistance);
                    Assert.LessOrEqual(Vector2.Angle(fixture.LastControllerForward, rightSeam ? Vector2.right : Vector2.left),
                        fixture.ControllerSettings.turnExitAlignmentDegrees);
                    Assert.AreEqual(commitAttempts, fixture.Controller.PlannerAttemptCount, "exit invalidates once and waits this tick");
                    Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
                    Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
                } else if (exited && fixture.Controller.LastCommand.throttle > 0f) {
                    Assert.Greater(fixture.Controller.PlannerAttemptCount, commitAttempts, "motion must follow a fresh plan");
                    resumed = true;
                }
            }
            Assert.IsTrue(committed); Assert.IsTrue(exited); Assert.IsTrue(resumed);
            Assert.IsTrue(rejectedFreshStart, "the physical turn must cross the original fresh-start tangent guard");
            Assert.IsTrue(postVertexMotion, "the retained cap must allow actual post-vertex propulsion");
            Assert.Greater(exitClock - commitClock, fixture.Binding.navigationSettings.refreshInterval);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted); Assert.IsFalse(fixture.Controller.IsTurnExpired);
            Assert.Greater(fixture.Controller.CursorProgress, exitProgress + 1f);
            Assert.Greater(rightSeam ? fixture.PolicePosition.x : -fixture.PolicePosition.x, fixture.ControllerSettings.turnExitDistance + 2f);
            Assert.Less(fixture.Gap, startGap - 4f);
            Assert.Less(Vector2.Angle(fixture.PoliceForward, rightSeam ? Vector2.right : Vector2.left), fixture.ControllerSettings.turnExitAlignmentDegrees);
            Assert.Greater(fixture.Controller.ProvenRoadContinuations, 0);
            Assert.Greater(fixture.Controller.ConnectorMetadataContinuations, 0, "displaced outgoing refresh must preserve its tracked suffix");
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.15f);
            Assert.Greater(fixture.ColliderGap, fixture.ControllerSettings.stopGap - 0.15f);
            AssertHealthy(fixture);
        }
    }

    /// <summary>Real motor-driven rotation retains the captured life despite derived scale rounding, including a nonzero scaled collider offset.</summary>
    [TestCase(0.02f, false, true)]
    [TestCase(0.03f, true, false)]
    public void RotationRoundoff_PreservesBindingAndRawGeometry(float deltaTime, bool reverseCreationOrder, bool rightSeam) {
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureCornerRoute(rightSeam, !rightSeam);
            fixture.Binding.body.MainCollider.offset = new Vector2(0.25f, -0.125f);
            fixture.CaptureBindingDiagnostics = true;
            Vector3 localScale = fixture.Binding.body.transform.localScale;
            Vector2 size = fixture.Binding.body.MainCollider.size;
            Vector2 offset = fixture.Binding.body.MainCollider.offset;
            Bind(fixture);
            bool observedRoundoff = false;
            for (int i = 0; i < 600 && Vector2.Angle(Vector2.up, fixture.PoliceForward) < 30f; i++) {
                fixture.Step(1);
                Assert.IsTrue(fixture.Controller.IsBound, fixture.FirstBindingLossDiagnostic ?? fixture.LastBindingDiagnostic);
                Vector3 derivedScale = fixture.Binding.body.transform.lossyScale;
                if (derivedScale.x != localScale.x || derivedScale.y != localScale.y) observedRoundoff = true;
            }
            Assert.GreaterOrEqual(Vector2.Angle(Vector2.up, fixture.PoliceForward), 30f, "the production motor must actually rotate the body");
            Assert.IsTrue(observedRoundoff, "the fixture must exercise derived rounding, not merely leave the body stationary");
            Assert.IsTrue(fixture.Controller.IsBound);
            Assert.AreEqual(localScale, fixture.Binding.body.transform.localScale);
            Assert.AreEqual(size, fixture.Binding.body.MainCollider.size);
            Assert.AreEqual(offset, fixture.Binding.body.MainCollider.offset);
            AssertHealthy(fixture);
        }
    }

    /// <summary>Exact raw guards reject applied one-ULP scale edits and larger geometry changes; each write must change actual readback first.</summary>
    [TestCase("scaleX", 1.0000001192092896f)]
    [TestCase("scaleZ", 1.0000001192092896f)]
    [TestCase("scaleX", 1.01f)]
    [TestCase("scaleZ", 1.01f)]
    [TestCase("size", 1.01f)]
    [TestCase("offset", 1.01f)]
    public void RawGeometryMutation_RejectsEvenWithinDerivedTolerance(string field, float factor) {
        using (var fixture = Corner()) {
            fixture.Binding.body.MainCollider.offset = new Vector2(0.25f, -0.125f);
            fixture.CaptureBindingDiagnostics = true;
            Commit(fixture);
            for (int i = 0; i < 100 && Vector2.Angle(Vector2.up, fixture.PoliceForward) < 5f; i++) {
                fixture.Step(1);
                Assert.IsTrue(fixture.Controller.IsBound, fixture.FirstBindingLossDiagnostic ?? fixture.LastBindingDiagnostic);
            }
            Assert.GreaterOrEqual(Vector2.Angle(Vector2.up, fixture.PoliceForward), 5f);
            var body = fixture.Binding.body;
            if (field == "scaleX" || field == "scaleZ") {
                Vector3 value = body.transform.localScale;
                float previous = field == "scaleX" ? value.x : value.z;
                if (field == "scaleX") value.x *= factor;
                else value.z *= factor;
                Assert.AreNotEqual(field == "scaleX" ? body.transform.localScale.x : body.transform.localScale.z,
                    field == "scaleX" ? value.x : value.z);
                body.transform.localScale = value;
                Assert.AreNotEqual(previous, field == "scaleX" ? body.transform.localScale.x : body.transform.localScale.z,
                    "the scale setter must actually apply the mutation before rejection is expected");
            } else if (field == "size") {
                Vector2 value = body.MainCollider.size;
                float previous = value.x;
                value.x *= factor;
                Assert.AreNotEqual(body.MainCollider.size.x, value.x);
                body.MainCollider.size = value;
                Assert.AreNotEqual(previous, body.MainCollider.size.x,
                    "the collider size setter must actually apply the mutation before rejection is expected");
            } else {
                Vector2 value = body.MainCollider.offset;
                float previous = value.x;
                value.x *= factor;
                Assert.AreNotEqual(body.MainCollider.offset.x, value.x);
                body.MainCollider.offset = value;
                Assert.AreNotEqual(previous, body.MainCollider.offset.x,
                    "the collider offset setter must actually apply the mutation before rejection is expected");
            }
            fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsBound, fixture.LastBindingDiagnostic);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            AssertStopped(fixture);
        }
    }

    /// <summary>Unity's ignored one-ULP collider writes leave actual geometry unchanged, so they must preserve the active binding.</summary>
    [TestCase("size")]
    [TestCase("offset")]
    public void IgnoredTinyColliderWrite_PreservesActualGeometryAndBinding(string field) {
        using (var fixture = Corner()) {
            fixture.Binding.body.MainCollider.offset = new Vector2(0.25f, -0.125f);
            fixture.CaptureBindingDiagnostics = true;
            Commit(fixture);
            for (int i = 0; i < 100 && Vector2.Angle(Vector2.up, fixture.PoliceForward) < 5f; i++) {
                fixture.Step(1);
                Assert.IsTrue(fixture.Controller.IsBound, fixture.FirstBindingLossDiagnostic ?? fixture.LastBindingDiagnostic);
            }
            Assert.GreaterOrEqual(Vector2.Angle(Vector2.up, fixture.PoliceForward), 5f);
            var collider = fixture.Binding.body.MainCollider;
            Vector2 before = field == "size" ? collider.size : collider.offset;
            Vector2 requested = before;
            requested.x *= 1.0000001192092896f;
            Assert.AreNotEqual(before.x, requested.x, "the requested value must differ even when the native setter ignores it");
            if (field == "size") collider.size = requested;
            else collider.offset = requested;
            Vector2 actual = field == "size" ? collider.size : collider.offset;
            Assert.AreEqual(before.x, actual.x, "this case covers a confirmed ignored write, not an applied mutation");
            Assert.AreEqual(before.y, actual.y);
            fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsBound, fixture.FirstBindingLossDiagnostic ?? fixture.LastBindingDiagnostic);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            actual = field == "size" ? collider.size : collider.offset;
            Assert.AreEqual(before.x, actual.x);
            Assert.AreEqual(before.y, actual.y);
        }
    }

    /// <summary>A fresh obstacle outside the body-to-anchor sweep still blocks the full maneuver; removal can resume within original limits.</summary>
    [Test]
    public void FreshManeuverBlocker_StopsThenResumesSameCertificate() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            int token = fixture.Turn.Token;
            var blocker = fixture.CreateBlocker(new Vector2(3f, 8f), false, Vector2.one * 0.5f);
            AssertBodyToAnchorClear(fixture);
            fixture.Step(1);
            AssertStopped(fixture); Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            blocker.SetActive(false);
            fixture.Step(1);
            Assert.AreEqual(token, fixture.Turn.Token);
            Assert.Greater(fixture.Controller.LastPlannedSpeed, 0f);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
        }
    }

    /// <summary>Static blockage and sensor saturation each keep spending the original clock until expiry, even across pause and obstacle removal.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void BlockedTurn_ExpiresWithoutAutomaticRenewal(bool saturateSensor) {
        using (var fixture = Corner()) {
            if (saturateSensor) fixture.ControllerSettings.sensorBuffer = 1;
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            float clock = fixture.LastControllerClock;
            float travel = fixture.Turn.MeasuredTravel;
            var blocker = saturateSensor
                ? fixture.CreateBlocker(fixture.PolicePosition + fixture.PoliceForward * 3f, true, Vector2.one)
                : fixture.CreateBlocker(new Vector2(3f, 8f), false, Vector2.one * 0.5f);
            fixture.Controller.Tick(fixture.StepDelta, true);
            Assert.AreEqual(travel, fixture.Turn.MeasuredTravel);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            fixture.Step(1);
            AssertStopped(fixture);
            if (saturateSensor) Assert.IsTrue(fixture.Sensor.LastQuerySaturated);
            int steps = Mathf.CeilToInt(fixture.ControllerSettings.maximumTurnActiveSeconds / fixture.StepDelta) + 2;
            fixture.Step(steps);
            Assert.Greater(fixture.World.SessionTime - clock, fixture.ControllerSettings.maximumTurnActiveSeconds);
            Assert.IsTrue(fixture.Controller.IsTurnExpired); Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            Assert.Greater(fixture.Turn.MeasuredTravel, travel, "braking displacement is still charged while blocked");
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            blocker.SetActive(false);
            fixture.Step(steps);
            Assert.IsTrue(fixture.Controller.IsTurnExpired); AssertStopped(fixture);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            AssertHealthy(fixture);
        }
    }

    /// <summary>A real moving-body collider is detected afresh during deferral and stopped short using the force-limited envelope.</summary>
    [TestCase(0.02f, false)]
    [TestCase(0.03f, true)]
    public void DynamicBlocker_BrakesDuringDeferral(float deltaTime, bool reverseCreationOrder) {
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            fixture.ConfigureCornerRoute(true, false);
            fixture.Profile.motorSettings.maxBrakeForce = 4f;
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            for (int i = 0; i < 100 && fixture.ForwardSpeed > 2.1f; i++) fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            Assert.LessOrEqual(fixture.ForwardSpeed, 2.1f);
            var blocker = fixture.CreateBlocker(fixture.PolicePosition + fixture.PoliceForward * 2.8f, true,
                new Vector2(4f, 1f), MapNavigationCoordinates.DirectionToHeadingDegrees(fixture.PoliceForward));
            fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            fixture.Step(50);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            Assert.Less(Mathf.Abs(fixture.ForwardSpeed), fixture.ControllerSettings.stoppedSpeedThreshold);
            Assert.Greater(fixture.Binding.body.MainCollider.Distance(blocker.GetComponent<Collider2D>()).distance, 0f);
            AssertHealthy(fixture);
        }
    }

    /// <summary>Budget exhaustion during strategic deferral stops motion and cannot suspend or restart active-turn expiry.</summary>
    [Test]
    public void DeferredWorkExhaustion_StopsAndExpiresOriginalTurn() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            fixture.SetRuntimeCursorWork(1);
            fixture.Step(1); AssertStopped(fixture);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            fixture.Step(Mathf.CeilToInt(fixture.ControllerSettings.maximumTurnActiveSeconds / fixture.StepDelta) + 2);
            Assert.IsTrue(fixture.Controller.IsTurnExpired);
            fixture.SetRuntimeCursorWork(fixture.ControllerSettings.cursorWork);
            fixture.Step(30);
            Assert.IsTrue(fixture.Controller.IsTurnExpired); AssertStopped(fixture);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
        }
    }

    /// <summary>A graph revision immediately revokes the held certificate, then permits normal cadence-controlled queries again.</summary>
    [Test]
    public void GraphRevision_CancelsCommitAndUnblocksPlanner() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            fixture.Binding.graph.Rebuild();
            fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted); AssertStopped(fixture);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            fixture.Step(Mathf.CeilToInt(fixture.Binding.navigationSettings.hardMinimumInterval / fixture.StepDelta) + 1);
            Assert.Greater(fixture.Controller.PlannerAttemptCount, attempts);
        }
    }

    /// <summary>Target position is validated against copied bounds while planning is deferred, even if the caller expands its mutable document.</summary>
    [Test]
    public void TargetLeavesCapturedBounds_CancelsBeforeDeferredQuery() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            fixture.Binding.navigation.localBounds = new Rect(-1000f, -1000f, 2000f, 2000f);
            fixture.PushPlayer(Vector2.right * 1000f);
            for (int i = 0; i < 30 && fixture.PlayerPosition.x <= 24f; i++) fixture.Step(1);
            Assert.Greater(fixture.PlayerPosition.x, 24f);
            fixture.Step(1);
            AssertStopped(fixture); Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
        }
    }

    /// <summary>Clearance uses the same immutable map bounds as the planner, not later authoring edits.</summary>
    [Test]
    public void MutableNavigationBounds_DoNotChangeCommittedClearance() {
        using (var fixture = Corner()) {
            Commit(fixture);
            fixture.Binding.navigation.localBounds = new Rect(100f, 100f, 1f, 1f);
            fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            Assert.Greater(fixture.Controller.LastPlannedSpeed, 0f);
        }
    }

    /// <summary>Clock regression and non-finite active time close the binding without depending on strategic planner calls.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void InvalidActiveClock_ClosesDeferredBinding(bool nonFinite) {
        using (var fixture = Corner()) {
            Commit(fixture);
            fixture.SetActiveClock(nonFinite ? float.NaN : fixture.LastControllerClock - fixture.StepDelta);
            fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsBound); AssertStopped(fixture);
        }
    }

    /// <summary>Nonzero gravity fails authoring immediately and a later mutation revokes the bound life before it can fall off-map.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void GravityInvariant_RejectsBindOrRuntimeMutation(bool afterBind) {
        using (var fixture = Corner()) {
            if (afterBind) Commit(fixture);
            fixture.SetPoliceGravity(1f);
            if (afterBind) fixture.Controller.Tick(fixture.StepDelta, false);
            else Assert.IsFalse(fixture.Controller.TryBind(fixture.Binding, out _));
            Assert.IsFalse(fixture.Controller.IsBound); AssertStopped(fixture);
            Assert.That(fixture.PolicePosition.y, Is.InRange(0f, 8f));
        }
    }

    /// <summary>A failed post-exit query keeps the release stopped and cannot reuse the previous strategic result.</summary>
    [Test]
    public void ExitQueryBudgetFailure_CannotReuseOldMotion() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            fixture.SetRuntimeQueryWork(1);
            for (int i = 0; i < 600 && fixture.Controller.IsTurnCommitted; i++) fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted); Assert.IsFalse(fixture.Controller.IsTurnExpired);
            AssertStopped(fixture); Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            fixture.Step(1);
            Assert.AreEqual(attempts + 1, fixture.Controller.PlannerAttemptCount);
            Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, fixture.Planner.CurrentResult.status);
            AssertStopped(fixture);
            fixture.Step(4); AssertStopped(fixture);
            Assert.AreEqual(attempts + 1, fixture.Controller.PlannerAttemptCount, "failed queries still respect the hard minimum");
        }
    }

    /// <summary>Spent suffix-clearance work and newly blocked connector clearance cannot fall through to new-route acquisition after exit.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void ExitContinuationFailure_CannotRebindThroughFailedSafety(bool exhaustedWork) {
        using (var fixture = Corner()) {
            var recording = new RecordingClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = recording;
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            for (int i = 0; i < 600 && fixture.Controller.IsTurnCommitted; i++) fixture.Step(1);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted); Assert.IsFalse(fixture.Controller.IsTurnExpired);
            AssertStopped(fixture);
            if (exhaustedWork) fixture.SetRuntimeCursorWork(4);
            else recording.blocked = true;
            fixture.Step(1);
            Assert.AreEqual(attempts + 1, fixture.Controller.PlannerAttemptCount);
            AssertStopped(fixture);
            for (int i = 0; i < 30; i++) {
                fixture.Step(1);
                AssertStopped(fixture);
                Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            }
        }
    }

    /// <summary>A physically moved same-life target does not redirect a committed turn; its changed suffix is processed safely after exit.</summary>
    [Test]
    public void MovingTarget_ChangedSuffixWaitsAfterExit() {
        using (var fixture = Corner()) {
            Commit(fixture);
            int attempts = fixture.Controller.PlannerAttemptCount;
            fixture.PushPlayer(Vector2.right * 0.5f);
            for (int i = 0; i < 600 && fixture.Controller.IsTurnCommitted; i++) {
                fixture.Step(1);
                Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            }
            Assert.IsFalse(fixture.Controller.IsTurnCommitted); Assert.IsFalse(fixture.Controller.IsTurnExpired);
            Assert.Greater(fixture.PlayerPosition.x, 12.5f);
            AssertStopped(fixture);
            fixture.Step(1);
            Assert.AreEqual(attempts + 1, fixture.Controller.PlannerAttemptCount);
            AssertStopped(fixture);
        }
    }

    /// <summary>Both fresh envelopes spend the same finite work allowance; the step box includes full discrete acceleration.</summary>
    [TestCase(0.02f)]
    [TestCase(0.03f)]
    public void StaticEnvelopes_ChargeSharedWorkAndBoundDiscreteAcceleration(float deltaTime) {
        using (var fixture = new PursuitFixture(deltaTime)) {
            fixture.ConfigureCornerRoute(true, false);
            var recording = new RecordingClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = recording;
            Commit(fixture);
            recording.sizes.Clear();
            fixture.SetRuntimeCursorWork(10);
            fixture.Step(1);
            Assert.IsTrue(fixture.Controller.IsTurnCommitted);
            AssertStopped(fixture);
            Assert.AreEqual(1, recording.sizes.Count, "the maneuver spends the final work unit; the one-step query cannot run for free");
            fixture.SetRuntimeCursorWork(fixture.ControllerSettings.cursorWork);
            recording.sizes.Clear();
            fixture.Step(1);
            Assert.AreEqual(2, recording.sizes.Count, "both envelopes must be fresh on a moving committed tick");
            float acceleration = Mathf.Max(
                Mathf.Min(fixture.Profile.motorSettings.acceleration, fixture.Profile.motorSettings.maxEngineForce / fixture.Binding.body.Body.mass),
                Mathf.Min(fixture.Profile.motorSettings.brakeDeceleration, fixture.Profile.motorSettings.maxBrakeForce / fixture.Binding.body.Body.mass));
            float radius = (fixture.LastControllerVelocity.magnitude + acceleration * deltaTime) * deltaTime;
            float expectedSize = 2f * radius + (fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin).magnitude;
            Assert.AreEqual(expectedSize, recording.sizes[1].x, 0.00001f);
            Assert.AreEqual(expectedSize, recording.sizes[1].y, 0.00001f);
        }
    }

    /// <summary>A maneuver blocker present before the first turn command cannot be bypassed by planning or rebinding.</summary>
    [Test]
    public void BlockedBeforeCommit_NeverAcquiresTurnThroughFallback() {
        using (var fixture = Corner()) {
            fixture.CreateBlocker(new Vector2(3f, 8f), false, Vector2.one * 0.5f);
            Bind(fixture);
            for (int i = 0; i < 300; i++) {
                fixture.Step(1);
                Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            }
            AssertStopped(fixture);
            Assert.Less(Mathf.Abs(fixture.ForwardSpeed), fixture.ControllerSettings.stoppedSpeedThreshold);
            AssertHealthy(fixture);
        }
    }

    /// <summary>A deliberately late lookahead cannot commit an old certificate after its fresh-start tangent allowance has been consumed.</summary>
    [Test]
    public void InsufficientIncomingTangent_RejectsLateCommit() {
        using (var fixture = Corner()) {
            fixture.ControllerSettings.lookaheadMin = fixture.ControllerSettings.lookaheadMax = 0.5f;
            // Keep the original certificate available to isolate the controller's own fresh-start guard.
            fixture.Binding.navigationSettings.refreshInterval = 20f;
            Bind(fixture);
            for (int i = 0; i < 300; i++) {
                fixture.Step(1);
                Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            }
            Assert.Greater(fixture.Controller.CursorProgress, 7f);
            AssertStopped(fixture);
            AssertHealthy(fixture);
        }
    }

    sealed class RecordingClearance : IAreaClearanceQuery {
        readonly IAreaClearanceQuery inner;
        internal bool blocked;
        internal readonly List<Vector2> sizes = new List<Vector2>();
        internal RecordingClearance(IAreaClearanceQuery inner) { this.inner = inner; }
        /// <summary>Records each actual safety query while preserving the isolated scene's collision answer.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            sizes.Add(footprint);
            return !blocked && inner.IsAreaClear(center, footprint, headingDegrees);
        }
    }

    static PursuitFixture Corner() {
        var fixture = new PursuitFixture();
        fixture.ConfigureCornerRoute(true, false);
        return fixture;
    }

    static void Bind(PursuitFixture fixture) {
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
    }

    static void Commit(PursuitFixture fixture) {
        Bind(fixture);
        for (int i = 0; i < 600 && !fixture.Controller.IsTurnCommitted; i++) fixture.Step(1);
        Assert.IsTrue(fixture.Controller.IsTurnCommitted, "fixture must physically acquire a legal turn before fault injection");
    }

    static void AssertFreshTurnRejected(PursuitFixture fixture, bool seam) {
        var constraints = new RouteTransitionFilter.VehicleConstraints(fixture.Profile.colliderSize.x,
            fixture.Binding.navigationSettings.clearanceMargin, fixture.Profile.motorSettings.minimumTurningRadius,
            fixture.Binding.navigationSettings.maxTurnAngle);
        var target = new RoadPathQuery.EdgeAnchor(seam ? "two" : "one", seam ? 12f : 20f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(fixture.Binding.graph, fixture.Cursor.CurrentAnchor, target,
            VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.NoPath, result.status, "the original geometry guard must remain intact");
    }

    static void AssertBodyToAnchorClear(PursuitFixture fixture) {
        Assert.IsTrue(RoadAnchorGeometry.TryGetPoint(fixture.Binding.graph, fixture.Cursor.CurrentAnchor.edgeId,
            fixture.Cursor.CurrentAnchor.distanceAlongEdge, new RoadPathQuery.SearchBudget(128), out var point, out _));
        float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(fixture.PoliceForward);
        Assert.IsTrue(RoadFootprintClearance.IsSweepClear(fixture.PolicePosition, point,
            fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
            Vector2.zero, heading, heading, fixture.Binding.navigation.localBounds, null, fixture.Binding.staticClearance));
    }

    static void AssertStopped(PursuitFixture fixture) {
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
    }

    static void AssertHealthy(PursuitFixture fixture) {
        Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        Assert.AreEqual(100f, fixture.Player.CurrentHealth);
    }
}
