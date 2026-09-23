using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Play-mode regression for directed road-role interpretation and Ram recovery.</summary>
public sealed class PoliceRoadRolePlayTests {
    /// <summary>Checks that null and empty edge roles retain the same police route semantics as an explicit role.</summary>
    [UnityTest]
    public IEnumerator RoadRoleMatrix() {
        Assert.IsNull(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene);
        Assert.AreEqual(1, SceneManager.sceneCount);
        Assert.IsTrue(string.IsNullOrEmpty(SceneManager.GetActiveScene().path));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<TrafficDamageWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        Assert.IsNull(GameManager.Instance);

        RunAllowedCase(null, "null-roles");
        RunAllowedCase(new List<VehicleRole>(), "empty-roles");
        RunAllowedCase(new List<VehicleRole> { VehicleRole.Police }, "police-role");
        RunDeniedCase();
    }

    /// <summary>Always restores Edit Mode after the isolated road-role matrix.</summary>
    [UnityTearDown]
    public IEnumerator ExitPlayAlways() {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    static void RunAllowedCase(List<VehicleRole> allowedRoles, string caseId) {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute();
            fixture.Binding.navigation.edges[0].allowedRoles = allowedRoles;
            fixture.Binding.graph.Rebuild();
            Assert.IsTrue(fixture.Profile.allowedRoles.Contains(VehicleRole.Police), Facts(fixture, caseId));
            Assert.IsTrue(fixture.TryQueryCurrent(out var route), Facts(fixture, caseId + " query failed"));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, route.status, Facts(fixture, caseId + " query status"));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), caseId + ": " + reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), caseId + ": " + reason);

            var planner = fixture.Planner;
            bool impactObserved = false;
            bool immediateCancel = false;
            fixture.PoliceReceiver.ImpactObserved += _ => {
                impactObserved = true;
                immediateCancel = !fixture.Controller.IsRamming && fixture.Controller.LastCommand.brake == 1f;
            };
            for (int step = 0; step < 160 && !impactObserved; step++) fixture.Step(1);
            Assert.IsTrue(impactObserved, Facts(fixture, caseId + " real impact"));
            Assert.Greater(fixture.Controller.RamStarts, 0, Facts(fixture, caseId + " Ram admission"));
            Assert.IsTrue(immediateCancel, Facts(fixture, caseId + " synchronous Ram cancel"));

            float impactClock = fixture.World.SessionTime;
            bool releaseObserved = false;
            bool releaseGateProof = false;
            int releasePlannerAttempt = -1;
            int releaseArrivalAttempt = -1;
            Vector2 gateAtRelease = Vector2.zero;
            Vector2 forwardAtRelease = Vector2.zero;
            for (int step = 0; step < 500 && !releaseObserved; step++) {
                fixture.Step(1);
                if (!fixture.Recovery.ResumeReady) continue;
                releaseObserved = true;
                releasePlannerAttempt = fixture.Controller.PlannerAttemptCount;
                releaseArrivalAttempt = fixture.RecoveryArrivalAttempt;
                gateAtRelease = fixture.Recovery.Gate;
                forwardAtRelease = fixture.PoliceForward;
                releaseGateProof = releaseArrivalAttempt >= 0 && releasePlannerAttempt > releaseArrivalAttempt &&
                    Vector2.Distance(fixture.LastControllerPosition, gateAtRelease) <= fixture.ControllerSettings.acquisitionTolerance &&
                    Vector2.Dot(fixture.LastControllerPosition - gateAtRelease, forwardAtRelease) >= 0f;
            }
            Assert.IsTrue(releaseObserved, Facts(fixture, caseId + " recovery release"));
            Assert.AreSame(planner, fixture.Planner, Facts(fixture, caseId + " planner identity"));
            Assert.GreaterOrEqual(releaseArrivalAttempt, 0, Facts(fixture, caseId + " arrival attempt"));
            Assert.Greater(releasePlannerAttempt, releaseArrivalAttempt, Facts(fixture, caseId + " fresh planner attempt"));
            Assert.IsTrue(releaseGateProof, Facts(fixture, caseId + " directed gate crossing"));
            Assert.LessOrEqual(fixture.World.SessionTime - impactClock, 8f, Facts(fixture, caseId + " episode deadline"));
            Assert.IsFalse(fixture.Controller.IsRecovering, Facts(fixture, caseId + " recovery still active"));
            Assert.IsFalse(fixture.Controller.IsRamming, Facts(fixture, caseId + " Ram still active"));
            Assert.Greater(fixture.Player.CurrentHealth, 0f, Facts(fixture, caseId + " player health"));
            Assert.Greater(fixture.PoliceReceiver.CurrentHealth, 0f, Facts(fixture, caseId + " police health"));
        }
    }

    static void RunDeniedCase() {
        const string caseId = "civilian-only";
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureRamRoute();
            fixture.Binding.navigation.edges[0].allowedRoles = new List<VehicleRole> { VehicleRole.Civilian };
            fixture.Binding.graph.Rebuild();
            Assert.IsTrue(fixture.Profile.allowedRoles.Contains(VehicleRole.Police), Facts(fixture, caseId));
            Assert.IsFalse(fixture.TryQueryCurrent(out var route), Facts(fixture, caseId + " disallowed query succeeded"));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, route.status, Facts(fixture, caseId + " query status"));
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), caseId + ": " + reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), caseId + ": " + reason);

            Vector2 initialPosition = fixture.PolicePosition;
            float initialPlayerHealth = fixture.Player.CurrentHealth;
            float initialPoliceHealth = fixture.PoliceReceiver.CurrentHealth;
            bool impactObserved = false;
            fixture.PoliceReceiver.ImpactObserved += _ => impactObserved = true;
            for (int step = 0; step < 160; step++) {
                fixture.Step(1);
                Assert.AreEqual(0, fixture.Controller.RamStarts, Facts(fixture, caseId + " Ram admission"));
                Assert.IsFalse(fixture.Controller.IsRamming, Facts(fixture, caseId + " active Ram"));
                Assert.IsFalse(fixture.Controller.IsRecovering, Facts(fixture, caseId + " recovery"));
                Assert.AreEqual(1f, fixture.Controller.LastCommand.brake, Facts(fixture, caseId + " brake"));
                Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle, Facts(fixture, caseId + " throttle"));
                Assert.IsFalse(fixture.MotorReversePermitted, Facts(fixture, caseId + " reverse permission"));
            }
            Assert.IsFalse(impactObserved, Facts(fixture, caseId + " impact"));
            Assert.AreEqual(initialPosition, fixture.PolicePosition, Facts(fixture, caseId + " motion"));
            Assert.AreEqual(Vector2.zero, fixture.PoliceVelocity, Facts(fixture, caseId + " velocity"));
            Assert.AreEqual(initialPlayerHealth, fixture.Player.CurrentHealth, Facts(fixture, caseId + " player health"));
            Assert.AreEqual(initialPoliceHealth, fixture.PoliceReceiver.CurrentHealth, Facts(fixture, caseId + " police health"));
            Assert.IsTrue(fixture.Controller.IsBound, Facts(fixture, caseId + " binding"));
        }
    }

    static string Facts(PursuitFixture fixture, string detail) {
        return "case=" + detail + ", phase=" + fixture.Controller.CurrentPhase +
            ", recovery=" + fixture.Controller.RecoveryPhase + ", bound=" + fixture.Controller.IsBound +
            ", ramStarts=" + fixture.Controller.RamStarts + ", ramming=" + fixture.Controller.IsRamming +
            ", brake=" + fixture.Controller.LastCommand.brake + ", reverse=" + fixture.Controller.LastCommand.reverseAllowed +
            ", clock=" + fixture.World.SessionTime + ", policePosition=" + fixture.PolicePosition +
            ", playerPosition=" + fixture.PlayerPosition + ", playerHealth=" + fixture.Player.CurrentHealth +
            ", policeHealth=" + fixture.PoliceReceiver.CurrentHealth;
    }
}
