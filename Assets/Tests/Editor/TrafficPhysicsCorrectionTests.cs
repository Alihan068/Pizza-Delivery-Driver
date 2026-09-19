using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Regression coverage for braking, collider geometry and isolated manual physics fixtures.</summary>
public sealed class TrafficPhysicsCorrectionTests {
    /// <summary>Braking consumes only the remaining longitudinal momentum, including reverse, partial brake and crash mode.</summary>
    [TestCase(0.02f, 1f, 0.08f, 1f, false)]
    [TestCase(0.02f, 1f, -0.08f, 1f, false)]
    [TestCase(0.01f, 0.25f, 0.06f, 0.8f, false)]
    [TestCase(0.04f, 8f, 0.06f, 1f, false)]
    [TestCase(0.02f, 1f, 0.04f, 0.1f, false)]
    [TestCase(0.02f, 1f, 0.051f, 1f, true)]
    [TestCase(0.02f, 10f, 5f, 1f, false)]
    [TestCase(0.04f, 2f, -3f, 0.5f, true)]
    public void Brake_StopsWithoutChangingDirectionOrExceedingAvailableForce(
        float deltaTime, float mass, float initialSpeed, float brake, bool crashMode) {
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var settings = new NpcMotorSettings { lateralGrip = 0f };
            var rig = world.CreateMotor(settings, mass, Vector2.zero);
            rig.motor.SetCrashMode(crashMode);
            rig.motor.SetCommand(new NpcDriveCommand(0f, brake, 0f, 0f, false));
            rig.rb.linearVelocity = new Vector2(2f, initialSpeed);

            float deceleration = Mathf.Min(settings.brakeDeceleration, settings.maxBrakeForce / mass)
                * brake * (crashMode ? rig.motor.crashModeForceMultiplier : 1f);
            float expected = Mathf.Max(0f, Mathf.Abs(initialSpeed) - deceleration * deltaTime);
            world.Step();
            Assert.AreEqual(Mathf.Sign(initialSpeed) * expected, rig.rb.linearVelocity.y, 0.00002f);
            Assert.AreEqual(2f, rig.rb.linearVelocity.x, 0.00002f, "Braking must preserve lateral impact momentum.");

            float previous = expected;
            int remainingSteps = Mathf.CeilToInt(expected / (deceleration * deltaTime)) + 3;
            for (int i = 0; i < remainingSteps; i++) {
                world.Step();
                float directedSpeed = rig.rb.linearVelocity.y * Mathf.Sign(initialSpeed);
                Assert.GreaterOrEqual(directedSpeed, -0.00002f, "Brake force must never reverse the travel direction.");
                Assert.LessOrEqual(directedSpeed, previous + 0.00002f);
                previous = directedSpeed;
            }
            Assert.AreEqual(0f, rig.rb.linearVelocity.y, 0.00002f);
        }
    }

    /// <summary>A non-square collider detects its forward edge lane and rejects an adjacent lane at each heading, with authored offset/scale too.</summary>
    [TestCase(0f, false)]
    [TestCase(45f, false)]
    [TestCase(90f, false)]
    [TestCase(0f, true)]
    [TestCase(45f, true)]
    [TestCase(90f, true)]
    public void Sensor_SweepsActualRotatedColliderGeometry(float angle, bool offsetAndScale) {
        using (var world = new TrafficPhysicsTestWorld()) {
            Vector2 scale = offsetAndScale ? new Vector2(1.5f, 0.75f) : Vector2.one;
            Vector2 offset = offsetAndScale ? new Vector2(0.6f, 0.8f) : Vector2.zero;
            var rb = world.CreateBody("Sensor", new Vector2(10f, 10f), new Vector2(1f, 4f),
                angle, scale, offset, RigidbodyType2D.Kinematic);
            var sensor = world.AddSensor(rb, new NpcMotorSettings { minimumGap = 2f });
            Vector2 forward = rb.transform.up;
            Vector2 right = rb.transform.right;
            Vector2 center = rb.position + right * offset.x * scale.x + forward * offset.y * scale.y;
            float halfWidth = 0.5f * scale.x;
            float halfLength = 2f * scale.y;
            const float obstacleHalfSize = 0.1f;
            const float clearance = 0.75f;

            var blocker = world.CreateBody("ForwardEdge", center + forward * (halfLength + obstacleHalfSize + clearance)
                + right * (halfWidth - 0.02f), Vector2.one * (2f * obstacleHalfSize),
                angle, bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.IsTrue(sensor.TryDetectForwardObstacle(out float distance, out Collider2D hit));
            Assert.AreSame(blocker.GetComponent<Collider2D>(), hit);
            Assert.AreEqual(clearance, distance, 0.03f, "Distance must start at the real leading surface.");
            Object.DestroyImmediate(blocker.gameObject);

            world.CreateBody("AdjacentLane", center + forward * (halfLength + obstacleHalfSize + clearance)
                + right * (halfWidth + obstacleHalfSize + 0.15f), Vector2.one * (2f * obstacleHalfSize),
                angle, bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.IsFalse(sensor.TryDetectForwardObstacle(out _, out _), "A neighbouring lane outside the swept shape is clear.");
        }
    }

    /// <summary>Triggers and sibling colliders are excluded before they can fill the bounded result buffer.</summary>
    [Test]
    public void Sensor_TriggerAndSiblingShapesDoNotHideSolidObstacle() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var rb = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(rb, new NpcMotorSettings { minimumGap = 5f }, 2);
            rb.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 0.8f;
            for (int i = 0; i < 4; i++) {
                var trigger = world.CreateBody("Trigger", new Vector2(0f, 1.5f + i * 0.3f),
                    Vector2.one * 0.2f, bodyType: RigidbodyType2D.Static);
                trigger.GetComponent<Collider2D>().isTrigger = true;
            }
            var solid = world.CreateBody("Solid", new Vector2(0f, 4f), bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.IsTrue(sensor.TryDetectForwardObstacle(out _, out Collider2D hit));
            Assert.AreSame(solid.GetComponent<Collider2D>(), hit);
            Assert.IsFalse(sensor.LastQuerySaturated);
        }
    }

    /// <summary>Every registered motor advances exactly once per simulation step, regardless of creation order.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void Fixture_EqualMotorsAdvanceEquallyWithoutOverlapping(bool reverseCreationOrder) {
        using (var world = new TrafficPhysicsTestWorld(0.03f)) {
            float firstX = reverseCreationOrder ? 10f : -10f;
            var first = world.CreateMotor(new NpcMotorSettings(), 1f, new Vector2(firstX, 0f));
            var second = world.CreateMotor(new NpcMotorSettings(), 1f, new Vector2(-firstX, 0f));
            first.motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false));
            second.motor.SetCommand(new NpcDriveCommand(1f, 0f, 0f, 6f, false));
            for (int i = 0; i < 10; i++) world.Step();
            Assert.AreEqual(1.2f, first.rb.linearVelocity.y, 0.0001f);
            Assert.AreEqual(first.rb.linearVelocity.y, second.rb.linearVelocity.y, 0.0001f);
            Assert.AreEqual(first.rb.position.y, second.rb.position.y, 0.0001f);
            Assert.AreEqual(20f, Mathf.Abs(first.rb.position.x - second.rb.position.x), 0.0001f);
        }
    }

    /// <summary>Turning uses the local simulation step without rewriting the editor's global fixed timestep.</summary>
    [TestCase(0.01f)]
    [TestCase(0.04f)]
    public void Motor_TurningUsesTheSameDeltaTimeAsLocalPhysics(float deltaTime) {
        using (var world = new TrafficPhysicsTestWorld(deltaTime)) {
            var settings = new NpcMotorSettings { turnRate = 500f, minimumTurningRadius = 3f };
            var rig = world.CreateMotor(settings, 1f, Vector2.zero);
            rig.rb.linearVelocity = Vector2.up;
            rig.motor.SetCommand(new NpcDriveCommand(0f, 0f, 1f, 0f, false));
            world.Step();
            Assert.AreEqual(Mathf.Rad2Deg / 3f * deltaTime, rig.rb.rotation, 0.01f);
        }
    }

    /// <summary>Local stepping and exception cleanup leave default-scene bodies, loaded scenes and global physics/time settings untouched.</summary>
    [Test]
    public void Fixture_IsolatesDefaultPhysicsAndRestoresSceneStateEvenOnFailure() {
        Scene originalActive = SceneManager.GetActiveScene();
        int originalCount = SceneManager.sceneCount;
        bool originalDirty = originalActive.isDirty;
        var mode = Physics2D.simulationMode;
        bool queriesHitTriggers = Physics2D.queriesHitTriggers;
        bool queriesStartInColliders = Physics2D.queriesStartInColliders;
        bool autoSync = Physics2D.autoSyncTransforms;
        Vector2 gravity = Physics2D.gravity;
        float fixedDeltaTime = Time.fixedDeltaTime;
        float timeScale = Time.timeScale;
        Scene sentinelScene = default;
        Scene testScene = default;
        GameObject temporaryBody = null;
        try {
            // A second disposable world proves unrelated physics is never stepped.
            sentinelScene = EditorSceneManager.NewPreviewScene();
            Rigidbody2D sentinel;
            try {
                var sentinelObject = new GameObject("UnrelatedPhysicsSentinel");
                SceneManager.MoveGameObjectToScene(sentinelObject, sentinelScene);
                sentinel = sentinelObject.AddComponent<Rigidbody2D>();
                sentinel.position = new Vector2(1000f, 1000f);
                sentinel.linearVelocity = new Vector2(3f, 4f);
            } finally {
                SceneManager.SetActiveScene(originalActive);
            }
            Vector2 sentinelPosition = sentinel.position;
            Vector2 sentinelVelocity = sentinel.linearVelocity;

            var failure = Assert.Throws<InvalidOperationException>(() => {
                using (var world = new TrafficPhysicsTestWorld(0.03f)) {
                    testScene = world.Scene;
                    var body = world.CreateBody("Temporary", Vector2.zero);
                    temporaryBody = body.gameObject;
                    body.linearVelocity = Vector2.up;
                    world.Step();
                    Assert.Greater(body.position.y, 0f, "The local world must really simulate.");
                    Assert.AreEqual(sentinelPosition, sentinel.position);
                    Assert.AreEqual(sentinelVelocity, sentinel.linearVelocity);
                    throw new InvalidOperationException("Intentional fixture cleanup probe");
                }
            });
            Assert.AreEqual("Intentional fixture cleanup probe", failure.Message);
            Assert.IsTrue(temporaryBody == null, "Closing the local scene must destroy its objects on failure.");
            Assert.IsFalse(testScene.IsValid());
            Assert.AreEqual(sentinelPosition, sentinel.position);
            Assert.AreEqual(sentinelVelocity, sentinel.linearVelocity);
        } finally {
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            if (sentinelScene.IsValid() && sentinelScene.isLoaded) EditorSceneManager.ClosePreviewScene(sentinelScene);
        }
        Assert.AreEqual(originalActive, SceneManager.GetActiveScene());
        Assert.AreEqual(originalCount, SceneManager.sceneCount);
        Assert.AreEqual(originalDirty, originalActive.isDirty);
        Assert.AreEqual(mode, Physics2D.simulationMode);
        Assert.AreEqual(queriesHitTriggers, Physics2D.queriesHitTriggers);
        Assert.AreEqual(queriesStartInColliders, Physics2D.queriesStartInColliders);
        Assert.AreEqual(autoSync, Physics2D.autoSyncTransforms);
        Assert.AreEqual(gravity, Physics2D.gravity);
        Assert.AreEqual(fixedDeltaTime, Time.fixedDeltaTime);
        Assert.AreEqual(timeScale, Time.timeScale);
    }
}

/// <summary>Disposable test-only local physics world shared by the existing and regression traffic fixtures.</summary>
internal sealed class TrafficPhysicsTestWorld : IDisposable {
    static readonly MethodInfo motorAwake = typeof(NpcVehicleMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo motorStep = typeof(NpcVehicleMotor).GetMethod("SimulateStep", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo sensorAwake = typeof(VehicleObstacleSensor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerAwake = typeof(CivilianRouteFollower).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo followerTick = typeof(CivilianRouteFollower).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
    readonly Scene scene;
    readonly PhysicsScene2D physics;
    readonly List<NpcVehicleMotor> motors = new List<NpcVehicleMotor>();
    readonly List<CivilianRouteFollower> followers = new List<CivilianRouteFollower>();

    internal float DeltaTime { get; }
    internal Scene Scene => scene;

    internal TrafficPhysicsTestWorld(float deltaTime = 0.02f) {
        DeltaTime = deltaTime;
        scene = EditorSceneManager.NewPreviewScene();
        if (!scene.IsValid()) throw new InvalidOperationException("Cannot create the temporary traffic test scene.");
        physics = scene.GetPhysicsScene2D();
        if (!physics.IsValid() || physics == Physics2D.defaultPhysicsScene) {
            EditorSceneManager.ClosePreviewScene(scene);
            throw new InvalidOperationException("Traffic fixtures require a separate valid PhysicsScene2D.");
        }
    }

    internal Rigidbody2D CreateBody(string name, Vector2 position, Vector2? size = null,
        float angle = 0f, Vector2? scale = null, Vector2? offset = null,
        RigidbodyType2D bodyType = RigidbodyType2D.Dynamic) {
        Scene previousActive = SceneManager.GetActiveScene();
        try {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
            Vector2 bodyScale = scale ?? Vector2.one;
            go.transform.localScale = new Vector3(bodyScale.x, bodyScale.y, 1f);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = bodyType;
            rb.gravityScale = 0f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            if (bodyType == RigidbodyType2D.Dynamic) rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = size ?? Vector2.one;
            collider.offset = offset ?? Vector2.zero;
            return rb;
        } finally {
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }
    }

    internal (GameObject go, NpcVehicleMotor motor, Rigidbody2D rb) CreateMotor(
        NpcMotorSettings settings, float mass, Vector2 position) {
        var rb = CreateBody("MotorRig", position);
        rb.mass = mass;
        var motor = rb.gameObject.AddComponent<NpcVehicleMotor>();
        motorAwake.Invoke(motor, null);
        motor.Configure(settings);
        motors.Add(motor);
        return (rb.gameObject, motor, rb);
    }

    internal VehicleObstacleSensor AddSensor(Rigidbody2D rb, NpcMotorSettings settings, int bufferSize = 8) {
        var sensor = rb.gameObject.AddComponent<VehicleObstacleSensor>();
        sensorAwake.Invoke(sensor, null);
        sensor.Configure(settings, bufferSize);
        return sensor;
    }

    internal CivilianRouteFollower AddFollower(NpcVehicleMotor motor, CivilianFollowerSettings settings = null) {
        var follower = motor.gameObject.AddComponent<CivilianRouteFollower>();
        followerAwake.Invoke(follower, null);
        if (settings != null) follower.Configure(settings);
        followers.Add(follower);
        return follower;
    }

    internal void Step() {
        // Followers issue commands first (mirrors DefaultExecutionOrder -10), then every live motor
        // applies once, then only this local world advances once.
        foreach (var follower in followers) {
            if (follower != null && follower.isActiveAndEnabled) followerTick.Invoke(follower, new object[] { DeltaTime });
        }
        foreach (var motor in motors) {
            if (motor != null && motor.isActiveAndEnabled) motorStep.Invoke(motor, new object[] { DeltaTime });
        }
        Assert.IsTrue(physics.Simulate(DeltaTime), "The isolated physics step must actually run.");
    }

    /// <summary>Closes only this transient scene and destroys every fixture object, including failed-test leftovers.</summary>
    public void Dispose() {
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
    }
}
