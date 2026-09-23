using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Focused physics-scene coverage for bounded ram-target exclusion in obstacle sensing.</summary>
public sealed class PoliceRamSensorTests {
    /// <summary>Excluding the nearest target still returns a farther physical wall.</summary>
    [Test]
    public void Sensor_ExcludedNearestTargetStillFindsFartherWall() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            var target = world.CreateBody("Target", new Vector2(0f, 2f));
            var wall = world.CreateBody("Wall", new Vector2(0f, 4f), Vector2.one, bodyType: RigidbodyType2D.Static);
            world.Step();

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(Vector2.up, 6f, target, out float gap, out Collider2D hit));
            Assert.AreSame(wall.GetComponent<Collider2D>(), hit);
            Assert.Greater(gap, 2f);
        }
    }

    /// <summary>Excluding the only target clears the sweep while the legacy query still blocks on that target.</summary>
    [Test]
    public void Sensor_ExcludedTargetOnlyClearsAndLegacyQueryStillBlocks() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            var target = world.CreateBody("Target", new Vector2(0f, 2f));
            world.Step();

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(Vector2.up, 6f, out _, out Collider2D legacyHit));
            Assert.AreSame(target.GetComponent<Collider2D>(), legacyHit);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Clear,
                sensor.QuerySweep(Vector2.up, 6f, target, out float excludedGap, out Collider2D excludedHit));
            Assert.AreEqual(float.PositiveInfinity, excludedGap);
            Assert.IsNull(excludedHit);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(Vector2.up, 6f, (Rigidbody2D)null, out _, out _));

            Object.DestroyImmediate(target.gameObject);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Clear,
                sensor.QuerySweep(Vector2.up, 6f, target, out _, out _));
        }
    }

    /// <summary>All colliders on the excluded target body are skipped, but another body remains blocking.</summary>
    [Test]
    public void Sensor_ExcludedCompoundTargetSkipsAllSiblingCollidersOnly() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            var target = world.CreateBody("Target", new Vector2(0f, 2f));
            var sibling = target.gameObject.AddComponent<BoxCollider2D>();
            sibling.size = new Vector2(0.8f, 0.8f);
            sibling.offset = new Vector2(0f, 0.35f);
            var other = world.CreateBody("OtherSameProfile", new Vector2(0f, 4f), Vector2.one,
                bodyType: RigidbodyType2D.Static);
            world.Step();

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(Vector2.up, 6f, target, out _, out Collider2D hit));
            Assert.AreSame(other.GetComponent<Collider2D>(), hit);
        }
    }

    /// <summary>Raw hit saturation remains fail-closed even when the saturated hit is the excluded target.</summary>
    [Test]
    public void Sensor_ExcludedTargetStillSaturatesFullRawBuffer() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings(), 2);
            var target = world.CreateBody("Target", new Vector2(0f, 2f));
            var sibling = target.gameObject.AddComponent<BoxCollider2D>();
            sibling.size = Vector2.one * 0.8f;
            sibling.offset = Vector2.up * 0.35f;
            world.Step();

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Saturated,
                sensor.QuerySweep(Vector2.up, 6f, target, out _, out _));
            Assert.IsTrue(sensor.LastQuerySaturated);
        }
    }

    /// <summary>A static collider without a rigidbody blocks ordinary, null-exclusion and target-exclusion queries.</summary>
    [Test]
    public void Sensor_BodylessWallIsNeverNullExclusion() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            var wallBody = world.CreateBody("StaticColliderOnly", Vector2.up * 2f, Vector2.one, bodyType: RigidbodyType2D.Static);
            var wallCollider = wallBody.GetComponent<Collider2D>();
            Object.DestroyImmediate(wallBody);
            var target = world.CreateBody("Target", Vector2.up * 4f);
            world.Step();
            Assert.IsNull(wallCollider.attachedRigidbody);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked, sensor.QuerySweep(Vector2.up, 6f, out _, out var ordinary));
            Assert.AreSame(wallCollider, ordinary);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked, sensor.QuerySweep(Vector2.up, 6f, null, out _, out var withoutExclusion));
            Assert.AreSame(wallCollider, withoutExclusion);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked, sensor.QuerySweep(Vector2.up, 6f, target, out _, out var excludingTarget));
            Assert.AreSame(wallCollider, excludingTarget);
        }
    }

    /// <summary>Self, foreign-scene, inactive, and nonsimulated exclusions are invalid rather than silently ignored.</summary>
    [Test]
    public void Sensor_InvalidExcludedTargetBindingsReturnInvalid() {
        using (var world = new TrafficPhysicsTestWorld()) {
            var sensorBody = world.CreateBody("Sensor", Vector2.zero);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            var self = sensorBody;
            var inactive = world.CreateBody("Inactive", new Vector2(0f, 2f));
            var nonsimulated = world.CreateBody("Nonsimulated", new Vector2(0f, 3f));
            nonsimulated.simulated = false;

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid,
                sensor.QuerySweep(Vector2.up, 6f, self, out _, out _));
            inactive.gameObject.SetActive(false);
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid,
                sensor.QuerySweep(Vector2.up, 6f, inactive, out _, out _));
            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid,
                sensor.QuerySweep(Vector2.up, 6f, nonsimulated, out _, out _));

            using (var foreignWorld = new TrafficPhysicsTestWorld()) {
                var foreign = foreignWorld.CreateBody("Foreign", Vector2.zero);
                Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Invalid,
                    sensor.QuerySweep(Vector2.up, 6f, foreign, out _, out _));
            }
        }
    }

    /// <summary>Trigger and same-body siblings remain filtered while rotated offset and scale still select the farther wall.</summary>
    [Test]
    public void Sensor_ExclusionPreservesTriggerSiblingAndRotatedOffsetShapeFiltering() {
        using (var world = new TrafficPhysicsTestWorld()) {
            Vector2 scale = new Vector2(1.5f, 0.75f);
            Vector2 offset = new Vector2(0.6f, 0.8f);
            var sensorBody = world.CreateBody("Sensor", new Vector2(10f, 10f), new Vector2(1f, 4f), 45f,
                scale, offset, RigidbodyType2D.Kinematic);
            var sensor = world.AddSensor(sensorBody, new NpcMotorSettings());
            Vector2 forward = sensorBody.transform.up;
            Vector2 right = sensorBody.transform.right;
            Vector2 center = sensorBody.position + right * offset.x * scale.x + forward * offset.y * scale.y;
            var target = world.CreateBody("Target", center + forward * 3.2f, Vector2.one * 0.2f,
                bodyType: RigidbodyType2D.Kinematic);
            var sibling = target.gameObject.AddComponent<BoxCollider2D>();
            sibling.size = Vector2.one * 0.4f;
            sibling.offset = new Vector2(0f, 0.4f);
            var trigger = world.CreateBody("Trigger", center + forward * 2.6f, Vector2.one * 0.2f,
                bodyType: RigidbodyType2D.Static);
            trigger.GetComponent<Collider2D>().isTrigger = true;
            var wall = world.CreateBody("Wall", center + forward * 4.8f, Vector2.one * 0.2f, 45f,
                bodyType: RigidbodyType2D.Static);
            world.Step();

            Assert.AreEqual(VehicleObstacleSensor.SweepStatus.Blocked,
                sensor.QuerySweep(forward, 8f, target, out float gap, out Collider2D hit));
            Assert.AreSame(wall.GetComponent<Collider2D>(), hit);
            Assert.Greater(gap, 0f);
        }
    }
}
