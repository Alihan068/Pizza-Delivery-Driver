using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Bounded native probes for the far ordinary-Road dispatch predicate.</summary>
public sealed class PoliceFarApproachPlayTests {
    const string MapPath = "Assets/ScriptableObjects/Map_NarrowDistrict.asset";
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string PoliceDataPath = "Assets/ScriptableObjects/Police/PoliceStandardData.asset";
    const string PoliceVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string PursueBehaviorPath = "Assets/ScriptableObjects/Police/PolicePursueBehavior.asset";

    /// <summary>Checks the authored turn candidate only until far Road progress and normal refresh cadence are measurable.</summary>
    [UnityTest]
    public IEnumerator NativeFarTurn_RoadProgressAndRefreshRemainBounded() {
        AssertEmptyBootstrap();
        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.Greater(snapshot.ColliderCount, 0);
            foreach (float deltaTime in new[] { 0.02f, 0.03f })
                RunFarCase(snapshot, deltaTime, new Vector2(6f, -12.5f), 0f, new Vector2(34f, 5f), "native-far-turn", "turn");
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

    /// <summary>Checks the authored final-only candidate on its straight edge without requiring final completion.</summary>
    [UnityTest]
    public IEnumerator NativeFarFinal_RoadProgressAndRefreshRemainBounded() {
        AssertEmptyBootstrap();
        VerifySourceAssetBindings();
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot, "The operator must capture NarrowDistrict before the TestRunner bootstrap.");
            Assert.Greater(snapshot.ColliderCount, 0);
            foreach (float deltaTime in new[] { 0.02f, 0.03f })
                RunFarCase(snapshot, deltaTime, new Vector2(20f, 5f), -90f, new Vector2(34f, 5f), "native-far-final", "final");
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

    /// <summary>Returns the isolated authored probe to Edit Mode after a physical diagnostic failure.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunFarCase(PoliceNarrowDistrictGeometrySnapshot snapshot, float deltaTime, Vector2 policePosition,
        float heading, Vector2 targetPosition, string caseId, string witnessCase) {
        PursuitFixture.AuthoredSetup authored = LoadAuthoredReferences();
        try {
            using (var fixture = new PursuitFixture(deltaTime, authored: authored)) {
                AssertFixtureClones(authored, fixture, caseId + "-" + deltaTime);
                using (snapshot.StageInto(fixture.FixtureScene)) {
                    MapNavigationDocument navigation = snapshot.ResolveNavigation();
                    Assert.IsNotNull(navigation, Facts(fixture, caseId + " detached navigation"));
                    fixture.ConfigureAuthoredNavigation(navigation, snapshot.Origin, policePosition, heading, targetPosition);
                    AssertNativeWitnesses(fixture, witnessCase);
                    Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), caseId + ": " + reason);
                    Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), caseId + ": " + reason);

                    float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
                    float initialPlayerHealth = fixture.Player.CurrentHealth;
                    bool impactObserved = false;
                    fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
                    Vector2 previousPosition = fixture.PolicePosition;
                    float movedDistance = 0f;
                    float maximumProgress = fixture.Controller.CursorProgress;
                    int previousAttempts = fixture.Controller.PlannerAttemptCount;
                    var plannerTimes = new List<float>();
                    bool sawCommit = false;
                    bool sawFinal = false;
                    bool sawHolding = false;
                    int steps = Mathf.CeilToInt(3f / deltaTime);
                    bool criteriaReached = false;
                    for (int step = 0; step < steps; step++) {
                        fixture.Step(1);
                        Vector2 currentPosition = fixture.PolicePosition;
                        movedDistance += Vector2.Distance(previousPosition, currentPosition);
                        previousPosition = currentPosition;
                        maximumProgress = Mathf.Max(maximumProgress, fixture.Controller.CursorProgress);
                        Assert.AreEqual(PolicePursuitController.TraversalPhase.Road, fixture.Controller.CurrentPhase,
                            Facts(fixture, caseId + " left Road at step=" + step));
                        Assert.IsFalse(fixture.Controller.IsTurnCommitted, Facts(fixture, caseId + " committed at step=" + step));
                        Assert.AreNotEqual(PolicePursuitController.TraversalPhase.FinalConnector, fixture.Controller.CurrentPhase,
                            Facts(fixture, caseId + " entered FinalConnector at step=" + step));
                        Assert.AreNotEqual(PolicePursuitController.TraversalPhase.Holding, fixture.Controller.CurrentPhase,
                            Facts(fixture, caseId + " entered Holding at step=" + step));
                        Assert.AreEqual(0f, fixture.Controller.LastCommand.steering,
                            Facts(fixture, caseId + " steered at step=" + step));
                        AssertFarOnRoadAim(fixture, witnessCase, caseId + " step=" + step);
                        sawCommit |= fixture.Controller.IsTurnCommitted;
                        sawFinal |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.FinalConnector;
                        sawHolding |= fixture.Controller.CurrentPhase == PolicePursuitController.TraversalPhase.Holding;
                        if (fixture.Controller.PlannerAttemptCount > previousAttempts) {
                            Assert.AreEqual(previousAttempts + 1, fixture.Controller.PlannerAttemptCount,
                                Facts(fixture, caseId + " planner counter skipped"));
                            plannerTimes.Add(fixture.LastControllerClock);
                            previousAttempts = fixture.Controller.PlannerAttemptCount;
                        }
                        criteriaReached = movedDistance > 1f && maximumProgress > 1f &&
                            fixture.Controller.PlannerAttemptCount >= 3 && plannerTimes.Count >= 3;
                        if (criteriaReached) break;
                    }

                    Assert.IsTrue(criteriaReached, Facts(fixture, caseId + " far bounded criteria not reached"));
                    Assert.Greater(movedDistance, 1f, Facts(fixture, caseId + " measured displacement"));
                    Assert.Greater(maximumProgress, 1f, Facts(fixture, caseId + " cursor progress"));
                    Assert.GreaterOrEqual(fixture.Controller.PlannerAttemptCount, 3, Facts(fixture, caseId + " planner attempts"));
                    AssertNormalPlannerCadence(fixture, plannerTimes, caseId);
                    Assert.IsFalse(sawCommit, Facts(fixture, caseId + " committed diagnostic state"));
                    Assert.IsFalse(sawFinal, Facts(fixture, caseId + " final diagnostic state"));
                    Assert.IsFalse(sawHolding, Facts(fixture, caseId + " holding diagnostic state"));
                    Assert.IsFalse(impactObserved, Facts(fixture, caseId + " impact callback"));
                    Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth,
                        Facts(fixture, caseId + " police HP changed"));
                    Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth,
                        Facts(fixture, caseId + " player HP changed"));
                    Debug.Log("NATIVE_FAR_APPROACH farRoadOnly acceptance case=" + caseId + ", delta=" + deltaTime +
                        ", movedDistance=" + movedDistance + ", maxProgress=" + maximumProgress +
                        ", attempts=" + fixture.Controller.PlannerAttemptCount + ", cadenceCount=" + plannerTimes.Count +
                        ", finalPhase=" + fixture.Controller.CurrentPhase + ", facts=" + Facts(fixture, caseId));
                }
                snapshot.AssertSourcesUnchanged();
            }
        }
        finally {
            snapshot.AssertSourcesUnchanged();
        }
    }

    static void AssertNormalPlannerCadence(PursuitFixture fixture, List<float> plannerTimes, string caseId) {
        Assert.GreaterOrEqual(plannerTimes.Count, 3, Facts(fixture, caseId + " requires two refresh intervals"));
        for (int index = 1; index < plannerTimes.Count; index++) {
            float interval = plannerTimes[index] - plannerTimes[index - 1];
            float clockRoundoff = 8f / 8388608f * Mathf.Max(1f, Mathf.Max(plannerTimes[index], plannerTimes[index - 1]));
            Assert.GreaterOrEqual(interval, fixture.Binding.navigationSettings.refreshInterval - clockRoundoff,
                Facts(fixture, caseId + " planner refresh lower bound"));
            float upper = fixture.Binding.navigationSettings.refreshInterval + fixture.StepDelta + clockRoundoff;
            Assert.LessOrEqual(interval, upper, Facts(fixture, caseId + " planner refresh upper bound"));
        }
    }

    static void AssertNativeWitnesses(PursuitFixture fixture, string witnessCase) {
        var colliders = new Collider2D[64];
        var filter = new ContactFilter2D { useTriggers = false };
        Vector2 center = witnessCase == "turn" ? new Vector2(6f, -11.25f) : new Vector2(21.25f, 5f);
        Vector2 size = witnessCase == "turn" ? new Vector2(6.60768f, 9.10768f) : new Vector2(9.10768f, 6.60768f);
        int count = fixture.Physics.OverlapBox(center, size, 0f, filter, colliders);
        Assert.Less(count, colliders.Length, Facts(fixture, witnessCase + " witness query saturated"));
        var names = new HashSet<string>();
        for (int index = 0; index < count; index++) {
            Collider2D collider = colliders[index];
            if (collider == null || collider.isTrigger || collider.attachedRigidbody != null) continue;
            names.Add(collider.gameObject.name);
        }
        if (witnessCase == "turn") {
            Assert.IsTrue(names.Contains("Kenney 679"), Facts(fixture, "turn witness Kenney 679 missing"));
            Assert.IsTrue(names.Contains("Produce crate"), Facts(fixture, "turn witness Produce crate missing"));
        }
        else Assert.IsTrue(names.Contains("Street bin"), Facts(fixture, "final witness Street bin missing"));
    }

    static void AssertFarOnRoadAim(PursuitFixture fixture, string witnessCase, string detail) {
        Vector2 aim = MapNavigationCoordinates.WorldToLocal(fixture.Controller.LastAimPoint, fixture.Binding.mapOriginWorld);
        float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(aim.x), Mathf.Abs(aim.y)));
        float epsilon = 8f / 8388608f * scale;
        if (witnessCase == "turn") {
            Assert.That(Mathf.Abs(aim.x - 6f), Is.LessThanOrEqualTo(epsilon), Facts(fixture, detail + " turn aim x"));
            Assert.GreaterOrEqual(aim.y, -22.5f - epsilon, Facts(fixture, detail + " turn aim lower endpoint"));
            Assert.LessOrEqual(aim.y, 5f + epsilon, Facts(fixture, detail + " turn aim upper endpoint"));
        }
        else {
            Assert.That(Mathf.Abs(aim.y - 5f), Is.LessThanOrEqualTo(epsilon), Facts(fixture, detail + " final aim y"));
            Assert.GreaterOrEqual(aim.x, 6f - epsilon, Facts(fixture, detail + " final aim lower endpoint"));
            Assert.LessOrEqual(aim.x, 29.5f + epsilon, Facts(fixture, detail + " final aim road endpoint"));
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
        Assert.IsNotNull(behavior.driving);
        return new PursuitFixture.AuthoredSetup { profile = profile, damage = damage, vehicle = vehicle, behavior = behavior };
    }

    static void AssertFixtureClones(PursuitFixture.AuthoredSetup authored, PursuitFixture fixture, string caseId) {
        Assert.AreNotSame(authored.profile, fixture.Profile, Facts(fixture, caseId + " profile clone"));
        Assert.AreNotSame(authored.vehicle, fixture.Binding.vehicle, Facts(fixture, caseId + " vehicle clone"));
        Assert.AreNotSame(authored.behavior, fixture.Binding.behavior, Facts(fixture, caseId + " behavior clone"));
        Assert.AreSame(fixture.Profile, fixture.Binding.vehicle.sharedNpc, Facts(fixture, caseId + " profile wrapper"));
        Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile), Facts(fixture, caseId + " profile values"));
        Assert.AreEqual(JsonUtility.ToJson(authored.behavior.driving), JsonUtility.ToJson(fixture.ControllerSettings),
            Facts(fixture, caseId + " driving values"));
    }

    static void VerifySourceAssetBindings() {
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

    static string Facts(PursuitFixture fixture, string detail) {
        string edge = fixture.Cursor != null && fixture.Cursor.IsBound ? fixture.Cursor.CurrentAnchor.edgeId : "<unbound>";
        float remaining = fixture.Cursor != null && fixture.Cursor.IsBound ? fixture.Cursor.RemainingDistance : float.NaN;
        return "case=" + detail + ", phase=" + fixture.Controller.CurrentPhase + ", edge=" + edge +
            ", progress=" + F(fixture.Controller.CursorProgress) + ", remaining=" + F(remaining) +
            ", planner=" + fixture.Controller.PlannerAttemptCount + ", position=" + V(fixture.PolicePosition) +
            ", velocity=" + V(fixture.PoliceVelocity) + ", rotation=" + F(fixture.Binding.body.Body.rotation) +
            ", angularVelocity=" + F(fixture.Binding.body.Body.angularVelocity) + ", forward=" + V(fixture.PoliceForward) +
            ", aim=" + V(fixture.Controller.LastAimPoint) + ", steering=" + F(fixture.Controller.LastCommand.steering) +
            ", throttle=" + F(fixture.Controller.LastCommand.throttle) + ", targetSpeed=" + F(fixture.Controller.LastCommand.targetSpeed);
    }

    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 value) => "(" + F(value.x) + "," + F(value.y) + ")";
}
