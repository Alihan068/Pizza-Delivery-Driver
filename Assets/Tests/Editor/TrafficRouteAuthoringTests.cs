using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only asset write and Undo acceptance without modifying the user's scene or saved assets.</summary>
public sealed class TrafficRouteAuthoringTests {
    TrafficMapData asset;
    Type window;

    /// <summary>Resolves the predefined editor assembly without introducing a runtime editor dependency.</summary>
    [SetUp]
    public void SetUp() {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            window = assembly.GetType("TrafficRouteAuthoringWindow");
            if (window != null) break;
        }
        Assert.IsNotNull(window);
        asset = ScriptableObject.CreateInstance<TrafficMapData>();
    }

    /// <summary>Removes only this test asset's Undo records and transient object.</summary>
    [TearDown]
    public void TearDown() {
        if (asset == null) return;
        Undo.ClearUndo(asset);
        UnityEngine.Object.DestroyImmediate(asset);
    }

    bool Apply(MapNavigationDocument document) => (bool)window.GetMethod("ApplyDraft", BindingFlags.Static | BindingFlags.Public)
        .Invoke(null, new object[] { asset, document, "Test traffic draft" });

    /// <summary>Serialization preserves the complete document; moving and Undo preserve IDs, geometry and spawn attachment.</summary>
    [Test]
    public void NodeMoveAndUndoRestoreExactDocument() {
        var document = Loop();
        Assert.IsTrue(Apply(document));
        string before = JsonUtility.ToJson(document);
        Assert.AreEqual(before, JsonUtility.ToJson(asset.ResolveDocument()));
        Undo.ClearUndo(asset);
        Undo.IncrementCurrentGroup();
        Assert.IsTrue((bool)window.GetMethod("MoveNode").Invoke(null, new object[] { asset, "b", new Vector2(0f, 40f) }));
        Assert.AreEqual(10f, asset.ResolveDocument().spawnPoints[0].distanceAlongEdge);
        Undo.FlushUndoRecordObjects();
        Undo.PerformUndo();
        Assert.AreEqual(before, JsonUtility.ToJson(asset.ResolveDocument()));
    }

    /// <summary>A loop opened by deleting its closing edge produces validation errors, keeping the window's valid-save gate closed.</summary>
    [Test]
    public void BrokenLoopIsInvalidAndDraftIsDetached() {
        var document = Loop();
        Assert.IsEmpty(TrafficMapValidator.Validate(document, null));
        Assert.IsTrue(Apply(document));
        document.civilianRoutes[0].edgeIds.RemoveAt(1);
        Assert.IsNotEmpty(TrafficMapValidator.Validate(document, null));
        Assert.IsEmpty(TrafficMapValidator.Validate(asset.ResolveDocument(), null));
    }

    /// <summary>Route, pool/target, spawn and police-entry edits operate on a detached candidate before asset Undo apply.</summary>
    [Test]
    public void SharedRouteSpawnAndPoliceCommandsApplyDetachedCandidate() {
        var document = Loop();
        MapNavigationDocument candidate = JsonUtility.FromJson<MapNavigationDocument>(JsonUtility.ToJson(document));
        Assert.IsTrue(MapNavigationEditCommands.TrySetRouteOrder(candidate, "loop", new List<string> { "ab", "ba" }, true, out string issue), issue);
        Assert.IsTrue(MapNavigationEditCommands.TryEditRoutePoolAndTarget(candidate, "loop", "pool", 2, out issue), issue);
        Assert.IsTrue(MapNavigationEditCommands.TryAddSpawn(candidate, new VehicleSpawnRecord {
            spawnId = "policeSpawn", edgeId = "ba", role = VehicleRole.Police, clearanceWidth = 1f, clearanceLength = 2f
        }, out issue), issue);
        Assert.IsTrue(MapNavigationEditCommands.TryAddPoliceEntry(candidate, "entry", "policeSpawn", new[] { VehicleRole.Police }, out issue), issue);
        Assert.IsTrue(Apply(candidate));

        Assert.IsEmpty(document.policeEntries);
        MapNavigationDocument applied = asset.ResolveDocument();
        Assert.AreEqual(2, applied.civilianRoutes[0].targetCount);
        Assert.AreEqual("policeSpawn", applied.policeEntries[0].spawnId);
    }

    /// <summary>Verifies the authoring helper retains a pending route edit between repeated redraw-style calls.</summary>
    [Test]
    public void PendingRouteHelperRetainsUnappliedTextAcrossCalls() {
        var editor = ScriptableObject.CreateInstance(window);
        try {
            var route = Loop().civilianRoutes[0];
            var getPending = window.GetMethod("GetPendingRoute", BindingFlags.Instance | BindingFlags.NonPublic);
            object pending = getPending.Invoke(editor, new object[] { route });
            pending.GetType().GetField("edgeIds", BindingFlags.Instance | BindingFlags.Public).SetValue(pending, "typed-but-not-applied");
            object samePending = getPending.Invoke(editor, new object[] { route });
            Assert.AreEqual("typed-but-not-applied", samePending.GetType().GetField("edgeIds", BindingFlags.Instance | BindingFlags.Public).GetValue(samePending));
        }
        finally {
            UnityEngine.Object.DestroyImmediate(editor);
        }
    }

    static MapNavigationDocument Loop() {
        var document = new MapNavigationDocument { mapId = "test", documentId = "authoring", localBounds = new Rect(-100f, -100f, 200f, 200f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a" });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", y = 20f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "ab", fromNodeId = "a", toNodeId = "b", usableWidth = 3f, speedLimit = 3f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "ba", fromNodeId = "b", toNodeId = "a", usableWidth = 3f, speedLimit = 3f });
        document.vehiclePools.Add(new VehiclePoolRecord { poolId = "pool", entries = new List<VehiclePoolEntry> { new VehiclePoolEntry { vehicleProfileId = "car", weight = 1f } } });
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "loop", loop = true, vehiclePoolId = "pool", targetCount = 1, edgeIds = new List<string> { "ab", "ba" } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "spawn", edgeId = "ab", routeId = "loop", distanceAlongEdge = 5f, clearanceWidth = 1f, clearanceLength = 2f });
        return document;
    }
}
