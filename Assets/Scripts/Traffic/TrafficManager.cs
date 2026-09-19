using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene composition root for civilian traffic: owns the per-session pool, population budget,
/// respawn scheduler, junction arbiter and route planner, materializes planned spawns into real
/// bodies (reserve → acquire → safe point → place while inactive → activate → bind damage →
/// follow), refills routes when wrecks are recycled and their deficits become ready, and performs
/// the single lifecycle transition for a stuck car that may be recycled. It never writes to a
/// moving body's transform; placement happens only on inactive bodies. Police are not handled
/// here. Everything is initialized explicitly by <see cref="Initialize"/> — no singleton, no
/// scene scan — and closed by <see cref="EndSession"/>.
/// </summary>
[DefaultExecutionOrder(-20)]
public sealed class TrafficManager : MonoBehaviour {
    /// <summary>Everything the manager needs, gathered by whoever composes the scene (S07 host wiring or a test).</summary>
    public sealed class Config {
        public MapNavigationDocument navigation;
        public ITrafficProfileCatalog catalog;
        public ICivilianVehicleBodyProvider bodies;
        public PopulationBudgetData budget;
        public RespawnTimingRules respawnRules;
        public VehicleIdentityRegistry identityRegistry;
        /// <summary>Optional damage world; null means NPCs take no damage this session.</summary>
        public TrafficDamageWorld damageWorld;
        /// <summary>Optional static-geometry clearance query for spawn footprints and rejoin paths.</summary>
        public IAreaClearanceQuery clearance;
        public Vector2 mapOriginWorld;
        public int seed;
        /// <summary>False when the frozen session rules disable civilian traffic (No Traffic).</summary>
        public bool civilianTrafficEnabled = true;
        /// <summary>Player world position provider; null counts as "far away".</summary>
        public Func<Vector2> playerWorldPosition;
        /// <summary>Player world velocity provider; null counts as stationary.</summary>
        public Func<Vector2> playerWorldVelocity;
        /// <summary>Camera world bounds provider; null means nothing is ever "in view" (tests) — pass a real one in game.</summary>
        public Func<Rect> cameraWorldBounds;
        public float cameraMargin = 4f;
        public float minApproachTimeSeconds = 2f;
        public float junctionHoldTimeoutSeconds = 10f;
    }

    sealed class CivilianRecord {
        public NpcVehicleInstance instance;
        public CivilianVehicleBody body;
        public NpcVehicleProfile profile;
        public string routeId;
        public string deficitId;
        public int lifeId;
    }

    [SerializeField] CivilianFollowerSettings followerSettings = new CivilianFollowerSettings();

    Config config;
    RoadGraphRuntime graph;
    CivilianPopulationPlanner planner;
    NpcVehiclePool pool;
    VehiclePopulationService population;
    TrafficRespawnScheduler respawns;
    JunctionReservationService junctions;
    readonly SessionClock clock = new SessionClock();
    readonly List<CivilianRecord> records = new List<CivilianRecord>();
    readonly Dictionary<int, CivilianRecord> recordsByLife = new Dictionary<int, CivilianRecord>();
    readonly List<CivilianPopulationPlanner.SpawnRequest> requestScratch = new List<CivilianPopulationPlanner.SpawnRequest>();
    readonly List<SpawnCandidate> candidateScratch = new List<SpawnCandidate>(1);
    readonly SpawnQueryContext queryContext = new SpawnQueryContext();
    readonly List<string> diagnostics = new List<string>();
    readonly List<string> pendingDeficits = new List<string>();
    readonly List<CivilianRecord> wrecks = new List<CivilianRecord>();
    readonly Dictionary<string, string> routeByDeficit = new Dictionary<string, string>();
    bool initialized;
    bool ended;
    float lastFleetRecycleAt = float.NegativeInfinity;

    /// <summary>True between Initialize and EndSession.</summary>
    public bool IsRunning => initialized && !ended;

    /// <summary>Active session seconds owned by this manager (pause/end never advance it).</summary>
    public float SessionSeconds => clock.ElapsedActiveSeconds;

    /// <summary>Alive civilians currently on the road (Active or CrashRecovery).</summary>
    public int AliveCivilianCount => records.Count;

    /// <summary>Planner (route targets and bookkeeping) for diagnostics and tests.</summary>
    public CivilianPopulationPlanner Planner => planner;

    /// <summary>Junction arbiter for diagnostics and external occupancy reports.</summary>
    public JunctionReservationService Junctions => junctions;

    /// <summary>Population budget accounting for diagnostics and tests.</summary>
    public VehiclePopulationService Population => population;

    /// <summary>Human-readable spawn/authoring diagnostics gathered so far (never cleared automatically).</summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    /// <summary>Follower tuning shared by every civilian this manager spawns.</summary>
    public CivilianFollowerSettings FollowerSettings => followerSettings;

    /// <summary>Builds every service and places the initial population. Safe to call once per session.</summary>
    /// <returns>False when the configuration is unusable; diagnostics say why.</returns>
    public bool Initialize(Config configuration) {
        if (initialized) return false;
        config = configuration;
        if (config == null || config.navigation == null || config.catalog == null || config.bodies == null || config.budget == null || config.identityRegistry == null) {
            diagnostics.Add("TrafficManager: missing navigation/catalog/bodies/budget/identity registry.");
            return false;
        }
        graph = new RoadGraphRuntime(config.navigation);
        pool = new NpcVehiclePool(config.budget, config.catalog, config.identityRegistry);
        population = new VehiclePopulationService(config.budget);
        respawns = new TrafficRespawnScheduler();
        var random = new System.Random(config.seed);
        planner = new CivilianPopulationPlanner(config.navigation, graph, config.catalog, random, config.civilianTrafficEnabled, config.budget.maxCivilianMoving);
        planner.CollectIssues(diagnostics);
        var junctionIds = new List<string>();
        if (config.navigation.junctions != null) foreach (var junction in config.navigation.junctions) if (junction != null) junctionIds.Add(junction.junctionId);
        junctions = new JunctionReservationService(graph, junctionIds, clock, config.junctionHoldTimeoutSeconds);
        if (config.damageWorld != null) config.damageWorld.VehicleDestroyed += HandleVehicleDestroyed;
        initialized = true;

        if (planner.IsEnabled) {
            requestScratch.Clear();
            planner.PlanDeficits(PlayerLocal(), requestScratch);
            foreach (var request in requestScratch) {
                if (!TryMaterialize(request, SpawnPlacementMode.InitialPlacement)) planner.NoteSpawnFailed(request.routeId);
            }
        }
        return true;
    }

    /// <summary>Pauses or resumes every session timer this manager owns.</summary>
    public void SetPaused(bool paused) {
        clock.SetPaused(paused);
    }

    /// <summary>Stops all civilians, releases junctions and returns every body; further ticks do nothing.</summary>
    public void EndSession() {
        if (!initialized || ended) return;
        ended = true;
        clock.End();
        if (config.damageWorld != null) config.damageWorld.VehicleDestroyed -= HandleVehicleDestroyed;
        for (int i = records.Count - 1; i >= 0; i--) Retire(records[i]);
        records.Clear();
        recordsByLife.Clear();
        population.ResetAll();
    }

    void FixedUpdate() {
        Tick(Time.fixedDeltaTime);
    }

    /// <summary>Advances timers, refills ready deficits and evaluates stuck cars. Called from FixedUpdate; tests may call it directly.</summary>
    public void Tick(float deltaTime) {
        if (!IsRunning) return;
        clock.Tick(deltaTime);
        if (clock.IsPaused) return;
        ProcessDeficits();
        EvaluateStuckCars();
    }

    /// <summary>Reports whether a life this manager spawned is still alive on a route, with its route id.</summary>
    public bool TryGetRouteOf(int lifeId, out string routeId) {
        routeId = null;
        if (!recordsByLife.TryGetValue(lifeId, out var record)) return false;
        routeId = record.routeId;
        return true;
    }

    void ProcessDeficits() {
        if (!planner.IsEnabled) return;
        Vector2 playerWorld = PlayerWorld();
        for (int i = pendingDeficits.Count - 1; i >= 0; i--) {
            string deficitId = pendingDeficits[i];
            if (!respawns.IsPending(deficitId)) { // someone else completed it
                pendingDeficits.RemoveAt(i);
                routeByDeficit.Remove(deficitId);
                continue;
            }
            if (!respawns.IsReadyToAttempt(deficitId, Now, playerWorld, config.respawnRules)) continue;
            string routeId = routeByDeficit[deficitId];
            if (!planner.TryPlanReplacement(routeId, PlayerLocal(), out var request)) continue;
            if (TryMaterialize(request, SpawnPlacementMode.ActiveReplacement)) {
                respawns.CompleteRespawn(deficitId);
                pendingDeficits.RemoveAt(i);
                routeByDeficit.Remove(deficitId);
            } else {
                planner.NoteSpawnFailed(routeId);
            }
        }
    }

    void EvaluateStuckCars() {
        Vector2 playerWorld = PlayerWorld();
        bool haveCamera = config.cameraWorldBounds != null;
        Rect view = haveCamera ? config.cameraWorldBounds() : default;
        for (int i = records.Count - 1; i >= 0; i--) {
            var record = records[i];
            var follower = record.body.Follower;
            if (!follower.IsStuck) continue;
            Vector2 position = record.body.Body.position;
            bool visible = haveCamera && Expand(view, config.cameraMargin).Contains(position);
            var decision = follower.EvaluateStuck(visible, Vector2.Distance(position, playerWorld), Now);
            if (decision != StuckMonitor.Decision.RequestRecycle) continue;
            lastFleetRecycleAt = Now;
            foreach (var other in records) other.body.Follower.NoteFleetRecycle(lastFleetRecycleAt);
            RecycleStuck(record);
        }
    }

    /// <summary>The one lifecycle transition for a stuck civilian: retire it, then register a respawn deficit at its position so the route refills elsewhere later.</summary>
    void RecycleStuck(CivilianRecord record) {
        Vector2 where = record.body.Body.position;
        string deficitId = "stuck-" + record.lifeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Retire(record);
        if (respawns.TryRegisterDeficit(deficitId, Now, where)) {
            pendingDeficits.Add(deficitId);
            routeByDeficit[deficitId] = record.routeId;
        }
    }

    void HandleVehicleDestroyed(VehicleDestroyedEvent death, float heat) {
        if (!recordsByLife.TryGetValue(death.victimLifeId, out var record)) return;
        // The receiver already moved the instance to Wreck, freed the moving slot and registered the
        // deficit under the life id; the body stays as a physical wreck until the receiver recycles it.
        record.body.Follower.StopFollowing();
        planner.NoteVehicleLost(record.routeId);
        records.Remove(record);
        recordsByLife.Remove(record.lifeId);
        if (respawns.IsPending(record.deficitId)) {
            pendingDeficits.Add(record.deficitId);
            routeByDeficit[record.deficitId] = record.routeId;
        }
        wrecks.Add(record); // the body stays as a physical wreck until the receiver releases it to the pool
    }

    /// <summary>Returns wreck bodies to the provider once the receiver has recycled their pool slot (or the session ended).</summary>
    void LateUpdate() {
        for (int i = wrecks.Count - 1; i >= 0; i--) {
            var record = wrecks[i];
            if (record.instance.State != VehicleLifeState.Pooled && !ended) continue;
            wrecks.RemoveAt(i);
            record.body.Follower.ResetForNewLife();
            config.bodies.Release(record.body);
        }
    }

    bool TryMaterialize(CivilianPopulationPlanner.SpawnRequest request, SpawnPlacementMode mode) {
        var profile = config.catalog.ResolveVehicleProfile(request.vehicleProfileId);
        if (profile == null) return Fail("unknown profile " + request.vehicleProfileId);
        if (!population.TryReserveSpawn(VehicleRole.Civilian)) return Fail("population budget refused a civilian");
        if (!pool.TryAcquire(request.vehicleProfileId, VehicleRole.Civilian, out var instance)) {
            population.CancelReservation(VehicleRole.Civilian);
            return Fail("pool exhausted or profile disallowed for " + request.vehicleProfileId);
        }

        Vector2 world = MapNavigationCoordinates.LocalToWorld(request.position, config.mapOriginWorld);
        var candidate = candidateScratch.Count > 0 ? candidateScratch[0] : null;
        if (candidate == null) { candidate = new SpawnCandidate(); candidateScratch.Add(candidate); }
        candidate.position = world;
        candidate.headingDegrees = request.headingDegrees;
        candidate.graphNodeId = graph.GetEdge(GetSpawnEdgeId(request))?.fromNodeId;
        candidate.footprint = profile.colliderSize;
        FillQueryContext(mode);
        if (!VehicleSpawnPolicy.IsSafe(candidate, queryContext, out string reason)) {
            population.CancelReservation(VehicleRole.Civilian);
            pool.Release(instance);
            return Fail("spawn " + request.spawnId + " unsafe: " + reason);
        }

        var body = config.bodies.Acquire(profile);
        if (body == null) {
            population.CancelReservation(VehicleRole.Civilian);
            pool.Release(instance);
            return Fail("no body for visual " + profile.visualCatalogId);
        }
        if (!body.PlaceForSpawn(world, request.headingDegrees)) {
            population.CancelReservation(VehicleRole.Civilian);
            pool.Release(instance);
            config.bodies.Release(body);
            return Fail("body was active during placement");
        }

        population.CommitSpawn(VehicleRole.Civilian);
        instance.TryActivate();
        body.gameObject.SetActive(true);
        body.Motor.ResetForNewLife();
        body.Motor.Configure(profile.motorSettings);
        body.Motor.SetMass(profile.baseMass);
        if (body.Sensor != null) body.Sensor.Configure(profile.motorSettings);
        if (config.damageWorld != null && body.Receiver != null) {
            if (!body.Receiver.BindNpc(config.damageWorld, instance, profile, pool, population, respawns)) {
                body.gameObject.SetActive(false);
                if (instance.TryMarkWrecked()) { population.MarkActiveVehicleWrecked(VehicleRole.Civilian); population.ReleaseWreck(); }
                pool.Release(instance);
                config.bodies.Release(body);
                return Fail("damage receiver refused to bind life " + instance.Identity?.lifeId);
            }
        }

        var route = FindRoute(request.routeId);
        var follower = body.Follower;
        follower.Configure(followerSettings);
        follower.SetRejoinClearance(config.clearance, config.navigation.localBounds);
        follower.SetJunctionArbiter(junctions, instance.Identity.Value.lifeId);
        if (!follower.TryBeginRoute(graph, route, profile.motorSettings, profile.colliderSize, config.mapOriginWorld, out string issue)) {
            if (body.Receiver != null) body.Receiver.Unbind();
            body.gameObject.SetActive(false);
            if (instance.TryMarkWrecked()) { population.MarkActiveVehicleWrecked(VehicleRole.Civilian); population.ReleaseWreck(); }
            pool.Release(instance);
            config.bodies.Release(body);
            return Fail("route " + request.routeId + " refused for " + profile.vehicleProfileId + ": " + issue);
        }

        var record = new CivilianRecord {
            instance = instance, body = body, profile = profile, routeId = request.routeId,
            lifeId = instance.Identity.Value.lifeId,
            deficitId = instance.Identity.Value.lifeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        records.Add(record);
        recordsByLife[record.lifeId] = record;
        planner.NoteSpawnCommitted(request.routeId);
        return true;
    }

    /// <summary>Takes an alive civilian off the road in one transition (stuck recycle or session end).</summary>
    void Retire(CivilianRecord record) {
        record.body.Follower.StopFollowing();
        record.body.Follower.ResetForNewLife();
        junctions.ReleaseAll(record.lifeId);
        if (record.body.Receiver != null) record.body.Receiver.Unbind();
        record.body.Motor.StopMovement();
        record.body.gameObject.SetActive(false);
        if (record.instance.TryMarkWrecked()) {
            population.MarkActiveVehicleWrecked(VehicleRole.Civilian);
            population.ReleaseWreck();
        }
        pool.Release(record.instance);
        config.bodies.Release(record.body);
        planner.NoteVehicleLost(record.routeId);
        records.Remove(record);
        recordsByLife.Remove(record.lifeId);
    }

    void FillQueryContext(SpawnPlacementMode mode) {
        queryContext.mode = mode;
        queryContext.graph = graph;
        queryContext.noSpawnRegions = config.navigation.noSpawnRegions;
        queryContext.clearanceQuery = config.clearance;
        // Initial placement happens before player control and may be in view; replacements never are.
        bool useCamera = mode == SpawnPlacementMode.ActiveReplacement && config.cameraWorldBounds != null;
        queryContext.cameraBounds = useCamera ? config.cameraWorldBounds() : NeverVisible;
        queryContext.cameraMargin = useCamera ? config.cameraMargin : 0f;
        queryContext.playerPosition = PlayerWorld();
        queryContext.playerVelocity = config.playerWorldVelocity != null ? config.playerWorldVelocity() : Vector2.zero;
        queryContext.minApproachTimeSeconds = config.minApproachTimeSeconds;
    }

    string GetSpawnEdgeId(CivilianPopulationPlanner.SpawnRequest request) {
        if (config.navigation.spawnPoints != null) foreach (var spawn in config.navigation.spawnPoints) {
            if (spawn != null && spawn.spawnId == request.spawnId) return spawn.edgeId;
        }
        return null;
    }

    CivilianRouteRecord FindRoute(string routeId) {
        if (config.navigation.civilianRoutes != null) foreach (var route in config.navigation.civilianRoutes) {
            if (route != null && route.routeId == routeId) return route;
        }
        return null;
    }

    bool Fail(string message) {
        diagnostics.Add(message);
        return false;
    }

    float Now => config.damageWorld != null ? config.damageWorld.SessionTime : clock.ElapsedActiveSeconds;

    static readonly Rect NeverVisible = new Rect(1e9f, 1e9f, 0f, 0f);
    static readonly Vector2 FarAway = new Vector2(1e6f, 1e6f);

    Vector2 PlayerWorld() => config.playerWorldPosition != null ? config.playerWorldPosition() : FarAway;

    Vector2 PlayerLocal() => MapNavigationCoordinates.WorldToLocal(PlayerWorld(), config.mapOriginWorld);

    static Rect Expand(Rect rect, float margin) => Rect.MinMaxRect(rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);
}
