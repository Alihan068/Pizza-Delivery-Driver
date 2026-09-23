using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Bounded real-life pursuit coverage using only active pool lives, registered player receivers, and isolated physics.</summary>
public sealed class PolicePursuitControllerTests {
    /// <summary>A valid reserved police life binds to the real player and closes distance without direct Rigidbody writes.</summary>
    [TestCase(0.02f, false)]
    [TestCase(0.03f, true)]
    public void StraightPursuit_ClosesDistanceAndStopsBeforePlayer(float deltaTime, bool reverseCreationOrder) {
        using (var fixture = new PursuitFixture(deltaTime, reverseCreationOrder)) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            float startGap = fixture.Gap;
            fixture.Step(400);
            Assert.Less(fixture.Gap, startGap - 1f);
            Assert.Greater(fixture.Controller.CursorProgress, 1f);
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.Greater(fixture.SurfaceGap, fixture.ControllerSettings.stopGap - 0.15f);
            Assert.AreEqual(fixture.Profile.maxHealth, fixture.PoliceReceiver.CurrentHealth);
            Assert.AreEqual(100f, fixture.Player.CurrentHealth);
        }
    }

    /// <summary>Receiver authority rejects an unbound body, a different world life, and a profile that is not the actual police profile.</summary>
    [Test]
    public void Binding_RejectsUnboundWrongWorldAndStaleProfile() {
        using (var fixture = new PursuitFixture()) {
            var unbound = fixture.CreateReceiver(Vector2.left);
            Assert.IsFalse(fixture.Controller.TrySetTarget(unbound, out _));
            var original = fixture.Binding.vehicle.sharedNpc;
            fixture.Binding.vehicle.sharedNpc = ScriptableObject.CreateInstance<NpcVehicleProfile>();
            Assert.IsFalse(fixture.Controller.TryBind(fixture.Binding, out _));
            Object.DestroyImmediate(fixture.Binding.vehicle.sharedNpc);
            fixture.Binding.vehicle.sharedNpc = original;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            var other = fixture.CreateOtherActiveWorldPlayer();
            Assert.AreEqual(fixture.Coordinator.PlayerIdentity.Value.lifeId, other.Identity.lifeId);
            Assert.IsFalse(other.IsBoundTo(fixture.World, fixture.Coordinator.PlayerIdentity.Value));
            Assert.IsFalse(fixture.Controller.TrySetTarget(other, out _));
            fixture.Binding.vehicle.sharedNpc = original; original.colliderSize = new Vector2(2f, 2f);
            Assert.IsFalse(fixture.Controller.TryBind(fixture.Binding, out _)); original.colliderSize = new Vector2(1f, 2f);
            fixture.Binding.behavior.tacticalRole = PoliceTacticalRole.Intercept;
            Assert.IsFalse(fixture.Controller.TryBind(fixture.Binding, out _)); fixture.Binding.behavior.tacticalRole = PoliceTacticalRole.Pursue;
        }
    }

    /// <summary>Post-bind motor and physical-body mutations close the captured life rather than silently changing navigation constraints.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void PostBindMutation_ClosesBoundLife(bool makeBodyKinematic) {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            if (makeBodyKinematic) fixture.SetPoliceBodyType(RigidbodyType2D.Kinematic);
            else fixture.Profile.motorSettings.acceleration += 1f;
            fixture.Controller.Tick(0.02f, false);
            Assert.IsFalse(fixture.Controller.IsBound);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>A target replacement clears only route state and retains the planner's attempt timeline rather than resetting cadence.</summary>
    [Test]
    public void Retarget_PreservesPlannerCadenceAndClearsProgress() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(5);
            Assert.AreEqual(1, fixture.Controller.PlannerAttemptCount);
            var replacement = fixture.ReplacePlayer(new Vector2(0f, 22f));
            Assert.IsTrue(fixture.Controller.TrySetTarget(replacement, out reason), reason);
            Assert.AreEqual(0f, fixture.Controller.CursorProgress);
            fixture.Step(1); Assert.AreEqual(1, fixture.Controller.PlannerAttemptCount);
            fixture.Step(8); Assert.AreEqual(2, fixture.Controller.PlannerAttemptCount);
        }
    }

    /// <summary>A cached planner result advances measured cursor progress without repeatedly rebinding the open route.</summary>
    [Test]
    public void CachedRoute_AdvancesWithoutResettingProgress() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(4);
            int attempts = fixture.Controller.PlannerAttemptCount;
            float firstProgress = fixture.Controller.CursorProgress;
            fixture.Step(4);
            Assert.AreEqual(attempts, fixture.Controller.PlannerAttemptCount);
            Assert.Greater(fixture.Controller.CursorProgress, firstProgress);
        }
    }

    /// <summary>A paused tick sends only a stopped command and performs no initial planner attempt.</summary>
    [Test]
    public void PausedTick_StopsWithoutPlanningOrPropulsion() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Controller.Tick(0.02f, true);
            Assert.AreEqual(0, fixture.Controller.PlannerAttemptCount);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>An obstacle in the sweep corridor causes an explicit braking command instead of a coast-through command.</summary>
    [Test]
    public void Obstacle_StopsWithExplicitBrake() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.CreateStaticBlocker(new Vector2(0f, 5f));
            fixture.Step(300);
            Assert.Greater(fixture.Controller.LastCommand.brake, 0f);
            Assert.AreEqual(0f, fixture.Controller.LastCommand.throttle);
            Assert.Less(fixture.ForwardSpeed, fixture.ControllerSettings.stoppedSpeedThreshold + 0.1f);
            Assert.Greater(fixture.PoliceSurfaceGapTo(5f), fixture.ControllerSettings.stopGap - 0.15f);
        }
    }

    /// <summary>A saturated non-allocating sweep is treated as unsafe rather than selecting an incomplete hit list.</summary>
    [Test]
    public void SaturatedSensor_Stops() {
        using (var fixture = new PursuitFixture()) {
            fixture.ControllerSettings.sensorBuffer = 1;
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.CreateStaticBlocker(new Vector2(0f, 2.3f)); fixture.CreateStaticBlocker(new Vector2(0f, 2.6f));
            fixture.Step(1);
            Assert.IsTrue(fixture.Sensor.LastQuerySaturated);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
        }
    }

    /// <summary>Session end and component disable revoke motion and never leave a stale command running.</summary>
    [Test]
    public void SessionEnd_StopsAndRejectsStaleTarget() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(10);
            fixture.Coordinator.NotifySessionEnded();
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.IsFalse(fixture.Controller.TrySetTarget(fixture.Player, out _));
        }
    }

    /// <summary>Explicitly invokes the production disable callback after healthy driving because EditMode component enable changes do not dispatch it automatically.</summary>
    [Test]
    public void DisableCallback_ClearsHealthyBindingAndBrakes() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(5); fixture.Controller.enabled = false; fixture.InvokeControllerDisable();
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.IsFalse(fixture.Controller.IsBound);
            Assert.AreEqual(Vector2.zero, fixture.Controller.LastAimPoint);
            Assert.IsFalse(fixture.Controller.TrySetTarget(fixture.Player, out _));
        }
    }

    /// <summary>A reused physical receiver receives a new pool identity and remains stopped until its controller explicitly binds that new life.</summary>
    [Test]
    public void PoolReuse_OldControllerCannotDriveFreshLifeUntilRebound() {
        using (var fixture = new PursuitFixture()) {
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out string reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
            fixture.Step(5);
            int oldLife = fixture.RebindPoliceToFreshPoolLife();
            fixture.Controller.Tick(0.02f, false);
            Assert.IsFalse(fixture.Controller.IsBound);
            Assert.AreEqual(1f, fixture.Controller.LastCommand.brake);
            Assert.Greater(fixture.PoliceReceiver.Identity.lifeId, oldLife);
            Assert.IsTrue(fixture.Controller.TryBind(fixture.Binding, out reason), reason);
            Assert.IsTrue(fixture.Controller.TrySetTarget(fixture.Player, out reason), reason);
        }
    }

}

/// <summary>Disposable fixture that preserves controller-to-motor-to-damage-to-isolated-physics ordering.</summary>
internal sealed class PursuitFixture : IDisposable {
    internal sealed class AuthoredSetup {
        internal NpcVehicleProfile profile;
        internal NpcVehicleProfile civilianProfile;
        internal TrafficDamageSettings damage;
        internal PoliceVehicleProfile vehicle;
        internal PoliceBehaviorProfile behavior;
    }

    internal sealed class CivilianActor {
        internal Rigidbody2D body;
        internal CivilianRouteFollower follower;
        internal VehicleDamageReceiver receiver;
        internal NpcVehicleProfile profile;
    }

    static readonly MethodInfo motorAwake = typeof(NpcVehicleMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo motorStep = typeof(NpcVehicleMotor).GetMethod("SimulateStep", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerAwake = typeof(CivilianRouteFollower).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerTick = typeof(CivilianRouteFollower).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sensorAwake = typeof(VehicleObstacleSensor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo bodyAwake = typeof(CivilianVehicleBody).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo controllerDisable = typeof(PolicePursuitController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo validatePoliceState = typeof(PolicePursuitController).GetMethod("ValidatePoliceState", BindingFlags.Instance | BindingFlags.NonPublic);
    readonly Scene scene;
    readonly PhysicsScene2D physics;
    readonly List<Object> assets = new List<Object>();
    readonly List<Scene> extraScenes = new List<Scene>();
    readonly List<NpcVehicleMotor> motors = new List<NpcVehicleMotor>();
    readonly List<CivilianRouteFollower> civilianFollowers = new List<CivilianRouteFollower>();
    readonly List<PolicePursuitController> controllers = new List<PolicePursuitController>();
    readonly float deltaTime;
    readonly bool authoredInputs;
    TrafficDamageWorld world;
    NpcVehiclePool pool;
    VehiclePopulationService population;
    NpcVehicleInstance policeLife;
    internal TrafficSessionCoordinator Coordinator { get; }
    internal TrafficDamageWorld World => world;
    internal PolicePursuitController Controller { get; }
    internal PolicePursuitController.Binding Binding { get; }
    internal VehicleDamageReceiver Player { get; private set; }
    internal NpcVehicleProfile Profile { get; }
    internal NpcVehicleProfile CivilianProfile { get; }
    internal PoliceDrivingSettings ControllerSettings { get; }
    internal VehicleDamageReceiver PoliceReceiver { get; private set; }
    internal VehicleObstacleSensor Sensor { get; private set; }
    Rigidbody2D policeBody;
    Rigidbody2D playerBody;

    internal PhysicsScene2D Physics => physics;
    internal Scene FixtureScene => scene;
    internal Vector2 PolicePosition => policeBody.position;
    internal Vector2 PoliceForward => policeBody.transform.up;
    internal Vector2 PlayerPosition => playerBody.position;
    internal Vector2 PlayerVelocity => playerBody.linearVelocity;
    internal Vector2 PoliceVelocity => policeBody.linearVelocity;
    internal float StepDelta => deltaTime;
    internal Vector2 LastControllerPosition { get; private set; }
    internal Vector2 LastControllerForward { get; private set; }
    internal float LastControllerClock { get; private set; }
    internal Vector2 LastControllerVelocity { get; private set; }
    internal bool CaptureBindingDiagnostics { get; set; }
    internal string LastBindingDiagnostic { get; private set; }
    internal string FirstBindingLossDiagnostic { get; private set; }
    internal float ColliderGap => policeBody.GetComponent<Collider2D>().Distance(playerBody.GetComponent<Collider2D>()).distance;
    internal PolicePathCursor Cursor => ControllerField<PolicePathCursor>("cursor");
    internal PolicePursuitTargetPlanner Planner => ControllerField<PolicePursuitTargetPlanner>("planner");
    internal PoliceTurnTraversal Turn => ControllerField<PoliceTurnTraversal>("turnTraversal");
    internal PoliceRecoveryIntegration Recovery => ControllerField<PoliceRecoveryIntegration>("recovery");
    internal int RecoveryArrivalAttempt => (int)typeof(PoliceRecoveryIntegration).GetField("arrivalAttempt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Recovery);
    internal bool MotorReversePermitted => (bool)typeof(NpcVehicleMotor).GetField("reverseManeuverPermitted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Binding.body.Motor);
    internal float Gap => Vector2.Distance(policeBody.position, playerBody.position);
    internal float SurfaceGap {
        get {
            Assert.IsFalse(authoredInputs, "Use ColliderGap for authored shapes.");
            return Gap - 2f;
        }
    }
    internal float ForwardSpeed => Vector2.Dot(policeBody.linearVelocity, policeBody.transform.up);
    internal Collider2D PoliceCollider => policeBody.GetComponent<Collider2D>();
    internal float PoliceSurfaceGapTo(float worldY) {
        Assert.IsFalse(authoredInputs, "Measure the actual colliders for authored shapes.");
        return worldY - policeBody.position.y - 1.5f;
    }

    /// <summary>
    /// Creates an owned isolated physics scene and detached actor lives without career services.
    /// Optional collision tuning is applied before world/receiver binding so every spawned life
    /// captures the same test-only damage coefficient. Authored inputs are instead cloned in full
    /// before any life is acquired or bound; they cannot be combined with synthetic damage tuning.
    /// Omitted authored inputs preserve the original synthetic fixture defaults.
    /// </summary>
    public PursuitFixture(float deltaTime = 0.02f, bool reverseCreationOrder = false, float? collisionDamageFactor = null,
        AuthoredSetup authored = null) {
        if (authored != null) {
            Assert.IsNull(collisionDamageFactor, "Authored damage must not be retuned by the synthetic fixture.");
            Assert.IsNotNull(authored.profile); Assert.IsNotNull(authored.damage);
            Assert.IsNotNull(authored.vehicle); Assert.IsNotNull(authored.behavior);
            Assert.AreSame(authored.profile, authored.vehicle.sharedNpc);
            if (authored.civilianProfile != null) Assert.IsTrue(authored.civilianProfile.allowedRoles.Contains(VehicleRole.Civilian));
            Assert.IsNotNull(authored.behavior.driving);
        }
        this.deltaTime = deltaTime;
        authoredInputs = authored != null;
        scene = Application.isPlaying
            ? SceneManager.CreateScene(Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics2D))
            : EditorSceneManager.NewPreviewScene();
        try {
            physics = scene.GetPhysicsScene2D();
            Assert.IsTrue(physics.IsValid()); Assert.IsFalse(physics == Physics2D.defaultPhysicsScene);
            var draft = new SessionSetupDraft("test", "map", "car", "difficulty", 1, false, Array.Empty<string>());
            Coordinator = new TrafficSessionCoordinator(new TrafficSessionContext(draft)); Coordinator.NotifyPlayerReady(); Coordinator.NotifyMapReady();
            world = Create("World").AddComponent<TrafficDamageWorld>();
            var damage = authored == null ? Asset<TrafficDamageSettings>() : CloneAsset(authored.damage);
            if (authored == null) damage.profiles = new[] { new TrafficDamageProfile { profileId = "damage", blastDamage = 1f, blastRadius = 1f } };
            if (collisionDamageFactor.HasValue) {
                Assert.IsFalse(float.IsNaN(collisionDamageFactor.Value) || float.IsInfinity(collisionDamageFactor.Value));
                Assert.GreaterOrEqual(collisionDamageFactor.Value, 0f);
                damage.profiles[0].collision.damageFactor = collisionDamageFactor.Value;
            }
            world.Configure(Coordinator, damage);
            Profile = authored == null ? Asset<NpcVehicleProfile>() : CloneAsset(authored.profile);
            CivilianProfile = authored != null && authored.civilianProfile != null ? CloneAsset(authored.civilianProfile) : null;
            if (authored == null) {
                Profile.vehicleProfileId = "police"; Profile.visualCatalogId = "test"; Profile.allowedRoles.Add(VehicleRole.Police);
                Profile.damageProfileId = "damage"; Profile.explosionProfileId = "damage"; Profile.maxHealth = 100f; Profile.colliderSize = new Vector2(1f, 2f);
                Profile.baseMass = 1f; Profile.motorSettings = new NpcMotorSettings { cruiseSpeed = 4f, maxSpeed = 5f, acceleration = 6f, brakeDeceleration = 8f, maxBrakeForce = 20f, minimumTurningRadius = 1f, turnRate = 180f, reactionTime = 0.1f, lateralGrip = 1f };
            }
            var budget = Asset<PopulationBudgetData>(); budget.maxTotalMoving = 4; budget.maxPoliceMoving = 4; budget.maxWreckSlots = 4; budget.maxCivilianMoving = 4;
            var catalogProfiles = CivilianProfile != null ? new[] { Profile, CivilianProfile } : new[] { Profile };
            pool = new NpcVehiclePool(budget, new BuiltInTrafficProfileCatalog(catalogProfiles), Coordinator.IdentityRegistry);
            population = new VehiclePopulationService(budget);
            if (reverseCreationOrder) CreateRegisteredPlayer(new Vector2(0f, 14f));
            var police = CreatePolice();
            policeBody = police.GetComponent<Rigidbody2D>();
            Controller = police.GetComponent<PolicePursuitController>(); controllers.Add(Controller);
            var graph = CreateStraightGraph();
            var wrapper = authored == null ? Asset<PoliceVehicleProfile>() : CloneAsset(authored.vehicle);
            wrapper.sharedNpc = Profile;
            var behavior = authored == null ? Asset<PoliceBehaviorProfile>() : CloneAsset(authored.behavior);
            if (authored == null) {
                wrapper.supportedTacticalRoles.Add(PoliceTacticalRole.Pursue);
                behavior.behaviorProfileId = "pursue"; behavior.tacticalRole = PoliceTacticalRole.Pursue;
            }
            ControllerSettings = authored == null
                ? new PoliceDrivingSettings { lookaheadMin = 2f, lookaheadMax = 4f, lookaheadTime = 0.2f, planningDistance = 8f, comfort = 0.7f, stopGap = 1.2f, brakeBand = 1f, stoppedSpeedThreshold = 0.1f, maxSweepDistance = 32f, sensorBuffer = 16, cursorWork = 64, bindWork = 256, cursorWindow = 6f, maxDeviation = 2f, displacementSlack = 1.5f, acquisitionTolerance = 0.05f }
                : behavior.driving.Clone();
            behavior.driving = ControllerSettings;
            Binding = new PolicePursuitController.Binding { body = police.GetComponent<PoliceVehicleBody>(), vehicle = wrapper, behavior = behavior, damageWorld = world, graph = graph, navigation = NavigationFor(graph), mapOriginWorld = Vector2.zero, navigationSettings = new PoliceNavigationSettings(refreshInterval: 20f, workBudget: 256) };
            if (!reverseCreationOrder) CreateRegisteredPlayer(new Vector2(0f, 14f));
        } catch {
            // A throwing constructor is never received by using; destroy partial owned lives now.
            Dispose();
            throw;
        }
    }

    internal void Step(int count) {
        for (int step = 0; step < count; step++) {
            LastControllerPosition = policeBody.position;
            LastControllerForward = policeBody.transform.up;
            LastControllerClock = world.SessionTime;
            LastControllerVelocity = policeBody.linearVelocity;
            foreach (var controller in controllers) if (controller != null && controller.isActiveAndEnabled) {
                bool observeBinding = CaptureBindingDiagnostics && controller == Controller && controller.IsBound;
                if (observeBinding) LastBindingDiagnostic = CapturePoliceBindingFacts();
                controller.Tick(deltaTime, false);
                if (observeBinding && !controller.IsBound && FirstBindingLossDiagnostic == null)
                    FirstBindingLossDiagnostic = LastBindingDiagnostic;
            }
            foreach (var follower in civilianFollowers) if (follower != null && follower.isActiveAndEnabled)
                followerTick.Invoke(follower, new object[] { deltaTime });
            foreach (var motor in motors) motorStep.Invoke(motor, new object[] { deltaTime });
            world.BeginPhysicsStep(); Assert.IsTrue(physics.Simulate(deltaTime)); world.Tick(deltaTime);
        }
    }

    internal VehicleDamageReceiver CreateReceiver(Vector2 position) {
        var body = Create("Unbound").AddComponent<Rigidbody2D>(); body.position = position; body.gravityScale = 0f; body.gameObject.AddComponent<BoxCollider2D>(); return body.gameObject.AddComponent<VehicleDamageReceiver>();
    }

    internal VehicleDamageReceiver CreateRegisteredPlayer(Vector2 position) {
        var go = Create("Player"); playerBody = go.AddComponent<Rigidbody2D>(); playerBody.position = position; playerBody.gravityScale = 0f; go.AddComponent<BoxCollider2D>().size = new Vector2(1f, 2f);
        var driver = go.AddComponent<Driver>(); driver.maxHealth = driver.currentHealth = 100f;
        var input = go.GetComponent<VehicleInput>(); if (input != null) input.enabled = false;
        var movement = go.GetComponent<VehicleMovement>(); if (movement != null) movement.enabled = false;
        var receiver = go.AddComponent<VehicleDamageReceiver>(); receiver.BindPlayer(world, driver, Coordinator.PlayerIdentity.Value, 0f);
        Player = receiver; return receiver;
    }

    internal VehicleDamageReceiver ReplacePlayer(Vector2 position) {
        Player.Unbind(); Player.gameObject.SetActive(false);
        return CreateRegisteredPlayer(position);
    }

    internal VehicleDamageReceiver CreateOtherActiveWorldPlayer() {
        var otherScene = EditorSceneManager.NewPreviewScene();
        extraScenes.Add(otherScene);
        var draft = new SessionSetupDraft("test", "map", "car", "difficulty", 1, false, Array.Empty<string>());
        var otherCoordinator = new TrafficSessionCoordinator(new TrafficSessionContext(draft)); otherCoordinator.NotifyPlayerReady(); otherCoordinator.NotifyMapReady();
        var worldObject = new GameObject("OtherWorld"); SceneManager.MoveGameObjectToScene(worldObject, otherScene);
        var otherWorld = worldObject.AddComponent<TrafficDamageWorld>(); var settings = Asset<TrafficDamageSettings>();
        settings.profiles = new[] { new TrafficDamageProfile { profileId = "damage", blastDamage = 1f, blastRadius = 1f } }; otherWorld.Configure(otherCoordinator, settings);
        var go = new GameObject("OtherPlayer"); SceneManager.MoveGameObjectToScene(go, otherScene); var rb = go.AddComponent<Rigidbody2D>(); rb.gravityScale = 0f; go.AddComponent<BoxCollider2D>();
        var driver = go.AddComponent<Driver>(); driver.maxHealth = driver.currentHealth = 100f; var receiver = go.AddComponent<VehicleDamageReceiver>();
        receiver.BindPlayer(otherWorld, driver, otherCoordinator.PlayerIdentity.Value, 0f); return receiver;
    }

    internal void CreateStaticBlocker(Vector2 position) {
        var go = Create("Blocker"); go.transform.position = position; var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Static; go.AddComponent<BoxCollider2D>().size = Vector2.one;
    }

    internal GameObject CreateBlocker(Vector2 position, bool dynamic, Vector2 size, float headingDegrees = 0f) {
        var go = Create("CornerBlocker");
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = dynamic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Static;
        rb.position = position;
        rb.rotation = headingDegrees;
        go.AddComponent<BoxCollider2D>().size = size;
        return go;
    }

    internal void SetBlockerVelocity(GameObject blocker, Vector2 velocity) {
        blocker.GetComponent<Rigidbody2D>().linearVelocity = velocity;
    }

    internal void PushPlayer(Vector2 impulse) { playerBody.AddForce(impulse, ForceMode2D.Impulse); }

    // Fault injection changes private work/clock inputs, never a driving body's pose or velocity.
    internal void SetRuntimeCursorWork(int work) { ControllerField<PoliceDrivingSettings>("driving").cursorWork = work; }
    internal void SetRuntimeQueryWork(int work) {
        ((PoliceNavigationSettings)typeof(PolicePursuitTargetPlanner).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Planner)).workBudget = work;
    }
    internal void SetActiveClock(float value) {
        typeof(TrafficDamageWorld).GetProperty("SessionTime").GetSetMethod(true).Invoke(world, new object[] { value });
    }
    T ControllerField<T>(string name) => (T)typeof(PolicePursuitController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Controller);

    string CapturePoliceBindingFacts() {
        // Test-only readback occurs before Tick can erase the binding. No runtime field is written.
        var report = new StringBuilder();
        report.Append("PRE-TICK BINDING SNAPSHOT; ValidatePoliceState=").Append(validatePoliceState.Invoke(Controller, null));
        report.Append("; clock=").Append(RoundTrip(world.SessionTime));
        report.Append("; lastActiveClock=").Append(RoundTrip(ControllerField<float>("lastActiveClock")));
        report.Append("; committed=").Append(Controller.IsTurnCommitted);
        report.Append("; attempts=").Append(Controller.PlannerAttemptCount);
        report.Append("; cursor=").Append(RoundTrip(Controller.CursorProgress));
        var capturedBody = ControllerField<PoliceVehicleBody>("body");
        var capturedWorld = ControllerField<TrafficDamageWorld>("damageWorld");
        var capturedProfile = ControllerField<NpcVehicleProfile>("sharedProfile");
        var capturedSettings = ControllerField<NpcMotorSettings>("motorSettings");
        var capturedVehicle = ControllerField<PoliceVehicleProfile>("vehicle");
        var capturedBehavior = ControllerField<PoliceBehaviorProfile>("behavior");
        report.Append("; refs(body/world/coordinator/graph/navigation/profile/settings/vehicle/behavior)=")
            .Append(capturedBody != null).Append('/').Append(capturedWorld != null).Append('/')
            .Append(ControllerField<TrafficSessionCoordinator>("coordinator") != null).Append('/')
            .Append(ControllerField<RoadGraphRuntime>("graph") != null).Append('/')
            .Append(ControllerField<MapNavigationDocument>("navigation") != null).Append('/')
            .Append(capturedProfile != null).Append('/').Append(capturedSettings != null).Append('/')
            .Append(capturedVehicle != null).Append('/').Append(capturedBehavior != null);
        if (capturedBody == null || capturedWorld == null || capturedProfile == null || capturedSettings == null ||
            capturedVehicle == null || capturedBehavior == null) return report.ToString();
        var rb = capturedBody.Body;
        var collider = capturedBody.MainCollider;
        var motor = capturedBody.Motor;
        var sensor = capturedBody.Sensor;
        var receiver = capturedBody.DamageReceiver;
        report.Append("; components(rb/collider/motor/sensor/receiver)=").Append(rb != null).Append('/')
            .Append(collider != null).Append('/').Append(motor != null).Append('/').Append(sensor != null).Append('/').Append(receiver != null);
        if (rb == null || collider == null || motor == null || sensor == null || receiver == null) return report.ToString();
        report.Append("; active(world/body/root/motor/sensor/receiver)=").Append(capturedWorld.IsActive).Append('/')
            .Append(capturedBody.isActiveAndEnabled).Append('/').Append(capturedBody.gameObject.activeInHierarchy).Append('/')
            .Append(motor.isActiveAndEnabled).Append('/').Append(sensor.isActiveAndEnabled).Append('/').Append(receiver.isActiveAndEnabled);
        report.Append("; rbType=").Append(rb.bodyType).Append("; simulated=").Append(rb.simulated)
            .Append("; colliderEnabled=").Append(collider.enabled).Append("; trigger=").Append(collider.isTrigger)
            .Append("; permanentStop=").Append(motor.IsStoppedPermanently);
        var identity = ControllerField<VehicleIdentity>("boundPoliceIdentity");
        report.Append("; receiverBound=").Append(receiver.IsBoundTo(capturedWorld, identity, capturedProfile))
            .Append("; expectedLife=").Append(identity.lifeId).Append("; actualLife=").Append(receiver.Identity.lifeId);
        report.Append("; profileReference=").Append(capturedVehicle.sharedNpc == capturedProfile)
            .Append("; role=").Append(capturedBehavior.tacticalRole)
            .Append("; allowsPolice=").Append(capturedProfile.allowedRoles != null && capturedProfile.allowedRoles.Contains(VehicleRole.Police))
            .Append("; settingsReference=").Append(capturedProfile.motorSettings == capturedSettings)
            .Append("; samePhysicsScene=").Append(rb.gameObject.scene.GetPhysicsScene2D() == capturedWorld.gameObject.scene.GetPhysicsScene2D());
        Vector2 scale = new Vector2(capturedBody.transform.lossyScale.x, capturedBody.transform.lossyScale.y);
        AppendVectorFact(report, "scale", ControllerField<Vector2>("boundScale"), scale);
        AppendVectorFact(report, "footprint", ControllerField<Vector2>("footprint"), capturedBody.WorldFootprint);
        AppendVectorFact(report, "profileColliderSize", ControllerField<Vector2>("boundProfileColliderSize"), capturedProfile.colliderSize);
        AppendVectorFact(report, "scaledOffset", ControllerField<Vector2>("boundColliderOffsetWorld"), Vector2.Scale(collider.offset, scale));
        AppendVectorFact(report, "rawLocalScaleXY", ControllerField<Vector3>("boundLocalScale"), capturedBody.transform.localScale);
        AppendFloatFact(report, "rawLocalScaleZ", ControllerField<Vector3>("boundLocalScale").z, capturedBody.transform.localScale.z);
        AppendVectorFact(report, "rawColliderSize", ControllerField<Vector2>("boundColliderSize"), collider.size);
        AppendVectorFact(report, "rawColliderOffset", ControllerField<Vector2>("boundColliderOffset"), collider.offset);
        report.Append("; localScale=").Append(VectorText(capturedBody.transform.localScale))
            .Append("; colliderSize=").Append(VectorText(collider.size)).Append("; colliderOffset=").Append(VectorText(collider.offset));
        AppendFloatFact(report, "mass", ControllerField<float>("boundMass"), rb.mass);
        AppendFloatFact(report, "crashMultiplier", ControllerField<float>("boundCrashMultiplier"), motor.crashModeForceMultiplier);
        AppendFloatFact(report, "gravity", 0f, rb.gravityScale);
        object motorFacts = ControllerField<object>("boundMotorFacts");
        foreach (var field in motorFacts.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)) {
            float expected = (float)field.GetValue(motorFacts);
            float actual = (float)typeof(NpcMotorSettings).GetField(field.Name).GetValue(capturedSettings);
            AppendFloatFact(report, field.Name, expected, actual);
        }
        report.Append("; bodyPosition=").Append(VectorText(rb.position)).Append("; velocity=").Append(VectorText(rb.linearVelocity))
            .Append("; rotation=").Append(RoundTrip(rb.rotation)).Append("; angularVelocity=").Append(RoundTrip(rb.angularVelocity))
            .Append("; forward=").Append(VectorText(capturedBody.transform.up)).Append("; targetPosition=").Append(VectorText(playerBody.position))
            .Append("; capturedBounds=").Append(ControllerField<Rect>("boundLocalBounds").ToString("R"));
        return report.ToString();
    }

    static void AppendVectorFact(StringBuilder report, string name, Vector2 expected, Vector2 actual) {
        report.Append("; ").Append(name).Append(" expected=").Append(VectorText(expected)).Append(" actual=").Append(VectorText(actual))
            .Append(" delta=").Append(VectorText(actual - expected)).Append(" exact=").Append(actual.x == expected.x && actual.y == expected.y);
    }

    static void AppendFloatFact(StringBuilder report, string name, float expected, float actual) {
        report.Append("; ").Append(name).Append(" expected=").Append(RoundTrip(expected)).Append(" actual=").Append(RoundTrip(actual))
            .Append(" exact=").Append(expected == actual);
    }

    static string RoundTrip(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string VectorText(Vector2 value) => "(" + RoundTrip(value.x) + "," + RoundTrip(value.y) + ")";

    internal int RebindPoliceToFreshPoolLife() {
        int oldLife = policeLife.Identity.Value.lifeId;
        Assert.IsTrue(policeLife.TryMarkWrecked()); Assert.IsTrue(population.MarkActiveVehicleWrecked(VehicleRole.Police));
        PoliceReceiver.Unbind(); Assert.IsTrue(pool.Release(policeLife)); Assert.IsTrue(population.ReleaseWreck());
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Police)); Assert.IsTrue(pool.TryAcquire(Profile.vehicleProfileId, VehicleRole.Police, out policeLife));
        Assert.IsTrue(population.CommitSpawn(VehicleRole.Police)); Assert.IsTrue(policeLife.TryActivate());
        Assert.IsTrue(PoliceReceiver.BindNpc(world, policeLife, Profile, pool, population));
        return oldLife;
    }

    internal void SetPolicePosition(Vector2 position) {
        policeBody.position = position;
    }

    /// <summary>Seeds one pre-simulation police velocity for a controlled physics contact.</summary>
    internal void SetPoliceVelocity(Vector2 velocity) {
        policeBody.linearVelocity = velocity;
    }

    internal void SetPoliceRotation(float degrees) {
        // Initial fixture placement must agree before the first controller tick; no driving uses this helper.
        policeBody.transform.rotation = Quaternion.Euler(0f, 0f, degrees);
        policeBody.rotation = degrees;
    }

    internal void SetPoliceColliderOffset(Vector2 offset) {
        policeBody.GetComponent<BoxCollider2D>().offset = offset;
    }

    internal void SetPoliceBodyType(RigidbodyType2D bodyType) {
        policeBody.bodyType = bodyType;
    }

    /// <summary>Changes only the fixture body's gravity for a fail-closed turn-prediction case.</summary>
    internal void SetPoliceGravity(float gravityScale) {
        policeBody.gravityScale = gravityScale;
    }

    /// <summary>Installs one clear 90-degree road-only route with the normal .75-second planner refresh.</summary>
    internal void ConfigureCornerRoute(bool right, bool interiorBend) {
        var document = CreateBentDocument(right, interiorBend);
        Binding.graph = new RoadGraphRuntime(document);
        Binding.navigation = document;
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 8);
        // Initial fixture placement, before any driving step, preserves reversed body creation order.
        playerBody.position = new Vector2(right ? 12f : -12f, 8f);
    }

    internal void ConfigureStraightInitialRoute() {
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 8);
    }

    /// <summary>Installs detached authored navigation and initial poses before binding, without replacing profile or behavior settings.</summary>
    internal void ConfigureAuthoredNavigation(MapNavigationDocument document, Vector2 origin,
        Vector2 policeInitialPosition, float heading, Vector2 targetInitialPosition) {
        Assert.IsNotNull(document);
        Assert.IsFalse(Controller.IsBound); Assert.AreEqual(0f, world.SessionTime);
        var detached = JsonUtility.FromJson<MapNavigationDocument>(JsonUtility.ToJson(document));
        Binding.navigation = detached; Binding.graph = new RoadGraphRuntime(detached);
        Binding.mapOriginWorld = origin;
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, origin, true, ControllerSettings.sensorBuffer);
        SetPolicePosition(policeInitialPosition); SetPoliceRotation(heading);
        playerBody.position = targetInitialPosition;
    }

    internal void ConfigureRotatedStraightRoute() {
        var document = new MapNavigationDocument { localBounds = new Rect(-10f, -24f, 50f, 48f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 40f, y = 0f });
        document.edges.Add(Edge("horizontal", "a", "b", Vector2.zero, Vector2.right * 40f));
        Binding.graph = new RoadGraphRuntime(document);
        Binding.navigation = document;
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 8);
        playerBody.position = new Vector2(14f, 0f);
        policeBody.position = new Vector2(-3f, 0f);
        SetPoliceRotation(-90f);
    }

    internal void ConfigureFinalApproachRoute(Vector2 target, float roadLength = 10f, bool horizontal = false,
        float boundsMaxY = 30f) {
        var document = new MapNavigationDocument { localBounds = new Rect(-24f, -10f, 48f, boundsMaxY + 10f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = horizontal ? roadLength : 0f, y = horizontal ? 0f : roadLength });
        document.edges.Add(Edge("finalRoad", "a", "b", Vector2.zero,
            horizontal ? Vector2.right * roadLength : Vector2.up * roadLength));
        Binding.graph = new RoadGraphRuntime(document);
        Binding.navigation = document;
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 8);
        playerBody.position = target;
    }

    internal void ConfigureRamRoute(float policeY = 11.3f, float targetY = 14f, float roadLength = 60f,
        float boundsMaxY = 80f, bool ramIntent = true, float roadWidth = 4f) {
        var document = new MapNavigationDocument { localBounds = new Rect(-30f, -10f, 60f, boundsMaxY + 10f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = roadLength });
        var edge = Edge("ramRoad", "a", "b", Vector2.zero, Vector2.up * roadLength);
        edge.usableWidth = roadWidth;
        document.edges.Add(edge);
        Binding.graph = new RoadGraphRuntime(document); Binding.navigation = document;
        Binding.navigationSettings = new PoliceNavigationSettings(refreshInterval: 0.75f, workBudget: 512);
        Binding.staticClearance = new PhysicsSceneAreaClearanceQuery(physics, Vector2.zero, true, 16);
        if (!Binding.vehicle.supportedTacticalRoles.Contains(PoliceTacticalRole.Ram)) Binding.vehicle.supportedTacticalRoles.Add(PoliceTacticalRole.Ram);
        Binding.behavior.tacticalRole = ramIntent ? PoliceTacticalRole.Ram : PoliceTacticalRole.Pursue;
        SetPolicePosition(Vector2.up * policeY); playerBody.position = Vector2.up * targetY;
    }

    internal VehicleDamageReceiver CreateTrafficBlocker(Vector2 position, VehicleRole role) {
        if (!Profile.allowedRoles.Contains(role)) Profile.allowedRoles.Add(role);
        Assert.IsTrue(population.TryReserveSpawn(role));
        Assert.IsTrue(pool.TryAcquire(Profile.vehicleProfileId, role, out var life));
        Assert.IsTrue(population.CommitSpawn(role)); Assert.IsTrue(life.TryActivate());
        var go = Create("TrafficBlocker"); var rigidbody = go.AddComponent<Rigidbody2D>();
        rigidbody.position = position; rigidbody.gravityScale = 0f;
        go.AddComponent<BoxCollider2D>().size = Profile.colliderSize;
        var motor = go.AddComponent<NpcVehicleMotor>(); motorAwake.Invoke(motor, null); motors.Add(motor);
        var receiver = go.AddComponent<VehicleDamageReceiver>();
        Assert.IsTrue(receiver.BindNpc(world, life, Profile, pool, population));
        return receiver;
    }

    /// <summary>Creates one authored civilian life with its real motor, follower, sensor, damage receiver, and route subscription.</summary>
    internal CivilianActor CreateAuthoredCivilian(CivilianRouteRecord route, Vector2 position, float heading = 0f) {
        Assert.IsNotNull(CivilianProfile);
        Assert.IsNotNull(route);
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Civilian));
        Assert.IsTrue(pool.TryAcquire(CivilianProfile.vehicleProfileId, VehicleRole.Civilian, out var life));
        Assert.IsTrue(population.CommitSpawn(VehicleRole.Civilian));
        Assert.IsTrue(life.TryActivate());

        var go = Create("Civilian");
        var rb = go.AddComponent<Rigidbody2D>();
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, heading));
        rb.position = position; rb.rotation = heading; rb.gravityScale = 0f;
        rb.mass = CivilianProfile.baseMass; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        go.AddComponent<BoxCollider2D>().size = CivilianProfile.colliderSize;
        var motor = go.AddComponent<NpcVehicleMotor>(); motorAwake.Invoke(motor, null); motors.Add(motor);
        var follower = go.AddComponent<CivilianRouteFollower>(); followerAwake.Invoke(follower, null); civilianFollowers.Add(follower);
        var sensor = go.AddComponent<VehicleObstacleSensor>(); sensorAwake.Invoke(sensor, null);
        var receiver = go.AddComponent<VehicleDamageReceiver>();
        var body = go.AddComponent<CivilianVehicleBody>(); bodyAwake.Invoke(body, null);
        Assert.IsTrue(receiver.BindNpc(world, life, CivilianProfile, pool, population));
        Assert.IsTrue(follower.TryBeginRoute(Binding.graph, route, CivilianProfile.motorSettings, CivilianProfile.colliderSize,
            Binding.mapOriginWorld, out string issue), issue);
        return new CivilianActor { body = rb, follower = follower, receiver = receiver, profile = CivilianProfile };
    }

    internal bool TryQueryCurrent(out PoliceRoadTargetQuery.Result result) {
        var input = new PoliceRoadTargetQuery.Input {
            graph = Binding.graph,
            navigation = Binding.navigation,
            profile = Profile,
            policePosition = policeBody.position,
            policeDirection = policeBody.transform.up,
            targetPosition = playerBody.position,
            localColliderOffset = policeBody.GetComponent<BoxCollider2D>().offset,
            hasKnownAnchor = false,
            staticClearance = Binding.staticClearance,
            settings = Binding.navigationSettings,
            budget = new RoadPathQuery.SearchBudget(Binding.navigationSettings.workBudget)
        };
        return PoliceRoadTargetQuery.TryQuery(input, out result);
    }

    internal void InvalidateGraph() {
        Binding.graph.Rebuild();
    }

    internal void InvokeControllerDisable() {
        controllerDisable.Invoke(Controller, null);
    }

    internal MapNavigationDocument CreateBentDocument(bool right = true, bool interiorBend = false) {
        float side = right ? 1f : -1f;
        var document = new MapNavigationDocument { localBounds = new Rect(-24f, -10f, 48f, 40f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 8f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = side * 16f, y = 8f });
        if (interiorBend) document.edges.Add(new RoadEdgeRecord { edgeId = "one", fromNodeId = "a", toNodeId = "c", usableWidth = 4f, speedLimit = 5f,
            orderedPoints = new List<Vector2> { Vector2.zero, Vector2.up * 8f, new Vector2(side * 16f, 8f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        else {
            document.edges.Add(Edge("one", "a", "b", Vector2.zero, Vector2.up * 8f));
            document.edges.Add(Edge("two", "b", "c", Vector2.up * 8f, new Vector2(side * 16f, 8f)));
        }
        return document;
    }

    internal MapNavigationDocument NavigationFor(RoadGraphRuntime graph) {
        var document = new MapNavigationDocument { localBounds = new Rect(-10f, -10f, 30f, 50f) };
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 0f, y = 40f });
        document.edges.Add(Edge("straight", "a", "b", Vector2.zero, Vector2.up * 40f));
        return document;
    }

    /// <summary>Closes isolated previews or destroys only the runtime fixture scene's roots before unloading; no fixture survives to a yielded player loop.</summary>
    public void Dispose() {
        if (world != null) world.EndSession();
        if (Application.isPlaying) {
            if (scene.IsValid() && scene.isLoaded) {
                foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(scene);
            }
        } else if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        foreach (var extra in extraScenes) if (extra.IsValid() && extra.isLoaded) EditorSceneManager.ClosePreviewScene(extra);
        foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
    }

    GameObject Create(string name) {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    T Asset<T>() where T : ScriptableObject {
        var asset = ScriptableObject.CreateInstance<T>();
        assets.Add(asset);
        return asset;
    }
    T CloneAsset<T>(T source) where T : ScriptableObject {
        var asset = Object.Instantiate(source);
        assets.Add(asset);
        return asset;
    }
    GameObject CreatePolice() {
        Assert.IsTrue(population.TryReserveSpawn(VehicleRole.Police)); Assert.IsTrue(pool.TryAcquire(Profile.vehicleProfileId, VehicleRole.Police, out policeLife)); Assert.IsTrue(population.CommitSpawn(VehicleRole.Police)); Assert.IsTrue(policeLife.TryActivate());
        var go = Create("Police"); var rb = go.AddComponent<Rigidbody2D>(); rb.gravityScale = 0f; rb.mass = Profile.baseMass; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; rb.interpolation = RigidbodyInterpolation2D.Interpolate; go.AddComponent<BoxCollider2D>().size = Profile.colliderSize;
        var motor = go.AddComponent<NpcVehicleMotor>(); motorAwake.Invoke(motor, null); motors.Add(motor);
        Sensor = go.AddComponent<VehicleObstacleSensor>(); sensorAwake.Invoke(Sensor, null); var receiver = go.AddComponent<VehicleDamageReceiver>(); go.AddComponent<PoliceVehicleBody>(); go.AddComponent<PolicePursuitController>();
        PoliceReceiver = receiver; Assert.IsTrue(receiver.BindNpc(world, policeLife, Profile, pool, population)); return go;
    }
    RoadGraphRuntime CreateStraightGraph() { var document = NavigationFor(null); return new RoadGraphRuntime(document); }
    static RoadEdgeRecord Edge(string id, string from, string to, Vector2 start, Vector2 end) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 4f, speedLimit = 5f, orderedPoints = new List<Vector2> { start, end }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } };
}
