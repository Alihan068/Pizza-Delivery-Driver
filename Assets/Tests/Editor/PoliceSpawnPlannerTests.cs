using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure EditMode coverage for whole-footprint safety and bounded police placement.</summary>
public sealed class PoliceSpawnPlannerTests {
    [Test]
    public void WholeFootprintVisibleWhenPivotIsOffscreenBecauseOfRotationAndOffset() {
        var candidate = new PoliceSpawnCandidate { position = new Vector2(-2f, -1f), footprint = Vector2.one, headingDegrees = 45f };
        var geometry = new PolicePrefabGeometry(new Vector2(1f, 1f), new Vector2(2f, 0f));
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(candidate, new Rect(0f, 0f, 1f, 1f), 0f, geometry));
    }

    [Test]
    public void LargerVisualEnvelopeMakesOffscreenCandidateVisible() {
        var candidate = new PoliceSpawnCandidate { position = new Vector2(-2f, 0.5f), footprint = Vector2.one, headingDegrees = 0f };
        var geometry = new PolicePrefabGeometry(Vector2.one, Vector2.zero, new Vector2(4f, 1f), Vector2.zero);
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(candidate, new Rect(0f, 0f, 1f, 1f), 0f, geometry));
    }

    [Test]
    public void InvalidZoomAndNullClearanceFailClosed() {
        var candidate = new PoliceSpawnCandidate { position = new Vector2(10f, 10f), footprint = Vector2.one };
        Assert.IsFalse(PoliceSpawnSafetyPolicy.IsSafe(candidate, new Rect(0f, 0f, 0f, 10f), 0f, Vector2.zero, 0f, new AlwaysClear(), out _));
        Assert.IsFalse(PoliceSpawnSafetyPolicy.IsSafe(candidate, new Rect(0f, 0f, 1f, 1f), 0f, Vector2.zero, 0f, null, out _));
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(candidate, new Rect(0f, 0f, 0f, 10f), 0f));
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(candidate, new Rect(0f, 0f, 1f, 1f), float.MaxValue));
    }

    [Test]
    public void SurfaceAndReactionDistanceAreIncluded() {
        var context = BaseSafetyContext();
        context.playerVelocity = new Vector2(1f, 0f);
        context.minimumDistance = 8f;
        context.reactionSeconds = 2f;
        context.playerSurfaceRadius = 1f;
        var candidate = new PoliceSpawnCandidate { position = new Vector2(12f, 0f), footprint = new Vector2(2f, 4f) };
        Assert.IsFalse(PoliceSpawnSafetyPolicy.IsSafe(candidate, context, out _));
        candidate.position = new Vector2(14f, 0f);
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsSafe(candidate, context, out _));
    }

    [Test]
    public void FullFootprintRejectsNoSpawnAndMapBoundary() {
        var context = BaseSafetyContext();
        context.cameraBounds = new Rect(-20f, -20f, 2f, 2f);
        context.localMapBounds = new Rect(-5f, -5f, 10f, 10f);
        context.noSpawnRegions = new List<NoSpawnRegion> { new NoSpawnRegion { area = new Rect(2f, 2f, 2f, 2f) } };
        var candidate = new PoliceSpawnCandidate { position = new Vector2(3f, 3f), footprint = new Vector2(2f, 2f) };
        Assert.IsFalse(PoliceSpawnSafetyPolicy.IsSafe(candidate, context, out _));
        candidate.position = new Vector2(4.8f, -3f);
        Assert.IsFalse(PoliceSpawnSafetyPolicy.IsSafe(candidate, context, out _));
    }

    [Test]
    public void PlannerRejectsUnreachableEntryAndSelectsReachableEntryWithinBudget() {
        var document = LinearDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "badA", x = 20f, y = -10f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "badB", x = 20f, y = 10f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "bad", fromNodeId = "badA", toNodeId = "badB", usableWidth = 4f, speedLimit = 10f,
            allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "badSpawn", edgeId = "bad", distanceAlongEdge = 10f, role = VehicleRole.Police });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "aBad", spawnId = "badSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "bGood", spawnId = "goodSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var profile = Profile();
        var planner = new PoliceSpawnPlanner();
        // projectionSearchRadius 40 / indexCellSize 4 spans 21 cells per axis: 21*21 = 441.
        var context = Context(document, profile, 4096);
        Assert.IsTrue(planner.TryPlan(context, Config(), out var accepted), planner.LastFailureReason);
        Assert.AreEqual("bGood", accepted.entryId);
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void PlannerReturnsFalseWhenSharedBudgetIsExhausted() {
        var document = LinearDocument();
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "entry", spawnId = "goodSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var profile = Profile();
        var context = Context(document, profile, 1);
        var planner = new PoliceSpawnPlanner();
        Assert.IsFalse(planner.TryPlan(context, Config(), out _));
        Assert.That(planner.LastFailureReason, Does.Contain("budget"));
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void PlannerRejectsSpawnWhoseAuthoredClearanceIsTooSmall() {
        var document = LinearDocument();
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "entry", spawnId = "goodSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.spawnPoints[0].clearanceWidth = 1f;
        var profile = Profile();
        var planner = new PoliceSpawnPlanner();
        Assert.IsFalse(planner.TryPlan(Context(document, profile, 256), Config(), out _));
        Object.DestroyImmediate(profile);
    }

    [Test]
    public void RequiredRearQuotaDoesNotFallbackToFrontEntry() {
        var document = LinearDocument();
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "frontSpawn", edgeId = "road", distanceAlongEdge = 35f, role = VehicleRole.Police, clearanceWidth = 2f, clearanceLength = 4f });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "aFront", spawnId = "frontSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "bRear", spawnId = "goodSpawn", allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var profile = Profile();
        // The spatial projection alone can inspect 21*21 = 441 cells; use the authored 4096 default.
        var context = Context(document, profile, 4096);
        context.playerWorldVelocity = new Vector2(0f, 1f);
        var planner = new PoliceSpawnPlanner();
        var config = Config();
        config.requireBehind = true;
        Assert.IsTrue(planner.TryPlan(context, config, out var accepted), planner.LastFailureReason);
        Assert.AreEqual("bRear", accepted.entryId);
        Object.DestroyImmediate(profile);
    }

    static PoliceSpawnSafetyContext BaseSafetyContext() {
        return new PoliceSpawnSafetyContext {
            cameraBounds = new Rect(-2f, -2f, 4f, 4f), cameraMargin = 0f, playerPosition = Vector2.zero,
            playerVelocity = Vector2.zero, playerSurfaceRadius = 0f, minimumDistance = 0f, reactionSeconds = 0f,
            mapOriginWorld = Vector2.zero, localMapBounds = new Rect(-100f, -100f, 200f, 200f),
            noSpawnRegions = null, geometry = new PolicePrefabGeometry(new Vector2(2f, 4f), Vector2.zero),
            worldClearance = new AlwaysClear(), localStaticClearance = new AlwaysClear()
        };
    }

    static MapNavigationDocument LinearDocument() {
        var document = new MapNavigationDocument { localBounds = new Rect(-30f, -30f, 60f, 60f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = -20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 20f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "road", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 10f,
            allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "goodSpawn", edgeId = "road", distanceAlongEdge = 10f, role = VehicleRole.Police, clearanceWidth = 2f, clearanceLength = 4f });
        return document;
    }

    static NpcVehicleProfile Profile() {
        var profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.allowedRoles.Add(VehicleRole.Police);
        profile.colliderSize = new Vector2(2f, 4f);
        profile.motorSettings.minimumTurningRadius = 1f;
        return profile;
    }

    static PoliceSpawnPlanner.Context Context(MapNavigationDocument document, NpcVehicleProfile profile, int workBudget) {
        return new PoliceSpawnPlanner.Context {
            navigation = document, graph = new RoadGraphRuntime(document), mapOriginWorld = Vector2.zero, profile = profile,
            prefabGeometry = new PolicePrefabGeometry(new Vector2(2f, 4f), Vector2.zero), playerWorldPosition = new Vector2(0f, 10f),
            playerWorldVelocity = Vector2.zero, cameraWorldBounds = new Rect(-2f, -2f, 4f, 4f),
            navigationSettings = new PoliceNavigationSettings(workBudget: workBudget, projectionSearchRadius: 40f, indexCellSize: 4f),
            worldClearance = new AlwaysClear(), localStaticClearance = new AlwaysClear()
        };
    }

    static PoliceSpawnPlanner.Config Config() {
        return new PoliceSpawnPlanner.Config {
            placementSettings = new PolicePlacementSettings { minimumDistance = 0f, reactionSeconds = 0f, cameraMargin = 0f, maxCandidates = 8 }
        };
    }

    sealed class AlwaysClear : IAreaClearanceQuery {
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => true;
    }
}
