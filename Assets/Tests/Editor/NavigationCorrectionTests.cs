using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>C07-C10 regressions. Pure DTO and injected geometry fixtures; no scenes or global physics state are changed.</summary>
public class NavigationCorrectionTests {
    static readonly Rect Bounds = new Rect(-100f, -100f, 200f, 200f);

    /// <summary>C07: malformed route elements return an actionable issue instead of dictionary exceptions.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void RouteMalformedElementIsRejected(string edgeId) {
        var route = new CivilianRouteRecord { edgeIds = new List<string> { edgeId } };
        Assert.IsFalse(CivilianRouteValidator.IsValid(StraightDocument(), route, out string issue));
        Assert.IsNotEmpty(issue);
    }

    /// <summary>C07: a missing document is independently rejected with a diagnostic.</summary>
    [Test]
    public void RouteNullDocumentIsRejected() {
        Assert.IsFalse(CivilianRouteValidator.IsValid(null, Route(), out string issue));
        Assert.IsNotEmpty(issue);
    }

    /// <summary>C07: a missing route is independently rejected with a diagnostic.</summary>
    [Test]
    public void RouteNullRecordIsRejected() {
        Assert.IsFalse(CivilianRouteValidator.IsValid(StraightDocument(), null, out string issue));
        Assert.IsNotEmpty(issue);
    }

    /// <summary>C07: an absent route edge list is reported without attempting enumeration.</summary>
    [Test]
    public void RouteNullEdgeListIsRejected() {
        var route = Route();
        route.edgeIds = null;
        Assert.IsFalse(CivilianRouteValidator.IsValid(StraightDocument(), route, out string issue));
        Assert.IsNotEmpty(issue);
    }

    /// <summary>C07: an absent document edge table makes references dangling rather than throwing.</summary>
    [Test]
    public void RouteNullDocumentEdgesIsRejected() {
        var document = StraightDocument();
        document.edges = null;
        Assert.IsFalse(CivilianRouteValidator.IsValid(document, Route(), out string issue));
        Assert.IsNotEmpty(issue);
    }

    /// <summary>C07: an unknown nonempty ID is distinguished from a valid open route.</summary>
    [Test]
    public void RouteDanglingEdgeIsRejectedAndValidRouteStillWorks() {
        var route = Route();
        Assert.IsTrue(CivilianRouteValidator.IsValid(StraightDocument(), route, out _));
        route.edgeIds[0] = "missing";
        Assert.IsFalse(CivilianRouteValidator.IsValid(StraightDocument(), route, out string issue));
        StringAssert.Contains("missing", issue);
    }

    /// <summary>C07: one broken route does not abort the full map diagnostic pass.</summary>
    [Test]
    public void MapValidationReportsMalformedRouteAndOtherIssues() {
        var document = StraightDocument();
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "broken", edgeIds = new List<string> { null } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "orphan", edgeId = "missing" });
        var issues = TrafficMapValidator.Validate(document, null);
        Assert.IsTrue(issues.Exists(issue => issue.code == "OpenOrDisconnectedRoute"));
        Assert.IsTrue(issues.Exists(issue => issue.code == "DanglingSpawnEdge"));
    }

    /// <summary>C08: either endpoint's misspelled junction is diagnosed and never removes a turn restriction.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void MissingJunctionReferenceFailsClosed(bool atEnd) {
        var document = JunctionDocument();
        if (atEnd) document.edges[0].endJunctionId = "typo";
        else document.edges[2].startJunctionId = "typo";
        Assert.IsTrue(TrafficMapValidator.Validate(document, null).Exists(issue => issue.code == "DanglingEdgeJunction"));
        Assert.IsFalse(RoadPathQuery.TryFindPath(new RoadGraphRuntime(document), "a", "east", VehicleRole.Police, 1f, out var path));
        Assert.IsNull(path);
    }

    /// <summary>C08: a missing junction table cannot turn its referenced roads into unrestricted roads.</summary>
    [Test]
    public void MissingJunctionTableFailsClosed() {
        var document = JunctionDocument();
        document.junctions = null;
        Assert.IsTrue(TrafficMapValidator.Validate(document, null).Exists(issue => issue.code == "DanglingEdgeJunction"));
        Assert.IsFalse(RoadPathQuery.TryFindPath(new RoadGraphRuntime(document), "a", "east", VehicleRole.Police, 1f, out _));
    }

    /// <summary>C08: correctly authored movement remains usable while the prohibited turn is rejected.</summary>
    [Test]
    public void ValidJunctionPreservesAllowedAndProhibitedTurns() {
        var document = JunctionDocument();
        Assert.IsEmpty(TrafficMapValidator.Validate(document, null));
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "north", VehicleRole.Police, 1f, out var path));
        CollectionAssert.AreEqual(new[] { "in", "allowed" }, path);
        Assert.IsFalse(RoadPathQuery.TryFindPath(graph, "a", "east", VehicleRole.Police, 1f, out _));
    }

    /// <summary>C08: restriction ownership on the incoming edge alone still prevents a forbidden movement.</summary>
    [Test]
    public void IncomingEndJunctionAlsoControlsTurn() {
        var document = JunctionDocument();
        document.edges[0].endJunctionId = "j";
        document.edges[1].startJunctionId = string.Empty;
        document.edges[2].startJunctionId = string.Empty;
        var graph = new RoadGraphRuntime(document);
        Assert.IsFalse(RoadPathQuery.TryFindPath(graph, "a", "east", VehicleRole.Police, 1f, out _));
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "north", VehicleRole.Police, 1f, out _));
    }

    /// <summary>C08: an empty authored allow-list or missing list never means all turns are allowed.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void EmptyOrMissingTransitionsFailClosed(bool missing) {
        var document = JunctionDocument();
        document.junctions[0].allowedTransitions = missing ? null : new List<JunctionTransition>();
        Assert.IsFalse(RoadPathQuery.TryFindPath(new RoadGraphRuntime(document), "a", "north", VehicleRole.Police, 1f, out _));
        if (missing) Assert.IsTrue(TrafficMapValidator.Validate(document, null).Exists(issue => issue.code == "MissingJunctionTransitions"));
    }

    /// <summary>C08: malformed movement records are reported and cannot leave a partially trusted junction.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void DanglingOrNullTransitionFailsClosed(bool nullRecord) {
        var document = JunctionDocument();
        document.junctions[0].allowedTransitions.Add(nullRecord ? null : new JunctionTransition { fromEdgeId = "missing", toEdgeId = "allowed" });
        Assert.IsTrue(TrafficMapValidator.Validate(document, null).Exists(issue => issue.code == "DanglingJunctionTransition"));
        Assert.IsFalse(RoadPathQuery.TryFindPath(new RoadGraphRuntime(document), "a", "north", VehicleRole.Police, 1f, out _));
    }

    /// <summary>C08: two real but disconnected edges do not form a legal junction movement.</summary>
    [Test]
    public void DisconnectedTransitionIsReportedAndRejected() {
        var document = JunctionDocument();
        document.junctions[0].allowedTransitions.Add(new JunctionTransition { fromEdgeId = "allowed", toEdgeId = "blocked" });
        Assert.IsTrue(TrafficMapValidator.Validate(document, null).Exists(issue => issue.code == "DisconnectedJunctionTransition"));
        Assert.IsFalse(RoadPathQuery.TryFindPath(new RoadGraphRuntime(document), "a", "north", VehicleRole.Police, 1f, out _));
    }

    /// <summary>C09: all existing graph views refresh node geometry and edge costs after the same accepted move.</summary>
    [Test]
    public void NodeMoveRefreshesEveryGraphAndRenormalizesSpawn() {
        var document = StraightDocument();
        document.spawnPoints.Add(Spawn(5f));
        var first = new RoadGraphRuntime(document);
        var second = new RoadGraphRuntime(document);
        Assert.AreEqual(10f, first.GetArcLength("e"));
        Assert.AreEqual(10f, second.GetArcLength("e"));
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 20f, 0f));
        Assert.AreEqual(20f, first.GetArcLength("e"));
        Assert.AreEqual(20f, second.GetArcLength("e"));
        Assert.AreEqual(20f, first.GetNode("b").x);
        Assert.AreEqual(10f, document.spawnPoints[0].distanceAlongEdge);
    }

    /// <summary>C09: shortest-path choice uses refreshed costs, not just refreshed node positions.</summary>
    [Test]
    public void NodeMoveChangesShortestPathUsingExistingGraph() {
        var document = StraightDocument();
        document.nodes.Add(Node("c", 0f, 12f));
        document.nodes.Add(Node("d", 10f, 8f));
        document.edges.Add(Edge("bd", "b", "d"));
        document.edges.Add(Edge("ac", "a", "c"));
        document.edges.Add(Edge("cd", "c", "d"));
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "d", VehicleRole.Police, 1f, out var before));
        CollectionAssert.AreEqual(new[] { "e", "bd" }, before);
        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 30f, 0f));
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "d", VehicleRole.Police, 1f, out var after));
        CollectionAssert.AreEqual(new[] { "ac", "cd" }, after);
    }

    /// <summary>C09: new nodes/edges refresh adjacency and ID lookup on graphs created before the edit.</summary>
    [Test]
    public void SharedCreateAndConnectRefreshIndexes() {
        var document = StraightDocument();
        var graph = new RoadGraphRuntime(document);
        Assert.IsTrue(MapNavigationEditCommands.TryCreateNode(document, "c", 20f, 0f));
        Assert.IsNotNull(graph.GetNode("c"));
        Assert.IsTrue(MapNavigationEditCommands.TryConnectEdge(document, "bc", "b", "c", 6f, 10f));
        Assert.AreEqual("bc", graph.GetOutgoingEdges("b")[0].edgeId);
        Assert.AreEqual(10f, graph.GetArcLength("bc"));
        Assert.IsTrue(RoadPathQuery.TryFindPath(graph, "a", "c", VehicleRole.Civilian, 1f, out var path));
        CollectionAssert.AreEqual(new[] { "e", "bc" }, path);
    }

    /// <summary>C09: rejected collapsed/non-finite geometry leaves both draft and indexed views unchanged.</summary>
    [TestCase(0f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectedMoveLeavesGeometrySpawnAndCacheUnchanged(float newX) {
        var document = StraightDocument();
        document.spawnPoints.Add(Spawn(5f));
        var graph = new RoadGraphRuntime(document);
        Assert.IsFalse(MapNavigationEditCommands.TryMoveNode(document, "b", newX, 0f));
        Assert.AreEqual(10f, document.nodes[1].x);
        Assert.AreEqual(5f, document.spawnPoints[0].distanceAlongEdge);
        Assert.AreEqual(10f, graph.GetArcLength("e"));
        Assert.AreEqual(10f, graph.GetNode("b").x);
    }

    /// <summary>C09: direct DTO writes stay detached until explicit rebuild, so geometry and costs cannot mix revisions.</summary>
    [Test]
    public void DirectDtoWritesRequireExplicitCoherentRebuild() {
        var document = StraightDocument();
        document.edges[0].orderedPoints.Add(new Vector2(5f, 0f));
        var graph = new RoadGraphRuntime(document);
        document.nodes[1].x = 20f;
        document.edges[0].orderedPoints[0] = new Vector2(10f, 0f);
        Assert.AreEqual(10f, graph.GetNode("b").x);
        Assert.AreEqual(5f, graph.GetEdge("e").orderedPoints[0].x);
        Assert.AreEqual(10f, graph.GetArcLength("e"));
        graph.Rebuild();
        Assert.AreEqual(20f, graph.GetNode("b").x);
        Assert.AreEqual(10f, graph.GetEdge("e").orderedPoints[0].x);
        Assert.AreEqual(20f, graph.GetArcLength("e"));
    }

    /// <summary>C10: the nose of a long footprint can overlap a building even when width and pivot fit.</summary>
    [Test]
    public void SpawnLengthRejectsBuildingBeyondPivot() {
        var document = StraightDocument();
        var spawn = Spawn(5f);
        var query = new StaticGeometryQuery(new Rect(7f, -0.25f, 0.25f, 0.5f));
        Assert.IsTrue(RoadClearanceChecker.FitsSpawnFootprint(spawn, document.edges[0], 0f));
        spawn.clearanceLength = 2f;
        Assert.IsTrue(CheckSpawn(document, spawn, Vector2.zero, query));
        spawn.clearanceLength = 6f;
        Assert.IsFalse(CheckSpawn(document, spawn, Vector2.zero, query));
    }

    /// <summary>C10: heading rotates both collider length and offset according to the +Y-forward contract.</summary>
    [Test]
    public void SpawnOrientationAndOffsetReachGeometryQuery() {
        var document = StraightDocument();
        var query = new StaticGeometryQuery();
        Assert.IsTrue(CheckSpawn(document, Spawn(5f), new Vector2(1f, 2f), query));
        Assert.That(query.center.x, Is.EqualTo(7f).Within(0.0001f));
        Assert.That(query.center.y, Is.EqualTo(-1f).Within(0.0001f));
        Assert.That(query.heading, Is.EqualTo(-90f).Within(0.0001f));
        Assert.AreEqual(new Vector2(2f, 4f), query.footprint);
        var wall = new StaticGeometryQuery(new Rect(8f, -1.1f, 0.1f, 0.2f));
        Assert.IsTrue(CheckSpawn(document, Spawn(5f), Vector2.zero, wall));
        Assert.IsFalse(CheckSpawn(document, Spawn(5f), new Vector2(1f, 2f), wall));
    }

    /// <summary>C10: local lateral offset must fit within the road corridor, not just the unshifted width.</summary>
    [Test]
    public void LateralOffsetCannotPushFootprintOutsideRoadWidth() {
        var document = StraightDocument();
        document.edges[0].usableWidth = 3f;
        Assert.IsTrue(CheckSpawn(document, Spawn(5f), Vector2.zero, new StaticGeometryQuery()));
        Assert.IsFalse(CheckSpawn(document, Spawn(5f), new Vector2(1f, 0f), new StaticGeometryQuery()));
    }

    /// <summary>C10: spawn orientation follows the polyline segment at its arc-distance attachment.</summary>
    [Test]
    public void SpawnUsesInteriorPolylineAndArcDistance() {
        var document = StraightDocument();
        document.nodes[1].x = 5f;
        document.nodes[1].y = 5f;
        document.edges[0].orderedPoints.Add(new Vector2(0f, 5f));
        var query = new StaticGeometryQuery();
        Assert.IsTrue(CheckSpawn(document, Spawn(7f), Vector2.zero, query));
        Assert.That(query.center.x, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(query.center.y, Is.EqualTo(5f).Within(0.0001f));
        Assert.That(query.heading, Is.EqualTo(-90f).Within(0.0001f));
        Assert.IsFalse(CheckSpawn(document, Spawn(11f), Vector2.zero, query));
    }

    /// <summary>C10: a thin building between clear endpoints blocks the continuous swept road envelope.</summary>
    [Test]
    public void ContinuousSweepRejectsObstacleBetweenClearEndpoints() {
        var document = StraightDocument();
        var query = new StaticGeometryQuery(new Rect(4.95f, -0.1f, 0.1f, 0.2f));
        var footprint = new Vector2(1f, 2f);
        Assert.IsTrue(RoadFootprintClearance.IsPoseClear(Vector2.zero, footprint, Vector2.zero, -90f, Bounds, null, query));
        Assert.IsTrue(RoadFootprintClearance.IsPoseClear(new Vector2(10f, 0f), footprint, Vector2.zero, -90f, Bounds, null, query));
        Assert.IsTrue(RoadClearanceChecker.FitsEdgeWidth(document.edges[0], footprint.x, 0f));
        Assert.IsFalse(CheckSweep(document, footprint, Vector2.zero, query));
        Assert.IsTrue(CheckSweep(document, footprint, Vector2.zero, new StaticGeometryQuery()));
    }

    /// <summary>C10: a bend includes the rotating corner envelope, beyond its two straight segment envelopes.</summary>
    [Test]
    public void PolylineSweepIncludesRotationAtBend() {
        var document = StraightDocument();
        document.nodes[1].x = 5f;
        document.nodes[1].y = 5f;
        document.edges[0].orderedPoints.Add(new Vector2(0f, 5f));
        var footprint = new Vector2(1f, 4f);
        var query = new StaticGeometryQuery(new Rect(-1.3f, 6.2f, 0.1f, 0.1f));
        Assert.IsTrue(RoadFootprintClearance.IsSweepClear(Vector2.zero, new Vector2(0f, 5f), footprint, Vector2.zero, 0f, 0f, Bounds, null, query));
        Assert.IsTrue(RoadFootprintClearance.IsSweepClear(new Vector2(0f, 5f), new Vector2(5f, 5f), footprint, Vector2.zero, -90f, -90f, Bounds, null, query));
        Assert.IsFalse(CheckSweep(document, footprint, Vector2.zero, query));
        Assert.IsTrue(CheckSweep(document, footprint, Vector2.zero, new StaticGeometryQuery()));
    }

    /// <summary>C10: bounds and exclusion checks use the complete rotated rectangle instead of only its pivot.</summary>
    [Test]
    public void FootprintBoundsAndExclusionsRejectPartialOverlap() {
        var footprint = new Vector2(2f, 6f);
        var query = new StaticGeometryQuery();
        Assert.IsFalse(RoadFootprintClearance.IsPoseClear(Vector2.zero, footprint, Vector2.zero, -90f,
            new Rect(-2f, -2f, 4f, 4f), null, query));
        var exclusions = new List<NoSpawnRegion> { new NoSpawnRegion { area = new Rect(2f, -0.2f, 0.2f, 0.4f) } };
        Assert.IsFalse(RoadFootprintClearance.IsPoseClear(Vector2.zero, footprint, Vector2.zero, -90f, Bounds, exclusions, query));
        Assert.IsTrue(RoadFootprintClearance.IsPoseClear(Vector2.zero, footprint, Vector2.zero, 0f, Bounds, exclusions, query));
    }

    /// <summary>C10: a swept collider offset is retained over the whole translation, including between endpoints.</summary>
    [Test]
    public void SweepIncludesRotatedColliderOffset() {
        var document = StraightDocument();
        var query = new StaticGeometryQuery(new Rect(4.9f, -1.1f, 0.2f, 0.2f));
        Assert.IsTrue(CheckSweep(document, new Vector2(1f, 2f), Vector2.zero, query));
        Assert.IsFalse(CheckSweep(document, new Vector2(1f, 2f), new Vector2(1f, 0f), query));
    }

    /// <summary>C10: absent environment checks and invalid dimensions never produce a clearance certificate.</summary>
    [Test]
    public void MissingQueryOrInvalidFootprintFailsClosed() {
        var document = StraightDocument();
        Assert.IsFalse(CheckSpawn(document, Spawn(5f), Vector2.zero, null));
        Assert.IsFalse(CheckSweep(document, new Vector2(2f, 4f), Vector2.zero, null));
        var spawn = Spawn(5f);
        spawn.clearanceLength = float.NaN;
        Assert.IsFalse(CheckSpawn(document, spawn, Vector2.zero, new StaticGeometryQuery()));
        spawn.clearanceLength = 0f;
        Assert.IsFalse(CheckSpawn(document, spawn, Vector2.zero, new StaticGeometryQuery()));
        Assert.IsFalse(RoadFootprintClearance.IsPoseClear(Vector2.zero, Vector2.one, Vector2.zero, float.NaN, Bounds, null, new StaticGeometryQuery()));
        Assert.IsFalse(RoadFootprintClearance.IsPoseClear(Vector2.zero, Vector2.one, Vector2.zero, 0f, default, null, new StaticGeometryQuery()));
    }

    static MapNavigationDocument StraightDocument() {
        var document = new MapNavigationDocument { localBounds = Bounds };
        document.nodes.Add(Node("a", 0f, 0f));
        document.nodes.Add(Node("b", 10f, 0f));
        document.edges.Add(Edge("e", "a", "b"));
        return document;
    }

    static MapNavigationDocument JunctionDocument() {
        var document = new MapNavigationDocument { localBounds = Bounds };
        document.nodes.Add(Node("a", -10f, 0f));
        document.nodes.Add(Node("hub", 0f, 0f));
        document.nodes.Add(Node("north", 0f, 10f));
        document.nodes.Add(Node("east", 10f, 0f));
        document.edges.Add(Edge("in", "a", "hub"));
        document.edges.Add(Edge("allowed", "hub", "north"));
        document.edges.Add(Edge("blocked", "hub", "east"));
        document.edges[1].startJunctionId = "j";
        document.edges[2].startJunctionId = "j";
        var junction = new JunctionRecord { junctionId = "j" };
        junction.allowedTransitions.Add(new JunctionTransition { fromEdgeId = "in", toEdgeId = "allowed" });
        document.junctions.Add(junction);
        return document;
    }

    static RoadNodeRecord Node(string id, float x, float y) => new RoadNodeRecord { nodeId = id, x = x, y = y };
    static RoadEdgeRecord Edge(string id, string from, string to) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f, speedLimit = 10f };
    static CivilianRouteRecord Route() => new CivilianRouteRecord { routeId = "r", edgeIds = new List<string> { "e" } };
    static VehicleSpawnRecord Spawn(float distance) => new VehicleSpawnRecord { spawnId = "s", edgeId = "e", distanceAlongEdge = distance, clearanceWidth = 2f, clearanceLength = 4f };

    static bool CheckSpawn(MapNavigationDocument document, VehicleSpawnRecord spawn, Vector2 offset, IAreaClearanceQuery query) =>
        RoadClearanceChecker.IsSpawnClear(document.nodes[0], document.edges[0], document.nodes[1], spawn, offset, 0f, document.localBounds, document.noSpawnRegions, query);

    static bool CheckSweep(MapNavigationDocument document, Vector2 footprint, Vector2 offset, IAreaClearanceQuery query) =>
        RoadClearanceChecker.IsEdgeSweepClear(document.nodes[0], document.edges[0], document.nodes[1], footprint, offset, 0f, document.localBounds, document.noSpawnRegions, query);

    // Independent rectangle projection fixture. Obstacles have nonzero area, so this tests full
    // injected geometry queries rather than canned true/false responses or point-only occupancy.
    sealed class StaticGeometryQuery : IAreaClearanceQuery {
        readonly Rect[] obstacles;
        public Vector2 center;
        public Vector2 footprint;
        public float heading;

        public StaticGeometryQuery(params Rect[] obstacles) { this.obstacles = obstacles; }

        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            this.center = center;
            this.footprint = footprint;
            heading = headingDegrees;
            Vector2 forward = MapNavigationCoordinates.HeadingDegreesToDirection(headingDegrees);
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 horizontal = right * (footprint.x * 0.5f), vertical = forward * (footprint.y * 0.5f);
            var corners = new[] { center - horizontal - vertical, center + horizontal - vertical,
                center + horizontal + vertical, center - horizontal + vertical };
            foreach (var obstacle in obstacles) {
                var obstacleCorners = new[] { new Vector2(obstacle.xMin, obstacle.yMin), new Vector2(obstacle.xMax, obstacle.yMin),
                    new Vector2(obstacle.xMax, obstacle.yMax), new Vector2(obstacle.xMin, obstacle.yMax) };
                bool separated = false;
                foreach (var axis in new[] { Vector2.right, Vector2.up, right, forward }) {
                    Project(corners, axis, out float min, out float max);
                    Project(obstacleCorners, axis, out float obstacleMin, out float obstacleMax);
                    if (max < obstacleMin || obstacleMax < min) { separated = true; break; }
                }
                if (!separated) return false;
            }
            return true;
        }

        static void Project(Vector2[] corners, Vector2 axis, out float min, out float max) {
            min = max = Vector2.Dot(corners[0], axis);
            for (int i = 1; i < corners.Length; i++) {
                float value = Vector2.Dot(corners[i], axis);
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
        }
    }
}
