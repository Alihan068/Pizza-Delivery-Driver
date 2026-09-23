using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>One bounded physical Intercept side-approach acceptance case using the shared pursuit fixture.</summary>
public sealed class PoliceInterceptDrivingTests {
    /// <summary>
    /// Selects an authored junction ahead of a moving player from a distinct side branch, then
    /// verifies measured police motion and the ordinary Pursue handoff without teleporting the target.
    /// </summary>
    [Test]
    public void Intercept_SideApproachSelectsJunctionAndHandsOffToPursue() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureAuthoredNavigation(CreateSideApproachDocument(), Vector2.zero,
                new Vector2(-8f, 20f), -90f, new Vector2(0f, 37.5f));
            fixture.Binding.navigationSettings = new PoliceNavigationSettings(hardMinimumInterval: 0.1f,
                refreshInterval: 0.3f, workBudget: 512);
            fixture.Binding.behavior.tacticalRole = PoliceTacticalRole.Intercept;
            fixture.Binding.behavior.intercept = new PoliceInterceptSettings {
                predictionSeconds = 2f,
                minimumSpeed = 0.5f,
                maxCandidates = 4
            };
            if (!fixture.Binding.vehicle.supportedTacticalRoles.Contains(PoliceTacticalRole.Intercept))
                fixture.Binding.vehicle.supportedTacticalRoles.Add(PoliceTacticalRole.Intercept);

            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            Vector2 start = fixture.PolicePosition;
            float health = fixture.PoliceReceiver.CurrentHealth;
            fixture.PushPlayer(Vector2.up * 1.5f);

            fixture.Step(1);
            Assert.Greater(fixture.PlayerVelocity.y, 0f);
            float totalPoliceTravel = 0f;
            float maximumInterceptMotion = 0f;
            Vector2 previousPosition = fixture.PolicePosition;
            bool sawInterceptBeforeJunction = false;
            int attemptsAtIntercept = -1;
            for (int step = 0; step < 80; step++) {
                fixture.Step(1);
                Vector2 currentPosition = fixture.PolicePosition;
                float stepMotion = Vector2.Distance(previousPosition, currentPosition);
                totalPoliceTravel += stepMotion;
                if (fixture.Planner.CurrentResult != null &&
                    fixture.Planner.CurrentResult.targetKind == PoliceRoadTargetQuery.TargetKind.InterceptJunction &&
                    fixture.PlayerPosition.y < 40f) {
                    sawInterceptBeforeJunction = true;
                    if (attemptsAtIntercept < 0) attemptsAtIntercept = fixture.Controller.PlannerAttemptCount;
                    maximumInterceptMotion = Mathf.Max(maximumInterceptMotion, stepMotion);
                }
                previousPosition = currentPosition;
            }
            Assert.Greater(fixture.PolicePosition.x, start.x + 0.5f);
            Assert.Greater(totalPoliceTravel, 1f, FailureState(fixture, totalPoliceTravel, maximumInterceptMotion));
            Assert.IsTrue(sawInterceptBeforeJunction, FailureState(fixture, totalPoliceTravel, maximumInterceptMotion));

            bool sawActualPlayerHandoffAfterJunction = false;
            float maximumHandoffWindowMotion = 0f;
            bool playerPassedJunction = false;
            for (int step = 0; step < 500; step++) {
                Vector2 before = fixture.PolicePosition;
                fixture.Step(1);
                float stepMotion = Vector2.Distance(before, fixture.PolicePosition);
                playerPassedJunction |= fixture.PlayerPosition.y >= 40f;
                if (playerPassedJunction && fixture.Planner.CurrentResult != null &&
                    fixture.Planner.CurrentResult.targetKind == PoliceRoadTargetQuery.TargetKind.ActualPlayer) {
                    sawActualPlayerHandoffAfterJunction = true;
                    maximumHandoffWindowMotion = Mathf.Max(maximumHandoffWindowMotion, stepMotion);
                    if (maximumHandoffWindowMotion > 0.01f) break;
                }
            }
            Assert.Greater(fixture.Controller.PlannerAttemptCount, attemptsAtIntercept);
            Assert.IsTrue(sawActualPlayerHandoffAfterJunction,
                FailureState(fixture, totalPoliceTravel, maximumHandoffWindowMotion));
            Assert.Greater(maximumHandoffWindowMotion, 0.01f,
                FailureState(fixture, totalPoliceTravel, maximumHandoffWindowMotion));
            Assert.AreEqual(health, fixture.PoliceReceiver.CurrentHealth);
        }
    }

    static string FailureState(PursuitFixture fixture, float totalMotion, float windowMotion) {
        var result = fixture.Planner.CurrentResult;
        string targetKind = result == null ? "null" : result.targetKind.ToString();
        string status = result == null ? "null" : result.status.ToString();
        return string.Format("position={0}; playerPosition={1}; planKind={2}; planStatus={3}; totalMotion={4:R}; windowMotion={5:R}",
            fixture.PolicePosition, fixture.PlayerPosition, targetKind, status, totalMotion, windowMotion);
    }

    static MapNavigationDocument CreateSideApproachDocument() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -5f, 40f, 70f) };
        document.nodes.Add(Node("sideStart", -12f, 20f));
        document.nodes.Add(Node("junction", 0f, 20f));
        document.nodes.Add(Node("playerJunction", 0f, 40f));
        document.nodes.Add(Node("exit", 0f, 60f));
        document.junctions.Add(Junction("sideJunction", "sideBranch", "playerApproach"));
        document.junctions.Add(Junction("playerAheadJunction", "playerApproach", "playerExit"));
        document.edges.Add(Edge("sideBranch", "sideStart", "junction", new Vector2(-12f, 20f), new Vector2(0f, 20f), string.Empty, "sideJunction"));
        document.edges.Add(Edge("playerApproach", "junction", "playerJunction", new Vector2(0f, 20f), new Vector2(0f, 40f), "sideJunction", "playerAheadJunction"));
        document.edges.Add(Edge("playerExit", "playerJunction", "exit", new Vector2(0f, 40f), new Vector2(0f, 60f), "playerAheadJunction", string.Empty));
        return document;
    }

    static RoadNodeRecord Node(string id, float x, float y) => new RoadNodeRecord { nodeId = id, x = x, y = y };

    static JunctionRecord Junction(string id, string incoming, params string[] outgoing) {
        var junction = new JunctionRecord { junctionId = id };
        foreach (string edge in outgoing)
            junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = incoming, toEdgeId = edge });
        return junction;
    }

    static RoadEdgeRecord Edge(string id, string from, string to, Vector2 start, Vector2 end,
        string startJunction, string endJunction) => new RoadEdgeRecord {
            edgeId = id,
            fromNodeId = from,
            toNodeId = to,
            startJunctionId = startJunction,
            endJunctionId = endJunction,
            usableWidth = 6f,
            speedLimit = 5f,
            orderedPoints = new List<Vector2> { start, end },
            allowedRoles = new List<VehicleRole> { VehicleRole.Police }
        };
}
