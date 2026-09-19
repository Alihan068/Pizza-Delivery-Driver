using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real receiver/physics-query integration regressions isolated from the open editor scene and career saves.</summary>
public sealed class DamageRuntimeCorrectionTests {
    Scene scene;
    readonly List<Object> assets = new List<Object>();
    TrafficDamageWorld world;
    TrafficSessionCoordinator coordinator;
    NpcVehiclePool pool;
    VehiclePopulationService population;
    TrafficRespawnScheduler respawns;
    NpcVehicleProfile profile;

    /// <summary>Creates a preview physics world without touching the open map.</summary>
    [SetUp]
    public void SetUp() {
        scene = EditorSceneManager.NewPreviewScene();
        var draft = new SessionSetupDraft("test", "map", "car", "difficulty", 5, false, Array.Empty<string>());
        coordinator = new TrafficSessionCoordinator(new TrafficSessionContext(draft));
        coordinator.NotifyPlayerReady(); coordinator.NotifyMapReady();
        var settings = ScriptableObject.CreateInstance<TrafficDamageSettings>(); assets.Add(settings);
        settings.profiles = new[] { new TrafficDamageProfile { profileId = "test", blastDamage = 500f, blastRadius = 6f } };
        settings.initialQueryCapacity = 1; settings.wreckLifetimeSeconds = 1f; settings.blastsPerTick = 1;
        world = CreateObject().AddComponent<TrafficDamageWorld>(); world.Configure(coordinator, settings);
        var budget = ScriptableObject.CreateInstance<PopulationBudgetData>(); assets.Add(budget);
        budget.maxWreckSlots = 4; budget.maxCivilianMoving = 4; budget.maxTotalMoving = 4;
        profile = ScriptableObject.CreateInstance<NpcVehicleProfile>(); assets.Add(profile);
        profile.vehicleProfileId = "car"; profile.allowedRoles.Add(VehicleRole.Civilian);
        profile.damageProfileId = "test"; profile.explosionProfileId = "test"; profile.maxHealth = 40f;
        pool = new NpcVehiclePool(budget, new BuiltInTrafficProfileCatalog(new[] { profile }), coordinator.IdentityRegistry);
        population = new VehiclePopulationService(budget); respawns = new TrafficRespawnScheduler();
    }

    GameObject CreateObject() {
        var go = new GameObject(); SceneManager.MoveGameObjectToScene(go, scene); return go;
    }

    VehicleDamageReceiver Spawn(Vector2 position, VehicleDamageReceiver reuse = null) {
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsTrue(pool.TryAcquire("car", VehicleRole.Civilian, out var life));
        Assert.IsTrue(population.CommitSpawn(VehicleRole.Civilian)); Assert.IsTrue(life.TryActivate());
        var go = reuse != null ? reuse.gameObject : CreateObject();
        go.transform.position = position;
        if (reuse == null) { go.AddComponent<Rigidbody2D>().gravityScale = 0f; go.AddComponent<BoxCollider2D>(); }
        go.SetActive(true);
        var receiver = reuse != null ? reuse : go.AddComponent<VehicleDamageReceiver>();
        Assert.IsTrue(receiver.BindNpc(world, life, profile, pool, population, respawns));
        return receiver;
    }

    /// <summary>Closes only disposable fixtures and their temporary assets.</summary>
    [TearDown]
    public void TearDown() {
        if (world != null) world.EndSession();
        if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        assets.Clear();
    }

    /// <summary>A real overlap finds B, its death queues the next explosion, both wrecks recycle, and stale damage cannot hit the reused body.</summary>
    [Test]
    public void RealChainRecyclesSameReceiverAndRetainsPlayerOriginWithoutVfx() {
        var a = Spawn(Vector2.zero); var b = Spawn(new Vector2(2f, 0f));
        int firstId = a.Identity.lifeId;
        var deaths = new List<VehicleDestroyedEvent>();
        world.VehicleDestroyed += (death, heat) => { deaths.Add(death); Assert.Greater(heat, 0f); };
        world.gameObject.AddComponent<TrafficExplosionVisualPool>().Initialize(world, null, 0);
        scene.GetPhysicsScene2D().Simulate(0.02f);
        var context = new DamageContext("impact", coordinator.PlayerIdentity.Value.lifeId, InstigatorKind.Player,
            coordinator.PlayerIdentity.Value.lifeId, DamageKind.Collision, "root");
        a.ApplyCollision(20f, context);
        Assert.IsTrue(a.IsWreck); Assert.IsFalse(b.IsWreck);
        world.Tick(0.1f);
        Assert.IsTrue(b.IsWreck); Assert.AreEqual(2, deaths.Count);
        Assert.AreEqual(DamageKind.Explosion, deaths[1].killingContext.damageKind);
        Assert.AreEqual("root", deaths[1].killingContext.rootIncidentId);
        Assert.IsTrue(a.gameObject.activeSelf); Assert.IsTrue(a.GetComponent<Collider2D>().enabled);
        world.Tick(2f);
        Assert.IsFalse(a.gameObject.activeSelf); Assert.IsFalse(b.gameObject.activeSelf);
        Assert.AreEqual(0, population.OccupiedWreckCount); Assert.AreEqual(2, respawns.PendingCount);
        var reused = Spawn(Vector2.zero, a);
        Assert.Greater(reused.Identity.lifeId, firstId); Assert.AreEqual(40f, reused.CurrentHealth);
        Assert.IsFalse(reused.ApplyBlast(new BlastApplication("old", firstId, VehicleRole.Civilian, 999f, context)));
        Assert.AreEqual(40f, reused.CurrentHealth);
    }

    /// <summary>Applications resolved before end cannot damage any receiver after the shared coordinator has closed.</summary>
    [Test]
    public void EndedSessionRejectsAlreadyResolvedApplicationsAndDoesNotAdvanceTime() {
        var receiver = Spawn(Vector2.zero);
        var application = new BlastApplication("stale", receiver.Identity.lifeId, VehicleRole.Civilian, 999f,
            new DamageContext("stale", 1, InstigatorKind.Player, 1, DamageKind.Explosion, "root"));
        coordinator.NotifySessionEnded(); world.Tick(50f);
        Assert.IsFalse(receiver.ApplyBlast(application)); Assert.AreEqual(40f, receiver.CurrentHealth);
        Assert.AreEqual(0f, world.SessionTime); Assert.AreEqual(0, world.PendingBlasts);
    }

    /// <summary>Zero/invalid damage cannot start the player's cooldown; an actual following blast is still accepted.</summary>
    [Test]
    public void ZeroDamageDoesNotConsumePlayerCooldownAndEndedSessionClosesAdapter() {
        var go = CreateObject(); go.SetActive(false); go.AddComponent<Rigidbody2D>();
        var driver = go.AddComponent<Driver>(); driver.currentHealth = driver.maxHealth = 100f;
        driver.BindDamageSession(coordinator);
        Assert.IsFalse(driver.ApplyBlastDamage(0f)); Assert.IsFalse(driver.ApplyBlastDamage(float.NaN));
        Assert.IsFalse(driver.ApplyBlastDamage(float.PositiveInfinity));
        Assert.IsTrue(driver.ApplyBlastDamage(10f)); Assert.AreEqual(90f, driver.currentHealth);
        coordinator.NotifySessionEnded(); Assert.IsFalse(driver.ApplyBlastDamage(10f));
    }

    /// <summary>Invalid authoring is rejected before an exception can leave a partially configured damage world.</summary>
    [Test]
    public void SettingsRejectInvalidBudgetsAndNonFiniteTiming() {
        var settings = ScriptableObject.CreateInstance<TrafficDamageSettings>(); assets.Add(settings);
        Assert.IsTrue(settings.IsValid());
        settings.maxQueryCapacity = settings.initialQueryCapacity - 1;
        Assert.IsFalse(settings.IsValid());
        settings.maxQueryCapacity = settings.initialQueryCapacity;
        settings.recoverySeconds = float.NaN;
        Assert.IsFalse(settings.IsValid());
    }

    /// <summary>First impact at time zero is accepted; repeated pairs cannot turn an ongoing contact into periodic damage.</summary>
    [Test]
    public void ContactPairsAndCooldownRequireSeparationBeforeAnotherImpact() {
        var receiver = Spawn(Vector2.zero);
        var tracker = new VehicleContactTracker();
        var context = new DamageContext("contact", 1, InstigatorKind.Player, 1, DamageKind.Collision, "contact");
        Assert.IsTrue(tracker.TryEnter(10, 20, 30));
        receiver.ApplyCollision(2f, context);
        float afterFirst = receiver.CurrentHealth;
        Assert.Less(afterFirst, 40f);
        receiver.ApplyCollision(2f, context);
        Assert.AreEqual(afterFirst, receiver.CurrentHealth);
        Assert.IsFalse(tracker.TryEnter(11, 20, 30));
        tracker.Exit(10, 20);
        world.Tick(1f);
        Assert.IsFalse(tracker.TryEnter(10, 20, 30));
        tracker.Exit(11, 20); tracker.Exit(10, 20);
        Assert.IsTrue(tracker.TryEnter(10, 20, 30));
        receiver.ApplyCollision(2f, context);
        Assert.Less(receiver.CurrentHealth, afterFirst);
        tracker.Clear();
        Assert.IsTrue(tracker.TryEnter(10, 20, 30));
    }

    /// <summary>Equal-kind contacts still identify the actual responsible actor; equal contribution remains neutral even at zero tolerance.</summary>
    [Test]
    public void SameRoleFaultReturnsResponsibleLifeAndNeutralTie() {
        var a = new ContactParticipant(5, InstigatorKind.Police, Vector2.right * 6f, 0f, Vector2.zero);
        var b = new ContactParticipant(9, InstigatorKind.Police, Vector2.zero, 0f, Vector2.right);
        Assert.AreEqual(InstigatorKind.Police, DamageAttributionResolver.ResolveFault(a, b, Vector2.zero, Vector2.right,
            new DamageAttributionRules(), out int life)); Assert.AreEqual(5, life);
        var tie = new ContactParticipant(9, InstigatorKind.Police, Vector2.left * 6f, 0f, Vector2.right);
        Assert.AreEqual(InstigatorKind.Environment, DamageAttributionResolver.ResolveFault(a, tie, Vector2.zero, Vector2.right,
            new DamageAttributionRules { attributionDeltaTolerance = 0f }, out _));
    }
}
