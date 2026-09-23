using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Focused S08.2b.2b cadence and read-only actual-map acceptance tests.</summary>
public sealed class PolicePursuitTargetPlannerTests {
    /// <summary>Confirms the first call at session time zero performs exactly one query.</summary>
    [Test]
    public void Planner_FirstCallAtZeroAttemptsOnce() {
        var planner = CreatePlanner(out var profile, out _);
        var result = Update(planner, 0f, new Vector2(0f, 4f));
        Assert.AreEqual(1, planner.AttemptCount);
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms paused or repeated timestamps do not repeat an unchanged query.</summary>
    [Test]
    public void Planner_RepeatedAndPausedTimeDoNotRequery() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        Update(planner, 0f, new Vector2(0f, 4f));
        Update(planner, 0.1f, new Vector2(0f, 4f));
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms displacement cannot bypass the hard minimum interval.</summary>
    [Test]
    public void Planner_DisplacementBeforeMinimumWaits() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        Update(planner, 0.1f, new Vector2(0f, 2f));
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms displacement can trigger a new query once the minimum interval elapses.</summary>
    [Test]
    public void Planner_DisplacementAtMinimumRequeries() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        Update(planner, 0.25f, new Vector2(0f, 2f));
        Assert.AreEqual(2, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms refresh cadence is measured from the last attempt, not the last success.</summary>
    [Test]
    public void Planner_UnchangedSuccessfulTargetRefreshesFromLastAttempt() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        Update(planner, 0.74f, new Vector2(0f, 4f));
        Assert.AreEqual(1, planner.AttemptCount);
        Update(planner, 0.75f, new Vector2(0f, 4f));
        Assert.AreEqual(2, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms failed attempts are timestamped and retry only after the hard minimum.</summary>
    [Test]
    public void Planner_FailedAttemptUsesSameCadence() {
        var planner = CreatePlanner(out var profile, out _);
        var failed = planner.Update(new Vector2(5f, -4f), Vector2.up, new Vector2(0f, 4f), 0f, false, default);
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, failed.status);
        UpdateFailed(planner, 0.1f);
        Assert.AreEqual(1, planner.AttemptCount);
        UpdateFailed(planner, 0.25f);
        Assert.AreEqual(2, planner.AttemptCount);
        Assert.Greater(planner.LastWorkConsumed, 0);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Uses the target body forward axis, not the side-facing police axis, for reverse detection.</summary>
    [Test]
    public void Intercept_SideFacingPoliceTreatsForwardPlayerVelocityAsNonReversing() {
        var planner = CreateInterceptPlanner(out var profile, out _);
        var result = planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f),
            Vector2.right, Vector2.right, 3, 0f, true, new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreNotEqual(PoliceRoadTargetQuery.Status.InvalidInput, result.status);
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms a graph rebuild invalidates the route immediately but respects cadence.</summary>
    [Test]
    public void Planner_GraphChangeInvalidatesWithoutBypassingMinimum() {
        var planner = CreatePlanner(out var profile, out var document);
        Update(planner, 0f, new Vector2(0f, 4f));
        document.nodes[1].y = 6f;
        var graph = GetGraph(planner);
        graph.Rebuild();
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.Update(new Vector2(0f, -4f), Vector2.up,
            new Vector2(0f, 4f), 0.1f, true, new RoadPathQuery.EdgeAnchor("edge", 1f)).status);
        Assert.AreEqual(1, planner.AttemptCount);
        planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f), 0.25f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(2, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms explicit invalidation exposes safe wait and follows normal cadence.</summary>
    [Test]
    public void Planner_ExplicitInvalidationDoesNotClearAttemptTimestamp() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        planner.InvalidateRoute();
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.CurrentResult.status);
        planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f), 0.1f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(1, planner.AttemptCount);
        planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f), 0.25f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(2, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms reset permits a new zero-time attempt and clears diagnostics.</summary>
    [Test]
    public void Planner_ResetRestartsTimelineAndCounters() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        var graph = GetGraph(planner);
        graph.Rebuild();
        planner.Reset();
        Assert.AreEqual(0, planner.AttemptCount);
        Assert.AreEqual(0, planner.LastWorkConsumed);
        Assert.AreEqual(graph.Version, planner.CurrentResult.graphVersion);
        planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f), 0f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms backward/nonfinite time and changed profile constraints never return a stale route.</summary>
    [Test]
    public void Planner_BadTimeAndProfileMutationFailClosed() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.Update(Vector2.zero, Vector2.up, new Vector2(0f, 4f), -0.1f,
            true, new RoadPathQuery.EdgeAnchor("edge", 1f)).status);
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.Update(Vector2.zero, Vector2.up, new Vector2(0f, 4f), float.NaN,
            true, new RoadPathQuery.EdgeAnchor("edge", 1f)).status);
        profile.colliderSize = new Vector2(1.5f, 2f);
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.Update(Vector2.zero, Vector2.up, new Vector2(0f, 4f), 0.25f,
            true, new RoadPathQuery.EdgeAnchor("edge", 1f)).status);
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms a negative clock is rejected before cadence can expose the cached route.</summary>
    [Test]
    public void Planner_NegativeClockFailsClosedInsideMinimum() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        var result = planner.Update(new Vector2(0f, -4f), Vector2.up, new Vector2(0f, 4f), -0.01f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, result.status);
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms invalid pose data fails closed before the hard-minimum cadence gate.</summary>
    [Test]
    public void Planner_InvalidPoseInsideMinimumFailsClosedWithoutRequery() {
        var planner = CreatePlanner(out var profile, out _);
        Update(planner, 0f, new Vector2(0f, 4f));
        var result = planner.Update(new Vector2(100f, 100f), Vector2.zero, new Vector2(0f, 4f), 0.1f, true,
            new RoadPathQuery.EdgeAnchor("edge", 1f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, result.status);
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms caller navigation bounds mutation cannot alter the planner's owned bounds snapshot.</summary>
    [Test]
    public void Planner_OriginalNavigationMutationDoesNotChangeOwnedBounds() {
        var planner = CreatePlanner(out var profile, out var document);
        document.localBounds = new Rect(100f, 100f, 1f, 1f);
        var result = Update(planner, 0f, new Vector2(0f, 4f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms CurrentResult invalidates a cached route as soon as the graph version changes.</summary>
    [Test]
    public void Planner_CurrentResultAfterGraphRebuildIsSafeWait() {
        var planner = CreatePlanner(out var profile, out var document);
        Update(planner, 0f, new Vector2(0f, 4f));
        document.nodes[1].y = 6f;
        GetGraph(planner).Rebuild();
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, planner.CurrentResult.status);
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Confirms bound settings are copied and later caller mutations do not alter cadence.</summary>
    [Test]
    public void Planner_SettingsMutationDoesNotChangePrivateSnapshot() {
        var settings = new PoliceNavigationSettings();
        var planner = CreatePlanner(out var profile, out _, settings);
        Update(planner, 0f, new Vector2(0f, 4f));
        settings.hardMinimumInterval = 0.01f;
        Update(planner, 0.1f, new Vector2(0f, 2f));
        Assert.AreEqual(1, planner.AttemptCount);
        Object.DestroyImmediate(profile);
    }

    /// <summary>Loads the detached NarrowDistrict map and confirms standard and sport reach the west target.</summary>
    [Test]
    public void ActualMap_WestTargetRoutesForStandardAndSport() {
        var map = LoadMap();
        var document = map.ResolveDocument();
        var graph = new RoadGraphRuntime(document);
        foreach (var path in new[] { "Assets/ScriptableObjects/Police/PoliceStandardData.asset", "Assets/ScriptableObjects/Police/PoliceSportData.asset" }) {
            var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>(path);
            var planner = new PolicePursuitTargetPlanner(graph, document, document.localBounds, profile, Vector2.zero, null, new PoliceNavigationSettings());
            var result = planner.Update(new Vector2(6f, -12.5f), Vector2.up, new Vector2(-20f, 26f), 0f, true,
                new RoadPathQuery.EdgeAnchor("nd-pilot-e0", 10f));
            Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, result.status, path);
            Assert.AreEqual(new Vector2(-20f, 26f), result.roadTarget, path);
            Assert.AreEqual("nd-west-e2", result.spans[result.spans.Count - 1].edgeId, path);
        }
    }

    /// <summary>Confirms heavy west rejection and heavy pilot routing on the same detached graph.</summary>
    [Test]
    public void ActualMap_HeavyRejectsWestButRoutesPilot() {
        var map = LoadMap();
        var document = map.ResolveDocument();
        var graph = new RoadGraphRuntime(document);
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavyData.asset");
        var planner = new PolicePursuitTargetPlanner(graph, document, document.localBounds, profile, Vector2.zero, null, new PoliceNavigationSettings());
        var west = planner.Update(new Vector2(6f, -12.5f), Vector2.up, new Vector2(-20f, 26f), 0f, true,
            new RoadPathQuery.EdgeAnchor("nd-pilot-e0", 10f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.SafeWait, west.status);
        planner.Reset();
        var pilot = planner.Update(new Vector2(6f, -12.5f), Vector2.up, new Vector2(20f, 5f), 0f, true,
            new RoadPathQuery.EdgeAnchor("nd-pilot-e0", 10f));
        Assert.AreEqual(PoliceRoadTargetQuery.Status.Route, pilot.status);
        Assert.AreEqual(new Vector2(20f, 5f), pilot.roadTarget);
    }

    /// <summary>Confirms clearing civilian routes on a detached copy does not change police navigation.</summary>
    [Test]
    public void ActualMap_CivilianRoutesDoNotAffectDetachedPoliceResult() {
        var map = LoadMap();
        var first = map.ResolveDocument();
        var second = map.ResolveDocument();
        second.civilianRoutes.Clear();
        var profile = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceStandardData.asset");
        var firstResult = QueryMap(first, profile, new Vector2(-20f, 26f));
        var secondResult = QueryMap(second, profile, new Vector2(-20f, 26f));
        Assert.AreEqual(firstResult.status, secondResult.status);
        Assert.AreEqual(firstResult.roadTarget, secondResult.roadTarget);
        CollectionAssert.AreEqual(EdgeIds(firstResult.spans), EdgeIds(secondResult.spans));
    }

    static PoliceRoadTargetQuery.Result QueryMap(MapNavigationDocument document, NpcVehicleProfile profile, Vector2 target) {
        var planner = new PolicePursuitTargetPlanner(new RoadGraphRuntime(document), document, document.localBounds, profile, Vector2.zero, null, new PoliceNavigationSettings());
        return planner.Update(new Vector2(6f, -12.5f), Vector2.up, target, 0f, true, new RoadPathQuery.EdgeAnchor("nd-pilot-e0", 10f));
    }

    static TrafficMapData LoadMap() => AssetDatabase.LoadAssetAtPath<TrafficMapData>("Assets/ScriptableObjects/Traffic/NarrowDistrict/Navigation.asset");

    static PolicePursuitTargetPlanner CreatePlanner(out NpcVehicleProfile profile, out MapNavigationDocument document,
        PoliceNavigationSettings settings = null) {
        document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 20f, 20f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = -5f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 5f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "edge", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.allowedRoles.Add(VehicleRole.Police);
        profile.colliderSize = new Vector2(1f, 2f);
        profile.motorSettings.minimumTurningRadius = 1f;
        return new PolicePursuitTargetPlanner(new RoadGraphRuntime(document), document, document.localBounds, profile, Vector2.zero, null,
            settings ?? new PoliceNavigationSettings());
    }

    static PolicePursuitTargetPlanner CreateInterceptPlanner(out NpcVehicleProfile profile, out MapNavigationDocument document) {
        var settings = new PoliceNavigationSettings();
        document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 20f, 20f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = -5f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 5f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "edge", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        profile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        profile.allowedRoles.Add(VehicleRole.Police);
        profile.colliderSize = new Vector2(1f, 2f);
        profile.motorSettings.minimumTurningRadius = 1f;
        return new PolicePursuitTargetPlanner(new RoadGraphRuntime(document), document, document.localBounds, profile, Vector2.zero, null,
            settings, new PoliceInterceptSettings(), PoliceTacticalRole.Intercept, 7, settings.projectionSearchRadius);
    }

    static PoliceRoadTargetQuery.Result Update(PolicePursuitTargetPlanner planner, float time, Vector2 target) => planner.Update(
        new Vector2(0f, -4f), Vector2.up, target, time, true, new RoadPathQuery.EdgeAnchor("edge", 1f));

    static PoliceRoadTargetQuery.Result UpdateFailed(PolicePursuitTargetPlanner planner, float time) => planner.Update(
        new Vector2(5f, -4f), Vector2.up, new Vector2(0f, 4f), time, false, default);

    static RoadGraphRuntime GetGraph(PolicePursuitTargetPlanner planner) {
        var field = typeof(PolicePursuitTargetPlanner).GetField("graph", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (RoadGraphRuntime)field.GetValue(planner);
    }

    static List<string> EdgeIds(IReadOnlyList<RoadPathQuery.PathSpan> spans) {
        var ids = new List<string>();
        foreach (var span in spans) ids.Add(span.edgeId);
        return ids;
    }
}
