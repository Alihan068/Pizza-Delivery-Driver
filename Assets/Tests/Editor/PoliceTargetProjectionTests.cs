using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused S08.2b.1 tests for cached directed road-segment projection.</summary>
public sealed class PoliceTargetProjectionTests {
    /// <summary>Confirms projection follows full bend geometry and arc distance instead of a chord.</summary>
    [Test]
    public void SpatialIndex_ProjectsBendSegmentAndArcDistance() {
        var graph = new RoadGraphRuntime(BendDocument());
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 2f, new RoadPathQuery.SearchBudget(1024), out var index, out var status));
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.Built, status);
        var result = index.Query(new Vector2(4f, 8f), 3f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.Found, result.status);
        Assert.AreEqual("bend", result.candidates[0].edgeId);
        Assert.AreEqual(1, result.candidates[0].segmentIndex);
        Assert.AreEqual(14f, result.candidates[0].distanceAlongEdge, 0.0001f);
        Assert.AreEqual(new Vector2(4f, 10f), result.candidates[0].point);
        Assert.AreEqual(Vector2.right, result.candidates[0].direction);
    }

    /// <summary>Confirms opposite directed edges remain separate candidates and sorting ignores author order.</summary>
    [Test]
    public void SpatialIndex_OppositeDirectionsRemainDistinctAndStable() {
        var document = LineDocument(false);
        document.edges.Add(Edge("reverse", "b", "a", 4f));
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 4f, new RoadPathQuery.SearchBudget(1024), out var index, out _));
        var result = index.Query(new Vector2(5f, 1f), 2f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.Found, result.status);
        Assert.AreEqual(2, result.candidates.Count);
        Assert.AreEqual("forward", result.candidates[0].edgeId);
        Assert.AreEqual("reverse", result.candidates[1].edgeId);
        Assert.AreEqual(Vector2.right, result.candidates[0].direction);
        Assert.AreEqual(Vector2.left, result.candidates[1].direction);
        var reversedAuthoring = new RoadGraphRuntime(LineDocument(true));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(reversedAuthoring, 4f, new RoadPathQuery.SearchBudget(1024), out var reversedIndex, out _));
        var reversedResult = reversedIndex.Query(new Vector2(5f, 1f), 2f, new RoadPathQuery.SearchBudget(1024));
        CollectionAssert.AreEqual(new[] { "forward", "reverse" }, EdgeIds(reversedResult.candidates));
    }

    /// <summary>Confirms a segment crossing many cells is emitted once after cell deduplication.</summary>
    [Test]
    public void SpatialIndex_LongSegmentDeduplicatesMultiCellVisits() {
        var graph = new RoadGraphRuntime(LineDocument(false));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 1f, new RoadPathQuery.SearchBudget(2048), out var index, out _));
        var result = index.Query(new Vector2(5f, 0.25f), 2f, new RoadPathQuery.SearchBudget(2048));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.Found, result.status);
        Assert.AreEqual(1, result.candidates.Count);
    }

    /// <summary>Confirms empty radius and invalid coordinates return explicit statuses with no candidates.</summary>
    [Test]
    public void SpatialIndex_EmptyAndInvalidQueriesFailClosed() {
        var graph = new RoadGraphRuntime(LineDocument(false));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 2f, new RoadPathQuery.SearchBudget(1024), out var index, out _));
        var empty = index.Query(new Vector2(100f, 100f), 0f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.NoCandidates, empty.status);
        Assert.IsEmpty(empty.candidates);
        var invalid = index.Query(new Vector2(float.NaN, 0f), 2f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.InvalidInput, invalid.status);
        Assert.IsEmpty(invalid.candidates);
        var negativeRadius = index.Query(Vector2.zero, -1f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.InvalidInput, negativeRadius.status);
        var infiniteRadius = index.Query(Vector2.zero, float.PositiveInfinity, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.InvalidInput, infiniteRadius.status);
    }

    /// <summary>Confirms build and query budgets fail explicitly without publishing or returning partial data.</summary>
    [Test]
    public void SpatialIndex_BudgetExhaustionHasNoPartialResults() {
        var graph = new RoadGraphRuntime(LineDocument(false));
        Assert.IsFalse(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 1f, new RoadPathQuery.SearchBudget(1), out var failed, out var buildStatus));
        Assert.IsNull(failed);
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.BudgetExceeded, buildStatus);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 1f, new RoadPathQuery.SearchBudget(1024), out var index, out _));
        var result = index.Query(new Vector2(5f, 0f), 2f, new RoadPathQuery.SearchBudget(1));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.BudgetExceeded, result.status);
        Assert.IsEmpty(result.candidates);
    }

    /// <summary>Confirms successful cache reuse requires graph identity, graph version, and cell size.</summary>
    [Test]
    public void SpatialIndex_CacheKeysGraphIdentityVersionAndCellSize() {
        var firstDocument = LineDocument(false);
        firstDocument.edges.Add(Edge("reverse", "b", "a", 4f));
        var firstGraph = new RoadGraphRuntime(firstDocument);
        var secondGraph = new RoadGraphRuntime(LineDocument(false));
        int before = RoadSegmentSpatialIndex.SuccessfulBuildCount;
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(firstGraph, 2f, new RoadPathQuery.SearchBudget(1024), out var first, out _));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(firstGraph, 2f, new RoadPathQuery.SearchBudget(1024), out var reused, out _));
        Assert.AreSame(first, reused);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(secondGraph, 2f, new RoadPathQuery.SearchBudget(1024), out var differentGraph, out _));
        Assert.AreNotSame(first, differentGraph);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(firstGraph, 4f, new RoadPathQuery.SearchBudget(1024), out var differentCell, out _));
        Assert.AreNotSame(first, differentCell);
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(firstDocument, "b", 10f, 1f));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(firstGraph, 2f, new RoadPathQuery.SearchBudget(1024), out var newVersion, out _));
        Assert.AreNotSame(first, newVersion);
        Assert.GreaterOrEqual(RoadSegmentSpatialIndex.SuccessfulBuildCount, before + 4);
    }

    /// <summary>Confirms a held index rejects queries immediately after its graph version changes.</summary>
    [Test]
    public void SpatialIndex_HeldIndexRejectsStaleGraphVersion() {
        var document = LineDocument(false);
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 2f, new RoadPathQuery.SearchBudget(1024), out var index, out _));
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 10f, 1f));
        var result = index.Query(new Vector2(5f, 0f), 2f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.StaleGraph, result.status);
        Assert.IsEmpty(result.candidates);
    }

    /// <summary>Confirms an empty graph publishes a complete empty index and returns no candidates.</summary>
    [Test]
    public void SpatialIndex_EmptyGraphIsCompleteAndQueryIsEmpty() {
        var graph = new RoadGraphRuntime(new MapNavigationDocument());
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 2f, new RoadPathQuery.SearchBudget(1024), out var index, out var status));
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.Built, status);
        var result = index.Query(Vector2.zero, 2f, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.NoCandidates, result.status);
        Assert.IsEmpty(result.candidates);
    }

    /// <summary>Confirms a failed giant-cell build is retryable and never leaves an incomplete cache entry.</summary>
    [Test]
    public void SpatialIndex_GiantCellRangeFailsAndCanRetryWithSafeCellSize() {
        var graph = new RoadGraphRuntime(GiantDocument());
        Assert.IsFalse(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 1f, new RoadPathQuery.SearchBudget(1024), out var failed, out var status));
        Assert.IsNull(failed);
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.BudgetExceeded, status);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 1000000000f, new RoadPathQuery.SearchBudget(1024), out var retry, out var retryStatus));
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.Built, retryStatus);
        var overflow = retry.Query(Vector2.zero, float.NaN, new RoadPathQuery.SearchBudget(1024));
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.InvalidInput, overflow.status);
        Assert.IsFalse(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 0.000001f, new RoadPathQuery.SearchBudget(1024), out var overflowBuild, out var overflowStatus));
        Assert.IsNull(overflowBuild);
        Assert.AreEqual(RoadSegmentSpatialIndex.BuildStatus.InvalidInput, overflowStatus);
    }

    /// <summary>Confirms a cached index still charges each later query against the caller's aggregate budget.</summary>
    [Test]
    public void SpatialIndex_CachedQueriesShareCallerBudget() {
        var graph = new RoadGraphRuntime(LineDocument(false));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 2f, new RoadPathQuery.SearchBudget(1024), out var index, out _));
        var probeBudget = new RoadPathQuery.SearchBudget(1024);
        var probe = index.Query(new Vector2(5f, 0f), 2f, probeBudget);
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.Found, probe.status);
        var budget = new RoadPathQuery.SearchBudget(probeBudget.ConsumedWork);
        var first = index.Query(new Vector2(5f, 0f), 2f, budget);
        var second = index.Query(new Vector2(5f, 0f), 2f, budget);
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.Found, first.status);
        Assert.AreEqual(RoadSegmentSpatialIndex.QueryStatus.BudgetExceeded, second.status);
        Assert.IsEmpty(second.candidates);
    }

    static MapNavigationDocument BendDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 10f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "bend", fromNodeId = "a", toNodeId = "b", usableWidth = 4f,
            orderedPoints = new List<Vector2> { new Vector2(0f, 10f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        return document;
    }

    static MapNavigationDocument LineDocument(bool reverseOrder) {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        var forward = Edge("forward", "a", "b", 4f);
        var reverse = Edge("reverse", "b", "a", 4f);
        document.edges.Add(reverseOrder ? reverse : forward);
        if (!reverseOrder) return document;
        document.edges.Add(forward);
        return document;
    }

    static MapNavigationDocument GiantDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = -1000000000f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 1000000000f, y = 0f });
        document.edges.Add(Edge("giant", "a", "b", 4f));
        return document;
    }

    static RoadEdgeRecord Edge(string id, string from, string to, float width) {
        return new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = width, speedLimit = 8f,
            orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police } };
    }

    static List<string> EdgeIds(IReadOnlyList<RoadSegmentSpatialIndex.ProjectionCandidate> candidates) {
        var ids = new List<string>();
        foreach (var candidate in candidates) ids.Add(candidate.edgeId);
        return ids;
    }
}
