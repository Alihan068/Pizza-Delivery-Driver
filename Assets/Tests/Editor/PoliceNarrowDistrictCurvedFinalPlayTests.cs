using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Real isolated physics acceptance against the captured native NarrowDistrict colliders and
/// detached default police assets. Each selector requires a fresh geometry capture and an empty
/// bootstrap from the single Unity operator; it never edits an authored scene or tunes a profile.
/// </summary>
public sealed class PoliceNarrowDistrictCurvedFinalPlayTests {
    static readonly FieldInfo traversalField = typeof(PolicePursuitController).GetField("finalApproachTraversal", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Default southbound police must physically traverse the arc and close the twelve-unit lateral gap.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrict_PerpendicularTarget_PhysicalClosure() => Run(Scenario.Perpendicular);

    /// <summary>A real target impulse during the final turn requires future replanning without renewing the active maneuver bounds.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrict_MovedTarget_PhysicalClosurePreservesBounds() => Run(Scenario.Moved);

    /// <summary>A newly inserted static collider on the committed arc revokes propulsion before actual contact.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrict_FreshArcBlocker_RefusesMotion() => Run(Scenario.Blocked);

    /// <summary>The previously accepted aligned native representative retains straight final holding.</summary>
    [UnityTest]
    public IEnumerator NarrowDistrict_AlignedRepresentative_Unchanged() => Run(Scenario.Aligned);

    enum Scenario { Perpendicular, Moved, Blocked, Aligned }

    static IEnumerator Run(Scenario scenario) {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path), "Use an empty TestRunner bootstrap after capturing NarrowDistrict.");
        Assert.IsNull(GameManager.Instance);
        yield return new EnterPlayMode();
        PoliceNarrowDistrictGeometrySnapshot snapshot = null;
        try {
            snapshot = PoliceNarrowDistrictGeometrySnapshot.ConsumeForTestRun();
            var authored = LoadAuthored();
            using (var fixture = new PursuitFixture(0.02f, authored: authored))
            using (snapshot.StageInto(fixture.FixtureScene)) {
                bool aligned = scenario == Scenario.Aligned;
                Vector2 start = aligned ? new Vector2(20f, 5f) : new Vector2(-46f, 0f);
                Vector2 target = aligned ? new Vector2(34f, 5f) : new Vector2(-58f, -10.5f);
                fixture.ConfigureAuthoredNavigation(snapshot.ResolveNavigation(), snapshot.Origin, start, aligned ? -90f : 180f, target);
                // Use the real default query budget, not a fixture-specific reduced allowance.
                fixture.Binding.navigationSettings = new PoliceNavigationSettings();
                Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile));
                Assert.AreEqual(JsonUtility.ToJson(authored.behavior.driving), JsonUtility.ToJson(fixture.ControllerSettings));
                Assert.IsTrue(fixture.TryQueryCurrent(out var result), "The native query must produce a reachable route.");
                Assert.IsTrue(result.hasFinalApproach, "Road-only fallback is not successful lateral closure.");
                Assert.AreEqual(!aligned, result.finalApproachCurved);
                if (!aligned) {
                    Assert.Greater(result.roadTarget.y, target.y, "The exit must precede the lateral target projection on the southbound segment.");
                    Assert.That(result.roadTarget.x, Is.EqualTo(-46f).Within(0.01f));
                    Assert.GreaterOrEqual(result.finalApproachPlan.radius, fixture.Profile.motorSettings.minimumTurningRadius);
                }
                Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
                Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
                typeof(PolicePursuitController).GetField("finalDiagnosticEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fixture.Controller, true);
                RunPhysics(fixture, scenario, start, target);
                Assert.AreEqual(JsonUtility.ToJson(authored.profile), JsonUtility.ToJson(fixture.Profile));
            }
            snapshot.AssertSourcesUnchanged();
        } finally {
            try { snapshot?.AssertSourcesUnchanged(); }
            finally { PoliceNarrowDistrictGeometrySnapshot.ClearPending(); }
        }
    }

    static void RunPhysics(PursuitFixture fixture, Scenario scenario, Vector2 start, Vector2 originalTarget) {
        bool aligned = scenario == Scenario.Aligned;
        bool turned = false, tangent = false, held = false, moved = false, replanned = false, braked = false;
        bool impact = false;
        fixture.PoliceReceiver.ImpactObserved += _ => impact = true;
        float policeHealth = fixture.PoliceReceiver.CurrentHealth, playerHealth = fixture.Player.CurrentHealth;
        float deadline = 0f, entryClock = 0f, roadProgress = 0f, measuredTravel = 0f, actualTravel = 0f;
        float moveClock = 0f;
        Vector2 previous = fixture.PolicePosition;
        Vector2 firstTarget = originalTarget;
        GameObject blocker = null;
        bool impulseCancelled = false;
        var chronology = new System.Text.StringBuilder();
        for (int frame = 0; frame < 1500; frame++) {
            fixture.Step(1);
            actualTravel += Vector2.Distance(previous, fixture.PolicePosition);
            previous = fixture.PolicePosition;
            Assert.IsTrue(fixture.Controller.IsBound, "Binding lost at frame " + frame);
            Assert.IsFalse(fixture.Controller.IsRecovering, "Unexpected collision/recovery during final approach.");
            Assert.IsFalse(float.IsNaN(fixture.PolicePosition.x) || float.IsNaN(fixture.PolicePosition.y));
            var phase = fixture.Controller.CurrentPhase;
            var traversal = (PoliceFinalApproachTraversal)traversalField.GetValue(fixture.Controller);
            if (frame % 50 == 0 && chronology.Length < 12000) chronology.AppendLine(Facts(fixture, traversal));
            if (phase == PolicePursuitController.TraversalPhase.FinalTurn && !turned) {
                turned = true; deadline = traversal.Deadline; entryClock = fixture.World.SessionTime;
                roadProgress = fixture.Controller.CursorProgress; firstTarget = traversal.Plan.target;
                Assert.That(deadline - entryClock, Is.EqualTo(fixture.ControllerSettings.maximumTurnActiveSeconds).Within(fixture.StepDelta * 2f));
                if (scenario == Scenario.Moved) {
                    // Only the player receives an impulse. Police pose, velocity and cursor are never written.
                    fixture.PushPlayer(Vector2.left * 0.5f);
                    moved = true; moveClock = fixture.World.SessionTime;
                }
                if (scenario == Scenario.Blocked) {
                    var plan = traversal.Plan;
                    Assert.Greater(plan.arcLength, 0f);
                    blocker = fixture.CreateBlocker(plan.Sample(plan.arcLength * 0.8f), false, Vector2.one * 0.12f);
                    fixture.Controller.Tick(fixture.StepDelta, false);
                    Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
                    Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
                    braked = true;
                }
            }
            if (turned) {
                Assert.AreEqual(roadProgress, fixture.Controller.CursorProgress, "Off-road travel must not earn road cursor credit.");
                Assert.AreEqual(deadline, traversal.Deadline, "Refresh renewed the original deadline.");
                Assert.GreaterOrEqual(traversal.MeasuredTravel, measuredTravel);
                measuredTravel = traversal.MeasuredTravel;
                Assert.LessOrEqual(measuredTravel, fixture.ControllerSettings.maximumTurnTravel);
                replanned |= Vector2.Distance(firstTarget, traversal.Plan.target) > 0.02f;
            }
            if (moved && !impulseCancelled && fixture.World.SessionTime - moveClock >= 1f) {
                fixture.PushPlayer(-fixture.PlayerVelocity * fixture.Player.GetComponent<Rigidbody2D>().mass);
                impulseCancelled = true;
            }
            tangent |= phase == PolicePursuitController.TraversalPhase.FinalConnector;
            held |= phase == PolicePursuitController.TraversalPhase.Holding;
            if (held) break;
            if (blocker != null && fixture.World.SessionTime > deadline + 1f) break;
        }
        Assert.AreEqual(policeHealth, fixture.PoliceReceiver.CurrentHealth);
        Assert.AreEqual(playerHealth, fixture.Player.CurrentHealth);
        Assert.IsFalse(impact, "Any actual impact invalidates this safe-approach acceptance.");
        Assert.Greater(actualTravel, 1f);
        if (scenario == Scenario.Blocked) {
            Assert.IsTrue(turned && braked);
            Assert.IsFalse(held);
            Assert.Greater(fixture.PoliceCollider.Distance(blocker.GetComponent<Collider2D>()).distance, 0f);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.LessOrEqual(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold);
            return;
        }
        Assert.AreEqual(!aligned, turned, "Only the lateral representative should enter FinalTurn.");
        string diagnostics = Facts(fixture, (PoliceFinalApproachTraversal)traversalField.GetValue(fixture.Controller)) + "\n" + chronology;
        Assert.IsTrue(tangent, "The measured arc must release into its tangent straight. " + diagnostics);
        Assert.IsTrue(held, "Must reach actual target-surface holding, not merely advance a plan or issue throttle. " + diagnostics);
        Assert.That(fixture.ColliderGap, Is.EqualTo(fixture.ControllerSettings.stopGap).Within(fixture.ControllerSettings.acquisitionTolerance));
        Assert.LessOrEqual(fixture.PoliceVelocity.magnitude, fixture.ControllerSettings.stoppedSpeedThreshold);
        if (!aligned) {
            Assert.Less(fixture.PolicePosition.x, start.x - 8f, "The twelve-unit lateral gap must actually close.");
            Assert.Greater(measuredTravel, 8f);
        }
        if (scenario == Scenario.Moved) {
            Assert.IsTrue(moved && impulseCancelled && replanned);
            Assert.Greater(Vector2.Distance(originalTarget, fixture.PlayerPosition), 0.1f);
            Assert.Greater(fixture.Controller.FinalConnectorRefreshes, 0);
        }
    }

    static string Facts(PursuitFixture fixture, PoliceFinalApproachTraversal traversal) {
        string rejection = (string)typeof(PolicePursuitController).GetField("firstFinalDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Controller);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "t={0:R}; pose={1}; heading={2:R}; velocity={3}; phase={4}; state={5}; progress={6:R}/{7:R}; travel={8:R}; deadline={9:R}; gap={10:R}; command={11}/{12}; firstReject={13}",
            fixture.World.SessionTime, fixture.PolicePosition, fixture.Binding.body.Body.rotation, fixture.PoliceVelocity,
            fixture.Controller.CurrentPhase, traversal.CurrentState, traversal.Progress, traversal.Plan.arcLength,
            traversal.MeasuredTravel, traversal.Deadline, fixture.ColliderGap,
            fixture.Controller.LastCommand.throttle, fixture.Controller.LastCommand.brake, rejection ?? "none");
    }

    static PursuitFixture.AuthoredSetup LoadAuthored() {
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandardData.asset");
        var vehicle = AssetDatabase.LoadAssetAtPath<PoliceVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandard.asset");
        var damage = AssetDatabase.LoadAssetAtPath<TrafficDamageSettings>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Damage.asset");
        var behavior = AssetDatabase.LoadAssetAtPath<PoliceBehaviorProfile>("Assets/ScriptableObjects/Police/PolicePursueBehavior.asset");
        Assert.IsNotNull(profile); Assert.IsNotNull(vehicle); Assert.IsNotNull(damage); Assert.IsNotNull(behavior);
        Assert.AreSame(profile, vehicle.sharedNpc);
        return new PursuitFixture.AuthoredSetup { profile = profile, vehicle = vehicle, damage = damage, behavior = behavior };
    }

    /// <summary>Always returns to Edit Mode after a failure; staged geometry belongs only to the isolated fixture.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
