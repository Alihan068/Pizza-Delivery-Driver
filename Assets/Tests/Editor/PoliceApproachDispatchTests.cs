using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused EditMode evidence for far-approach dispatch and legacy safety preservation.</summary>
public sealed class PoliceApproachDispatchTests {
    /// <summary>Checks that a near turn reaches the legacy envelope after route binding and brakes without fallback motion.</summary>
    [Test]
    public void NearTurn_UsesLegacyBlockedBrakeWithoutFallbackMotion() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureCornerRoute(true, false);
            fixture.SetPolicePosition(new Vector2(0f, 6.25f));
            // X=1.25 intersects the physical padded envelope without inflating the initial body or straight footprint.
            var blocker = fixture.CreateBlocker(new Vector2(1.25f, 8f), false, Vector2.one * 0.5f);
            AssertRouteQuery(fixture, "near-turn preflight");
            var tracing = new CallsiteClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = tracing;
            Bind(fixture);

            Vector2 initial = fixture.PolicePosition;
            fixture.Step(1);
            AssertBoundRoute(fixture, "near-turn first controlled tick");
            AssertStopped(fixture, Facts(fixture, "near-turn legacy brake"));
            Assert.Greater(tracing.ControllerLegacyCalls, 0, Facts(fixture, "near-turn legacy callsite"));
            Assert.Greater(tracing.NativeLegacyFailures, 0, Facts(fixture, "near-turn physical envelope refusal"));
            AssertNextTurn(fixture, false);
            for (int step = 0; step < 5; step++) {
                fixture.Step(1);
                AssertStopped(fixture, Facts(fixture, "near-turn fallback motion"));
                Assert.IsFalse(fixture.Controller.IsTurnCommitted, Facts(fixture, "near-turn committed despite refusal"));
            }
            Assert.LessOrEqual(Vector2.Distance(initial, fixture.PolicePosition), fixture.ControllerSettings.acquisitionTolerance,
                Facts(fixture, "near-turn moved after legacy refusal"));
            AssertHealthy(fixture);
            AssertSeparated(fixture, blocker);
        }
    }

    /// <summary>Checks that a near final approach reaches the final legacy guard and cannot move through a failed envelope.</summary>
    [Test]
    public void NearFinal_UsesLegacyBlockedBrakeWithoutFallbackMotion() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureFinalApproachRoute(new Vector2(0f, 14f), roadLength: 10f);
            fixture.SetPolicePosition(new Vector2(0f, 8.25f));
            var blocker = fixture.CreateBlocker(new Vector2(1.25f, 9f), false, Vector2.one * 0.5f);
            var route = AssertRouteQuery(fixture, "near-final preflight");
            Assert.IsTrue(route.hasFinalApproach);
            Assert.IsFalse(route.requiresConnector);
            var tracing = new CallsiteClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = tracing;
            Bind(fixture);

            Vector2 initial = fixture.PolicePosition;
            fixture.Step(1);
            AssertBoundRoute(fixture, "near-final first controlled tick");
            AssertStopped(fixture, Facts(fixture, "near-final legacy brake"));
            Assert.Greater(tracing.ControllerLegacyCalls, 0, Facts(fixture, "near-final legacy callsite"));
            Assert.Greater(tracing.NativeLegacyFailures, 0, Facts(fixture, "near-final physical envelope refusal"));
            Assert.LessOrEqual(fixture.Cursor.RemainingDistance, fixture.ControllerSettings.lookaheadMin);
            for (int step = 0; step < 5; step++) {
                fixture.Step(1);
                AssertStopped(fixture, Facts(fixture, "near-final fallback motion"));
                Assert.AreNotEqual(PolicePursuitController.TraversalPhase.Holding, fixture.Controller.CurrentPhase,
                    Facts(fixture, "near-final entered holding through blocked approach"));
            }
            Assert.LessOrEqual(Vector2.Distance(initial, fixture.PolicePosition), fixture.ControllerSettings.acquisitionTolerance,
                Facts(fixture, "near-final moved after legacy refusal"));
            AssertHealthy(fixture);
            AssertSeparated(fixture, blocker);
        }
    }

    /// <summary>Checks that a selected straight-proof failure stops at the helper callsite without a legacy retry.</summary>
    [Test]
    public void FarStraight_SelectedProofFailureStopsWithoutLegacyRetry() {
        using (var fixture = FarBendFixture(2f)) {
            var tracing = new CallsiteClearance(fixture.Binding.staticClearance, rejectHelper: true);
            fixture.Binding.staticClearance = tracing;
            AssertRouteQuery(fixture, "far-straight preflight");
            Bind(fixture);

            Vector2 initial = fixture.PolicePosition;
            fixture.Step(1);
            AssertBoundRoute(fixture, "far-straight first controlled tick");
            AssertNextTurn(fixture, true);
            Assert.Greater(tracing.HelperCalls, 0, Facts(fixture, "selected straight proof callsite"));
            Assert.AreEqual(0, tracing.ControllerLegacyCalls, Facts(fixture, "selected proof retried through legacy envelope"));
            AssertStopped(fixture, Facts(fixture, "selected straight proof failure"));
            Assert.LessOrEqual(Vector2.Distance(initial, fixture.PolicePosition), fixture.ControllerSettings.acquisitionTolerance,
                Facts(fixture, "failed selected proof moved through fallback"));
            Assert.AreEqual(0, fixture.Controller.RamStarts, Facts(fixture, "selected proof unexpectedly rammed"));
            AssertHealthy(fixture);
        }
    }

    /// <summary>Checks that a clear collinear seam advances by measured rear-footprint crossing before the far bend.</summary>
    [Test]
    public void FarBend_CollinearSeamCrossesWithRearFootprintClear() {
        using (var fixture = FarBendFixture(2f)) {
            AssertRouteQuery(fixture, "clear-seam preflight");
            Bind(fixture);
            bool crossed = false;
            int maximumSteps = Mathf.CeilToInt((24f / Mathf.Max(1f, fixture.Profile.motorSettings.cruiseSpeed) + 5f) / fixture.StepDelta);
            for (int step = 0; step < maximumSteps && !crossed; step++) {
                fixture.Step(1);
                crossed = fixture.PoliceCollider.bounds.min.y > 8f + fixture.Binding.navigationSettings.clearanceMargin * 0.5f;
                Assert.IsFalse(fixture.Controller.IsTurnCommitted, Facts(fixture, "far bend committed before near threshold"));
                AssertHealthy(fixture);
            }
            Assert.IsTrue(crossed, Facts(fixture, "rear footprint did not cross collinear seam"));
            AssertBoundRoute(fixture, "clear-seam final route");
            Assert.AreEqual("e1", fixture.Cursor.CurrentAnchor.edgeId, Facts(fixture, "cursor did not enter seam edge"));
            Assert.Greater(fixture.Controller.CursorProgress, 6f, Facts(fixture, "relative seam progress was not measured"));
        }
    }

    /// <summary>Checks that an off-path seam prop is still rejected by the mandatory legacy envelope at the seam.</summary>
    [Test]
    public void FarBend_CollinearSeamBlockedStopsWithLegacyEnvelope() {
        using (var fixture = FarBendFixture(6.75f)) {
            var blocker = fixture.CreateBlocker(new Vector2(1.25f, 8f), false, Vector2.one * 0.5f);
            AssertRouteQuery(fixture, "blocked-seam preflight");
            var tracing = new CallsiteClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = tracing;
            Bind(fixture);
            float health = fixture.Player.CurrentHealth;
            fixture.Step(1);
            AssertBoundRoute(fixture, "blocked-seam first controlled tick");
            AssertStopped(fixture, Facts(fixture, "blocked seam legacy envelope"));
            Assert.Greater(tracing.ControllerLegacyCalls, 0, Facts(fixture, "blocked seam did not reach legacy callsite"));
            Assert.Greater(tracing.NativeLegacyFailures, 0, Facts(fixture, "blocked seam physical refusal"));
            AssertNextTurn(fixture, true);
            for (int step = 0; step < 20; step++) {
                fixture.Step(1);
                AssertStopped(fixture, Facts(fixture, "blocked seam fallback motion"));
                Assert.IsFalse(fixture.Controller.IsTurnCommitted, Facts(fixture, "blocked seam committed turn"));
            }
            Assert.AreEqual(health, fixture.Player.CurrentHealth, Facts(fixture, "blocked seam caused impact"));
            AssertHealthy(fixture);
            AssertSeparated(fixture, blocker);
        }
    }

    /// <summary>Checks that an acute far bend with high stopping demand cannot bypass the old mandatory envelope.</summary>
    [Test]
    public void AcuteFarBend_LargeStoppingDemandCannotBypassMandatoryEnvelope() {
        using (var fixture = new PursuitFixture(0.5f)) {
            ConfigureAcuteFarBend(fixture);
            fixture.ControllerSettings.planningDistance = 20f;
            fixture.Profile.motorSettings.acceleration = 40f;
            fixture.Profile.motorSettings.maxEngineForce = 40f;
            fixture.Profile.motorSettings.brakeDeceleration = 1f;
            fixture.Profile.motorSettings.maxBrakeForce = 1f;
            var blocker = fixture.CreateBlocker(new Vector2(1.25f, 3f), false, Vector2.one * 0.5f);
            var tracing = new CallsiteClearance(fixture.Binding.staticClearance);
            fixture.Binding.staticClearance = tracing;
            AssertRouteQuery(fixture, "acute far-bend preflight");
            Bind(fixture);

            Vector2 initial = fixture.PolicePosition;
            fixture.Step(1);
            AssertBoundRoute(fixture, "acute far-bend first controlled tick");
            Assert.Greater(tracing.ControllerLegacyCalls, 0, Facts(fixture, "acute far-bend made no legacy envelope call"));
            Assert.Greater(tracing.NativeLegacyFailures, 0, Facts(fixture, "acute physical envelope refusal"));
            AssertNextTurn(fixture, true);
            AssertStopped(fixture, Facts(fixture, "acute far-bend stopping demand"));
            Assert.LessOrEqual(Vector2.Distance(initial, fixture.PolicePosition), fixture.ControllerSettings.acquisitionTolerance,
                Facts(fixture, "acute far-bend bypassed mandatory envelope"));
            AssertHealthy(fixture);
            AssertSeparated(fixture, blocker);
        }
    }

    static PursuitFixture FarBendFixture(float policeY) {
        var fixture = new PursuitFixture();
        fixture.ControllerSettings.planningDistance = 20f;
        var document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 30f, 40f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 8f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 0f, y = 16f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "d", x = 16f, y = 16f });
        document.edges.Add(Edge("e0", "a", "b", Vector2.zero, Vector2.up * 8f));
        document.edges.Add(Edge("e1", "b", "c", Vector2.up * 8f, Vector2.up * 16f));
        document.edges.Add(Edge("e2", "c", "d", Vector2.up * 16f, new Vector2(16f, 16f)));
        fixture.Binding.graph = new RoadGraphRuntime(document);
        fixture.Binding.navigation = document;
        fixture.Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        fixture.Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(fixture.Physics, Vector2.zero, true, 8);
        fixture.SetPolicePosition(new Vector2(0f, policeY));
        fixture.SetPoliceRotation(0f);
        fixture.ReplacePlayer(new Vector2(10f, 16f));
        return fixture;
    }

    static void ConfigureAcuteFarBend(PursuitFixture fixture) {
        fixture.Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        fixture.Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(fixture.Physics, Vector2.zero, true, 8);
        fixture.SetPolicePosition(new Vector2(0f, 2f));
        fixture.SetPoliceRotation(0f);
        fixture.ReplacePlayer(new Vector2(8f, 16f));
        var document = new MapNavigationDocument { localBounds = new Rect(-40f, -20f, 80f, 60f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 8f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 8f, y = 16f });
        document.edges.Add(Edge("acute0", "a", "b", Vector2.zero, Vector2.up * 8f));
        document.edges.Add(Edge("acute1", "b", "c", Vector2.up * 8f, new Vector2(8f, 16f)));
        fixture.Binding.graph = new RoadGraphRuntime(document);
        fixture.Binding.navigation = document;
    }

    static RoadEdgeRecord Edge(string id, string from, string to, Vector2 start, Vector2 end) {
        return new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f, speedLimit = 5f,
            orderedPoints = new List<Vector2> { start, end }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } };
    }

    static PoliceRoadTargetQuery.Result AssertRouteQuery(PursuitFixture fixture, string detail) {
        Assert.IsTrue(fixture.TryQueryCurrent(out var result), Facts(fixture, detail + " query failed"));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status, Facts(fixture, detail + " query status"));
        return result;
    }

    static void AssertNextTurn(PursuitFixture fixture, bool far) {
        int work = 128;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found, fixture.Cursor.TryGetNextTurn(
            fixture.ControllerSettings.planningDistance, ref work, out _, out _, out _, out float distance));
        if (far) Assert.Greater(distance, fixture.ControllerSettings.lookaheadMin);
        else Assert.LessOrEqual(distance, fixture.ControllerSettings.lookaheadMin);
    }

    static void AssertSeparated(PursuitFixture fixture, GameObject blocker) {
        var separation = fixture.PoliceCollider.Distance(blocker.GetComponent<Collider2D>());
        Assert.IsTrue(separation.isValid);
        Assert.IsFalse(separation.isOverlapped);
        Assert.Greater(separation.distance, 0f, Facts(fixture, "actual static separation"));
    }

    static void Bind(PursuitFixture fixture) {
        Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
        Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
    }

    static void AssertStopped(PursuitFixture fixture, string facts) {
        Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, facts);
        Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle, facts);
    }

    static void AssertHealthy(PursuitFixture fixture) {
        Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
        Assert.AreEqual(100f, fixture.Player.CurrentHealth);
    }

    static void AssertBoundRoute(PursuitFixture fixture, string detail) {
        Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture, detail + " controller"));
        Assert.IsTrue(fixture.Cursor.IsBound, Facts(fixture, detail + " cursor"));
        Assert.IsFalse(string.IsNullOrEmpty(fixture.Cursor.CurrentAnchor.edgeId), Facts(fixture, detail + " anchor"));
        Assert.IsFalse(float.IsNaN(fixture.Controller.CursorProgress) || float.IsInfinity(fixture.Controller.CursorProgress),
            Facts(fixture, detail + " progress"));
    }

    static string Facts(PursuitFixture fixture, string detail) {
        return detail + "; phase=" + fixture.Controller.CurrentPhase + ", cursor=" + fixture.Controller.CursorProgress +
            ", gap=" + fixture.ColliderGap + ", police=" + fixture.PolicePosition + ", velocity=" + fixture.PoliceVelocity +
            ", attempts=" + fixture.Controller.PlannerAttemptCount;
    }

    sealed class CallsiteClearance : IAreaClearanceQuery {
        readonly IAreaClearanceQuery inner;
        readonly bool rejectHelper;
        internal int HelperCalls { get; private set; }
        internal int ControllerLegacyCalls { get; private set; }
        internal int NativeLegacyFailures { get; private set; }

        internal CallsiteClearance(IAreaClearanceQuery inner, bool rejectHelper = false) {
            this.inner = inner;
            this.rejectHelper = rejectHelper;
        }

        /// <summary>Classifies production clearance callers and rejects only the selected proof path.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            bool helper = false;
            bool controllerEnvelope = false;
            StackFrame[] frames = new StackTrace().GetFrames();
            if (frames != null) {
                for (int i = 0; i < frames.Length; i++) {
                    var method = frames[i].GetMethod();
                    var declaringType = method == null ? null : method.DeclaringType;
                    if (declaringType == typeof(PoliceStraightRoadClearance) && method.Name == "TryEnclosedQuery") helper = true;
                    if (declaringType == typeof(PolicePursuitController) && method != null &&
                        method.Name == "TryValidateMovementEnvelope")
                        controllerEnvelope = true;
                }
            }
            if (helper) HelperCalls++;
            if (controllerEnvelope) ControllerLegacyCalls++;
            if (rejectHelper && helper) return false;
            bool clear = inner.IsAreaClear(center, footprint, headingDegrees);
            if (controllerEnvelope && !clear) NativeLegacyFailures++;
            return clear;
        }
    }
}
