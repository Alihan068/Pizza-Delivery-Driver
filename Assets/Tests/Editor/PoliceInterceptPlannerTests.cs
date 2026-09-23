using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Bounded pure coverage for velocity-driven Intercept candidate selection and exact anchors.</summary>
public sealed class PoliceInterceptPlannerTests {
    /// <summary>Low-speed, stationary and reverse travel fall back without selecting a junction.</summary>
    [Test]
    public void Planner_LowSpeedAndReverseVelocityFailClosed() {
        var graph = CreateGraph(out var navigation);
        var planner = CreatePlanner(graph, navigation);
        Assert.IsFalse(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.zero, 1, new RoadPathQuery.SearchBudget(4096), out _));
        Assert.IsFalse(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.left, 1, new RoadPathQuery.SearchBudget(4096), out _));
    }

    /// <summary>Actual forward drift selects an exact incoming-edge endpoint rather than a predicted player point.</summary>
    [Test]
    public void Planner_ForwardDriftSelectsExactAuthoredEndpoint() {
        var graph = CreateGraph(out var navigation);
        var planner = CreatePlanner(graph, navigation);
        // Include the lateral projection distance inside the two-second prediction horizon.
        Assert.IsTrue(planner.TrySelectAnchor(new Vector2(3f, 0.1f), new Vector2(4f, 0.2f), 0,
            new RoadPathQuery.SearchBudget(4096), out var anchor));
        Assert.AreEqual(graph.GetArcLength(anchor.edgeId), anchor.distanceAlongEdge);
        Assert.AreNotEqual(string.Empty, anchor.edgeId);
    }

    /// <summary>Stable life identity changes only the deterministic candidate preference, not route validity.</summary>
    [Test]
    public void Planner_LifePreferenceIsStableAndCanDistributeCandidates() {
        var graph = CreateGraph(out var navigation);
        var planner = CreatePlanner(graph, navigation);
        var budgetA = new RoadPathQuery.SearchBudget(4096);
        var budgetB = new RoadPathQuery.SearchBudget(4096);
        Assert.IsTrue(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.right * 10f, 0, budgetA, out var first));
        planner.Reset();
        Assert.IsTrue(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.right * 10f, 1, budgetB, out var second));
        Assert.AreNotEqual(first.edgeId, second.edgeId);
        planner.Reset();
        Assert.IsTrue(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.right * 10f, 0,
            new RoadPathQuery.SearchBudget(4096), out var repeated));
        Assert.AreEqual(first.edgeId, repeated.edgeId);
        Assert.AreEqual(first.distanceAlongEdge, repeated.distanceAlongEdge);
    }

    /// <summary>One bounded scan exposes reachable alternative incoming edges at the authored junction.</summary>
    [Test]
    public void Planner_CollectsReachableAlternativesUnderOneBudget() {
        var graph = CreateGraph(out var navigation);
        var planner = CreatePlanner(graph, navigation);
        var anchors = new List<RoadPathQuery.EdgeAnchor>();
        Assert.IsTrue(planner.TryCollectCandidates(new Vector2(2f, 0f), Vector2.right * 10f, 2,
            new RoadPathQuery.SearchBudget(4096), anchors));
        Assert.GreaterOrEqual(anchors.Count, 2);
        foreach (var anchor in anchors) Assert.AreEqual(graph.GetArcLength(anchor.edgeId), anchor.distanceAlongEdge);
    }

    /// <summary>Narrow exact anchors are rejected by the existing police role and width constraints.</summary>
    [Test]
    public void Query_InterceptExactAnchorRejectsNarrowIncomingEdge() {
        var graph = CreateGraph(out var navigation, narrowFirstEdge: true);
        var profile = Profile();
        var input = new PoliceRoadTargetQuery.Input {
            graph = graph,
            navigation = navigation,
            profile = profile,
            policePosition = new Vector2(1f, 0f),
            policeDirection = Vector2.right,
            targetPosition = new Vector2(10f, 0f),
            hasKnownAnchor = true,
            knownAnchor = new RoadPathQuery.EdgeAnchor("e0", 1f),
            targetKind = PoliceRoadTargetQuery.TargetKind.InterceptJunction,
            hasExactTargetAnchor = true,
            exactTargetAnchor = new RoadPathQuery.EdgeAnchor("e0", graph.GetArcLength("e0")),
            settings = new PoliceNavigationSettings(),
            budget = new RoadPathQuery.SearchBudget(4096)
        };
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, result.status);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Shared budget exhaustion prevents partial Intercept selection and later hidden work.</summary>
    [Test]
    public void Planner_ExhaustedSharedBudgetFailsClosed() {
        var graph = CreateGraph(out var navigation);
        var planner = CreatePlanner(graph, navigation);
        var budget = new RoadPathQuery.SearchBudget(1);
        Assert.IsFalse(planner.TrySelectAnchor(new Vector2(2f, 0f), Vector2.right * 4f, 1, budget, out _));
        Assert.IsTrue(budget.IsExhausted);
    }

    static PoliceInterceptPlanner CreatePlanner(RoadGraphRuntime graph, MapNavigationDocument navigation) =>
        new PoliceInterceptPlanner(graph, navigation, new PoliceInterceptSettings(), 30f);

    static NpcVehicleProfile Profile() {
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.allowedRoles.Add(VehicleRole.Police);
        profile.colliderSize = new Vector2(2f, 2f);
        profile.motorSettings.minimumTurningRadius = 1f;
        return profile;
    }

    static RoadGraphRuntime CreateGraph(out MapNavigationDocument navigation, bool narrowFirstEdge = false) {
        navigation = new MapNavigationDocument { localBounds = new Rect(-5f, -5f, 40f, 30f) };
        navigation.nodes.Add(Node("a", 0f, 0f));
        navigation.nodes.Add(Node("b", 10f, 0f));
        navigation.nodes.Add(Node("c", 20f, 0f));
        navigation.nodes.Add(Node("d", 10f, 10f));
        navigation.nodes.Add(Node("e", 20f, 10f));
        navigation.nodes.Add(Node("f", 30f, 0f));
        navigation.nodes.Add(Node("g", 30f, 10f));
        navigation.junctions.Add(Junction("j0", "e0", "e1", "e2"));
        navigation.junctions.Add(Junction("j1", "e1", "e3"));
        navigation.junctions.Add(Junction("j2", "e2", "e4"));
        navigation.edges.Add(Edge("e0", "a", "b", narrowFirstEdge ? 1f : 6f, string.Empty, "j0"));
        navigation.edges.Add(Edge("e1", "b", "c", 6f, "j0", "j1"));
        navigation.edges.Add(Edge("e2", "b", "d", 6f, "j0", "j2"));
        navigation.edges.Add(Edge("e3", "c", "f", 6f, "j1", string.Empty));
        navigation.edges.Add(Edge("e4", "d", "e", 6f, "j2", string.Empty));
        return new RoadGraphRuntime(navigation);
    }

    static RoadNodeRecord Node(string id, float x, float y) => new RoadNodeRecord { nodeId = id, x = x, y = y };

    static JunctionRecord Junction(string id, string incoming, params string[] outgoing) {
        var junction = new JunctionRecord { junctionId = id };
        foreach (string edge in outgoing) junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = incoming, toEdgeId = edge });
        return junction;
    }

    static RoadEdgeRecord Edge(string id, string from, string to, float width, string startJunction, string endJunction) =>
        new RoadEdgeRecord {
            edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = width, speedLimit = 8f,
            startJunctionId = startJunction, endJunctionId = endJunction,
            allowedRoles = new List<VehicleRole> { VehicleRole.Police }
        };
}
