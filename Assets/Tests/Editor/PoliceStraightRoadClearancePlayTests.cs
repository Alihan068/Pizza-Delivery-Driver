using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Focused physical acceptance for the authored straight-road clearance branch.</summary>
public sealed class PoliceStraightRoadClearancePlayTests {
    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string PoliceDataPath = "Assets/ScriptableObjects/Police/PoliceStandardData.asset";
    const string PoliceVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string PursueBehaviorPath = "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset";

    /// <summary>Runs the authored straight positive at both supported steps and creation orders.</summary>
    [UnityTest]
    public IEnumerator StraightRoadAuthoredMatrix() {
        AssertEmptyBootstrap();
        VerifySources();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.Greater(snapshot.ColliderCount, 0);
            RunCase(snapshot, 0.02f, false);
            RunCase(snapshot, 0.02f, true);
            RunCase(snapshot, 0.03f, false);
            RunCase(snapshot, 0.03f, true);
        }
        finally {
            try {
                if (snapshot != null) snapshot.AssertSourcesUnchanged();
            }
            finally {
                PoliceNarrowDistrictGeometrySnapshot.ClearPending();
            }
        }
    }

    /// <summary>Returns the editor to Edit Mode after any physical acceptance failure.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunCase(PoliceNarrowDistrictGeometrySnapshot snapshot, float deltaTime, bool reverseCreationOrder) {
        var authored = LoadAuthoredReferences();
        try {
            using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder, authored: authored)) {
                Assert.AreNotSame(authored.profile, fixture.Profile);
                Assert.AreEqual(authored.profile.vehicleProfileId, fixture.Profile.vehicleProfileId);
                Assert.AreEqual(authored.profile.colliderSize, fixture.Profile.colliderSize);
                Assert.AreEqual(authored.profile.baseMass, fixture.Profile.baseMass);
                Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile));
                using (snapshot.StageInto(fixture.FixtureScene)) {
                    AssertOldBStaticWitnesses(fixture);
                    var navigation = snapshot.ResolveNavigation();
                    Assert.IsNotNull(navigation);
                    fixture.ConfigureAuthoredNavigation(navigation, snapshot.Origin,
                        new Vector2(6f, -12.5f), 0f, new Vector2(6f, -3f));
                    Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                    Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                    Assert.GreaterOrEqual(fixture.ColliderGap, fixture.ControllerSettings.stopGap,
                        Facts(fixture, "target must begin separated by the actual Collider2D distance"));

                    int initialAttempts = fixture.Controller.PlannerAttemptCount;
                    float initialPlayerHealth = fixture.Player.CurrentHealth;
                    float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
                    Vector2 initialPosition = fixture.PolicePosition;
                    bool impactObserved = false;
                    bool steeringWasZero = true;
                    bool measuredProgress = false;
                    int stableStandoffFrames = 0;
                    float maximumProgress = fixture.Controller.CursorProgress;
                    var plannerAttemptTimes = new List<float>();
                    int previousPlannerAttempts = initialAttempts;
                    fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
                    int steps = Mathf.CeilToInt(20f / deltaTime);
                    for (int step = 0; step < steps; step++) {
                        fixture.Step(1);
                        steeringWasZero &= fixture.Controller.LastCommand.steering == 0f;
                        maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
                        measuredProgress |= Vector2.Distance(initialPosition, fixture.PolicePosition) > fixture.ControllerSettings.acquisitionTolerance;
                        if (fixture.Controller.PlannerAttemptCount > previousPlannerAttempts) {
                            for (int attempt = previousPlannerAttempts; attempt < fixture.Controller.PlannerAttemptCount; attempt++) {
                                plannerAttemptTimes.Add(PlannerLastAttemptSeconds(fixture.Planner));
                            }
                            previousPlannerAttempts = fixture.Controller.PlannerAttemptCount;
                        }
                        bool separatedAndStopped = measuredProgress && (fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold ||
                            Mathf.Approximately(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold));
                        bool actualStandoff = WithinMeasuredStandoff(fixture);
                        stableStandoffFrames = separatedAndStopped && actualStandoff ? stableStandoffFrames + 1 : 0;
                        AssertFinite(fixture, Facts(fixture, "delta=" + deltaTime + ", reverse=" + reverseCreationOrder));
                        if (stableStandoffFrames >= 5) break;
                    }

                    Assert.IsTrue(steeringWasZero, Facts(fixture, "straight branch issued steering"));
                    Assert.Greater(maximumProgress, 0f, Facts(fixture, "straight branch made no route progress"));
                    Assert.IsTrue(measuredProgress, Facts(fixture, "straight branch never exceeded acquisition tolerance"));
                    Assert.GreaterOrEqual(stableStandoffFrames, 5, Facts(fixture, "straight pursuit did not sustain measured standoff"));
                    Assert.Greater(plannerAttemptTimes.Count, 1, Facts(fixture, "planner did not produce sustained attempts"));
                    for (int index = 1; index < plannerAttemptTimes.Count; index++) {
                        float interval = plannerAttemptTimes[index] - plannerAttemptTimes[index - 1];
                        Assert.IsTrue(interval >= fixture.Binding.navigationSettings.hardMinimumInterval ||
                            Mathf.Approximately(interval, fixture.Binding.navigationSettings.hardMinimumInterval), Facts(fixture, "planner hard cadence"));
                        Assert.IsTrue(interval >= fixture.Binding.navigationSettings.refreshInterval ||
                            Mathf.Approximately(interval, fixture.Binding.navigationSettings.refreshInterval), Facts(fixture, "planner refresh cadence lower bound"));
                        float clockRoundoff = 8f / 8388608f * Mathf.Max(1f, plannerAttemptTimes[index]);
                        float cadenceUpper = fixture.Binding.navigationSettings.refreshInterval + deltaTime + clockRoundoff;
                        Assert.LessOrEqual(interval, cadenceUpper, Facts(fixture, "planner refresh cadence upper bound"));
                    }
                    Assert.AreEqual(0, fixture.Controller.RamStarts, Facts(fixture, "straight pursuit admitted Ram"));
                    Assert.IsFalse(impactObserved, Facts(fixture, "straight positive produced physical impact"));
                    Assert.IsTrue(fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold ||
                        Mathf.Approximately(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold), Facts(fixture));
                    Assert.IsTrue(WithinMeasuredStandoff(fixture), Facts(fixture, "final measured standoff"));
                    Assert.Greater(Vector2.Distance(initialPosition, fixture.PolicePosition), fixture.ControllerSettings.acquisitionTolerance,
                        Facts(fixture, "measured displacement"));
                    Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth, Facts(fixture, "player health"));
                    Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture, "police health"));
                    Debug.Log("STRAIGHT_CLEARANCE delta=" + deltaTime + ", reverse=" + reverseCreationOrder +
                        ", progress=" + maximumProgress + ", displacement=" + Vector2.Distance(initialPosition, fixture.PolicePosition));
                }
            }
        }
        finally {
            snapshot.AssertSourcesUnchanged();
        }
    }

    /// <summary>Checks real collinear traversal until the complete padded rear footprint clears the seam.</summary>
    [UnityTest]
    public IEnumerator CollinearSeam_PreservesRearClearanceAndCrossesWithoutFalseStop() {
        AssertEmptyBootstrap();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        using (var fixture = new PursuitFixture()) {
            ConfigureCollinearRoute(fixture);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            float initialHealth = fixture.Player.CurrentHealth;
            float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
            float maximumProgress = fixture.Controller.CursorProgress;
            float paddedRear = float.NegativeInfinity;
            bool impact = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impact = true;
            for (int step = 0; step < Mathf.CeilToInt(8f / fixture.StepDelta); step++) {
                fixture.Step(1);
                maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
                Assert.AreEqual(0f, fixture.Controller.LastCommand.steering, SeamFacts(fixture, "straight seam steering"));
                paddedRear = fixture.Binding.body.MainCollider.bounds.min.y - fixture.Binding.navigationSettings.clearanceMargin * 0.5f;
                if (paddedRear > 8f) break;
            }
            // Cursor progress begins at the accepted start y=6, not at the graph node y=0.
            Assert.Greater(maximumProgress, 2f, SeamFacts(fixture, "cursor did not cross the collinear seam"));
            Assert.Greater(paddedRear, 8f, SeamFacts(fixture, "padded rear footprint did not clear the seam"));
            Assert.IsFalse(impact, SeamFacts(fixture, "collinear seam caused impact"));
            Assert.AreEqual(initialHealth, fixture.Player.CurrentHealth, SeamFacts(fixture, "collinear seam damaged target"));
            Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth, SeamFacts(fixture, "collinear seam damaged police"));
        }
    }

    /// <summary>Accepts a disposable width-three straight road while keeping old wide-clearance witnesses outside the certified corridor.</summary>
    [UnityTest]
    public IEnumerator SyntheticStraightWidthThree_MakesMeasuredProgressWithoutSideContact() {
        AssertEmptyBootstrap();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);

        var authored = LoadAuthoredReferences();
        foreach (float deltaTime in new[] { 0.02f, 0.03f }) {
            foreach (bool reverseCreationOrder in new[] { false, true }) {
                using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder, authored: authored)) {
                    ConfigureSyntheticStraight(fixture, 3f, 4f, 45f);
                    fixture.CreateBlocker(new Vector2(-2.5f, 18f), false, Vector2.one);
                    fixture.CreateBlocker(new Vector2(2.5f, 18f), false, Vector2.one);
                    Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                    Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                    float initialHealth = fixture.PoliceReceiver.CurrentHealth;
                    float initialPlayerHealth = fixture.Player.CurrentHealth;
                    Vector2 initialPosition = fixture.PolicePosition;
                    float maximumDisplacement = 0f;
                    int plannerAttempts = fixture.Controller.PlannerAttemptCount;
                    bool steeringWasZero = true;
                    bool impactObserved = false;
                    fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
                    int steps = Mathf.CeilToInt((Vector2.Distance(initialPosition, fixture.PlayerPosition) /
                        Mathf.Max(0.1f, fixture.Profile.motorSettings.cruiseSpeed) + 6f) / deltaTime);
                    for (int step = 0; step < steps; step++) {
                        fixture.Step(1);
                        steeringWasZero &= fixture.Controller.LastCommand.steering == 0f;
                        maximumDisplacement = Mathf.Max(maximumDisplacement, Vector2.Distance(initialPosition, fixture.PolicePosition));
                    }

                    Assert.IsTrue(steeringWasZero, SyntheticFacts(fixture, "synthetic side-witness steering"));
                    Assert.Greater(maximumDisplacement, fixture.ControllerSettings.acquisitionTolerance,
                        SyntheticFacts(fixture, "synthetic straight made no physical progress"));
                    Assert.Greater(fixture.PolicePosition.y, 19f, SyntheticFacts(fixture, "police never passed the side witnesses"));
                    Assert.AreEqual(initialHealth, fixture.PoliceReceiver.CurrentHealth,
                        SyntheticFacts(fixture, "synthetic side witness caused damage"));
                    Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth,
                        SyntheticFacts(fixture, "synthetic target health changed"));
                    Assert.IsFalse(impactObserved, SyntheticFacts(fixture, "synthetic straight produced physical impact"));
                    Assert.AreEqual(0, fixture.Controller.RamStarts, SyntheticFacts(fixture, "synthetic straight entered Ram"));
                    Assert.Greater(fixture.Controller.PlannerAttemptCount, plannerAttempts,
                        SyntheticFacts(fixture, "synthetic straight did not continue fresh planning"));
                    Assert.IsTrue(fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold ||
                        Mathf.Approximately(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold),
                        SyntheticFacts(fixture, "synthetic straight did not stop"));
                    Assert.IsTrue(WithinMeasuredStandoff(fixture), SyntheticFacts(fixture, "synthetic final collider gap"));
                    Debug.Log("SYNTHETIC_STRAIGHT delta=" + deltaTime + ", reverse=" + reverseCreationOrder +
                        ", displacement=" + maximumDisplacement + ", throttle=" + fixture.Controller.LastCommand.throttle +
                        ", steering=" + fixture.Controller.LastCommand.steering);
                }
            }
        }
    }

    /// <summary>Proves a force-limited fresh certificate brakes before a thin wall, including a larger-step recomputation.</summary>
    [UnityTest]
    public IEnumerator ForceLimitedStraight_UsesFractionalThrottleAndStopsBeforeThinWall() {
        AssertEmptyBootstrap();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);

        foreach (float deltaTime in new[] { 0.02f, 0.1f }) {
            using (var fixture = new PursuitFixture(deltaTime)) {
                fixture.Profile.baseMass = 4f;
                fixture.Profile.motorSettings.acceleration = 1.5f;
                fixture.Profile.motorSettings.maxEngineForce = 2f;
                fixture.Profile.motorSettings.brakeDeceleration = 1.2f;
                fixture.Profile.motorSettings.maxBrakeForce = 2.5f;
                fixture.Binding.body.Body.mass = fixture.Profile.baseMass;
                fixture.Binding.body.Motor.crashModeForceMultiplier = 0.2f;
                ConfigureSyntheticStraight(fixture, 3f, 2f, 45f);
                GameObject wall = fixture.CreateBlocker(new Vector2(0f, 14f), false, new Vector2(2.5f, 0.1f));
                Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                float initialHealth = fixture.PoliceReceiver.CurrentHealth;
                Vector2 initialPosition = fixture.PolicePosition;
                float accumulatedDisplacement = 0f;
                Vector2 previousPosition = fixture.PolicePosition;
                bool sawFractionalCommand = false;
                float minimumWallGap = float.PositiveInfinity;
                float acceleration = Mathf.Min(fixture.Profile.motorSettings.acceleration,
                    fixture.Profile.motorSettings.maxEngineForce / fixture.Binding.body.Body.mass);
                float braking = Mathf.Min(fixture.Profile.motorSettings.brakeDeceleration,
                    fixture.Profile.motorSettings.maxBrakeForce / fixture.Binding.body.Body.mass) *
                    fixture.ControllerSettings.comfort * fixture.Binding.body.Motor.crashModeForceMultiplier;
                float horizon = 2f * Mathf.Sqrt(2f * Vector2.Distance(initialPosition, wall.transform.position) / braking) +
                    2f * fixture.Profile.motorSettings.cruiseSpeed / acceleration;
                int steps = Mathf.CeilToInt(horizon / deltaTime);
                for (int step = 0; step < steps; step++) {
                    fixture.Step(1);
                    accumulatedDisplacement += Vector2.Distance(previousPosition, fixture.PolicePosition);
                    previousPosition = fixture.PolicePosition;
                    sawFractionalCommand |= fixture.Controller.LastCommand.throttle > 0f && fixture.Controller.LastCommand.throttle < 1f;
                    ColliderDistance2D distance = fixture.PoliceCollider.Distance(wall.GetComponent<Collider2D>());
                    Assert.IsTrue(distance.isValid, SyntheticFacts(fixture, "force-limited wall distance invalid"));
                    Assert.IsFalse(distance.isOverlapped, SyntheticFacts(fixture, "force-limited wall overlapped"));
                    Assert.GreaterOrEqual(distance.distance, fixture.ControllerSettings.stopGap,
                        SyntheticFacts(fixture, "force-limited wall breached stop gap"));
                    minimumWallGap = Mathf.Min(minimumWallGap, distance.distance);
                    if (sawFractionalCommand && distance.distance <= fixture.ControllerSettings.stopGap + fixture.ControllerSettings.acquisitionTolerance &&
                        fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold) break;
                }

                ColliderDistance2D wallDistance = fixture.PoliceCollider.Distance(wall.GetComponent<Collider2D>());
                Assert.IsTrue(sawFractionalCommand, SyntheticFacts(fixture, "force-limited case never exposed q below one"));
                Assert.Greater(accumulatedDisplacement, fixture.ControllerSettings.acquisitionTolerance,
                    SyntheticFacts(fixture, "force-limited case made no accumulated displacement"));
                Assert.IsTrue(wallDistance.isValid && !wallDistance.isOverlapped,
                    SyntheticFacts(fixture, "force-limited case final wall separation"));
                Assert.GreaterOrEqual(wallDistance.distance, fixture.ControllerSettings.stopGap,
                    SyntheticFacts(fixture, "force-limited case final wall stop gap"));
                Assert.IsTrue(fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold ||
                    Mathf.Approximately(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold),
                    SyntheticFacts(fixture, "force-limited case did not settle"));
                Assert.AreEqual(initialHealth, fixture.PoliceReceiver.CurrentHealth,
                    SyntheticFacts(fixture, "force-limited case damaged police"));
                Assert.AreEqual(0, fixture.Controller.RamStarts, SyntheticFacts(fixture, "force-limited case entered Ram"));
                Debug.Log("FORCE_LIMITED_STRAIGHT freshStepCase delta=" + deltaTime + ", accumulatedDisplacement=" + accumulatedDisplacement +
                    ", minimumWallGap=" + minimumWallGap + ", throttle=" + fixture.Controller.LastCommand.throttle +
                    ", scope=independent-fresh-step-case");
            }
        }
    }

    /// <summary>Requires fresh straight proof to refuse padding, impulse, sensor, static-query, and work-budget hazards.</summary>
    [UnityTest]
    public IEnumerator StraightClearance_FreshRefusalsBrakeWithoutFallback() {
        AssertEmptyBootstrap();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);

        using (var padding = new PursuitFixture()) {
            ConfigureSyntheticStraight(padding, 3f, 4f, 45f);
            padding.SetPoliceColliderOffset(new Vector2(0.35f, 0f));
            Assert.IsTrue(padding.Controller.TryBind(padding.Binding, out string reason), reason);
            Assert.IsTrue(padding.Controller.TrySetTarget(padding.Player, out reason), reason);
            padding.Step(1);
            Assert.Greater(padding.Controller.LastCommand.throttle, 0f, SyntheticFacts(padding, "padding setup did not accept a positive road step"));
            Vector2 right = new Vector2(padding.PoliceForward.y, -padding.PoliceForward.x);
            GameObject paddingWall = padding.CreateBlocker(padding.PolicePosition + right * 1.1f, false, new Vector2(0.2f, 0.2f));
            ColliderDistance2D paddingDistance = padding.PoliceCollider.Distance(paddingWall.GetComponent<Collider2D>());
            Assert.IsTrue(paddingDistance.isValid, SyntheticFacts(padding, "padding-only wall distance invalid"));
            Assert.IsFalse(paddingDistance.isOverlapped, SyntheticFacts(padding, "padding-only wall initially overlapped"));
            Assert.Greater(paddingDistance.distance, 0f, SyntheticFacts(padding, "padding-only wall touched raw collider"));
            padding.Step(1);
            AssertRefused(padding, "padding-only wall");
        }

        using (var impulse = new PursuitFixture()) {
            impulse.Profile.motorSettings.lateralGrip = 0.1f;
            ConfigureSyntheticStraight(impulse, 3f, 4f, 45f);
            Assert.IsTrue(impulse.Controller.TryBind(impulse.Binding, out string reason), reason);
            Assert.IsTrue(impulse.Controller.TrySetTarget(impulse.Player, out reason), reason);
            impulse.Step(1);
            Vector2 lateralImpulse = new Vector2(impulse.PoliceForward.y, -impulse.PoliceForward.x) * 14f;
            impulse.Binding.body.Body.AddForce(lateralImpulse, ForceMode2D.Impulse);
            impulse.Step(1);
            AssertRefused(impulse, "low-grip lateral impulse");
        }

        using (var sensor = new PursuitFixture()) {
            sensor.ControllerSettings.sensorBuffer = 1;
            ConfigureSyntheticStraight(sensor, 3f, 4f, 45f);
            sensor.CreateBlocker(new Vector2(0f, 12f), false, Vector2.one);
            sensor.CreateBlocker(new Vector2(0f, 13f), false, Vector2.one);
            Assert.IsTrue(sensor.Controller.TryBind(sensor.Binding, out string reason), reason);
            Assert.IsTrue(sensor.Controller.TrySetTarget(sensor.Player, out reason), reason);
            sensor.Step(1);
            Assert.IsTrue(sensor.Sensor.LastQuerySaturated, SyntheticFacts(sensor, "raw sensor did not report saturation"));
            AssertRefused(sensor, "raw sensor saturation");
        }

        using (var staticQuery = new PursuitFixture()) {
            ConfigureSyntheticStraight(staticQuery, 3f, 4f, 45f);
            var capturedQuery = new CapturingStaticClearance(staticQuery.Physics, Vector2.zero, 1);
            staticQuery.Binding.staticClearance = capturedQuery;
            Assert.IsTrue(staticQuery.Controller.TryBind(staticQuery.Binding, out string reason), reason);
            Assert.IsTrue(staticQuery.Controller.TrySetTarget(staticQuery.Player, out reason), reason);
            staticQuery.Step(1);
            Assert.GreaterOrEqual(capturedQuery.RawCount, capturedQuery.Capacity,
                SyntheticFacts(staticQuery, "static raw overlap did not reach capacity"));
            Assert.Greater(capturedQuery.MovingCount, 0,
                SyntheticFacts(staticQuery, "static raw saturation did not include the moving self collider"));
            AssertRefused(staticQuery, "static raw saturation including ignored self");
        }

        using (var work = new PursuitFixture()) {
            ConfigureSyntheticStraight(work, 3f, 4f, 45f);
            Assert.IsTrue(work.Controller.TryBind(work.Binding, out string reason), reason);
            Assert.IsTrue(work.Controller.TrySetTarget(work.Player, out reason), reason);
            work.SetRuntimeCursorWork(1);
            work.Step(1);
            AssertRefused(work, "exhausted cursor work budget");
        }
    }

    /// <summary>Runs the long-body collinear seam acceptance where rear support exceeds the planner lookahead minimum.</summary>
    [UnityTest]
    public IEnumerator LongBodyCollinearSeam_ClearsRearSupportBeforeFreshStop() {
        AssertEmptyBootstrap();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        var owned = new List<UnityEngine.Object>();
        try {
            PursuitFixture.AuthoredSetup authored = CreateLongBodyAuthoredSetup(owned);
            using (var fixture = new PursuitFixture(0.02f, authored: authored)) {
                ConfigureCollinearRoute(fixture);
                fixture.ReplacePlayer(new Vector2(0f, 19f));
                Assert.Greater(fixture.Profile.colliderSize.y * 0.5f, fixture.ControllerSettings.lookaheadMin,
                    SeamFacts(fixture, "long-body setup did not exceed lookahead minimum"));
                Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
                float initialPlayerHealth = fixture.Player.CurrentHealth;
                bool impactObserved = false;
                fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
                float maximumProgress = fixture.Controller.CursorProgress;
                float paddedRear = float.NegativeInfinity;
                int steps = Mathf.CeilToInt(16f / fixture.StepDelta);
                for (int step = 0; step < steps; step++) {
                    fixture.Step(1);
                    maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
                    paddedRear = fixture.Binding.body.MainCollider.bounds.min.y - fixture.Binding.navigationSettings.clearanceMargin * 0.5f;
                    Assert.AreEqual(0f, fixture.Controller.LastCommand.steering, SeamFacts(fixture, "long-body seam steering"));
                    if (paddedRear > 8f && fixture.PoliceVelocity.magnitude <= fixture.ControllerSettings.stoppedSpeedThreshold) break;
                }

                Assert.Greater(maximumProgress, 2f, SeamFacts(fixture, "long-body cursor did not cross seam"));
                Assert.Greater(paddedRear, 8f, SeamFacts(fixture, "long-body padded rear did not clear seam"));
                Assert.IsFalse(impactObserved, SeamFacts(fixture, "long-body seam caused impact"));
                Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth, SeamFacts(fixture, "long-body police damaged"));
                Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth, SeamFacts(fixture, "long-body target damaged"));
            }
        }
        finally {
            foreach (UnityEngine.Object asset in owned) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    static void ConfigureSyntheticStraight(PursuitFixture fixture, float width, float policeY, float targetY) {
        var document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 20f, 80f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 60f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "syntheticStraight", fromNodeId = "a", toNodeId = "b", usableWidth = width,
            speedLimit = 5f, allowedRoles = new List<VehicleRole> { VehicleRole.Police }, orderedPoints = new List<Vector2> { Vector2.zero, Vector2.up * 60f } });
        fixture.Binding.navigation = document;
        fixture.Binding.graph = new RoadGraphRuntime(document);
        fixture.Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        fixture.Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(fixture.Physics, Vector2.zero, true, 16);
        fixture.SetPolicePosition(new Vector2(0f, policeY));
        fixture.SetPoliceRotation(0f);
        fixture.ReplacePlayer(new Vector2(0f, targetY));
    }

    static PursuitFixture.AuthoredSetup CreateLongBodyAuthoredSetup(List<UnityEngine.Object> owned) {
        PursuitFixture.AuthoredSetup source = LoadAuthoredReferences();
        var profile = UnityEngine.Object.Instantiate(source.profile); owned.Add(profile);
        var damage = UnityEngine.Object.Instantiate(source.damage); owned.Add(damage);
        var vehicle = UnityEngine.Object.Instantiate(source.vehicle); owned.Add(vehicle);
        var behavior = UnityEngine.Object.Instantiate(source.behavior); owned.Add(behavior);
        profile.colliderSize = new Vector2(profile.colliderSize.x, 6f);
        vehicle.sharedNpc = profile;
        return new PursuitFixture.AuthoredSetup { profile = profile, damage = damage, vehicle = vehicle, behavior = behavior };
    }

    static void AssertRefused(PursuitFixture fixture, string detail) {
        Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle, SyntheticFacts(fixture, detail + " admitted throttle"));
        Assert.Greater(fixture.Controller.LastCommand.brake, 0f, SyntheticFacts(fixture, detail + " did not brake"));
        Assert.IsFalse(fixture.Controller.LastCommand.reverseAllowed, SyntheticFacts(fixture, detail + " permitted reverse"));
        Assert.AreEqual(0, fixture.Controller.RamStarts, SyntheticFacts(fixture, detail + " entered Ram"));
    }

    static string SyntheticFacts(PursuitFixture fixture, string detail) {
        return Facts(fixture, "synthetic: " + detail);
    }

    sealed class CapturingStaticClearance : IAreaClearanceQuery {
        readonly PhysicsScene2D physics;
        readonly Vector2 origin;
        readonly Collider2D[] results;
        readonly ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        readonly PhysicsSceneAreaClearanceQuery inner;
        internal int RawCount { get; private set; }
        internal int MovingCount { get; private set; }
        internal int Capacity => results.Length;

        internal CapturingStaticClearance(PhysicsScene2D physics, Vector2 origin, int capacity) {
            this.physics = physics; this.origin = origin; results = new Collider2D[Mathf.Max(1, capacity)];
            inner = new PhysicsSceneAreaClearanceQuery(physics, origin, true, capacity);
        }

        /// <summary>Records bounded native occupancy evidence before forwarding the exact rectangle to the real adapter.</summary>
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            int count = physics.OverlapBox(center + origin, footprint, headingDegrees, filter, results);
            RawCount = Mathf.Max(RawCount, count);
            int moving = 0;
            for (int index = 0; index < Mathf.Min(count, results.Length); index++) {
                Collider2D collider = results[index];
                if (collider != null && collider.attachedRigidbody != null) moving++;
            }
            if (count >= Capacity) MovingCount = Mathf.Max(MovingCount, moving);
            return inner.IsAreaClear(center, footprint, headingDegrees);
        }
    }

    static PursuitFixture.AuthoredSetup LoadAuthoredReferences() {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(PoliceDataPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(PoliceVehiclePath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(PursueBehaviorPath);
        Assert.IsNotNull(profile, PoliceDataPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.IsNotNull(vehicle, PoliceVehiclePath);
        Assert.IsNotNull(behavior, PursueBehaviorPath);
        Assert.AreSame(profile, vehicle.sharedNpc);
        return new PursuitFixture.AuthoredSetup { profile = profile, damage = damage, vehicle = vehicle, behavior = behavior };
    }

    static void VerifySources() {
        var map = AssetDatabase.LoadAssetAtPath<MapData>(MapPath);
        var navigation = AssetDatabase.LoadAssetAtPath<TrafficMapData>(NavigationPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        Assert.IsNotNull(map, MapPath);
        Assert.IsNotNull(navigation, NavigationPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.AreEqual("4c46aef37a1f4f468fc0915175730643", map.mapId);
        Assert.AreEqual("NarrowDistrict", map.sceneName);
        Assert.AreEqual(map.mapId, navigation.ResolveDocument().mapId);
        Assert.IsTrue(damage.TryResolve("nd-civilian", out _));
    }

    static void AssertEmptyBootstrap() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
    }

    static void AssertOldBStaticWitnesses(PursuitFixture fixture) {
        var witnesses = new Collider2D[64];
        var filter = new ContactFilter2D { useTriggers = false };
        int count = fixture.Physics.OverlapBox(new Vector2(6f, -11.25f), new Vector2(6.60768f, 9.10768f), 0f, filter, witnesses);
        Assert.Less(count, witnesses.Length, "old-B witness query must not saturate");
        int staticCount = 0;
        var names = new HashSet<string>();
        for (int index = 0; index < count; index++) {
            Collider2D collider = witnesses[index];
            if (collider == null || collider.isTrigger || collider.attachedRigidbody != null) continue;
            staticCount++;
            Assert.IsFalse(string.IsNullOrEmpty(collider.gameObject.name), "old-B witness must retain its authored name");
            names.Add(collider.gameObject.name);
        }
        Assert.GreaterOrEqual(staticCount, 2, "old-B native query rectangle must contain two static witnesses");
        Assert.GreaterOrEqual(names.Count, 2, "old-B static witnesses must remain distinct named solids");
        Assert.IsTrue(names.Contains("Kenney 679") && names.Contains("Produce crate"), "the two observed old-B native witnesses must remain present");
    }

    static float PlannerLastAttemptSeconds(PolicePursuitTargetPlanner planner) {
        var field = typeof(PolicePursuitTargetPlanner).GetField("lastAttemptSeconds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "planner last-attempt diagnostic field is required for cadence evidence");
        return (float)field.GetValue(planner);
    }

    static void ConfigureCollinearRoute(PursuitFixture fixture) {
        var document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 20f, 40f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 8f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 0f, y = 20f });
        document.edges.Add(CollinearEdge("e0", "a", "b", 0f, 8f));
        document.edges.Add(CollinearEdge("e1", "b", "c", 8f, 20f));
        fixture.Binding.navigation = document;
        fixture.Binding.graph = new RoadGraphRuntime(document);
        fixture.Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        fixture.Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(fixture.Physics, Vector2.zero, true, 8);
        fixture.SetPolicePosition(new Vector2(0f, 6f));
        fixture.SetPoliceRotation(0f);
        fixture.ReplacePlayer(new Vector2(0f, 14f));
    }

    static RoadEdgeRecord CollinearEdge(string id, string from, string to, float start, float end) {
        return new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f,
            speedLimit = 5f, allowedRoles = new List<VehicleRole> { VehicleRole.Police },
            orderedPoints = new List<Vector2> { new Vector2(0f, start), new Vector2(0f, end) } };
    }

    static bool WithinMeasuredStandoff(PursuitFixture fixture) {
        float lower = fixture.ControllerSettings.stopGap;
        float upper = lower + fixture.ControllerSettings.acquisitionTolerance;
        return (fixture.ColliderGap >= lower || Mathf.Approximately(fixture.ColliderGap, lower)) &&
            (fixture.ColliderGap <= upper || Mathf.Approximately(fixture.ColliderGap, upper));
    }

    static void AssertFinite(PursuitFixture fixture, string facts) {
        Assert.IsFalse(float.IsNaN(fixture.PolicePosition.x) || float.IsInfinity(fixture.PolicePosition.x), facts);
        Assert.IsFalse(float.IsNaN(fixture.PolicePosition.y) || float.IsInfinity(fixture.PolicePosition.y), facts);
        Assert.IsFalse(float.IsNaN(fixture.PoliceVelocity.x) || float.IsInfinity(fixture.PoliceVelocity.x), facts);
        Assert.IsFalse(float.IsNaN(fixture.PoliceVelocity.y) || float.IsInfinity(fixture.PoliceVelocity.y), facts);
    }

    static string Facts(PursuitFixture fixture, string detail = "straight-clearance") {
        return "case=" + detail + ", phase=" + fixture.Controller.CurrentPhase + ", cursor=" + fixture.Controller.CursorProgress +
            ", planner=" + fixture.Controller.PlannerAttemptCount + ", ramStarts=" + fixture.Controller.RamStarts +
            ", gap=" + fixture.ColliderGap + ", policePosition=" + fixture.PolicePosition +
            ", policeVelocity=" + fixture.PoliceVelocity + ", playerHealth=" + fixture.Player.CurrentHealth;
    }

    static string SeamFacts(PursuitFixture fixture, string detail) => Facts(fixture, "collinear-seam: " + detail);
}
