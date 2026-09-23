using NUnit.Framework;
using UnityEngine;

/// <summary>Isolated real-physics acceptance for the bounded aligned initial connector phase.</summary>
public sealed class PoliceInitialConnectorTests {
    /// <summary>Each supported timestep and creation order reaches the straight road through a measured connector.</summary>
    [TestCase(0.02f, false, -3f)]
    [TestCase(0.02f, true, -0.2f)]
    [TestCase(0.03f, false, -0.2f)]
    [TestCase(0.03f, true, -3f)]
    public void InitialConnector_StraightRouteTraversesMeasuredJoin(float deltaTime, bool reverseCreationOrder, float startY) {
        using (var fixture = CreateStraightFixture(deltaTime, reverseCreationOrder, startY)) {
            Bind(fixture);
            fixture.Step(500);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Greater(fixture.PolicePosition.y, 1f);
            Assert.Greater(fixture.Controller.CursorProgress, 1f);
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.That(fixture.SurfaceGap, Is.EqualTo(fixture.ControllerSettings.stopGap).Within(0.2f));
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>The .75 refresh is observed while the initial phase is still active without cursor advancement.</summary>
    [Test]
    public void InitialConnector_RefreshRetainsLineProgressUntilMeasuredJoin() {
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            Bind(fixture);
            bool sawInitial = false;
            bool sawRefreshWhileInitial = false;
            bool reachedRoad = false;
            float previousLineProgress = 0f;
            int previousAttempts = fixture.Controller.PlannerAttemptCount;
            for (int step = 0; step < 500; step++) {
                var phaseBefore = fixture.Controller.CurrentPhase;
                int attemptsBefore = fixture.Controller.PlannerAttemptCount;
                fixture.Step(1);
                if (phaseBefore == PolicePursuitController.TraversalPhase.InitialConnector &&
                    fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.InitialConnector) {
                    sawInitial = true;
                    Assert.AreEqual(0f, fixture.Controller.CursorProgress);
                    Assert.GreaterOrEqual(fixture.Controller.InitialConnectorProgress, previousLineProgress);
                    previousLineProgress = fixture.Controller.InitialConnectorProgress;
                    if (fixture.Controller.PlannerAttemptCount > attemptsBefore && fixture.Controller.PlannerAttemptCount > previousAttempts)
                        sawRefreshWhileInitial = true;
                }
                previousAttempts = fixture.Controller.PlannerAttemptCount;
                if (fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Road && fixture.Controller.CursorProgress > 0f) {
                    reachedRoad = true;
                    break;
                }
            }
            Assert.IsTrue(sawInitial);
            Assert.IsTrue(sawRefreshWhileInitial);
            Assert.IsTrue(reachedRoad);
            Assert.Greater(fixture.Controller.CursorProgress, 0f);
        }
    }

    /// <summary>A near-road bend is not crossed by the initial aim before physical join and road commitment.</summary>
    [Test]
    public void InitialConnector_NearBendAimStopsAtFirstBendUntilJoin() {
        using (var fixture = CreateCornerFixture(0.02f, false, -0.2f)) {
            fixture.ControllerSettings.lookaheadMin = 12f;
            fixture.ControllerSettings.lookaheadMax = 12f;
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.InitialConnector, fixture.Controller.CurrentPhase);
            Assert.IsFalse(fixture.Controller.IsTurnCommitted);
            Assert.Greater(fixture.Controller.LastCommand.throttle, 0f);
            Assert.That(fixture.Controller.LastAimPoint.x, Is.EqualTo(0f).Within(0.001f));
            Assert.LessOrEqual(fixture.Controller.LastAimPoint.y, 8f);
        }
    }

    /// <summary>Actual and padded sweeps distinguish a clear raw footprint from a blocked navigation envelope.</summary>
    [Test]
    public void InitialConnector_PaddingAndBoundsRejectOnlyPaddedEnvelope() {
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            var blocker = fixture.CreateBlocker(new Vector2(0.7f, -1.5f), false, new Vector2(0.1f, 0.1f));
            var query = fixture.Binding.staticClearance;
            Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(0f, -3f), new Vector2(0f, 0f),
                fixture.Profile.colliderSize, Vector2.zero, 0f, 0f, fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsSweepClear(new Vector2(0f, -3f), new Vector2(0f, 0f),
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                Vector2.zero, 0f, 0f, fixture.Binding.navigation.localBounds, null, query));
            Object.DestroyImmediate(blocker);
            Bind(fixture);
            fixture.CreateBlocker(new Vector2(0.7f, -1.5f), false, new Vector2(0.1f, 0.1f));
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
        using (var fixture = CreateStraightFixture(0.02f, true, -3f)) {
            fixture.Binding.navigation.localBounds = new Rect(-10f, -4.1f, 20f, 18.1f);
            var query = fixture.Binding.staticClearance;
            Assert.IsTrue(RoadFootprintClearance.IsPoseClear(new Vector2(0f, -3f), fixture.Profile.colliderSize,
                Vector2.zero, 0f, fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsPoseClear(new Vector2(0f, -3f),
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                Vector2.zero, 0f, fixture.Binding.navigation.localBounds, null, query));
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>A rotated road and nonzero collider offset still use the padded rotated footprint for rejection.</summary>
    [Test]
    public void InitialConnector_RotatedOffsetPaddingRejectsWithPhysicalProof() {
        using (var fixture = CreateRotatedStraightFixture(0.03f, true)) {
            fixture.SetPoliceColliderOffset(new Vector2(0.1f, 0.2f));
            var query = fixture.Binding.staticClearance;
            fixture.CreateBlocker(new Vector2(0f, 0.6f), false, new Vector2(0.1f, 0.1f));
            Assert.That(fixture.PoliceForward.x, Is.EqualTo(1f).Within(0.001f));
            Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(-3f, 0f), Vector2.zero,
                fixture.Profile.colliderSize, new Vector2(0.1f, 0.2f), -90f, -90f,
                fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsSweepClear(new Vector2(-3f, 0f), Vector2.zero,
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                new Vector2(0.1f, 0.2f), -90f, -90f, fixture.Binding.navigation.localBounds, null, query));
            Bind(fixture);
            fixture.Step(1);
            Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
            Assert.IsFalse(fixture.Sensor.LastQuerySaturated);
        }
    }

    /// <summary>A moving connector approach physically brakes before dynamic contact at both tested timesteps.</summary>
    [TestCase(0.02f, false)]
    [TestCase(0.03f, true)]
    public void InitialConnector_DynamicBlockerBrakesWithoutSaturation(float deltaTime, bool reverseCreationOrder) {
        using (var fixture = CreateStraightFixture(deltaTime, reverseCreationOrder, -6f)) {
            Bind(fixture);
            for (int i = 0; i < 100 && fixture.ForwardSpeed < 2f; i++) fixture.Step(1);
            Assert.GreaterOrEqual(fixture.ForwardSpeed, 2f);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.InitialConnector, fixture.Controller.CurrentPhase);
            var blocker = fixture.CreateBlocker(fixture.PolicePosition + Vector2.up * 4f, true, Vector2.one);
            bool braked = false;
            for (int i = 0; i < 200; i++) {
                fixture.Step(1);
                braked |= fixture.Controller.LastCommand.brake > 0f;
            }
            Assert.IsTrue(braked);
            Assert.IsFalse(fixture.Sensor.LastQuerySaturated);
            Assert.Less(Mathf.Abs(fixture.ForwardSpeed), fixture.ControllerSettings.stoppedSpeedThreshold);
            Assert.Greater(fixture.Binding.body.MainCollider.Distance(blocker.GetComponent<Collider2D>()).distance, 0f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>A changed target suffix or failed query cannot reuse the previous initial certificate on refresh.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void InitialConnector_ChangedOrFailedRefreshRevokesMotion(bool queryFailure) {
        using (var fixture = CreateStraightFixture(0.02f, false, -6f)) {
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.InitialConnector, fixture.Controller.CurrentPhase);
            int attempts = fixture.Controller.PlannerAttemptCount;
            if (queryFailure) fixture.SetRuntimeQueryWork(1);
            else fixture.PushPlayer(Vector2.up * 0.5f);
            for (int i = 0; i < 100 && fixture.Controller.PlannerAttemptCount == attempts; i++) fixture.Step(1);
            Assert.AreEqual(attempts + 1, fixture.Controller.PlannerAttemptCount);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.AreEqual(0f, fixture.Controller.CursorProgress);
            if (queryFailure) Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, fixture.Planner.CurrentResult.status);
            else Assert.Greater(fixture.PlayerPosition.y, 14f);
        }
    }

    /// <summary>A zero-only road query cannot supply a made-up positive tangent for connector acquisition.</summary>
    [Test]
    public void InitialConnector_ZeroRoadCannotInventJoinTangent() {
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            fixture.ReplacePlayer(Vector2.zero);
            Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(new PoliceRoadTargetQuery.Input {
                graph = fixture.Binding.graph, navigation = fixture.Binding.navigation, profile = fixture.Profile,
                policePosition = fixture.PolicePosition, policeDirection = fixture.PoliceForward,
                targetPosition = fixture.PlayerPosition, staticClearance = fixture.Binding.staticClearance,
                settings = fixture.Binding.navigationSettings, budget = new RoadPathQuery.SearchBudget(512)
            }, out var route));
            Assert.IsTrue(route.requiresConnector);
            Assert.AreEqual(1, route.spans.Count);
            Assert.AreEqual(route.spans[0].startDistance, route.spans[0].endDistance);
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.IsFalse(fixture.Cursor.IsBound);
            Assert.AreEqual(1, fixture.Controller.PlannerAttemptCount);
        }
    }

    /// <summary>Sensor saturation remains a separate fail-closed condition.</summary>
    [Test]
    public void InitialConnector_SaturatedSensorStops() {
        using (var fixture = CreateStraightFixture(0.02f, true, -3f)) {
            fixture.ControllerSettings.sensorBuffer = 1;
            Bind(fixture);
            fixture.CreateBlocker(new Vector2(0f, -1.5f), true, Vector2.one);
            fixture.CreateBlocker(new Vector2(0f, -1f), true, Vector2.one);
            fixture.Step(1);
            Assert.IsTrue(fixture.Sensor.LastQuerySaturated);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>Lateral, backward-facing, and missing-clearance starts wait rather than inventing an initial route.</summary>
    [TestCase(3f, -3f, 0f)]
    [TestCase(-3f, -3f, 0f)]
    [TestCase(0f, -3f, 180f)]
    public void InitialConnector_InvalidAlignmentWaits(float x, float y, float rotation) {
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            fixture.SetPolicePosition(new Vector2(x, y));
            fixture.SetPoliceRotation(rotation);
            Bind(fixture);
            fixture.Step(2);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>Graph invalidation, target replacement, and life reset clear an active initial phase without same-tick fallback.</summary>
    [Test]
    public void InitialConnector_ActivePhaseInvalidationStopsSafely() {
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.InitialConnector, fixture.Controller.CurrentPhase);
            fixture.InvalidateGraph();
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
        using (var fixture = CreateStraightFixture(0.02f, true, -3f)) {
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.InitialConnector, fixture.Controller.CurrentPhase);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.ReplacePlayer(new Vector2(0f, 14f)), out _));
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            fixture.Controller.ResetForNewLife();
            Assert.IsFalse(fixture.Controller.IsBound);
        }
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            Bind(fixture);
            fixture.Step(1);
            fixture.SetRuntimeCursorWork(1);
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
        using (var fixture = CreateStraightFixture(0.02f, false, -3f)) {
            fixture.Binding.staticClearance = null;
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    static PursuitFixture CreateStraightFixture(float deltaTime, bool reverseCreationOrder, float startY) {
        var fixture = new PursuitFixture(deltaTime, reverseCreationOrder);
        fixture.ConfigureStraightInitialRoute();
        fixture.SetPolicePosition(new Vector2(0f, startY));
        return fixture;
    }

    static PursuitFixture CreateCornerFixture(float deltaTime, bool reverseCreationOrder, float startY) {
        var fixture = new PursuitFixture(deltaTime, reverseCreationOrder);
        fixture.ConfigureCornerRoute(true, false);
        fixture.SetPolicePosition(new Vector2(0f, startY));
        return fixture;
    }

    static PursuitFixture CreateRotatedStraightFixture(float deltaTime, bool reverseCreationOrder) {
        var fixture = new PursuitFixture(deltaTime, reverseCreationOrder);
        fixture.ConfigureRotatedStraightRoute();
        return fixture;
    }

    static void Bind(PursuitFixture fixture) {
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
    }
}
