using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused b.2a tests for stateless police road targeting and static clearance.</summary>
public sealed class PoliceRoadTargetQueryTests {
    readonly List<NpcVehicleProfile> profiles = new List<NpcVehicleProfile>();

    /// <summary>Destroys in-memory profile fixtures after each edit-mode test.</summary>
    [TearDown]
    public void TearDown() {
        foreach (var profile in profiles) if (profile != null) Object.DestroyImmediate(profile);
        profiles.Clear();
    }

    /// <summary>Settings remain authorable and survive JsonUtility round-trip with non-default values.</summary>
    [Test]
    public void Settings_PublicValuesRoundTripAndClone() {
        var settings = new PoliceNavigationSettings { projectionSearchRadius = 13f, clearanceMargin = 0.8f, workBudget = 77 };
        var copy = JsonUtility.FromJson<PoliceNavigationSettings>(JsonUtility.ToJson(settings));
        Assert.AreEqual(13f, copy.projectionSearchRadius);
        Assert.AreEqual(0.8f, copy.clearanceMargin);
        Assert.AreEqual(77, copy.workBudget);
        Assert.AreEqual(copy.workBudget, copy.Clone().workBudget);
    }

    /// <summary>A known directed anchor can reach a bent target and returns a non-current road point.</summary>
    [Test]
    public void Query_BentTargetUsesDirectedAnchorAndActualTargetDistance() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f), Node("c", 10f, 10f) },
            Edge("road", "a", "c", new Vector2(10f, 0f)));
        var input = Input(graph, new Vector2(2f, 0f), new Vector2(0f, 1f), new Vector2(10f, 8f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
        Assert.Greater(Vector2.Distance(result.roadTarget, input.policePosition), 1f);
        Assert.AreEqual("road", result.startAnchor.edgeId);
    }

    /// <summary>A static connector block yields SafeWait rather than a partial route.</summary>
    [Test]
    public void Query_BlockedInitialConnectorReturnsSafeWait() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(0f, 5f), Vector2.right, new Vector2(8f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        input.staticClearance = new FakeClearance(false);
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, result.status);
        Assert.AreEqual(PoliceRoadTargetQuery.WaitReason.BlockedConnector, result.waitReason);
        Assert.AreEqual(0, result.spans.Count);
    }

    /// <summary>Spawn exclusions do not act as driving bans during a static clearance query.</summary>
    [Test]
    public void Query_IgnoresSpawnExclusionsForRoadClearance() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(8f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        input.staticClearance = new FakeClearance(true);
        input.navigation.noSpawnRegions.Add(new NoSpawnRegion { area = new Rect(0f, -5f, 10f, 10f) });
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
    }

    /// <summary>Out-of-bounds police or target positions fail closed before graph search.</summary>
    [Test]
    public void Query_OutOfBoundsInputIsInvalid() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(-21f, 0f), Vector2.right, new Vector2(8f, 0f));
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.InvalidInput, result.status);
    }

    /// <summary>Exhaustion during final clearance cannot return an earlier route as success.</summary>
    [Test]
    public void Query_BudgetExhaustionReturnsNoPartialRoute() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(8f, 3f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        input.staticClearance = new FakeClearance(true);
        input.budget = new RoadPathQuery.SearchBudget(4);
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, result.status);
        Assert.AreEqual(0, result.spans.Count);
    }

    /// <summary>A known directed anchor is never replaced by a nearby reverse-directed edge.</summary>
    [Test]
    public void Query_KnownAnchorNeverAutoswapsReverseDirection() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("forward", "a", "b"));
        var input = Input(graph, new Vector2(8f, 0f), Vector2.left, new Vector2(2f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("forward", 8f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual("forward", result.startAnchor.edgeId);
    }

    /// <summary>A displaced known anchor without a static query preserves an on-road-only safe wait.</summary>
    [Test]
    public void Query_MissingStaticQueryBlocksOnlyDisplacedConnector() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(8f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var roadResult));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, roadResult.status);
        input.policePosition = new Vector2(2f, 1f);
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var displacedResult));
        Assert.AreEqual(PoliceRoadTargetQuery.WaitReason.BlockedConnector, displacedResult.waitReason);
    }

    /// <summary>The clipped current suffix uses target distance rather than an invented zero-distance goal.</summary>
    [Test]
    public void Query_ClippedCurrentSuffixUsesActualTargetDistance() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(8f, 0f), Vector2.right, new Vector2(9f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 8f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(9f, result.roadTarget.x, 0.001f);
    }

    /// <summary>Final clearance receives the authored local offset and can reject a thin blocker.</summary>
    [Test]
    public void Query_FinalApproachUsesOffsetAndTargetHeading() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 0f, 10f) }, Edge("north", "a", "b"));
        var input = Input(graph, new Vector2(0f, 1f), Vector2.up, new Vector2(0f, 14f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("north", 1f);
        input.localColliderOffset = new Vector2(1f, 0f);
        var clearance = new RectangleBlockerClearance(new Rect(1.65f, 11.9f, 0.05f, 0.2f));
        input.staticClearance = clearance;
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.IsFalse(result.hasFinalApproach);
        Assert.AreEqual(new Vector2(1f, 12f), clearance.lastCenter);
        Assert.AreEqual(0f, clearance.lastHeading, 0.001f);
        input.localColliderOffset = Vector2.zero;
        input.budget = new RoadPathQuery.SearchBudget(4096);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out result));
        Assert.IsTrue(result.hasFinalApproach);
        Assert.AreEqual(new Vector2(0f, 10f), result.roadTarget);
    }

    /// <summary>A disconnected nearest target projection is exhausted before a farther reachable projection.</summary>
    [Test]
    public void Query_DisconnectedNearestTargetFallsBackToFartherReachableProjection() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -20f, 40f, 40f) };
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 10f, 0f));
        document.nodes.Add(Node("c", 9f, 0.1f));
        document.nodes.Add(Node("d", 10f, 0.1f));
        document.edges.Add(Edge("road", "a", "b"));
        document.edges.Add(Edge("disconnected", "c", "d"));
        var graph = new RoadGraphRuntime(document);
        var input = Input(graph, new Vector2(0f, 0f), Vector2.right, new Vector2(9f, 0.1f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 0f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(0f, result.roadTarget.y, 0.001f);
    }

    /// <summary>Unknown starts resolve the nearest exact distance group and prefer aligned direction.</summary>
    [Test]
    public void Query_UnknownStartUsesNearestAlignedCandidate() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -20f, 40f, 40f) };
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 10f, 0f));
        document.nodes.Add(Node("c", 10f, 0f));
        document.nodes.Add(Node("d", 0f, 0f));
        document.edges.Add(Edge("forward", "a", "b"));
        document.edges.Add(Edge("reverse", "c", "d"));
        var graph = new RoadGraphRuntime(document);
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(8f, 0f));
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual("forward", result.startAnchor.edgeId);
    }

    /// <summary>A found candidate does not hide budget exhaustion while resolving its remaining equal-distance group.</summary>
    [Test]
    public void Query_FoundCandidateWithUnresolvedEqualGroupReturnsBudgetExceeded() {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -20f, 40f, 40f) };
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 10f, 0f));
        document.nodes.Add(Node("c", 20f, 0f));
        document.edges.Add(Edge("start", "a", "b"));
        document.edges.Add(Edge("t1", "b", "c"));
        document.edges.Add(Edge("t2", "b", "c"));
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 8f, new RoadPathQuery.SearchBudget(512), out _, out _));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(15f, 0f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("start", 2f);
        input.budget = new RoadPathQuery.SearchBudget(4096);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var firstResult));
        Assert.AreEqual(new Vector2(15f, 0f), firstResult.roadTarget);
        Assert.AreEqual("t1", firstResult.spans[firstResult.spans.Count - 1].edgeId);
        int fullCost = input.budget.ConsumedWork;
        Assert.Greater(fullCost, 1);
        input.budget = new RoadPathQuery.SearchBudget(fullCost - 1);
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, result.status);
        Assert.AreEqual(0, result.spans.Count);
    }

    /// <summary>Final approach uses the selected bent target direction and exact sweep footprint.</summary>
    [Test]
    public void Query_BentTargetUsesTargetDirectionForFinalApproach() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f), Node("c", 10f, 10f) },
            Edge("bend", "a", "c", new Vector2(10f, 0f)));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(10f, 14f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("bend", 2f);
        var clearance = new RecordingClearance(true);
        input.staticClearance = clearance;
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.IsTrue(result.hasFinalApproach);
        Assert.AreEqual(0f, clearance.lastHeading, 0.001f);
        Assert.AreEqual(new Vector2(10f, 12f), clearance.lastCenterOffset);
        Assert.AreEqual(new Vector2(2.5f, 8.5f), clearance.lastFootprint);
    }

    /// <summary>Nearest disconnected projection is skipped only after its group is fully resolved.</summary>
    [Test]
    public void Query_NearestDisconnectedProjectionFallsBackToMainRoad() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 20f, 0f), Node("c", 10f, 5f), Node("d", 20f, 5f) },
            Edge("main", "a", "b"), Edge("disconnected", "c", "d"));
        var input = Input(graph, new Vector2(2f, 0f), Vector2.right, new Vector2(15f, 10f));
        input.settings.projectionSearchRadius = 4f;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("main", 2f);
        input.hasKnownAnchor = true;
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(15f, result.roadTarget.x, 0.001f);
        Assert.AreEqual(0f, result.roadTarget.y, 0.001f);
    }

    /// <summary>A target beyond the local projection radius still resolves to a reachable road route.</summary>
    [Test]
    public void Query_TargetOutsideProjectionRadiusUsesCompleteRoadProjectionFallback() {
        var graph = Graph(new[] { Node("a", -15f, 0f), Node("b", 15f, 0f) }, Edge("road", "a", "b"));
        var input = Input(graph, new Vector2(-10f, 0f), Vector2.right, new Vector2(12f, 15f));
        input.settings.projectionSearchRadius = 5f;
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
        Assert.AreEqual(12f, result.roadTarget.x, 0.001f);
        Assert.AreEqual(0f, result.roadTarget.y, 0.001f);
    }

    /// <summary>Complete projection exhaustion returns budget failure without exposing a partial route.</summary>
    [Test]
    public void Query_CompleteProjectionBudgetExhaustionReturnsNoPartialRoute() {
        var graph = Graph(new[] { Node("a", -15f, 0f), Node("b", 15f, 0f) }, Edge("road", "a", "b"));
        Assert.IsTrue(RoadSegmentSpatialIndex.TryGetOrBuild(graph, 8f, new RoadPathQuery.SearchBudget(512), out _, out _));
        var input = Input(graph, new Vector2(-10f, 0f), Vector2.right, new Vector2(12f, 15f));
        input.settings.projectionSearchRadius = 5f;
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("road", 2f);
        input.budget = new RoadPathQuery.SearchBudget(12);
        Assert.IsFalse(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.BudgetExceeded, result.status);
        Assert.AreEqual(0, result.spans.Count);
    }

    /// <summary>Known forward anchors keep their direction when a distinct reverse edge is nearby.</summary>
    [Test]
    public void Query_KnownForwardAnchorKeepsDirectionAgainstDistinctReverseEdge() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f), Node("c", 10f, 0.01f), Node("d", 0f, 0.01f) },
            Edge("forward", "a", "b"), Edge("reverse", "c", "d"));
        var input = Input(graph, new Vector2(8f, 0.01f), Vector2.left, new Vector2(2f, 0.01f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("forward", 8f);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual("forward", result.startAnchor.edgeId);
        Assert.AreEqual(8f, result.roadTarget.x, 0.001f);
    }

    /// <summary>Unknown starts choose the nearest aligned connector, including opposite directed overlays.</summary>
    [Test]
    public void Query_UnknownStartChoosesNearestAlignedDirection() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 10f, 0f), Node("c", 10f, 0f), Node("d", 0f, 0f), Node("e", 0f, 2f), Node("f", 10f, 2f) },
            Edge("forward", "a", "b"), Edge("reverse", "c", "d"), Edge("far", "e", "f"));
        var input = Input(graph, new Vector2(2f, 0.1f), Vector2.right, new Vector2(8f, 0f));
        input.staticClearance = new FakeClearance(true);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.AreEqual("forward", result.startAnchor.edgeId);
    }

    /// <summary>Northbound final approach uses target direction and ignores spawn-only rectangles.</summary>
    [Test]
    public void Query_NorthboundFinalApproachIgnoresSpawnOnlyExclusion() {
        var graph = Graph(new[] { Node("a", 0f, 0f), Node("b", 0f, 10f) }, Edge("north", "a", "b"));
        var input = Input(graph, new Vector2(0f, 1f), Vector2.up, new Vector2(0f, 14f));
        input.hasKnownAnchor = true;
        input.knownAnchor = new RoadPathQuery.EdgeAnchor("north", 1f);
        input.navigation.noSpawnRegions.Add(new NoSpawnRegion { area = new Rect(-2f, 9f, 4f, 6f) });
        input.staticClearance = new FakeClearance(true);
        Assert.IsTrue(PoliceRoadTargetQuery.TryQuery(input, out var result));
        Assert.IsTrue(result.hasFinalApproach);
    }

    PoliceRoadTargetQuery.Input Input(RoadGraphRuntime graph, Vector2 police, Vector2 direction, Vector2 target) {
        var navigation = new MapNavigationDocument { localBounds = new Rect(-20f, -20f, 40f, 40f) };
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profiles.Add(profile);
        profile.allowedRoles.Add(VehicleRole.Police);
        profile.colliderSize = new Vector2(2f, 4f);
        profile.motorSettings.minimumTurningRadius = 0f;
        return new PoliceRoadTargetQuery.Input {
            graph = graph, navigation = navigation, profile = profile, policePosition = police,
            policeDirection = direction, targetPosition = target, settings = new PoliceNavigationSettings(),
            budget = new RoadPathQuery.SearchBudget(4096)
        };
    }

    static RoadGraphRuntime Graph(RoadNodeRecord[] nodes, params RoadEdgeRecord[] edges) {
        var document = new MapNavigationDocument { localBounds = new Rect(-20f, -20f, 40f, 40f), nodes = new List<RoadNodeRecord>(nodes), edges = new List<RoadEdgeRecord>(edges) };
        return new RoadGraphRuntime(document);
    }

    static RoadNodeRecord Node(string id, float x, float y) => new RoadNodeRecord { nodeId = id, x = x, y = y };
    static RoadEdgeRecord Edge(string id, string from, string to, Vector2? interior = null) {
        var edge = new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f, allowedRoles = new List<VehicleRole> { VehicleRole.Police } };
        if (interior.HasValue) edge.orderedPoints.Add(interior.Value);
        return edge;
    }

    sealed class FakeClearance : IAreaClearanceQuery {
        readonly bool clear;
        public FakeClearance(bool clear) { this.clear = clear; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => clear;
    }

    sealed class RecordingClearance : IAreaClearanceQuery {
        readonly bool clear;
        public Vector2 lastCenterOffset;
        public Vector2 lastFootprint;
        public float lastHeading;
        public RecordingClearance(bool clear) { this.clear = clear; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            lastCenterOffset = center;
            lastFootprint = footprint;
            lastHeading = headingDegrees;
            return clear;
        }
    }

    sealed class RectangleBlockerClearance : IAreaClearanceQuery {
        readonly Rect blocker;
        public Vector2 lastCenter;
        public float lastHeading;
        public RectangleBlockerClearance(Rect blocker) { this.blocker = blocker; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            Assert.AreEqual(0f, headingDegrees, 0.001f);
            lastCenter = center;
            lastHeading = headingDegrees;
            return !new Rect(center - footprint * 0.5f, footprint).Overlaps(blocker);
        }
    }
}
