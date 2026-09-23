using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Native NarrowDistrict recovery acceptance: Ram impact, bounded reverse, gate rejoin, and fresh pursuit.</summary>
public sealed class PoliceNarrowDistrictRecoveryAcceptanceTests {
    const string NavigationPath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset";
    const string DamagePath = "Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset";
    const string PoliceDataPath = "Assets/ScriptableObjects/Police/PoliceStandardData.asset";
    const string PoliceVehiclePath = "Assets/ScriptableObjects/Police/PoliceStandard.asset";
    const string RamBehaviorPath = "Assets/ScriptableObjects/Police/PoliceRamBehavior.asset";

    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Proves one authored police life survives a genuine native Ram contact, performs its finite
    /// reverse, reaches the measured road gate, releases recovery, and resumes fresh pursuit.
    /// </summary>
    [UnityTest]
    public IEnumerator NativeRamRecovery_RejoinsGateAndResumesFreshPursuit() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        VerifySourceBindings();

        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            Assert.IsNotNull(snapshot);
            Assert.IsNotEmpty(snapshot.Token);
            Assert.Greater(snapshot.ColliderCount, 0);

            var authored = LoadAuthored();
            using (var fixture = new PursuitFixture(0.02f, authored: authored)) {
                Assert.AreNotSame(authored.profile, fixture.Profile);
                Assert.AreNotSame(authored.vehicle, fixture.Binding.vehicle);
                Assert.AreNotSame(authored.behavior, fixture.Binding.behavior);
                using (snapshot.StageInto(fixture.FixtureScene)) {
                    var navigation = snapshot.ResolveNavigation();
                    Assert.IsNotNull(navigation);
                    fixture.ConfigureAuthoredNavigation(navigation, snapshot.Origin,
                        new Vector2(6f, -12.5f), 0f, new Vector2(6f, -9.45f));

                    // Insert a thin stationary obstacle only after the impacted player has actually
                    // cleared it. Spawning a unit box inside the player would push both vehicles
                    // backwards and test depenetration instead of a certified powered reverse.
                    // The settled police front is near y=-9.2: y=-8.5 leaves room for its
                    // unchanged padded one-step envelope while remaining inside the 1.2 stop gap.
                    Vector2 blockerPosition = new Vector2(6f, -8.5f);
                    Vector2 blockerSize = new Vector2(1f, 0.12f);
                    var reverseBlocker = fixture.CreateBlocker(blockerPosition, false, blockerSize);
                    // This obstacle is disabled before the first simulation. Initialize its authored
                    // Transform as well as its physics pose so reactivation cannot restore origin.
                    reverseBlocker.transform.position = blockerPosition;
                    reverseBlocker.SetActive(false);

                    // Both fixture colliders are centered on the authored pilot lane; no collider
                    // or body offset is guessed, and no pose/velocity is written after setup.
                    Assert.AreEqual(fixture.PoliceCollider.bounds.center.x,
                        fixture.Player.GetComponent<Collider2D>().bounds.center.x, 0.0001f);
                    Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                    Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                    typeof(PoliceRecoveryIntegration).GetField("diagnosticEnabled", PrivateInstance).SetValue(fixture.Recovery, true);

                    float initialHealth = fixture.PoliceReceiver.CurrentHealth;
                    int callbacks = 0;
                    fixture.PoliceReceiver.ImpactObserved += _ => callbacks++;
                    bool sawReverse = false;
                    bool sawNegativeVelocity = false;
                    bool sawEvidence = false;
                    bool sawGate = false;
                    bool sawResume = false;
                    bool sawFreshPursuit = false;
                    bool sawReverseCommand = false;
                    bool reverseBlockerActivated = false;
                    bool reverseBlockerRemoved = false;
                    float maximumReverseTravel = 0f;
                    float measuredSignedReverseMotion = 0f;
                    float measuredReverseWithBraking = 0f;
                    Vector2 reverseAxis = fixture.PoliceForward;
                    Vector2 previousPolicePosition = fixture.PolicePosition;
                    int resumePlannerAttempt = -1;
                    var observations = new System.Text.StringBuilder();
                    string firstRecoveryDiagnostic = null;
                    string diagnosticHistory = null;
                    float nextObservation = 1f;
                    int steps = Mathf.CeilToInt(30f / fixture.StepDelta);
                    for (int step = 0; step < steps; step++) {
                        fixture.Step(1);
                        if (!reverseBlockerActivated && callbacks > 0 && fixture.Player.GetComponent<Collider2D>().bounds.min.y >
                            blockerPosition.y + blockerSize.y * 0.5f + fixture.ControllerSettings.acquisitionTolerance * 2f) {
                            reverseBlocker.SetActive(true);
                            reverseBlockerActivated = true;
                            Assert.That(Vector2.Distance(reverseBlocker.GetComponent<Rigidbody2D>().position, blockerPosition), Is.LessThan(0.0001f),
                                "The reactivated stationary obstacle must retain its fixture setup pose.");
                            observations.AppendLine(BlockerFacts(fixture, reverseBlocker));
                            Assert.Greater(reverseBlocker.GetComponent<Collider2D>().Distance(fixture.Player.GetComponent<Collider2D>()).distance,
                                fixture.ControllerSettings.acquisitionTolerance, "Obstacle insertion must not overlap or depenetrate the moving player.");
                            Assert.Greater(reverseBlocker.GetComponent<Collider2D>().Distance(fixture.PoliceCollider).distance,
                                fixture.Binding.navigationSettings.clearanceMargin, "Obstacle insertion must not overlap the recovering police.");
                        }
                        Vector2 currentPolicePosition = fixture.PolicePosition;
                        bool reverseAuthorized = fixture.MotorReversePermitted && fixture.Controller.LastCommand.reverseAllowed;
                        if (reverseBlockerActivated) {
                            float signedReverseStep = Vector2.Dot(previousPolicePosition - currentPolicePosition, reverseAxis);
                            if (reverseAuthorized && fixture.ForwardSpeed < 0f && signedReverseStep > 0f)
                                measuredSignedReverseMotion += signedReverseStep;
                            if ((reverseAuthorized || sawReverse) && fixture.Recovery.IsRecovering && fixture.ForwardSpeed < 0f && signedReverseStep > 0f)
                                measuredReverseWithBraking += signedReverseStep;
                            // Braking revokes propulsion permission, not the genuine negative
                            // displacement already initiated by the bounded reverse command.
                            if (!reverseBlockerRemoved && measuredReverseWithBraking >= 0.15f) {
                                reverseBlocker.SetActive(false);
                                reverseBlockerRemoved = true;
                            }
                        }
                        previousPolicePosition = currentPolicePosition;
                        sawReverseCommand |= fixture.Controller.LastCommand.reverseAllowed;
                        sawReverse |= reverseAuthorized && fixture.ForwardSpeed < 0f;
                        sawNegativeVelocity |= fixture.ForwardSpeed < 0f;
                        sawEvidence |= RecoveryFlag(fixture, "hasEvidence");
                        sawGate |= RecoveryFlag(fixture, "hasGate");
                        maximumReverseTravel = Mathf.Max(maximumReverseTravel, fixture.Recovery.ReverseTravel);
                        string stepDiagnostic = (string)typeof(PoliceRecoveryIntegration).GetField("firstDiagnostic", PrivateInstance).GetValue(fixture.Recovery);
                        if (firstRecoveryDiagnostic == null && stepDiagnostic != null) firstRecoveryDiagnostic = stepDiagnostic;
                        var history = (System.Text.StringBuilder)typeof(PoliceRecoveryIntegration).GetField("diagnosticHistory", PrivateInstance).GetValue(fixture.Recovery);
                        if (history != null) diagnosticHistory = history.ToString();
                        if (fixture.Recovery.ResumeReady) {
                            sawResume = true;
                            if (resumePlannerAttempt < 0) resumePlannerAttempt = fixture.Controller.PlannerAttemptCount;
                        }
                        if (sawResume && !fixture.Controller.IsRecovering && !fixture.Controller.IsRamming &&
                            fixture.Controller.LastCommand.throttle > 0f &&
                            fixture.Controller.PlannerAttemptCount > resumePlannerAttempt) sawFreshPursuit = true;
                        AssertFinite(fixture, step);
                        if (callbacks > 0 && fixture.World.SessionTime >= nextObservation && nextObservation <= 8f) {
                            observations.AppendLine(RecoveryFacts(fixture));
                            observations.AppendLine(BlockerFacts(fixture, reverseBlocker));
                            nextObservation *= 2f;
                        }
                    }

                    Assert.Greater(callbacks, 0, "Acceptance requires a genuine native Ram impact callback.");
                    Assert.Less(fixture.PoliceReceiver.CurrentHealth, initialHealth);
                    Assert.IsTrue(sawEvidence, "Recovery must retain the native road evidence from impact.");
                    Assert.IsTrue(sawReverse,
                        "Recovery must issue the authored reverse command. command=" + sawReverseCommand +
                        "; travel=" + maximumReverseTravel.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        "; motor=" + fixture.MotorReversePermitted + "; diagnostic=" + (firstRecoveryDiagnostic ?? "none") +
                        "; diagnosticHistory=" + (diagnosticHistory ?? "none") + "; " + observations);
                    Assert.IsTrue(sawNegativeVelocity, "Recovery must produce measured negative forward velocity.");
                    Assert.Greater(measuredSignedReverseMotion, 0f, "Recovery must produce actual reverse displacement.");
                    Assert.GreaterOrEqual(measuredReverseWithBraking, 0.15f, "Removal requires measured reverse motion including its braking tail, never policy credit alone.");
                    Assert.GreaterOrEqual(maximumReverseTravel, 0.15f);
                    Assert.IsTrue(reverseBlockerRemoved, "The temporary blocker may be removed only after measured reverse travel reaches 0.15. powered=" +
                        measuredSignedReverseMotion + "; withBraking=" + measuredReverseWithBraking + "; traversal=" + maximumReverseTravel +
                        "; diagnostics=" + diagnosticHistory + "; " + observations);
                    Assert.IsFalse((diagnosticHistory ?? string.Empty).Contains("gate."), "Gate observations must not mask the actual reverse/rejoin rejection predicate.");
                    Assert.IsTrue(sawGate, "Recovery must choose a native road rejoin gate.");
                    Assert.IsTrue(sawResume, "Recovery must report a fresh legal resume.");
                    Assert.IsTrue(sawFreshPursuit, "The police life must return to a fresh pursuit plan after recovery.");
                }
            }
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

    /// <summary>Always exits Play Mode after the native acceptance probe, including assertion failures.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static PursuitFixture.AuthoredSetup LoadAuthored() {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(PoliceDataPath);
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>(PoliceVehiclePath);
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>(RamBehaviorPath);
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath);
        Assert.IsNotNull(profile, PoliceDataPath);
        Assert.IsNotNull(vehicle, PoliceVehiclePath);
        Assert.IsNotNull(behavior, RamBehaviorPath);
        Assert.IsNotNull(damage, DamagePath);
        Assert.AreSame(profile, vehicle.sharedNpc);
        Assert.IsNotNull(behavior.driving);
        return new PursuitFixture.AuthoredSetup { profile = profile, vehicle = vehicle, behavior = behavior, damage = damage };
    }

    static void VerifySourceBindings() {
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TrafficMapData>(NavigationPath), NavigationPath);
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>(DamagePath), DamagePath);
    }

    static bool RecoveryFlag(PursuitFixture fixture, string field) {
        var member = typeof(PoliceRecoveryIntegration).GetField(field, PrivateInstance);
        Assert.IsNotNull(member, field);
        return (bool)member.GetValue(fixture.Recovery);
    }

    static string BlockerFacts(PursuitFixture fixture, GameObject blocker) {
        var collider = blocker.GetComponent<Collider2D>();
        var rb = blocker.GetComponent<Rigidbody2D>();
        var sensor = fixture.Binding.body.Sensor;
        var status = sensor.QuerySweep(fixture.PoliceForward, fixture.ControllerSettings.maxSweepDistance, out float gap, out var hit);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "blocker t={0:R}; active={1}; rb={2}; transform={3}; bounds={4}; enabled={5}; trigger={6}; simulated={7}; sameScene={8}; layer={9}; sweep={10}/{11:R}; hitsBlocker={12}; hitsPlayer={13}",
            fixture.World.SessionTime, blocker.activeInHierarchy, rb.position, blocker.transform.position, collider.bounds,
            collider.enabled, collider.isTrigger, rb.simulated, blocker.scene == fixture.FixtureScene, blocker.layer,
            status, gap, hit == collider, hit != null && hit.attachedRigidbody == fixture.Player.GetComponent<Rigidbody2D>());
    }

    static string RecoveryFacts(PursuitFixture fixture) {
        var rb = fixture.Binding.body.Body;
        var result = fixture.Planner.CurrentResult;
        var traversal = (PoliceRecoveryTraversal)typeof(PoliceRecoveryIntegration).GetField("traversal", PrivateInstance).GetValue(fixture.Recovery);
        var diagnostic = (string)typeof(PoliceRecoveryIntegration).GetField("firstDiagnostic", PrivateInstance).GetValue(fixture.Recovery);
        var frontStatus = fixture.Binding.body.Sensor.QuerySweep(fixture.PoliceForward,
            fixture.ControllerSettings.maxSweepDistance, out float frontGap, out _);
        var rearStatus = fixture.Binding.body.Sensor.QuerySweep(-fixture.PoliceForward,
            fixture.ControllerSettings.maxSweepDistance, out float rearGap, out _);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "t={0:R}; pos={1}; vel={2}; forwardSpeed={3:R}; rotation={4:R}; angular={5:R}; phase={6}; exhausted={7}; evidence={8}; gate={9}/{10}; planner={11}/{12}; canStartReverse={13}; reverseCreditAvailable={14}; reverseAttempted={15}; reverseActive={16}; reverseBraking={17}; reverseTravelSettled={18}; reverseSeconds={19:R}; reverseTravel={20:R}; player={21}; command={22}/{23}; recoveryDiagnostic={24}; frontSweep={25}/{26:R}; rearSweep={27}/{28:R}",
            fixture.World.SessionTime, rb.position, rb.linearVelocity, fixture.ForwardSpeed, rb.rotation,
            rb.angularVelocity, fixture.Recovery.Phase, fixture.Recovery.IsExhausted,
            RecoveryFlag(fixture, "hasEvidence"), RecoveryFlag(fixture, "hasGate"), fixture.Recovery.Gate,
            result == null ? "null" : result.status.ToString(), result == null ? "null" : result.waitReason.ToString(),
            traversal.CanStartReverse, TraversalField<bool>(traversal, "reverseCreditAvailable"),
            TraversalField<bool>(traversal, "reverseAttempted"), TraversalField<bool>(traversal, "reverseActive"),
            TraversalField<bool>(traversal, "reverseBraking"), TraversalField<bool>(traversal, "reverseTravelSettled"),
            traversal.ReverseSeconds, traversal.ReverseTravel, fixture.PlayerPosition, fixture.Controller.LastCommand.throttle,
            fixture.Controller.LastCommand.brake, diagnostic ?? "none", frontStatus, frontGap, rearStatus, rearGap);
    }

    static T TraversalField<T>(PoliceRecoveryTraversal traversal, string fieldName) {
        return (T)typeof(PoliceRecoveryTraversal).GetField(fieldName, PrivateInstance).GetValue(traversal);
    }

    static void AssertFinite(PursuitFixture fixture, int step) {
        AssertFinite(fixture.PolicePosition.x, "police x at step " + step);
        AssertFinite(fixture.PolicePosition.y, "police y at step " + step);
        AssertFinite(fixture.PoliceVelocity.x, "police velocity x at step " + step);
        AssertFinite(fixture.PoliceVelocity.y, "police velocity y at step " + step);
        AssertFinite(fixture.Binding.body.Body.rotation, "police rotation at step " + step);
        AssertFinite(fixture.Binding.body.Body.angularVelocity, "police angular velocity at step " + step);
        AssertFinite(fixture.World.SessionTime, "session clock at step " + step);
    }

    static void AssertFinite(float value, string message) {
        Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), message);
    }
}
