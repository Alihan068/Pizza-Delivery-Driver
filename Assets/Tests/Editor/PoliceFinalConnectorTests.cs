using NUnit.Framework;
using UnityEngine;

/// <summary>Focused physical acceptance for final connector traversal and measured holding.</summary>
public sealed class PoliceFinalConnectorTests {
    /// <summary>Aligned road-to-player approaches pass through the completed road endpoint and hold at measured standoff.</summary>
    [TestCase(0.02f, false, 0f, true)]
    [TestCase(0.03f, true, 0f, false)]
    [TestCase(0.02f, true, -3f, false)]
    [TestCase(0.03f, false, -3f, false)]
    public void FinalConnector_TraversesAndHoldsWithMeasuredProgress(float deltaTime, bool reverseCreationOrder,
        float initialY, bool requireRefreshCounter) {
        using (var fixture = CreateFixture(deltaTime, reverseCreationOrder, initialY, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool observedFinal = false;
            bool observedHolding = false;
            bool observedOffRoadRefresh = false;
            int holdingTicks = 0;
            float previousProgress = 0f;
            for (int step = 0; step < 700; step++) {
                var phaseBefore = fixture.Controller.CurrentPhase;
                int attemptsBefore = fixture.Controller.PlannerAttemptCount;
                fixture.Step(1);
                var phaseAfter = fixture.Controller.CurrentPhase;
                if (observedFinal) Assert.AreNotEqual(PolicePursuitController.TraversalPhase.InitialConnector, phaseAfter);
                if ((phaseBefore == PolicePursuitController.TraversalPhase.FinalConnector ||
                    phaseAfter == PolicePursuitController.TraversalPhase.FinalConnector) &&
                    fixture.Controller.PlannerAttemptCount > attemptsBefore)
                    observedOffRoadRefresh = true;
                if (phaseAfter == PolicePursuitController.TraversalPhase.FinalConnector ||
                    phaseAfter == PolicePursuitController.TraversalPhase.Holding) {
                    observedFinal = true;
                    Assert.AreNotEqual(PolicePursuitController.TraversalPhase.InitialConnector, phaseAfter);
                    Assert.GreaterOrEqual(fixture.Controller.FinalConnectorProgress, previousProgress);
                    previousProgress = fixture.Controller.FinalConnectorProgress;
                    if (phaseAfter == PolicePursuitController.TraversalPhase.Holding) {
                        observedHolding = true;
                        holdingTicks++;
                    }
                }
                if (holdingTicks >= 80) break;
            }
            Assert.IsTrue(observedFinal);
            Assert.IsTrue(observedHolding);
            Assert.GreaterOrEqual(fixture.Controller.CursorProgress, 10f);
            Assert.GreaterOrEqual(holdingTicks, 80);
            Assert.Greater(fixture.PolicePosition.y, 10f);
            Assert.That(fixture.SurfaceGap, Is.EqualTo(fixture.ControllerSettings.stopGap).Within(0.2f));
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
            if (requireRefreshCounter) Assert.Greater(fixture.Controller.FinalConnectorRefreshes, 0);
            Assert.IsTrue(observedOffRoadRefresh);
        }
    }

    /// <summary>A final line shorter than the required standoff stops before the road endpoint.</summary>
    [Test]
    public void FinalConnector_SubGapStopsBeforeRoadEndpoint() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 10.2f))) {
            Bind(fixture);
            fixture.Step(600);
            Assert.Less(fixture.PolicePosition.y, 10f);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.That(fixture.SurfaceGap, Is.EqualTo(fixture.ControllerSettings.stopGap).Within(0.2f));
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>A clear but heading-misaligned optional final approach is proven by the stateless query but rejected by traversal.</summary>
    [Test]
    public void FinalConnector_MisalignedOptionalApproachStopsAtRoadEnd() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(3f, 14f))) {
            fixture.SetPolicePosition(new Vector2(0f, 8f));
            Assert.IsTrue(fixture.TryQueryCurrent(out var result));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
            Assert.IsTrue(result.hasFinalApproach);
            fixture.SetPolicePosition(Vector2.zero);
            Bind(fixture);
            fixture.Step(600);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Less(fixture.PolicePosition.y, 10f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    /// <summary>A completed road with no positive tangent cannot invent a final connector direction.</summary>
    [Test]
    public void FinalConnector_ZeroPositiveRoadTangentStopsWithoutInventedLine() {
        using (var fixture = CreateFixture(0.02f, false, 10f, new Vector2(0f, 14f))) {
            Assert.IsTrue(fixture.TryQueryCurrent(out var result));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
            Assert.IsTrue(result.hasFinalApproach);
            Assert.IsTrue(result.spans != null && result.spans.Count > 0);
            for (int i = 0; i < result.spans.Count; i++) Assert.AreEqual(result.spans[i].startDistance, result.spans[i].endDistance);
            Bind(fixture);
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.AreNotEqual(PolicePursuitController.TraversalPhase.FinalConnector, fixture.Controller.CurrentPhase);
        }
    }

    /// <summary>A final approach revoked by a fresh static blocker remains a route result and restores road-end braking.</summary>
    [Test]
    public void FinalConnector_RevokedApproachRestoresRoadEndBraking() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool reachedPreEnd = false;
            for (int step = 0; step < 600 && !reachedPreEnd; step++) {
                fixture.Step(1);
                reachedPreEnd = fixture.PolicePosition.y >= 6f;
            }
            Assert.IsTrue(reachedPreEnd);
            fixture.CreateBlocker(new Vector2(0f, 12f), false, new Vector2(0.4f, 0.4f));
            Assert.IsTrue(fixture.TryQueryCurrent(out var result));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
            Assert.IsFalse(result.hasFinalApproach);
            fixture.Step(220);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Less(fixture.PolicePosition.y, 10f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>A final-corridor blocker outside the raw footprint but inside the padded offset envelope is rejected.</summary>
    [Test]
    public void FinalConnector_PaddedOffsetWallStopsWithRawClearProof() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 14f))) {
            fixture.SetPoliceColliderOffset(new Vector2(0.2f, 0.1f));
            fixture.CreateBlocker(new Vector2(0.9f, 12f), false, new Vector2(0.1f, 0.1f), 23f);
            var query = fixture.Binding.staticClearance;
            Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(0f, 10f), new Vector2(0f, 14f),
                fixture.Profile.colliderSize, new Vector2(0.2f, 0.1f), 0f, 0f,
                fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsSweepClear(new Vector2(0f, 10f), new Vector2(0f, 14f),
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                new Vector2(0.2f, 0.1f), 0f, 0f, fixture.Binding.navigation.localBounds, null, query));
            Bind(fixture);
            fixture.Step(600);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Less(fixture.PolicePosition.y, 10f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    /// <summary>Once final traversal is proven, exhausted query work waits on the normal cadence without old-line fallback.</summary>
    [Test]
    public void FinalConnector_RefreshWorkExhaustionStopsWithoutFallback() {
        using (var fixture = CreateFixture(0.02f, true, 0f, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 600 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            fixture.SetRuntimeQueryWork(1);
            int attempts = fixture.Controller.PlannerAttemptCount;
            bool attempted = false;
            for (int step = 0; step < 100; step++) {
                fixture.Step(1);
                if (fixture.Controller.PlannerAttemptCount > attempts) { attempted = true; break; }
            }
            Assert.IsTrue(attempted);
            Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, fixture.Planner.CurrentResult.status);
            float cursorBeforeWait = fixture.Controller.CursorProgress;
            bool stopped = false;
            for (int step = 0; step < 80; step++) {
                fixture.Step(1);
                Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, fixture.Planner.CurrentResult.status);
                Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
                Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
                if (fixture.ForwardSpeed <= fixture.ControllerSettings.stoppedSpeedThreshold) {
                    stopped = true;
                    break;
                }
            }
            Assert.AreEqual(cursorBeforeWait, fixture.Controller.CursorProgress);
            Assert.IsTrue(stopped);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    /// <summary>A moving dynamic final-corridor blocker brakes before contact, independently from saturation.</summary>
    [Test]
    public void FinalConnector_MovingDynamicBlockerStopsBeforeContact() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 24f), 10f)) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            Vector2 beforeBlocker = fixture.PolicePosition;
            for (int step = 0; step < 30; step++) {
                fixture.Step(1);
                if (Vector2.Distance(beforeBlocker, fixture.PolicePosition) > 0.01f &&
                    fixture.ForwardSpeed > fixture.ControllerSettings.stoppedSpeedThreshold) break;
            }
            Assert.Greater(Vector2.Distance(beforeBlocker, fixture.PolicePosition), 0.01f);
            Assert.Greater(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold);
            Assert.IsTrue(fixture.TryQueryCurrent(out var clearResult));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, clearResult.status);
            Assert.IsTrue(clearResult.hasFinalApproach);
            float health = fixture.PoliceReceiver.CurrentHealth;
            var blocker = fixture.CreateBlocker(new Vector2(0f, fixture.PolicePosition.y + 4f), true, new Vector2(0.4f, 0.4f));
            Assert.IsTrue(fixture.TryQueryCurrent(out var dynamicResult));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, dynamicResult.status);
            Assert.IsTrue(dynamicResult.hasFinalApproach);
            Vector2 blockerStart = blocker.transform.position;
            fixture.SetBlockerVelocity(blocker, Vector2.down * 0.5f);
            for (int step = 0; step < 120 && fixture.ForwardSpeed > fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f; step++) fixture.Step(1);
            Assert.IsFalse(fixture.Sensor.LastQuerySaturated);
            Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
            Assert.LessOrEqual(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.Greater(Vector2.Distance(blockerStart, blocker.transform.position), 0.01f);
            Assert.Greater(Physics2D.Distance(fixture.PoliceCollider, blocker.GetComponent<Collider2D>()).distance, 0f);
            Assert.AreEqual(health, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>Final-corridor sensor saturation remains a distinct fail-closed condition.</summary>
    [Test]
    public void FinalConnector_SaturatedSensorStops() {
        using (var fixture = CreateFixture(0.02f, true, 0f, new Vector2(0f, 24f), 10f)) {
            fixture.ControllerSettings.sensorBuffer = 1;
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            fixture.CreateBlocker(new Vector2(0f, fixture.PolicePosition.y + 4f), true, Vector2.one);
            fixture.CreateBlocker(new Vector2(0f, fixture.PolicePosition.y + 5f), true, Vector2.one);
            fixture.Step(1);
            Assert.IsTrue(fixture.Sensor.LastQuerySaturated);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>Target replacement, graph invalidation, and life reset after final proof clear the phase and brake.</summary>
    [Test]
    public void FinalConnector_LifecycleInvalidationBrakesSafely() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            fixture.InvalidateGraph();
            fixture.Step(1);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
        using (var fixture = CreateFixture(0.03f, true, 0f, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.ReplacePlayer(new Vector2(0f, 14f)), out _));
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 14f))) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            fixture.Controller.ResetForNewLife();
            Assert.IsFalse(fixture.Controller.IsBound);
        }
    }

    /// <summary>Actual target motion invalidates the fixed final line and brakes on the fresh query.</summary>
    [Test]
    public void FinalConnector_TargetMotionDuringRefreshBrakesWithoutOldLine() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 24f), 10f)) {
            Bind(fixture);
            bool finalSeen = false;
            for (int step = 0; step < 700 && !finalSeen; step++) {
                fixture.Step(1);
                finalSeen = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
            }
            Assert.IsTrue(finalSeen);
            int attemptsBefore = fixture.Controller.PlannerAttemptCount;
            Vector2 targetBefore = fixture.PlayerPosition;
            fixture.PushPlayer(Vector2.up * 0.5f);
            for (int step = 0; step < 60; step++) {
                fixture.Step(1);
            }
            Assert.Greater(Vector2.Distance(targetBefore, fixture.PlayerPosition), 0.01f);
            Assert.Greater(fixture.Controller.PlannerAttemptCount, attemptsBefore);
            Assert.AreNotEqual(PolicePursuitController.TraversalPhase.FinalConnector, fixture.Controller.CurrentPhase);
            Assert.AreNotEqual(PolicePursuitController.TraversalPhase.Holding, fixture.Controller.CurrentPhase);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>A rotated horizontal final connector reaches holding with a rotated body and offset.</summary>
    [Test]
    public void FinalConnector_RotatedHorizontalApproachHolds() {
        using (var fixture = CreateFixture(0.02f, true, 0f, new Vector2(14f, 0f), 10f, true)) {
            fixture.SetPoliceColliderOffset(new Vector2(0.1f, 0.2f));
            fixture.SetPoliceRotation(-90f);
            Bind(fixture);
            bool holding = false;
            for (int step = 0; step < 700 && !holding; step++) {
                fixture.Step(1);
                holding = fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Holding;
            }
            Assert.IsTrue(holding);
            Assert.That(fixture.PoliceForward.x, Is.EqualTo(1f).Within(0.05f));
            Assert.GreaterOrEqual(fixture.Controller.CursorProgress, 10f);
            Assert.That(fixture.ColliderGap, Is.EqualTo(fixture.ControllerSettings.stopGap).Within(0.2f));
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>A horizontal final corridor blocked only by padded wall clearance stops at the road end.</summary>
    [Test]
    public void FinalConnector_RotatedPaddingWallRejectsFinalApproach() {
        using (var fixture = CreateFixture(0.02f, true, 0f, new Vector2(14f, 0f), 10f, true)) {
            fixture.SetPoliceColliderOffset(new Vector2(0.1f, 0.2f));
            fixture.SetPoliceRotation(-90f);
            fixture.CreateBlocker(new Vector2(12f, 0.6f), false, new Vector2(0.1f, 0.1f));
            var query = fixture.Binding.staticClearance;
            Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(10f, 0f), new Vector2(14f, 0f),
                fixture.Profile.colliderSize, new Vector2(0.1f, 0.2f), -90f, -90f,
                fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsSweepClear(new Vector2(10f, 0f), new Vector2(14f, 0f),
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                new Vector2(0.1f, 0.2f), -90f, -90f,
                fixture.Binding.navigation.localBounds, null, query));
            Bind(fixture);
            fixture.Step(600);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Less(fixture.PolicePosition.x, 10f);
            Assert.Greater(fixture.PolicePosition.x, 4f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    /// <summary>A vertical final corridor rejected only by padded upper bounds stops before its endpoint.</summary>
    [Test]
    public void FinalConnector_PaddedUpperBoundsRejectFinalApproach() {
        using (var fixture = CreateFixture(0.02f, false, 0f, new Vector2(0f, 14f), 10f, false, 15.1f)) {
            var query = fixture.Binding.staticClearance;
            Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(0f, 10f), new Vector2(0f, 14f),
                fixture.Profile.colliderSize, Vector2.zero, 0f, 0f,
                fixture.Binding.navigation.localBounds, null, query));
            Assert.IsFalse(RoadFootprintClearance.IsSweepClear(new Vector2(0f, 10f), new Vector2(0f, 14f),
                fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin,
                Vector2.zero, 0f, 0f,
                fixture.Binding.navigation.localBounds, null, query));
            Bind(fixture);
            fixture.Step(600);
            Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase);
            Assert.Less(fixture.PolicePosition.y, 10f);
            Assert.Greater(fixture.PolicePosition.y, 4f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    static PursuitFixture CreateFixture(float deltaTime, bool reverseCreationOrder, float initialY, Vector2 target,
        float roadLength = 10f, bool horizontal = false, float boundsMaxY = 30f) {
        var fixture = new PursuitFixture(deltaTime, reverseCreationOrder);
        fixture.ConfigureFinalApproachRoute(target, roadLength, horizontal, boundsMaxY);
        fixture.SetPolicePosition(new Vector2(0f, initialY));
        return fixture;
    }

    static void Bind(PursuitFixture fixture) {
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
    }
}
