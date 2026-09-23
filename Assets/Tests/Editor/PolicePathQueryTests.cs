using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused S08.2a.1 tests for shared road graph versioning and cached geometry.</summary>
public sealed class PolicePathQueryTests {
    /// <summary>Confirms every explicit rebuild advances the graph version and exposes read-only edges.</summary>
    [Test]
    public void RoadGraphRuntime_RebuildAlwaysAdvancesVersionAndEdgesAreReadOnly() {
        var document = MakeDocument();
        var graph = new RoadGraphRuntime(document);
        long initialVersion = graph.Version;
        Assert.Greater(initialVersion, 0L);
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("edge", graph.Edges[0].edgeId);
        graph.Rebuild();
        Assert.Greater(graph.Version, initialVersion);
        long manualVersion = graph.Version;
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 12f, 0f));
        Assert.AreEqual(12f, graph.GetArcLength("edge"));
        Assert.Greater(graph.Version, manualVersion);
    }

    /// <summary>Confirms duplicate consecutive vertices are removed while node endpoints remain authoritative.</summary>
    [Test]
    public void RoadGraphRuntime_CachesNormalizedPolylineAndCumulativeLengths() {
        var document = MakeDocument();
        document.edges[0].orderedPoints.Add(new Vector2(0f, 0f));
        document.edges[0].orderedPoints.Add(new Vector2(5f, 0f));
        document.edges[0].orderedPoints.Add(new Vector2(5f, 0f));
        document.edges[0].orderedPoints.Add(new Vector2(10f, 0f));
        var graph = new RoadGraphRuntime(document);
        var points = graph.GetPolyline("edge");
        var cumulative = graph.GetCumulativeLengths("edge");
        CollectionAssert.AreEqual(new[] { new Vector2(0f, 0f), new Vector2(5f, 0f), new Vector2(10f, 0f) }, points);
        CollectionAssert.AreEqual(new[] { 0f, 5f, 10f }, cumulative);
        Assert.AreEqual(10f, graph.GetGeometry("edge").Length, 0.0001f);
        Assert.AreEqual(10f, graph.GetArcLength("edge"), 0.0001f);
    }

    /// <summary>Confirms an explicit rebuild refreshes cached geometry after a direct DTO write.</summary>
    [Test]
    public void RoadGraphRuntime_ManualRebuildInvalidatesGeometryCache() {
        var document = MakeDocument();
        var graph = new RoadGraphRuntime(document);
        long oldVersion = graph.Version;
        document.edges[0].orderedPoints.Add(new Vector2(5f, 4f));
        Assert.AreEqual(10f, graph.GetArcLength("edge"), 0.0001f);
        graph.Rebuild();
        Assert.Greater(graph.Version, oldVersion);
        Assert.AreEqual(12.806248f, graph.GetArcLength("edge"), 0.0001f);
        Assert.AreEqual(12.806248f, graph.GetCumulativeLengths("edge")[2], 0.0001f);
    }

    /// <summary>Confirms Version observes shared edits before cache consumers read it and preserves old snapshots.</summary>
    [Test]
    public void RoadGraphRuntime_VersionReadRefreshesAfterSharedEditAndPreservesOldSnapshot() {
        var document = MakeDocument();
        var graph = new RoadGraphRuntime(document);
        long capturedVersion = graph.Version;
        var oldPolyline = graph.GetPolyline("edge");
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 12f, 0f));
        long refreshedVersion = graph.Version;
        Assert.Greater(refreshedVersion, capturedVersion);
        Assert.AreEqual(10f, oldPolyline[oldPolyline.Count - 1].x, 0.0001f);
        Assert.AreEqual(12f, graph.GetPolyline("edge")[graph.GetPolyline("edge").Count - 1].x, 0.0001f);
    }

    /// <summary>Confirms missing endpoint geometry fails closed with zero length instead of NaN.</summary>
    [Test]
    public void RoadGraphRuntime_MissingEndpointReturnsEmptyZeroLengthGeometry() {
        var document = MakeDocument();
        document.edges[0].toNodeId = "missing";
        var graph = new RoadGraphRuntime(document);
        Assert.AreEqual(0f, graph.GetArcLength("edge"));
        Assert.IsNotNull(graph.GetPolyline("edge"));
        Assert.AreEqual(0, graph.GetPolyline("edge").Count);
    }

    /// <summary>Confirms anchored search preserves directed loop spans and does not prune the goal after revisiting its start edge.</summary>
    [Test]
    public void RoadPathQuery_AnchoredSquareLoopPreservesRepeatedEdgeAndDistance() {
        var graph = new RoadGraphRuntime(SquareDocument(true));
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        var budget = new RoadPathQuery.SearchBudget(512);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 10f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, budget, out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.Found, result.status);
        CollectionAssert.AreEqual(new[] { "e0", "e1", "e2", "e3", "e0" }, EdgeIds(result.spans));
        Assert.AreEqual(75f, result.totalDistance, 0.0001f);
    }

    /// <summary>Confirms a directed same-edge reverse request is not accepted without a legal cycle.</summary>
    [Test]
    public void RoadPathQuery_AnchoredBehindWithoutCycleReturnsNoPath() {
        var graph = new RoadGraphRuntime(SquareDocument(false));
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 10f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.NoPath, result.status);
        Assert.IsEmpty(result.spans);
    }

    /// <summary>Confirms a narrow shortcut is rejected during expansion while a wider detour remains selectable.</summary>
    [Test]
    public void RoadPathQuery_AnchoredExpansionRejectsNarrowShortcutAndFindsDetour() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 10f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "t", x = 20f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "z", x = 30f, y = 20f });
        document.edges.Add(TestEdge("e0", "a", "b", 4f));
        document.edges.Add(TestEdge("narrow", "b", "t", 1f));
        document.edges.Add(TestEdge("detour", "b", "c", 4f));
        document.edges.Add(TestEdge("detour2", "c", "t", 4f));
        document.edges.Add(TestEdge("target", "t", "z", 4f));
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(2f, 0f, 0f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 10f), new RoadPathQuery.EdgeAnchor("target", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        CollectionAssert.AreEqual(new[] { "e0", "detour", "detour2", "target" }, EdgeIds(result.spans));
    }

    /// <summary>Confirms finite input validation and explicit budget exhaustion are distinct from no path.</summary>
    [Test]
    public void RoadPathQuery_AnchoredInvalidInputAndBudgetFailClosed() {
        var graph = new RoadGraphRuntime(SquareDocument(true));
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", float.NaN), new RoadPathQuery.EdgeAnchor("e1", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var invalid));
        Assert.AreEqual(RoadPathQuery.QueryStatus.InvalidInput, invalid.status);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 10f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(1), out var exhausted));
        Assert.AreEqual(RoadPathQuery.QueryStatus.BudgetExceeded, exhausted.status);
        Assert.IsEmpty(exhausted.spans);
    }

    /// <summary>Confirms a positive-radius turn cannot be hidden by near-end and near-start clipped spans.</summary>
    [Test]
    public void RoadPathQuery_AnchoredTurnUsesFullDirectionsAndClippedAvailability() {
        var graph = new RoadGraphRuntime(SquareDocument(true));
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 1f, 135f);
        var start = new RoadPathQuery.EdgeAnchor("e0", 19.995f);
        var target = new RoadPathQuery.EdgeAnchor("e1", 0.005f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, start, target, VehicleRole.Police, constraints,
            new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.NoPath, result.status);
        Assert.IsEmpty(result.spans);
    }

    /// <summary>Confirms same-position anchors are valid and anchor edge role/width gates fail closed.</summary>
    [Test]
    public void RoadPathQuery_AnchoredSamePositionAndAnchorPermissionsAreValidated() {
        var document = SquareDocument(true);
        document.edges[0].allowedRoles = new List<VehicleRole> { VehicleRole.Civilian };
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 5f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var denied));
        Assert.AreEqual(RoadPathQuery.QueryStatus.InvalidInput, denied.status);
        document.edges[0].allowedRoles = new List<VehicleRole> { VehicleRole.Police };
        graph.Rebuild();
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 5f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var same));
        Assert.AreEqual(0f, same.totalDistance, 0.0001f);
        document.edges[0].usableWidth = 0.5f;
        graph.Rebuild();
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 5f), new RoadPathQuery.EdgeAnchor("e0", 6f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var narrow));
        Assert.AreEqual(RoadPathQuery.QueryStatus.InvalidInput, narrow.status);
    }

    /// <summary>Confirms a bend outside the travelled span does not reject a straight partial route.</summary>
    [Test]
    public void RoadPathQuery_AnchoredSpanIgnoresUntravelledBend() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 20f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "bend", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> { new Vector2(10f, 10f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 10f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("bend", 15f), new RoadPathQuery.EdgeAnchor("bend", 18f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(3f, result.totalDistance, 0.0001f);
    }

    /// <summary>Confirms a genuinely short normalized interior turn cannot bypass its radius requirement.</summary>
    [Test]
    public void RoadPathQuery_AnchoredShortInteriorTurnFailsClosed() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "shortTurn", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> { new Vector2(0.005f, 0f), new Vector2(0.005f, 0.005f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 1f, 135f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("shortTurn", 0f), new RoadPathQuery.EdgeAnchor("shortTurn", 10f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.NoPath, result.status);
    }

    /// <summary>Confirms a junction allow-list gates both the initial and terminal edge transition.</summary>
    [Test]
    public void RoadPathQuery_AnchoredJunctionTransitionMustBeAuthored() {
        var document = SquareDocument(true);
        document.edges[0].endJunctionId = "j";
        document.edges[1].startJunctionId = "j";
        document.junctions.Add(new JunctionRecord { junctionId = "j", allowedTransitions = new List<JunctionTransition>() });
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 10f), new RoadPathQuery.EdgeAnchor("e1", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(512), out var result));
        Assert.AreEqual(RoadPathQuery.QueryStatus.NoPath, result.status);
    }

    /// <summary>Confirms a target prefix rejected on the first approach can be traversed fully and reached later.</summary>
    [Test]
    public void RoadPathQuery_TargetPrefixFailureFallsBackToFullIntermediateEdge() {
        var document = new MapNavigationDocument();
        AddNode(document, "a", -20f, 0f); AddNode(document, "b", 0f, 0f); AddNode(document, "c", 0f, 20f);
        AddNode(document, "d", -20f, 20f); AddNode(document, "e", -20f, -20f); AddNode(document, "f", 0f, -20f);
        document.edges.Add(TestEdge("s", "a", "b", 4f)); document.edges.Add(TestEdge("t", "b", "c", 4f));
        document.edges.Add(TestEdge("cd", "c", "d", 4f)); document.edges.Add(TestEdge("de", "d", "e", 4f));
        document.edges.Add(TestEdge("ef", "e", "f", 4f)); document.edges.Add(TestEdge("fb", "f", "b", 4f));
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 1f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("s", 10f), new RoadPathQuery.EdgeAnchor("t", 0.5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(2048), out var result));
        CollectionAssert.AreEqual(new[] { "s", "t", "cd", "de", "ef", "fb", "t" }, EdgeIds(result.spans));
        Assert.AreEqual(130.5f, result.totalDistance, 0.0001f);
    }

    /// <summary>Confirms a partial seed state is not pruned when a full lap returns to its incoming edge.</summary>
    [Test]
    public void RoadPathQuery_InitialPartialStateCanRevisitAsFullApproach() {
        var document = new MapNavigationDocument();
        AddNode(document, "a", 0f, -20f); AddNode(document, "b", 0f, 0f); AddNode(document, "c", 20f, 0f);
        AddNode(document, "d", 0f, 20f); AddNode(document, "e", -20f, 20f); AddNode(document, "f", -20f, -20f);
        document.edges.Add(TestEdge("s", "a", "b", 4f)); document.edges.Add(TestEdge("t", "b", "c", 4f));
        document.edges.Add(TestEdge("bd", "b", "d", 4f)); document.edges.Add(TestEdge("de", "d", "e", 4f));
        document.edges.Add(TestEdge("ef", "e", "f", 4f)); document.edges.Add(TestEdge("fa", "f", "a", 4f));
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 1f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("s", 19.995f), new RoadPathQuery.EdgeAnchor("t", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(2048), out var result));
        CollectionAssert.AreEqual(new[] { "s", "bd", "de", "ef", "fa", "s", "t" }, EdgeIds(result.spans));
        Assert.AreEqual(125.005f, result.totalDistance, 0.0001f);
    }

    /// <summary>Confirms equal-cost routes choose the same edge-id order regardless of authored edge order.</summary>
    [Test]
    public void RoadPathQuery_AnchoredEqualCostTieUsesStableEdgeIdOrder() {
        var first = EqualCostDocument(false); var second = EqualCostDocument(true);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(new RoadGraphRuntime(first), new RoadPathQuery.EdgeAnchor("s", 0f), new RoadPathQuery.EdgeAnchor("target", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(1024), out var firstResult));
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(new RoadGraphRuntime(second), new RoadPathQuery.EdgeAnchor("s", 0f), new RoadPathQuery.EdgeAnchor("target", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(1024), out var secondResult));
        CollectionAssert.AreEqual(EdgeIds(firstResult.spans), EdgeIds(secondResult.spans));
        Assert.AreEqual("left", firstResult.spans[1].edgeId);
    }

    /// <summary>Confirms one finite budget is consumed across two candidate queries.</summary>
    [Test]
    public void RoadPathQuery_SharedBudgetExhaustsSecondQuery() {
        var graph = new RoadGraphRuntime(SquareDocument(true));
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        var budget = new RoadPathQuery.SearchBudget(1);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 5f), new RoadPathQuery.EdgeAnchor("e0", 5f), VehicleRole.Police, constraints, budget, out var first));
        Assert.AreEqual(RoadPathQuery.QueryStatus.Found, first.status);
        Assert.IsFalse(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("e0", 5f), new RoadPathQuery.EdgeAnchor("e0", 6f), VehicleRole.Police, constraints, budget, out var second));
        Assert.AreEqual(RoadPathQuery.QueryStatus.BudgetExceeded, second.status);
        Assert.IsEmpty(second.spans);
    }

    /// <summary>Confirms a forbidden direct terminal transition leaves a legal intermediate approach available.</summary>
    [Test]
    public void RoadPathQuery_TerminalGateIsCheckedAfterIntermediateApproach() {
        var document = new MapNavigationDocument();
        AddNode(document, "a", -20f, 0f); AddNode(document, "b", 0f, 0f); AddNode(document, "x", 0f, 20f); AddNode(document, "c", 20f, 20f); AddNode(document, "d", 20f, 0f);
        document.edges.Add(TestEdge("s", "a", "b", 4f)); document.edges.Add(TestEdge("direct", "b", "c", 4f));
        document.edges.Add(TestEdge("detour", "b", "x", 4f)); document.edges.Add(TestEdge("detour2", "x", "c", 4f)); document.edges.Add(TestEdge("target", "c", "d", 4f));
        document.edges[1].endJunctionId = "j"; document.edges[3].endJunctionId = "j"; document.edges[4].startJunctionId = "j";
        document.junctions.Add(new JunctionRecord { junctionId = "j", allowedTransitions = new List<JunctionTransition> { new JunctionTransition { fromEdgeId = "detour2", toEdgeId = "target" } } });
        var graph = new RoadGraphRuntime(document);
        var constraints = new RouteTransitionFilter.VehicleConstraints(1f, 0f, 0f, 135f);
        Assert.IsTrue(RoadPathQuery.TryFindAnchoredPath(graph, new RoadPathQuery.EdgeAnchor("s", 10f), new RoadPathQuery.EdgeAnchor("target", 5f), VehicleRole.Police, constraints, new RoadPathQuery.SearchBudget(2048), out var result));
        CollectionAssert.AreEqual(new[] { "s", "detour", "detour2", "target" }, EdgeIds(result.spans));
    }

    static List<string> EdgeIds(IReadOnlyList<RoadPathQuery.PathSpan> spans) {
        var ids = new List<string>();
        foreach (var span in spans) ids.Add(span.edgeId);
        return ids;
    }

    static MapNavigationDocument SquareDocument(bool closed) {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 20f, y = 20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "d", x = 20f, y = 0f });
        document.edges.Add(TestEdge("e0", "a", "b", 4f));
        document.edges.Add(TestEdge("e1", "b", "c", 4f));
        document.edges.Add(TestEdge("e2", "c", "d", 4f));
        if (closed) document.edges.Add(TestEdge("e3", "d", "a", 4f));
        return document;
    }

    static MapNavigationDocument EqualCostDocument(bool reverseOrder) {
        var document = new MapNavigationDocument();
        AddNode(document, "a", 0f, 0f); AddNode(document, "b", 0f, 10f); AddNode(document, "c", 10f, 10f); AddNode(document, "d", 20f, 10f);
        document.edges.Add(TestEdge("s", "a", "b", 4f));
        var left = TestEdge("left", "b", "c", 4f); var right = TestEdge("right", "b", "c", 4f);
        if (reverseOrder) { document.edges.Add(right); document.edges.Add(left); } else { document.edges.Add(left); document.edges.Add(right); }
        document.edges.Add(TestEdge("target", "c", "d", 4f));
        return document;
    }

    static void AddNode(MapNavigationDocument document, string id, float x, float y) {
        document.nodes.Add(new RoadNodeRecord { nodeId = id, x = x, y = y });
    }

    static RoadEdgeRecord TestEdge(string id, string from, string to, float width) {
        return new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = width, speedLimit = 8f,
            orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police } };
    }

    static MapNavigationDocument MakeDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        document.edges.Add(new RoadEdgeRecord {
            edgeId = "edge", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police }
        });
        return document;
    }
}
