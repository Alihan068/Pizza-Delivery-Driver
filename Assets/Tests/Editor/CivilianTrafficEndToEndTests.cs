using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// S06.8 gate: the real <see cref="TrafficManager"/> composed with the S03 pool/budget/scheduler,
/// the S05 damage world, real motors/followers/sensors and an isolated PhysicsScene2D. Covers
/// initial route population, driving, a player-caused wreck, the respawn of a fresh random vehicle,
/// pause and session end — with slot/collider bookkeeping checked at every stage.
/// </summary>
public sealed class CivilianTrafficEndToEndTests {
    const float DeltaTime = 0.02f;
    static readonly MethodInfo bodyAwake = typeof(CivilianVehicleBody).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo motorAwake = typeof(NpcVehicleMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo motorStep = typeof(NpcVehicleMotor).GetMethod("SimulateStep", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerAwake = typeof(CivilianRouteFollower).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerTick = typeof(CivilianRouteFollower).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sensorAwake = typeof(VehicleObstacleSensor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo managerLateUpdate = typeof(TrafficManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    Scene scene;
    PhysicsScene2D physics;
    readonly List<Object> assets = new List<Object>();
    TrafficSessionCoordinator coordinator;
    TrafficDamageWorld world;
    TrafficManager manager;
    FakeBodyProvider provider;
    NpcVehicleProfile profile;

    /// <summary>Creates bodies in the preview scene and invokes the Awake methods EditMode never calls.</summary>
    sealed class FakeBodyProvider : ICivilianVehicleBodyProvider {
        readonly Scene scene;
        public readonly List<CivilianVehicleBody> created = new List<CivilianVehicleBody>();
        public readonly Stack<CivilianVehicleBody> free = new Stack<CivilianVehicleBody>();
        public int Released { get; private set; }

        public FakeBodyProvider(Scene scene) { this.scene = scene; }

        public CivilianVehicleBody Acquire(NpcVehicleProfile profile) {
            if (free.Count > 0) return free.Pop();
            var go = new GameObject("Civilian" + created.Count);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.SetActive(false);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f; rb.linearDamping = 0f; rb.angularDamping = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<BoxCollider2D>().size = new Vector2(1.5f, 3f);
            var motor = go.AddComponent<NpcVehicleMotor>(); motorAwake.Invoke(motor, null);
            var follower = go.AddComponent<CivilianRouteFollower>(); followerAwake.Invoke(follower, null);
            var sensor = go.AddComponent<VehicleObstacleSensor>(); sensorAwake.Invoke(sensor, null);
            go.AddComponent<VehicleDamageReceiver>();
            var body = go.AddComponent<CivilianVehicleBody>(); bodyAwake.Invoke(body, null);
            body.VisualCatalogId = profile.visualCatalogId;
            created.Add(body);
            return body;
        }

        public void Release(CivilianVehicleBody body) {
            body.gameObject.SetActive(false);
            free.Push(body);
            Released++;
        }
    }

    /// <summary>Stands in for the static-geometry query: a spot is clear unless an active body already sits within a few units of it (a real map adds buildings).</summary>
    sealed class BodyClearance : IAreaClearanceQuery {
        readonly FakeBodyProvider provider;
        public BodyClearance(FakeBodyProvider provider) { this.provider = provider; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) {
            foreach (var body in provider.created) {
                if (body.gameObject.activeSelf && Vector2.Distance(body.Body.position, center) < footprint.y + 1f) return false;
            }
            return true;
        }
    }

    [SetUp]
    public void SetUp() {
        scene = EditorSceneManager.NewPreviewScene();
        physics = scene.GetPhysicsScene2D();
        Assert.IsTrue(physics.IsValid() && physics != Physics2D.defaultPhysicsScene, "isolated physics scene required");
        var draft = new SessionSetupDraft("e2e", "map", "car", "difficulty", 5, false, Array.Empty<string>());
        coordinator = new TrafficSessionCoordinator(new TrafficSessionContext(draft));
        coordinator.NotifyPlayerReady(); coordinator.NotifyMapReady();

        var damage = ScriptableObject.CreateInstance<TrafficDamageSettings>(); assets.Add(damage);
        damage.profiles = new[] { new TrafficDamageProfile { profileId = "test", blastDamage = 10f, blastRadius = 0.5f } };
        damage.wreckLifetimeSeconds = 1f; damage.recoverySeconds = 0.5f; damage.initialQueryCapacity = 4;
        var worldGo = new GameObject("World"); SceneManager.MoveGameObjectToScene(worldGo, scene);
        world = worldGo.AddComponent<TrafficDamageWorld>(); world.Configure(coordinator, damage);

        profile = ScriptableObject.CreateInstance<NpcVehicleProfile>(); assets.Add(profile);
        profile.vehicleProfileId = "sedan"; profile.visualCatalogId = "sedan"; profile.allowedRoles.Add(VehicleRole.Civilian);
        profile.damageProfileId = "test"; profile.explosionProfileId = "test"; profile.maxHealth = 40f;
        profile.colliderSize = new Vector2(1.5f, 3f);
        profile.motorSettings = new NpcMotorSettings { cruiseSpeed = 6f, acceleration = 4f, brakeDeceleration = 8f, turnRate = 120f, minimumTurningRadius = 3f, minimumGap = 2f, sensorInterval = 0.2f, lateralGrip = 0.9f };

        var managerGo = new GameObject("TrafficManager"); SceneManager.MoveGameObjectToScene(managerGo, scene);
        manager = managerGo.AddComponent<TrafficManager>();
        provider = new FakeBodyProvider(scene);
    }

    [TearDown]
    public void TearDown() {
        if (manager != null) manager.EndSession();
        if (world != null) world.EndSession();
        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        assets.Clear();
    }

    TrafficManager.Config BuildConfig(MapNavigationDocument document, int seed, bool civiliansEnabled) {
        var budget = ScriptableObject.CreateInstance<PopulationBudgetData>(); assets.Add(budget);
        budget.maxCivilianMoving = 4; budget.maxTotalMoving = 4; budget.maxWreckSlots = 4; budget.maxPoliceMoving = 0;
        var rules = ScriptableObject.CreateInstance<RespawnTimingRules>(); assets.Add(rules);
        rules.minimumDelaySeconds = 1f; rules.minimumPlayerDistance = 0f; rules.replacementDeadlineSeconds = 5f;
        return new TrafficManager.Config {
            navigation = document,
            catalog = new BuiltInTrafficProfileCatalog(new[] { profile }),
            bodies = provider,
            budget = budget,
            respawnRules = rules,
            identityRegistry = coordinator.IdentityRegistry,
            damageWorld = world,
            clearance = new BodyClearance(provider),
            mapOriginWorld = Vector2.zero,
            seed = seed,
            civilianTrafficEnabled = civiliansEnabled,
            playerWorldPosition = () => new Vector2(200f, 200f),
            cameraWorldBounds = null
        };
    }

    static MapNavigationDocument LoopDocument(int target) {
        var document = new MapNavigationDocument { localBounds = new Rect(-50f, -50f, 200f, 200f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 30f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 30f, y = 30f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "d", x = 30f, y = 0f });
        document.edges.Add(Edge("e0", "a", "b")); document.edges.Add(Edge("e1", "b", "c")); document.edges.Add(Edge("e2", "c", "d")); document.edges.Add(Edge("e3", "d", "a"));
        document.vehiclePools.Add(new VehiclePoolRecord { poolId = "sedans", entries = new List<VehiclePoolEntry> { new VehiclePoolEntry { vehicleProfileId = "sedan", weight = 1f } } });
        document.civilianRoutes.Add(new CivilianRouteRecord { routeId = "loop", loop = true, vehiclePoolId = "sedans", targetCount = target, edgeIds = new List<string> { "e0", "e1", "e2", "e3" } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s0", edgeId = "e0", distanceAlongEdge = 5f, routeId = "loop", role = VehicleRole.Civilian, clearanceWidth = 1.5f, clearanceLength = 3f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s2", edgeId = "e2", distanceAlongEdge = 5f, routeId = "loop", role = VehicleRole.Civilian, clearanceWidth = 1.5f, clearanceLength = 3f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s1", edgeId = "e1", distanceAlongEdge = 15f, routeId = "loop", role = VehicleRole.Civilian, clearanceWidth = 1.5f, clearanceLength = 3f });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "s3", edgeId = "e3", distanceAlongEdge = 15f, routeId = "loop", role = VehicleRole.Civilian, clearanceWidth = 1.5f, clearanceLength = 3f });
        return document;
    }

    static RoadEdgeRecord Edge(string id, string from, string to) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 6f, speedLimit = 10f };

    /// <summary>One full frame in the order the scene would run it: pre-step samples, follower commands, motor forces, physics, damage world, manager, wreck return.</summary>
    void Step() {
        world.BeginPhysicsStep();
        foreach (var body in provider.created) {
            if (!body.gameObject.activeSelf) continue;
            followerTick.Invoke(body.Follower, new object[] { DeltaTime });
            motorStep.Invoke(body.Motor, new object[] { DeltaTime });
        }
        Assert.IsTrue(physics.Simulate(DeltaTime));
        world.Tick(DeltaTime);
        manager.Tick(DeltaTime);
        managerLateUpdate.Invoke(manager, null);
    }

    int ActiveBodies() {
        int count = 0;
        foreach (var body in provider.created) if (body.gameObject.activeSelf) count++;
        return count;
    }

    /// <summary>Loop → drive → player-caused wreck → wreck recycled → fresh random vehicle refills the route → pause freezes timers → end returns everything, slots consistent throughout.</summary>
    [Test]
    public void Civilians_LoopCrashRespawnPauseAndEndKeepSlotsAndCollidersConsistent() {
        Assert.IsTrue(manager.Initialize(BuildConfig(LoopDocument(2), seed: 7, civiliansEnabled: true)), string.Join("; ", manager.Diagnostics));
        Assert.AreEqual(2, manager.AliveCivilianCount, string.Join("; ", manager.Diagnostics));
        Assert.AreEqual(2, manager.Population.ActiveCivilianCount);
        Assert.AreEqual(2, manager.Planner.AliveOn("loop"));
        Assert.AreEqual(2, ActiveBodies());
        Assert.AreEqual(2, manager.Population.ReservedFutureWreckCount, "every accepted spawn reserves its future wreck token");

        for (int i = 0; i < 300; i++) Step();
        foreach (var body in provider.created) {
            Assert.IsTrue(body.Follower.IsFollowing);
            Assert.Greater(body.Body.linearVelocity.magnitude, 1f, body.name + " must be driving");
            Assert.AreEqual(CrashRecoveryPolicy.Phase.Driving, body.Follower.RecoveryPhase);
        }
        Assert.AreEqual(0, manager.Diagnostics.Count, "no spawn diagnostics on a clean map: " + string.Join("; ", manager.Diagnostics));

        // Player-caused lethal impact on the first car.
        var victim = provider.created[0];
        int victimLife = victim.Receiver.Identity.lifeId;
        var deaths = new List<VehicleDestroyedEvent>();
        world.VehicleDestroyed += (death, heat) => deaths.Add(death);
        var player = coordinator.PlayerIdentity.Value;
        victim.Receiver.ApplyCollision(20f, new DamageContext("impact", player.lifeId, InstigatorKind.Player, player.lifeId, DamageKind.Collision, "root"));
        Assert.IsTrue(victim.Receiver.IsWreck);
        Assert.AreEqual(1, deaths.Count);
        Assert.AreEqual(victimLife, deaths[0].victimLifeId);
        Assert.AreEqual(1, manager.AliveCivilianCount, "wreck is no longer an alive civilian");
        Assert.AreEqual(1, manager.Planner.AliveOn("loop"));
        Assert.AreEqual(1, manager.Population.ActiveCivilianCount);
        Assert.AreEqual(1, manager.Population.OccupiedWreckCount);
        Assert.IsTrue(victim.gameObject.activeSelf, "the wreck stays as a physical body for its lifetime");
        Assert.IsFalse(victim.Follower.IsFollowing);
        Assert.IsTrue(victim.Motor.IsStoppedPermanently);

        // Wreck lifetime (1 s) and respawn delay (1 s) pass: a fresh vehicle refills the route.
        for (int i = 0; i < 200; i++) Step();
        Assert.AreEqual(0, manager.Population.OccupiedWreckCount, "wreck recycled after its lifetime");
        Assert.AreEqual(2, manager.AliveCivilianCount, "route refilled: " + string.Join("; ", manager.Diagnostics));
        Assert.AreEqual(2, manager.Population.ActiveCivilianCount);
        Assert.AreEqual(2, ActiveBodies(), "exactly two colliders alive: no leaked wreck, no double spawn");
        Assert.GreaterOrEqual(provider.Released, 1, "the wreck body went back to the provider");
        bool freshLife = false;
        foreach (var body in provider.created) if (body.gameObject.activeSelf && body.Receiver.Identity.lifeId > victimLife) freshLife = true;
        Assert.IsTrue(freshLife, "the replacement carries a new life id");

        // Pause: manager clock frozen while the world keeps its own accounting untouched.
        manager.SetPaused(true);
        float frozen = manager.SessionSeconds;
        manager.Tick(DeltaTime); manager.Tick(DeltaTime);
        Assert.AreEqual(frozen, manager.SessionSeconds);
        manager.SetPaused(false);
        manager.Tick(DeltaTime);
        Assert.Greater(manager.SessionSeconds, frozen);

        // End: everything returns, nothing keeps driving.
        manager.EndSession();
        Assert.IsFalse(manager.IsRunning);
        Assert.AreEqual(0, manager.AliveCivilianCount);
        Assert.AreEqual(0, manager.Population.ActiveCivilianCount);
        Assert.AreEqual(0, ActiveBodies());
        foreach (var body in provider.created) Assert.IsFalse(body.Follower.IsFollowing);
        Assert.AreEqual(0, manager.Junctions.HolderCount("none"));
    }

    /// <summary>With civilian traffic disabled by the session rules nothing is ever requested or spawned, while the manager still runs cleanly.</summary>
    [Test]
    public void Civilians_NoTrafficRulesSpawnNothing() {
        Assert.IsTrue(manager.Initialize(BuildConfig(LoopDocument(3), seed: 1, civiliansEnabled: false)));
        Assert.IsFalse(manager.Planner.IsEnabled);
        Assert.AreEqual(0, manager.AliveCivilianCount);
        for (int i = 0; i < 100; i++) Step();
        Assert.AreEqual(0, manager.AliveCivilianCount);
        Assert.AreEqual(0, provider.created.Count);
        Assert.AreEqual(0, manager.Population.ActiveCivilianCount + manager.Population.PendingCivilianCount);
    }

    /// <summary>Two managers with the same seed place the same profiles at the same spawn points; the population never exceeds the budget even when the route asks for more.</summary>
    [Test]
    public void Civilians_SeedIsDeterministicAndBudgetCapsRouteTarget() {
        Assert.IsTrue(manager.Initialize(BuildConfig(LoopDocument(9), seed: 3, civiliansEnabled: true)));
        Assert.AreEqual(4, manager.Planner.TargetOf("loop"), "route target trimmed to the civilian budget");
        Assert.LessOrEqual(manager.AliveCivilianCount, 4);
        Assert.AreEqual(manager.AliveCivilianCount, manager.Population.ActiveCivilianCount);
        var positions = new List<Vector2>();
        foreach (var body in provider.created) if (body.gameObject.activeSelf) positions.Add(body.Body.position);

        var secondGo = new GameObject("TrafficManager2"); SceneManager.MoveGameObjectToScene(secondGo, scene);
        var second = secondGo.AddComponent<TrafficManager>();
        var secondProvider = new FakeBodyProvider(scene);
        var config = BuildConfig(LoopDocument(9), seed: 3, civiliansEnabled: true);
        config.bodies = secondProvider;
        config.clearance = new BodyClearance(secondProvider);
        config.damageWorld = null; // second fleet is a placement comparison only
        Assert.IsTrue(second.Initialize(config));
        var secondPositions = new List<Vector2>();
        foreach (var body in secondProvider.created) if (body.gameObject.activeSelf) secondPositions.Add(body.Body.position);
        CollectionAssert.AreEqual(positions, secondPositions, "same seed and data → identical initial placement");
        second.EndSession();
    }
}
