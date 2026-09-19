using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure per-route civilian population bookkeeping and spawn planning. Reads every route's target
/// from the navigation data, caps the sum at the session's civilian limit (routes are trimmed in
/// authoring order so the same data always yields the same targets), picks a fresh weighted
/// profile for every new life from the route's pool, and spreads spawns over the route's authored
/// spawn points preferring the ones farthest from the player. Randomness comes only from the
/// injected <see cref="System.Random"/> (seeded per session) — never from UnityEngine.Random, so
/// customers and other systems keep their own streams. When civilian traffic is disabled by the
/// session rules, no request is ever produced. The planner decides; the manager owns the actual
/// pool/population/physics transitions and reports back through the Note* methods.
/// </summary>
public sealed class CivilianPopulationPlanner {
    /// <summary>One concrete spawn the manager should try to materialize.</summary>
    public readonly struct SpawnRequest {
        public readonly string routeId;
        public readonly string spawnId;
        public readonly string vehicleProfileId;
        /// <summary>Map-local pose.</summary>
        public readonly Vector2 position;
        public readonly float headingDegrees;

        public SpawnRequest(string routeId, string spawnId, string vehicleProfileId, Vector2 position, float headingDegrees) {
            this.routeId = routeId;
            this.spawnId = spawnId;
            this.vehicleProfileId = vehicleProfileId;
            this.position = position;
            this.headingDegrees = headingDegrees;
        }
    }

    sealed class RouteState {
        public CivilianRouteRecord route;
        public VehiclePoolRecord pool;
        public int target;
        public int alive;
        public int pending;
        public readonly List<VehicleSpawnRecord> spawns = new List<VehicleSpawnRecord>();
        public readonly List<string> issues = new List<string>();
        public int Deficit => Mathf.Max(0, target - alive - pending);
    }

    readonly RoadGraphRuntime graph;
    readonly ITrafficProfileCatalog catalog;
    readonly System.Random random;
    readonly bool enabled;
    readonly List<RouteState> routes = new List<RouteState>();
    readonly Dictionary<string, RouteState> routesById = new Dictionary<string, RouteState>();
    readonly List<VehicleSpawnRecord> scratchSpawns = new List<VehicleSpawnRecord>();

    /// <summary>Builds route targets from the document, capped at the session's civilian limit.</summary>
    /// <param name="document">Validated navigation document (routes, pools, spawn points).</param>
    /// <param name="graph">Indexed graph over the same document.</param>
    /// <param name="catalog">Profile catalog used to validate pool entries.</param>
    /// <param name="random">Seeded random stream owned by this session.</param>
    /// <param name="civilianTrafficEnabled">False (No Traffic) means no request is ever produced.</param>
    /// <param name="maxCivilians">Session-wide moving civilian cap.</param>
    public CivilianPopulationPlanner(MapNavigationDocument document, RoadGraphRuntime graph, ITrafficProfileCatalog catalog,
        System.Random random, bool civilianTrafficEnabled, int maxCivilians) {
        this.graph = graph;
        this.catalog = catalog;
        this.random = random ?? new System.Random(0);
        enabled = civilianTrafficEnabled && document != null && graph != null && maxCivilians > 0;
        if (document == null) return;

        int remaining = Mathf.Max(0, maxCivilians);
        if (document.civilianRoutes != null) foreach (var route in document.civilianRoutes) {
            if (route == null || string.IsNullOrEmpty(route.routeId) || routesById.ContainsKey(route.routeId)) continue;
            var state = new RouteState { route = route };
            if (document.vehiclePools != null) foreach (var pool in document.vehiclePools) {
                if (pool != null && pool.poolId == route.vehiclePoolId) { state.pool = pool; break; }
            }
            if (state.pool == null) state.issues.Add("route " + route.routeId + " references unknown pool " + route.vehiclePoolId);
            if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) {
                if (spawn != null && spawn.routeId == route.routeId && spawn.role == VehicleRole.Civilian) state.spawns.Add(spawn);
            }
            if (state.spawns.Count == 0) state.issues.Add("route " + route.routeId + " has no civilian spawn points");
            int wanted = Mathf.Max(0, route.targetCount);
            state.target = state.issues.Count == 0 ? Mathf.Min(wanted, remaining) : 0;
            remaining -= state.target;
            routes.Add(state);
            routesById[route.routeId] = state;
        }
    }

    /// <summary>True when civilian traffic may spawn at all this session.</summary>
    public bool IsEnabled => enabled;

    /// <summary>Number of routes known to the planner.</summary>
    public int RouteCount => routes.Count;

    /// <summary>Effective target for a route after the global cap, or 0 for unknown/invalid routes.</summary>
    public int TargetOf(string routeId) => routesById.TryGetValue(routeId ?? string.Empty, out var state) ? state.target : 0;

    /// <summary>Currently alive vehicles the manager reported on a route.</summary>
    public int AliveOn(string routeId) => routesById.TryGetValue(routeId ?? string.Empty, out var state) ? state.alive : 0;

    /// <summary>Spawns planned but not yet committed or failed on a route.</summary>
    public int PendingOn(string routeId) => routesById.TryGetValue(routeId ?? string.Empty, out var state) ? state.pending : 0;

    /// <summary>Sum of every route's effective target.</summary>
    public int TotalTarget {
        get {
            int total = 0;
            foreach (var state in routes) total += state.target;
            return total;
        }
    }

    /// <summary>Authoring problems found per route (unknown pool, no spawn points, unpickable pool entries), in encounter order.</summary>
    public void CollectIssues(List<string> into) {
        if (into == null) return;
        foreach (var state in routes) into.AddRange(state.issues);
    }

    /// <summary>
    /// Plans one spawn per missing vehicle on every route (initial population or a refill), spread
    /// over the farthest spawn points from the player. Each request bumps the route's pending
    /// count until the manager reports commit or failure.
    /// </summary>
    /// <param name="playerLocalPosition">Player position in map-local coordinates.</param>
    /// <param name="into">Receives the requests; not cleared first.</param>
    /// <returns>Number of requests added.</returns>
    public int PlanDeficits(Vector2 playerLocalPosition, List<SpawnRequest> into) {
        if (!enabled || into == null) return 0;
        int added = 0;
        foreach (var state in routes) {
            int deficit = state.Deficit;
            if (deficit == 0) continue;
            OrderSpawnsByDistance(state, playerLocalPosition);
            int spawnIndex = 0;
            for (int i = 0; i < deficit; i++) {
                if (!TryPlanOne(state, ref spawnIndex, out var request)) break;
                into.Add(request);
                state.pending++;
                added++;
            }
        }
        return added;
    }

    /// <summary>Plans a single replacement on one route (used when a wreck's respawn deficit becomes ready).</summary>
    public bool TryPlanReplacement(string routeId, Vector2 playerLocalPosition, out SpawnRequest request) {
        request = default;
        if (!enabled || !routesById.TryGetValue(routeId ?? string.Empty, out var state) || state.Deficit == 0) return false;
        OrderSpawnsByDistance(state, playerLocalPosition);
        int spawnIndex = 0;
        if (!TryPlanOne(state, ref spawnIndex, out request)) return false;
        state.pending++;
        return true;
    }

    /// <summary>The manager materialized a planned spawn: pending → alive.</summary>
    public void NoteSpawnCommitted(string routeId) {
        if (!routesById.TryGetValue(routeId ?? string.Empty, out var state)) return;
        state.pending = Mathf.Max(0, state.pending - 1);
        state.alive++;
    }

    /// <summary>A planned spawn could not be placed (no safe point, pool exhausted, budget refused): pending is given back, nothing else changes.</summary>
    public void NoteSpawnFailed(string routeId) {
        if (!routesById.TryGetValue(routeId ?? string.Empty, out var state)) return;
        state.pending = Mathf.Max(0, state.pending - 1);
    }

    /// <summary>An alive vehicle left its route for good (wreck, recycle, session end): alive − 1, so the route shows a deficit again.</summary>
    public void NoteVehicleLost(string routeId) {
        if (!routesById.TryGetValue(routeId ?? string.Empty, out var state)) return;
        state.alive = Mathf.Max(0, state.alive - 1);
    }

    /// <summary>Lowers a route's target at runtime (never raises it above data); pending requests beyond the new target are the manager's to cancel. Visible vehicles are never removed here.</summary>
    public void ReduceTarget(string routeId, int newTarget) {
        if (!routesById.TryGetValue(routeId ?? string.Empty, out var state)) return;
        state.target = Mathf.Clamp(newTarget, 0, state.target);
    }

    bool TryPlanOne(RouteState state, ref int spawnIndex, out SpawnRequest request) {
        request = default;
        if (state.pool == null || scratchSpawns.Count == 0) return false;
        if (!VehiclePoolResolver.TryPickWeighted(state.pool, catalog, (float)random.NextDouble(), out string profileId, out var issues)) {
            foreach (var issue in issues) if (!state.issues.Contains(issue)) state.issues.Add(issue);
            return false;
        }
        for (int attempts = 0; attempts < scratchSpawns.Count; attempts++) {
            var spawn = scratchSpawns[spawnIndex % scratchSpawns.Count];
            spawnIndex++;
            if (!SpawnPoseLocator.TryLocate(graph, spawn, out Vector2 position, out float heading)) continue;
            request = new SpawnRequest(state.route.routeId, spawn.spawnId, profileId, position, heading);
            return true;
        }
        return false;
    }

    void OrderSpawnsByDistance(RouteState state, Vector2 playerLocalPosition) {
        scratchSpawns.Clear();
        scratchSpawns.AddRange(state.spawns);
        // Farthest first; ties broken by spawn id so the order is stable for a given player position.
        scratchSpawns.Sort((a, b) => {
            float da = SpawnPoseLocator.TryLocate(graph, a, out Vector2 pa, out _) ? (pa - playerLocalPosition).sqrMagnitude : -1f;
            float db = SpawnPoseLocator.TryLocate(graph, b, out Vector2 pb, out _) ? (pb - playerLocalPosition).sqrMagnitude : -1f;
            int byDistance = db.CompareTo(da);
            return byDistance != 0 ? byDistance : string.CompareOrdinal(a.spawnId, b.spawnId);
        });
    }
}
