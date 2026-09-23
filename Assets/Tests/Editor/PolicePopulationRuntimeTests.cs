using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Bounded EditMode integration coverage for police materialization and ownership cleanup.</summary>
public sealed class PolicePopulationRuntimeTests {
    /// <summary>Free relocation completes graphlessly without replacing the police life or health.</summary>
    [Test]
    public void FreeRelocationPreservesLifeAndReturnsInsideManagementRadius() {
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Assert.IsTrue(harness.TryRequest(out var request));
            harness.Runtime.TryMaterialize(request);
            for (int step = 0; step < 200 && harness.Runtime.ActiveCount == 0; step++) harness.Runtime.PreparePhysics(0f);
            Assert.AreEqual(1, harness.Runtime.ActiveCount, Join(harness.Runtime.Diagnostics));
            var body = harness.Provider.LastBody;
            int life = body.DamageReceiver.Identity.lifeId;
            float health = body.DamageReceiver.CurrentHealth;
            body.Body.position = new Vector2(0f, -20f);
            body.Body.rotation = 0f;
            body.Body.linearVelocity = Vector2.zero;
            var player = harness.Fixture.Player.GetComponent<Rigidbody2D>();
            player.linearVelocity = Vector2.up;
            Physics2D.SyncTransforms();
            harness.Fixture.World.Tick(1f);
            harness.Runtime.Tick(harness.Fixture.World.SessionTime);
            harness.Fixture.World.Tick(4f);
            harness.Runtime.Tick(harness.Fixture.World.SessionTime);
            for (int step = 0; step < 300 && body.Body.position.y < -19f; step++) harness.Runtime.PreparePhysics(5f);
            Assert.Greater(body.Body.position.y, -19f, Join(harness.Runtime.Diagnostics));
            Assert.Less(Vector2.Distance(body.Body.position, player.position), 3f * Mathf.Sqrt(18f));
            Assert.AreEqual(life, body.DamageReceiver.Identity.lifeId);
            Assert.AreEqual(health, body.DamageReceiver.CurrentHealth);
            Assert.AreEqual(1, harness.Services.Population.ActivePoliceCount);
            Assert.IsTrue(body.GetComponent<PolicePursuitController>().IsBound);
        }
    }

    /// <summary>Ending during a deferred spawn releases its director request without a population life.</summary>
    [Test]
    public void FreePendingSpawnEndsWithoutReservationsOrLives() {
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsFalse(harness.Runtime.TryMaterialize(request));
            Assert.IsTrue(harness.Director.IsInFlight(request));
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
            harness.Runtime.End();
            Assert.IsFalse(harness.Director.IsInFlight(request));
            Assert.AreEqual(0, harness.Runtime.ActiveCount);
            Assert.AreEqual(0, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(1, harness.Provider.ReleaseCount);
        }
    }

    /// <summary>The graphless spawn faces the player using Unity's transform.up/CCW convention.</summary>
    [Test]
    public void FreeSpawnFacesPlayerAndOwnsOnlyOnePopulationLife() {
        using (var harness = new PopulationHarness(freeDrive: true)) {
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsFalse(harness.Runtime.TryMaterialize(request), "Free placement must defer until reachability completes.");
            Assert.AreEqual(0, harness.Services.Population.ActivePoliceCount);
            for (int step = 0; step < 200 && harness.Runtime.ActiveCount == 0; step++)
                harness.Runtime.PreparePhysics(0f);
            Assert.AreEqual(1, harness.Runtime.ActiveCount, Join(harness.Runtime.Diagnostics));
            var body = harness.Provider.LastBody;
            Physics2D.SyncTransforms();
            Vector2 playerPosition = harness.Fixture.Player.GetComponent<Rigidbody2D>().position;
            Vector2 towardPlayer = playerPosition - body.Body.position;
            Assert.Greater(Vector2.Dot(body.transform.up, towardPlayer.normalized), 0.999f,
                "position=" + body.Body.position + " forward=" + body.transform.up + " target=" + playerPosition);
            Assert.IsTrue(body.GetComponent<PolicePursuitController>().enabled);
            Assert.IsTrue(body.GetComponent<PolicePursuitController>().UsesFreeDrive);
            Assert.AreEqual(1, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
        }
    }

    [Test]
    public void PopulationRuntime_RejectsRegressingActiveClock() {
        using (var runtime = new PolicePopulationRuntime(null)) {
            runtime.Tick(3f);
            runtime.Tick(2.999f);
            Assert.AreEqual(3f, runtime.LastActiveClock);
            runtime.Tick(4f);
            Assert.AreEqual(4f, runtime.LastActiveClock);
        }
    }

    [Test]
    public void Materialize_UsesRealBodyAndCommitsOneIdentity() {
        using (var harness = new PopulationHarness()) {
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsTrue(harness.Runtime.TryMaterialize(request), Join(harness.Runtime.Diagnostics));
            Assert.AreEqual(1, harness.Runtime.ActiveCount);
            Assert.AreEqual(1, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
            Assert.IsTrue(harness.Runtime.TryGetBody(harness.Provider.LastLifeId, out var body));
            Assert.IsNotNull(body);
            Assert.IsTrue(body.DamageReceiver.IsBoundTo(harness.Fixture.World,
                body.DamageReceiver.Identity, harness.Fixture.Profile));
            Assert.IsTrue(body.GetComponent<PolicePursuitController>().IsBound);
            Assert.AreEqual(1, harness.Director.ActiveCount);
        }
    }

    [Test]
    public void ProviderRejectsWithoutLeakingPoolOrPendingPopulation() {
        using (var harness = new PopulationHarness()) {
            harness.Provider.RejectAcquire = true;
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsFalse(harness.Runtime.TryMaterialize(request));
            Assert.AreEqual(0, harness.Runtime.ActiveCount);
            Assert.AreEqual(harness.Services.Pool.Capacity, harness.Services.Pool.PooledCount);
            Assert.AreEqual(0, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
            Assert.AreEqual(0, harness.Services.Population.ReservedFutureWreckCount);
            Assert.AreEqual(0, harness.Director.PendingCount);
        }
    }

    [Test]
    public void PhysicsBlockedCandidate_LeavesAllOwnedCountersClean() {
        using (var harness = new PopulationHarness()) {
            harness.Fixture.CreateStaticBlocker(new Vector2(0f, -15f));
            harness.Fixture.CreateStaticBlocker(new Vector2(0f, -5f));
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsFalse(harness.Runtime.TryMaterialize(request));
            Assert.AreEqual(0, harness.Runtime.ActiveCount);
            Assert.AreEqual(harness.Services.Pool.Capacity, harness.Services.Pool.PooledCount);
            Assert.AreEqual(0, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
            Assert.AreEqual(0, harness.Services.Population.ReservedFutureWreckCount);
            Assert.AreEqual(0, harness.Director.PendingCount);
        }
    }

    [Test]
    public void DeathMovesOneLifeToWreckAndSecondDeathIsIgnored() {
        using (var harness = new PopulationHarness(reuseSingleSlot: true)) {
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsTrue(harness.Runtime.TryMaterialize(request));
            int lifeId = harness.Provider.LastLifeId;
            var body = harness.Provider.LastBody;
            var player = harness.Fixture.Coordinator.PlayerIdentity.Value;
            var context = new DamageContext("population-test", player.lifeId, InstigatorKind.Player, player.lifeId,
                DamageKind.Explosion, "population-test");
            Assert.IsTrue(body.DamageReceiver.ApplyBlast(new BlastApplication("population-test", lifeId,
                VehicleRole.Police, 10000f, context)));
            Assert.AreEqual(0, harness.Runtime.ActiveCount);
            Assert.AreEqual(1, harness.Runtime.WreckCount);
            Assert.AreEqual(1, harness.Services.Population.OccupiedWreckCount);
            Assert.IsFalse(body.DamageReceiver.ApplyBlast(new BlastApplication("population-test-2", lifeId,
                VehicleRole.Police, 10000f, context)));
            Assert.AreEqual(1, harness.Runtime.WreckCount);
            Assert.AreEqual(1, harness.Services.Population.OccupiedWreckCount);
            harness.Fixture.World.Tick(harness.Fixture.World.Settings.wreckLifetimeSeconds + 0.1f);
            Assert.IsTrue(harness.Services.Population.TryReserveSpawn(VehicleRole.Civilian));
            Assert.IsTrue(harness.Services.Pool.TryAcquire(harness.CivilianProfile.vehicleProfileId,
                VehicleRole.Civilian, out var replacement));
            Assert.IsTrue(replacement.TryActivate());
            Assert.IsTrue(harness.Services.Population.CommitSpawn(VehicleRole.Civilian));
            Assert.AreNotEqual(lifeId, replacement.Identity.Value.lifeId);
            Assert.AreSame(harness.OriginalInstance, replacement);
            harness.Runtime.Tick(harness.Fixture.World.SessionTime + 1f);
            Assert.AreEqual(0, harness.Runtime.WreckCount);
            Assert.AreEqual(0, harness.Services.Population.OccupiedWreckCount);
            Assert.AreEqual(1, harness.Provider.ReleaseCount);
            Assert.AreEqual(VehicleLifeState.Active, replacement.State);
            harness.Runtime.End();
            Assert.AreEqual(1, harness.Services.Population.ActiveCivilianCount);
            Assert.AreEqual(VehicleLifeState.Active, replacement.State);
            Assert.IsTrue(replacement.TryMarkWrecked());
            Assert.IsTrue(harness.Services.Population.MarkActiveVehicleWrecked(VehicleRole.Civilian));
            Assert.IsTrue(harness.Services.Pool.Release(replacement));
            Assert.IsTrue(harness.Services.Population.ReleaseWreck());
        }
    }

    [Test]
    public void EndResetsPoliceOwnershipButDoesNotResetCivilianCounters() {
        using (var harness = new PopulationHarness()) {
            Assert.IsTrue(harness.Services.Population.TryReserveSpawn(VehicleRole.Civilian));
            Assert.IsTrue(harness.Services.Population.CommitSpawn(VehicleRole.Civilian));
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsTrue(harness.Runtime.TryMaterialize(request));
            harness.Runtime.End();
            Assert.AreEqual(0, harness.Services.Population.ActivePoliceCount);
            Assert.AreEqual(0, harness.Services.Population.PendingPoliceCount);
            Assert.AreEqual(1, harness.Services.Population.ActiveCivilianCount);
            Assert.IsFalse(harness.Services.IsClosed);
        }
    }

    [Test]
    public void RelocationKeepsLifeAndHealthAndVisiblePoliceIsNotMoved() {
        using (var harness = new PopulationHarness()) {
            Assert.IsTrue(harness.TryRequest(out var request));
            Assert.IsTrue(harness.Runtime.TryMaterialize(request));
            var body = harness.Provider.LastBody;
            int lifeId = harness.Provider.LastLifeId;
            float health = body.DamageReceiver.CurrentHealth;
            Vector2 beforeVisibleTick = body.Body.position;
            harness.Fixture.Player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.up;
            var player = harness.Fixture.Coordinator.PlayerIdentity.Value;
            var context = new DamageContext("relocation-test", player.lifeId, InstigatorKind.Player, player.lifeId,
                DamageKind.Explosion, "relocation-test");
            Assert.IsTrue(body.DamageReceiver.ApplyBlast(new BlastApplication("relocation-test", lifeId,
                VehicleRole.Police, 10f, context)));
            float postDamageHealth = body.DamageReceiver.CurrentHealth;
            Assert.Less(postDamageHealth, health);
            harness.Fixture.World.Tick(harness.Fixture.World.Settings.recoverySeconds + 0.1f);
            Assert.IsTrue(body.DamageReceiver.CanTakeDamage);
            harness.CameraBounds = new Rect(-20f, -20f, 40f, 40f);
            harness.Runtime.Tick(4f);
            Assert.AreEqual(beforeVisibleTick, body.Body.position);
            harness.CameraBounds = new Rect(-3f, 10f, 6f, 6f);
            harness.Runtime.Tick(8f);
            harness.Runtime.Tick(12f);
            Assert.AreEqual(1, harness.Runtime.ActiveCount);
            Assert.IsTrue(harness.Runtime.TryGetBody(lifeId, out var sameBody));
            Assert.AreSame(body, sameBody);
            Assert.AreNotEqual(beforeVisibleTick, body.Body.position);
            Assert.AreEqual(postDamageHealth, body.DamageReceiver.CurrentHealth);
            Assert.AreEqual(lifeId, body.DamageReceiver.Identity.lifeId);
        }
    }

    static string Join(IReadOnlyList<string> values) {
        return values == null || values.Count == 0 ? string.Empty : string.Join("; ", values);
    }
}

/// <summary>Real isolated-physics composition harness; it owns only test-created shared services.</summary>
internal sealed class PopulationHarness : IDisposable {
    internal readonly PursuitFixture Fixture;
    internal readonly PopulationBudgetData Budget;
    internal readonly TrafficSessionServices Services;
    internal readonly PoliceDirectorRuntime Director;
    internal readonly PolicePopulationRuntime Runtime;
    internal readonly TestPoliceBodyProvider Provider;
    internal readonly NpcVehicleProfile CivilianProfile;
    internal readonly NpcVehicleInstance OriginalInstance;
    readonly PoliceVehiclePrefabCatalog prefabCatalog;
    readonly MapNavigationDocument navigation;
    readonly PoliceDirectorData authoredDirector;
    readonly Texture2D policeTexture;
    readonly Sprite policeSprite;
    readonly SpriteRenderer policeRenderer;
    internal Rect CameraBounds { get; set; } = new Rect(-3f, 10f, 6f, 6f);

    internal PopulationHarness(bool reuseSingleSlot = false, bool freeDrive = false, float deltaTime = .02f) {
        Fixture = new PursuitFixture(deltaTime);
        Fixture.Binding.body.gameObject.SetActive(false);
        Fixture.Controller.ResetForNewLife();
        Fixture.PoliceReceiver.Unbind();
        policeTexture = new Texture2D(1, 2, TextureFormat.RGBA32, false);
        policeTexture.SetPixels(new[] { Color.white, Color.white });
        policeTexture.Apply();
        policeSprite = Sprite.Create(policeTexture, new Rect(0f, 0f, 1f, 2f), new Vector2(0.5f, 0.5f), 1f);
        policeRenderer = Fixture.Binding.body.gameObject.AddComponent<SpriteRenderer>();
        policeRenderer.sprite = policeSprite;
        policeRenderer.enabled = true;
        Budget = ScriptableObject.CreateInstance<PopulationBudgetData>();
        Budget.maxCivilianMoving = 4; Budget.maxPoliceMoving = 2; Budget.maxTotalMoving = 4;
        Budget.maxWreckSlots = reuseSingleSlot ? 1 : 4;
        Budget.maxTotalPhysicsObjects = reuseSingleSlot ? 1 : 8;
        navigation = Navigation();
        var graph = new RoadGraphRuntime(navigation);
        CivilianProfile = ScriptableObject.CreateInstance<NpcVehicleProfile>();
        CivilianProfile.vehicleProfileId = "civilian-replacement";
        CivilianProfile.visualCatalogId = "civilian-replacement";
        CivilianProfile.allowedRoles.Add(VehicleRole.Civilian);
        CivilianProfile.maxHealth = 100f;
        var catalog = new BuiltInTrafficProfileCatalog(new[] { Fixture.Profile, CivilianProfile });
        var pool = new NpcVehiclePool(Budget, catalog, Fixture.Coordinator.IdentityRegistry);
        var population = new VehiclePopulationService(Budget);
        Services = new TrafficSessionServices(graph, catalog, pool, population, Budget,
            Fixture.Coordinator.IdentityRegistry);
        if (reuseSingleSlot) {
            Assert.IsTrue(pool.TryAcquire(CivilianProfile.vehicleProfileId, VehicleRole.Civilian, out var originalInstance));
            OriginalInstance = originalInstance;
            Assert.IsTrue(pool.Release(OriginalInstance));
        }
        authoredDirector = DirectorData();
        Director = new PoliceDirectorRuntime(authoredDirector, true, 19);
        Provider = new TestPoliceBodyProvider(Fixture.Binding.body, Fixture.Profile);
        prefabCatalog = ScriptableObject.CreateInstance<PoliceVehiclePrefabCatalog>();
        prefabCatalog.entries.Add(new PoliceVehiclePrefabCatalog.Entry {
            visualCatalogId = Fixture.Profile.visualCatalogId, prefab = Fixture.Binding.body
        });
        Runtime = new PolicePopulationRuntime(new PolicePopulationRuntime.Config {
            sessionCoordinator = Fixture.Coordinator, services = Services, damageWorld = Fixture.World,
            directorRuntime = Director, bodyProvider = Provider, policeCatalog = new PoliceProfileCatalog(catalog,
                new[] { Fixture.Binding.vehicle }, new[] { Fixture.Binding.behavior }), policePrefabCatalog = prefabCatalog,
            navigation = navigation, mapOriginWorld = Vector2.zero, playerReceiver = Fixture.Player,
            playerBody = Fixture.Player.GetComponent<Rigidbody2D>(), currentCameraBounds = () => CameraBounds,
            worldClearance = new PhysicsSceneAreaClearanceQuery(Fixture.Physics, Vector2.zero, false, 32),
            localStaticClearance = new PhysicsSceneAreaClearanceQuery(Fixture.Physics, Vector2.zero, true, 32),
            navigationSettings = new PoliceNavigationSettings(workBudget: 512, projectionSearchRadius: 40f,
                indexCellSize: 4f),
            placementSettings = new PolicePlacementSettings { minimumDistance = 2f, reactionSeconds = 0f,
                cameraMargin = 0f, maxCandidates = 4, offscreenDwellSeconds = 3f, keepRearPursuers = 1,
                authoredCameraHorizontalWorldSize = 6f, authoredCameraAspect = 1f },
            deterministicSeed = 19, playerSurfaceRadius = 1f,
            navigationMode = freeDrive ? PoliceNavigationMode.FreeDrive : PoliceNavigationMode.LegacyRoad,
            freeChaseSettings = new PoliceFreeChaseSettings()
        });
    }

    internal bool TryRequest(out PoliceDirectorRuntime.SpawnRequest request) {
        Director.Tick(0f);
        return Director.TryDequeue(out request);
    }

    MapNavigationDocument Navigation() {
        var document = new MapNavigationDocument { localBounds = new Rect(-30f, -30f, 60f, 60f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "south", x = 0f, y = -20f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "north", x = 0f, y = 20f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "police-road", fromNodeId = "south", toNodeId = "north",
            usableWidth = 6f, speedLimit = 10f, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "police-spawn", edgeId = "police-road",
            distanceAlongEdge = 5f, role = VehicleRole.Police, clearanceWidth = 2f, clearanceLength = 3f });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "police-entry", spawnId = "police-spawn",
            allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        document.spawnPoints.Add(new VehicleSpawnRecord { spawnId = "police-spawn-rear", edgeId = "police-road",
            distanceAlongEdge = 15f, role = VehicleRole.Police, clearanceWidth = 2f, clearanceLength = 3f });
        document.policeEntries.Add(new PoliceEntryRecord { entryId = "police-entry-rear", spawnId = "police-spawn-rear",
            allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        return document;
    }

    static PoliceDirectorData DirectorData() {
        var data = ScriptableObject.CreateInstance<PoliceDirectorData>();
        data.profileId = "test-director"; data.earliestPoliceTime = 0f; data.maximumHeat = 100f;
        var tier = new PoliceHeatTier { tierId = "test-tier", minimumHeat = 0f, targetCount = 1,
            reinforcementInterval = 1f, destroyedReplacementDelay = 0f, relocationCooldown = 0f };
        tier.compositions.Add(new PoliceCompositionEntry { vehicleProfileId = "police", behaviorProfileId = "pursue",
            tacticalRole = PoliceTacticalRole.Pursue, weight = 1f });
        data.heatTiers.Add(tier);
        return data;
    }

    public void Dispose() {
        Runtime?.Dispose(); Director?.Dispose(); Provider?.Dispose();
        if (prefabCatalog != null) UnityEngine.Object.DestroyImmediate(prefabCatalog);
        if (authoredDirector != null) UnityEngine.Object.DestroyImmediate(authoredDirector);
        if (Budget != null) UnityEngine.Object.DestroyImmediate(Budget);
        if (policeRenderer != null) UnityEngine.Object.DestroyImmediate(policeRenderer);
        if (policeSprite != null) UnityEngine.Object.DestroyImmediate(policeSprite);
        if (policeTexture != null) UnityEngine.Object.DestroyImmediate(policeTexture);
        if (CivilianProfile != null) UnityEngine.Object.DestroyImmediate(CivilianProfile);
        Fixture?.Dispose();
    }
}

/// <summary>Provider over the fixture's real police body; no fake receiver or callback is substituted.</summary>
internal sealed class TestPoliceBodyProvider : IPoliceVehicleBodyProvider, IDisposable {
    readonly PoliceVehicleBody body;
    readonly NpcVehicleProfile profile;
    bool leased;
    internal bool RejectAcquire { get; set; }
    internal int ReleaseCount { get; private set; }
    internal int LastLifeId => body.DamageReceiver.Identity.lifeId;
    internal PoliceVehicleBody LastBody => body;

    internal TestPoliceBodyProvider(PoliceVehicleBody body, NpcVehicleProfile profile) {
        this.body = body; this.profile = profile;
    }

    public PoliceVehicleBody Acquire(NpcVehicleProfile requested) {
        if (RejectAcquire || leased || requested != profile) return null;
        leased = true; return body;
    }

    public void Release(PoliceVehicleBody released) {
        if (released != body || !leased) return;
        leased = false; ReleaseCount++;
    }

    public void Dispose() { leased = false; }
}
