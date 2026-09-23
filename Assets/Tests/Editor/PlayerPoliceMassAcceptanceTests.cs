using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>Acceptance tests for bounded player mass authoring and its isolated collision consequence.</summary>
public sealed class PlayerPoliceMassAcceptanceTests {
    static readonly string[] playerAssetPaths = {
        "Assets/ScriptableObjects/ScooterData.asset",
        "Assets/ScriptableObjects/GreenSedanData.asset",
        "Assets/ScriptableObjects/SportData.asset",
        "Assets/ScriptableObjects/VanData.asset"
    };

    static readonly float[] expectedBaseMasses = { 0.85f, 1.4f, 1.1f, 2.8f };
    static readonly float[] expectedMaximumMasses = { 1.15f, 2f, 1.58f, 5.2f };

    /// <summary>Checks positive finite masses, monotonic ramps, exact endpoints, and surviving prefab references for every Health level.</summary>
    [Test]
    public void PlayerMassAuthoring_AllHealthLevelsAreBoundedMonotonicAndPreservePrefabReferences() {
        for (int vehicleIndex = 0; vehicleIndex < playerAssetPaths.Length; vehicleIndex++) {
            VehicleData vehicle = AssetDatabase.LoadAssetAtPath<VehicleData>(playerAssetPaths[vehicleIndex]);
            Assert.IsNotNull(vehicle, playerAssetPaths[vehicleIndex]);
            Assert.IsNotNull(vehicle.vehiclePrefab, vehicle.vehicleName + " must retain its player prefab reference.");
            Assert.IsNotNull(vehicle.bodySettings, vehicle.vehicleName + " must retain body settings.");

            int maximumHealthLevel = vehicle.GetMaxLevel(VehicleStatId.Health);
            Assert.AreEqual(maximumHealthLevel + 1, vehicle.bodySettings.massByHealthLevel.Length, vehicle.vehicleName + " mass table length.");
            Assert.AreEqual(expectedBaseMasses[vehicleIndex], vehicle.bodySettings.baseMass, 0.0001f, vehicle.vehicleName + " base mass.");
            Assert.AreEqual(expectedBaseMasses[vehicleIndex], vehicle.bodySettings.ResolveMass(0), 0.0001f, vehicle.vehicleName + " level zero resolution.");
            Assert.AreEqual(expectedMaximumMasses[vehicleIndex], vehicle.bodySettings.ResolveMass(maximumHealthLevel), 0.0001f, vehicle.vehicleName + " maximum resolution.");

            float previous = 0f;
            for (int healthLevel = 0; healthLevel <= maximumHealthLevel; healthLevel++) {
                float resolved = vehicle.bodySettings.ResolveMass(healthLevel);
                Assert.IsTrue(!float.IsNaN(resolved) && !float.IsInfinity(resolved) && resolved > 0f,
                    vehicle.vehicleName + " level " + healthLevel + " must be finite and positive.");
                if (healthLevel > 0) Assert.GreaterOrEqual(resolved, previous, vehicle.vehicleName + " mass must not decrease.");
                previous = resolved;
            }
        }
    }

    /// <summary>Uses the existing local Physics2D fixture to compare actual player mass resolutions against PoliceHeavy.</summary>
    [Test]
    public void PlayerMassAuthoring_ActualVanMaximumTransfersMorePushMomentumThanBaseVanAndScooter() {
        VehicleData scooter = AssetDatabase.LoadAssetAtPath<VehicleData>(playerAssetPaths[0]);
        VehicleData van = AssetDatabase.LoadAssetAtPath<VehicleData>(playerAssetPaths[3]);
        NpcVehicleProfile policeHeavy = AssetDatabase.LoadAssetAtPath<NpcVehicleProfile>("Assets/ScriptableObjects/Police/PoliceHeavyData.asset");
        GameObject policePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Police/PoliceHeavy.prefab");
        Assert.IsNotNull(scooter);
        Assert.IsNotNull(van);
        Assert.IsNotNull(policeHeavy);
        Assert.IsNotNull(policePrefab);

        Vector2 scooterCollider = LoadPlayerColliderSize(scooter);
        Vector2 vanCollider = LoadPlayerColliderSize(van);
        Vector2 policeCollider = LoadColliderSize(policePrefab, "PoliceHeavy");

        PushResult scooterResult = RunPush(scooter.bodySettings.ResolveMass(0), scooterCollider, policeHeavy.baseMass, policeCollider);
        PushResult vanBaseResult = RunPush(van.bodySettings.ResolveMass(0), vanCollider, policeHeavy.baseMass, policeCollider);
        PushResult vanMaximumResult = RunPush(van.bodySettings.ResolveMass(van.maxHealthLevel), vanCollider, policeHeavy.baseMass, policeCollider);

        Assert.Greater(vanMaximumResult.targetDisplacement, vanBaseResult.targetDisplacement);
        Assert.Greater(vanMaximumResult.targetDisplacement, scooterResult.targetDisplacement);
        Assert.Greater(vanMaximumResult.targetMomentum, vanBaseResult.targetMomentum);
        Assert.Greater(vanMaximumResult.targetMomentum, scooterResult.targetMomentum);
    }

    static Vector2 LoadPlayerColliderSize(VehicleData vehicle) {
        return LoadColliderSize(vehicle.vehiclePrefab, vehicle.vehicleName);
    }

    static Vector2 LoadColliderSize(GameObject prefab, string label) {
        Collider2D collider = prefab.GetComponentInChildren<Collider2D>(true);
        Assert.IsNotNull(collider, label + " must retain an authored 2D collider for the comparison.");
        if (collider is BoxCollider2D box) {
            Vector3 scale = box.transform.lossyScale;
            return new Vector2(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y));
        }
        if (collider is CapsuleCollider2D capsule) {
            Vector3 scale = capsule.transform.lossyScale;
            return new Vector2(Mathf.Abs(capsule.size.x * scale.x), Mathf.Abs(capsule.size.y * scale.y));
        }
        if (collider is CircleCollider2D circle) {
            Vector3 scale = circle.transform.lossyScale;
            return new Vector2(Mathf.Abs(circle.radius * 2f * scale.x), Mathf.Abs(circle.radius * 2f * scale.y));
        }
        if (collider is PolygonCollider2D polygon) {
            // The isolated fixture is box-based, so this is the authored polygon's world-space
            // envelope; it makes no claim about exact polygon contact impulse geometry.
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int pathIndex = 0; pathIndex < polygon.pathCount; pathIndex++) {
                Vector2[] path = polygon.GetPath(pathIndex);
                foreach (Vector2 point in path) {
                    Vector3 worldPoint = polygon.transform.TransformPoint(point + polygon.offset);
                    min = Vector2.Min(min, worldPoint);
                    max = Vector2.Max(max, worldPoint);
                }
            }
            Vector2 size = max - min;
            Assert.Greater(size.x, 0f, label + " authored polygon width.");
            Assert.Greater(size.y, 0f, label + " authored polygon height.");
            return size;
        }
        Assert.Fail(label + " uses unsupported authored collider type " + collider.GetType().Name + ".");
        return Vector2.zero;
    }

    static PushResult RunPush(float pusherMass, Vector2 pusherSize, float targetMass, Vector2 targetSize) {
        using (var world = new TrafficPhysicsTestWorld()) {
            Rigidbody2D pusher = world.CreateBody("PlayerMassPusher", new Vector2(0f, -4f), pusherSize);
            Rigidbody2D target = world.CreateBody("PoliceHeavyTarget", Vector2.zero, targetSize);
            pusher.mass = pusherMass;
            target.mass = targetMass;
            pusher.linearVelocity = new Vector2(0f, 8f);
            target.linearVelocity = Vector2.zero;
            Vector2 targetStart = target.position;

            for (int step = 0; step < 40; step++) world.Step();

            return new PushResult(Vector2.Distance(targetStart, target.position), target.mass * target.linearVelocity.magnitude);
        }
    }

    /// <summary>Captures the target displacement and scalar momentum measured after one isolated push run.</summary>
    readonly struct PushResult {
        /// <summary>Target travel distance from its pre-impact position.</summary>
        public readonly float targetDisplacement;
        /// <summary>Target momentum magnitude, calculated as mass multiplied by velocity magnitude.</summary>
        public readonly float targetMomentum;

        /// <summary>Stores one isolated physics comparison result.</summary>
        public PushResult(float targetDisplacement, float targetMomentum) {
            this.targetDisplacement = targetDisplacement;
            this.targetMomentum = targetMomentum;
        }
    }
}
