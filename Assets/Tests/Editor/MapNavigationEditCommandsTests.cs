using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused Edit Mode coverage for the bounded shared navigation edit authority.</summary>
public sealed class MapNavigationEditCommandsTests {
    /// <summary>Split, undo and redo preserve route order, deterministic ids and normalized spawn distance.</summary>
    [Test]
    public void SplitUndoRedoPreservesRouteOrderAndSpawnDistance() {
        MapNavigationDocument document = CreateTriangle();
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "e1", distanceAlongEdge = 7f, role = VehicleRole.Civilian });
        var history = new MapNavigationEditHistory(document);

        Assert.IsTrue(MapNavigationEditCommands.TrySplitEdge(document, "e1", 4f, "split", "e1a", "e1b", out string issue), issue);
        history.Record(document);
        Assert.That(document.civilianRoutes[0].edgeIds, Is.EqualTo(new[] { "e1a", "e1b", "e2", "e3" }));
        Assert.That(document.spawnPoints[0].edgeId, Is.EqualTo("e1b"));
        Assert.That(document.spawnPoints[0].distanceAlongEdge, Is.EqualTo(3f).Within(0.0001f));

        Assert.IsTrue(history.TryUndo(document));
        Assert.That(document.edges.Find(edge => edge.edgeId == "e1"), Is.Not.Null);
        Assert.That(document.civilianRoutes[0].edgeIds, Is.EqualTo(new[] { "e1", "e2", "e3" }));
        Assert.That(document.spawnPoints[0].distanceAlongEdge, Is.EqualTo(7f).Within(0.0001f));

        Assert.IsTrue(history.TryRedo(document));
        Assert.That(document.civilianRoutes[0].edgeIds, Is.EqualTo(new[] { "e1a", "e1b", "e2", "e3" }));
        Assert.That(document.spawnPoints[0].distanceAlongEdge, Is.EqualTo(3f).Within(0.0001f));
    }

    /// <summary>Referenced edge deletion and invalid splitting reject without changing the DTO.</summary>
    [Test]
    public void ReferencedDeleteAndInvalidSplitAreAtomic() {
        MapNavigationDocument document = CreateTriangle();
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "e1", distanceAlongEdge = 4f });
        string before = JsonUtility.ToJson(document);

        Assert.IsFalse(MapNavigationEditCommands.TryDeleteEdge(document, "e1", out string deleteIssue));
        Assert.That(deleteIssue, Is.Not.Empty);
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
        Assert.IsFalse(MapNavigationEditCommands.TrySplitEdge(document, "e1", 0f, "split", "e1a", "e1b", out string splitIssue));
        Assert.That(splitIssue, Is.Not.Empty);
        Assert.That(JsonUtility.ToJson(document), Is.EqualTo(before));
    }

    /// <summary>Moving a connected node preserves an attached spawn's normalized arc position.</summary>
    [Test]
    public void MoveNodePreservesSpawnNormalization() {
        var document = new MapNavigationDocument();
        Assert.IsTrue(MapNavigationEditCommands.TryCreateNode(document, "a", 0f, 0f));
        Assert.IsTrue(MapNavigationEditCommands.TryCreateNode(document, "b", 10f, 0f));
        Assert.IsTrue(MapNavigationEditCommands.TryConnectEdge(document, "e", "a", "b", 4f, 8f));
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "e", distanceAlongEdge = 5f });

        Assert.IsTrue(MapNavigationEditCommands.TryMoveNode(document, "b", 20f, 0f, out string issue), issue);
        Assert.That(document.spawnPoints[0].distanceAlongEdge, Is.EqualTo(10f).Within(0.0001f));
        Assert.That(RoadEdgeGeometry.ComputeLength(document.nodes[0], document.edges[0], document.nodes[1]), Is.EqualTo(20f).Within(0.0001f));
    }

    /// <summary>Splitting an incoming junction edge rewrites the incoming transition reference and remains structurally valid.</summary>
    [Test]
    public void SplitRewritesJunctionIncomingReferenceAndValidates() {
        MapNavigationDocument document = CreateTriangle();
        document.edges.Find(edge => edge.edgeId == "e1").endJunctionId = "junction";
        document.edges.Find(edge => edge.edgeId == "e2").startJunctionId = "junction";
        document.junctions.Add(new JunctionRecord {
            junctionId = "junction",
            allowedTransitions = new List<JunctionTransition> { new JunctionTransition { fromEdgeId = "e1", toEdgeId = "e2", priority = 1 } }
        });
        Assert.IsEmpty(TrafficMapValidator.Validate(document, null));

        Assert.IsTrue(MapNavigationEditCommands.TrySplitEdge(document, "e1", 4f, "split", "e1a", "e1b", out string issue), issue);
        JunctionTransition transition = document.junctions[0].allowedTransitions[0];
        Assert.That(transition.fromEdgeId, Is.EqualTo("e1b"));
        Assert.That(transition.toEdgeId, Is.EqualTo("e2"));
        Assert.IsEmpty(TrafficMapValidator.Validate(document, null));
    }

    static MapNavigationDocument CreateTriangle() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 10f, y = 10f });
        document.edges.Add(Edge("e1", "a", "b"));
        document.edges.Add(Edge("e2", "b", "c"));
        document.edges.Add(Edge("e3", "c", "a"));
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "route", edgeIds = new List<string> { "e1", "e2", "e3" }, loop = true });
        return document;
    }

    static RoadEdgeRecord Edge(string id, string from, string to) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 4f, speedLimit = 8f };
}
