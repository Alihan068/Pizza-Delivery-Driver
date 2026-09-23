using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Physics-scene coverage for explicit bounded police collider sweeps.</summary>
public sealed class PoliceExplicitSensorTests {
    /// <summary>Confirms a nonunit explicit direction is normalized and finds geometry beyond the old nominal range.</summary>
    [Test]
    public void Sensor_ExplicitDirectionNormalizesAndFindsBeyondNominalRange() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero, Vector2.one, -90f);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings { minimumGap = 1f });
            var wall = world.CreateBody("Wall", new Vector2(5f, 0f), Vector2.one, bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.IsFalse(sensor.TryDetectForwardObstacle(out _, out _), "The old zero-speed nominal sweep must not reach the wall.");
            var status = sensor.QuerySweep(new Vector2(2f, 0f), 6f, out float gap, out Collider2D hit);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked, status);
            Assert.AreSame(wall.GetComponent<Collider2D>(), hit);
            Assert.AreEqual(4f, gap, 0.05f);
        }
    }

    /// <summary>Confirms rotated and offset sensor geometry, rather than a center ray, determines the hit.</summary>
    [Test]
    public void Sensor_ExplicitSweepUsesRotatedOffsetColliderShape() {
        using (var world = new TrafficPhysicsTestWorld()) {
            Vector2 scale = new Vector2(1.5f, 0.75f);
            Vector2 offset = new Vector2(0.6f, 0.8f);
            var body = world.CreateBody("Sensor", new Vector2(10f, 10f), new Vector2(1f, 4f), 45f, scale, offset,
                RigidbodyType2D.Kinematic);
            var sensor = world.AddSensor(body, new NpcMotorSettings());
            Vector2 forward = body.transform.up;
            Vector2 right = body.transform.right;
            Vector2 center = body.position + right * offset.x * scale.x + forward * offset.y * scale.y;
            var wall = world.CreateBody("OffsetWall", center + forward * (2f * scale.y + 0.75f), Vector2.one * 0.2f,
                45f, bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(forward, 8f, out float gap, out Collider2D hit));
            Assert.AreSame(wall.GetComponent<Collider2D>(), hit);
            Assert.AreEqual(0.65f, gap, 0.05f);
        }
    }

    /// <summary>Confirms triggers and same-body sibling colliders are ignored while a later solid blocker remains visible.</summary>
    [Test]
    public void Sensor_TriggerAndSiblingShapesAreIgnored() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var body = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(body, new NpcMotorSettings(), 2);
            body.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * 0.8f;
            for (int i = 0; i < 4; i++) {
                var trigger = world.CreateBody("Trigger", new Vector2(0f, 1.5f + i * 0.3f), Vector2.one * 0.2f,
                    bodyType: RigidbodyType2D.Static);
                trigger.GetComponent<Collider2D>().isTrigger = true;
            }
            var solid = world.CreateBody("Solid", new Vector2(0f, 4f), bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(Vector2.up, 5f, out _, out Collider2D hit));
            Assert.AreSame(solid.GetComponent<Collider2D>(), hit);
            Assert.IsFalse(sensor.LastQuerySaturated);
        }
    }

    /// <summary>Confirms a full hit buffer has explicit saturated precedence over a blocking result.</summary>
    [Test]
    public void Sensor_FullBufferReturnsSaturated() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var body = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(body, new NpcMotorSettings(), 1);
            for (int i = 0; i < 3; i++) world.CreateBody("Wall" + i, new Vector2(0f, 2f + i * 0.4f), Vector2.one * 0.4f, bodyType: RigidbodyType2D.Static);
            world.Step();
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Saturated,
                sensor.QuerySweep(Vector2.up, 6f, out _, out _));
            Assert.IsTrue(sensor.LastQuerySaturated);
        }
    }

    /// <summary>Confirms null/unconfigured, disabled, nonfinite, zero-direction, and invalid-range requests fail closed.</summary>
    [Test]
    public void Sensor_InvalidConfigurationDirectionAndRangeReturnInvalid() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var unconfiguredBody = world.CreateBody("Unconfigured", Vector2.zero);
            var unconfigured = world.AddSensor(unconfiguredBody, null);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid,
                unconfigured.QuerySweep(Vector2.up, 1f, out _, out _));
            var body = world.CreateBody("Sensor", new Vector2(4f, 0f));
            var sensor = world.AddSensor(body, new NpcMotorSettings());
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(Vector2.zero, 1f, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(new Vector2(float.Epsilon, 0f), 1f, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(new Vector2(float.MaxValue, float.MaxValue), 1f, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(Vector2.up, 0f, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(Vector2.up, float.PositiveInfinity, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(new Vector2(float.NaN, 1f), 1f, out _, out _));
            sensor.enabled = false;
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid, sensor.QuerySweep(Vector2.up, 1f, out _, out _));
            Object.DestroyImmediate(unconfiguredBody.gameObject);
        }
    }
}
