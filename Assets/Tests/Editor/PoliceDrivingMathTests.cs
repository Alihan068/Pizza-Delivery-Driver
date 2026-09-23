using NUnit.Framework;
using UnityEngine;

/// <summary>Deterministic coverage for bounded police driving settings and pure driving math.</summary>
public sealed class PoliceDrivingMathTests {
    /// <summary>Confirms JSON persistence and cloning preserve values without sharing object identity.</summary>
    [Test]
    public void Settings_JsonRoundTripAndClonePreserveValues() {
        var original = new PoliceDrivingSettings();
        original.lookaheadMax = 9f;
        original.bindWork = 1234;
        original.comfort = 0.6f;
        string json = JsonUtility.ToJson(original);
        var roundTrip = JsonUtility.FromJson<PoliceDrivingSettings>(json);
        var clone = original.Clone();
        Assert.IsTrue(original.IsValid(out string originalReason), originalReason);
        Assert.IsTrue(roundTrip.IsValid(out string roundTripReason), roundTripReason);
        Assert.IsTrue(clone.IsValid(out string cloneReason), cloneReason);
        Assert.AreEqual(9f, roundTrip.lookaheadMax, 0.0001f);
        Assert.AreEqual(1234, roundTrip.bindWork);
        Assert.AreEqual(0.6f, roundTrip.comfort, 0.0001f);
        Assert.AreEqual(original.lookaheadMax, clone.lookaheadMax, 0.0001f);
        Assert.AreEqual(original.bindWork, clone.bindWork);
        Assert.AreEqual(original.comfort, clone.comfort, 0.0001f);
        Assert.AreNotSame(original, clone);
        clone.comfort = 0.5f;
        Assert.AreEqual(0.6f, original.comfort, 0.0001f);
    }

    /// <summary>Confirms invalid settings fail closed for ordering, comfort, angles, and work bounds.</summary>
    [Test]
    public void Settings_InvalidValuesAreRejected() {
        var settings = new PoliceDrivingSettings { lookaheadMin = 9f, lookaheadMax = 2f };
        Assert.IsFalse(settings.IsValid(out string reason));
        Assert.IsNotEmpty(reason);
        settings = new PoliceDrivingSettings { comfort = 0f };
        Assert.IsFalse(settings.IsValid(out _));
        settings = new PoliceDrivingSettings { fullSlowdownAngle = 181f };
        Assert.IsFalse(settings.IsValid(out _));
        settings = new PoliceDrivingSettings { sensorBuffer = 0 };
        Assert.IsFalse(settings.IsValid(out _));
        settings = new PoliceDrivingSettings { acquisitionTolerance = float.NaN };
        Assert.IsFalse(settings.IsValid(out _));
    }

    /// <summary>Confirms force-limited nominal and crash braking values use the authored comfort factor.</summary>
    [Test]
    public void Math_BrakingUsesForceLimitCrashMultiplierAndComfort() {
        var motor = new NpcMotorSettings { brakeDeceleration = 8f, maxBrakeForce = 16f };
        Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, 4f, false, 0.35f, 0.7f, out float nominal));
        Assert.AreEqual(2.8f, nominal, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, 4f, true, 0.35f, 0.7f, out float crash));
        Assert.AreEqual(0.98f, crash, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, 8f, false, 1f, 0.7f, out float heavier));
        Assert.AreEqual(1.4f, heavier, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryBrakeDeceleration(motor, 8f, true, 0.35f, 0.7f, out float heavierCrash));
        Assert.AreEqual(0.49f, heavierCrash, 0.0001f);
    }

    /// <summary>Confirms sweep distance uses stopping, reaction, and gap terms and fails when its bound is short.</summary>
    [Test]
    public void Math_SweepDistanceUsesFullEnvelopeWithoutTruncation() {
        Assert.IsTrue(PoliceDrivingMath.TrySweepDistance(4f, 2f, 0.3f, 1.2f, 7f, out float range));
        Assert.AreEqual(6.4f, range, 0.0001f);
        Assert.IsFalse(PoliceDrivingMath.TrySweepDistance(4f, 2f, 0.3f, 1.2f, 6f, out range));
        Assert.AreEqual(0f, range, 0f);
        Assert.IsFalse(PoliceDrivingMath.TrySweepDistance(float.MaxValue, 1f, 0f, 1f, float.MaxValue, out range));
    }

    /// <summary>Confirms allowed speed follows the finite square-root braking relation.</summary>
    [Test]
    public void Math_AllowedSpeedUsesFiniteBrakingDistance() {
        Assert.IsTrue(PoliceDrivingMath.TryAllowedSpeed(2f, 6f, 2f, out float speed));
        Assert.AreEqual(Mathf.Sqrt(28f), speed, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryAllowedSpeed(1f, 1e30f, 1e10f, out speed));
        Assert.AreEqual((float)System.Math.Sqrt(2e40), speed, 1e14f);
        Assert.IsFalse(PoliceDrivingMath.TryAllowedSpeed(float.MaxValue, float.MaxValue, float.MaxValue, out speed));
        Assert.AreEqual(0f, speed, 0f);
    }

    /// <summary>Confirms turning rejects infeasible curvature and caps feasible yaw by the motor rate.</summary>
    [Test]
    public void Math_SteeringEnforcesRadiusAndYawRate() {
        var motor = new NpcMotorSettings { maxSpeed = 10f, turnRate = 90f, minimumTurningRadius = 1f };
        Assert.IsTrue(PoliceDrivingMath.TrySteering(Vector2.up, new Vector2(1f, 1f), 2f, motor, out float steering, out float cap));
        Assert.AreEqual(Mathf.PI / 2f, cap, 0.0001f);
        Assert.AreEqual(-1f, steering, 0.0001f);
        motor.minimumTurningRadius = 4f;
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.up, new Vector2(1f, 1f), 2f, motor, out steering, out cap));
        Assert.AreEqual(0f, steering, 0f);
        Assert.AreEqual(0f, cap, 0f);
    }

    /// <summary>Confirms straight, stationary, behind, lateral, and nonfinite steering cases fail or remain bounded.</summary>
    [Test]
    public void Math_SteeringHandlesStraightStationaryAndInvalidGeometry() {
        var motor = new NpcMotorSettings { maxSpeed = 10f, turnRate = 90f, minimumTurningRadius = 1f };
        Assert.IsTrue(PoliceDrivingMath.TrySteering(Vector2.up, Vector2.up * 3f, 0f, motor, out float stationarySteering, out float stationaryCap));
        Assert.AreEqual(0f, stationarySteering, 0f);
        Assert.AreEqual(10f, stationaryCap, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TrySteering(Vector2.up, Vector2.up * 3f, 2f, motor, out float straightSteering, out float straightCap));
        Assert.AreEqual(0f, straightSteering, 0f);
        Assert.AreEqual(10f, straightCap, 0.0001f);
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.up, Vector2.down, 2f, motor, out _, out _));
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.zero, Vector2.up, 2f, motor, out _, out _));
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.up, new Vector2(float.MaxValue, float.MaxValue), 2f, motor, out float invalidSteering, out float invalidCap));
        Assert.AreEqual(0f, invalidSteering, 0f);
        Assert.AreEqual(0f, invalidCap, 0f);
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.up, new Vector2(float.NaN, 1f), 2f, motor, out _, out _));
    }

    /// <summary>Confirms Quaternion quarter-turn roundoff is straight while a real small bend remains steering and a rear target is rejected.</summary>
    [Test]
    public void Math_SteeringTreatsQuarterTurnRoundoffAsStraightWithoutHidingRealBends() {
        var motor = new NpcMotorSettings { maxSpeed = 10f, turnRate = 90f, minimumTurningRadius = 1f };
        Vector3 positiveQuarterTurn = Quaternion.AngleAxis(-90f, Vector3.forward) * Vector3.up;
        Vector3 negativeQuarterTurn = Quaternion.AngleAxis(90f, Vector3.forward) * Vector3.up;

        Assert.IsTrue(PoliceDrivingMath.TrySteering(new Vector2(positiveQuarterTurn.x, positiveQuarterTurn.y), Vector2.right,
            3f, motor, out float positiveSteering, out _));
        Assert.AreEqual(0f, positiveSteering, 0f);
        Assert.IsTrue(PoliceDrivingMath.TrySteering(new Vector2(negativeQuarterTurn.x, negativeQuarterTurn.y), Vector2.left,
            3f, motor, out float negativeSteering, out _));
        Assert.AreEqual(0f, negativeSteering, 0f);

        Assert.IsTrue(PoliceDrivingMath.TrySteering(Vector2.up, new Vector2(0.01f, 1f), 3f, motor,
            out float smallBendSteering, out _));
        Assert.AreNotEqual(0f, smallBendSteering);
        Assert.IsFalse(PoliceDrivingMath.TrySteering(Vector2.up, Vector2.down, 3f, motor, out _, out _));
    }

    /// <summary>Confirms corner interpolation never raises the cruise cap and reaches the authored corner speed.</summary>
    [Test]
    public void Math_CornerTargetNeverRaisesSpeedCap() {
        Assert.IsTrue(PoliceDrivingMath.TryCornerTargetSpeed(5f, 2f, 45f, 90f, out float halfAngle));
        Assert.AreEqual(3.5f, halfAngle, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryCornerTargetSpeed(5f, 2f, 90f, 90f, out float fullAngle));
        Assert.AreEqual(2f, fullAngle, 0.0001f);
        Assert.IsTrue(PoliceDrivingMath.TryCornerTargetSpeed(5f, 8f, 90f, 90f, out float largerCorner));
        Assert.AreEqual(5f, largerCorner, 0.0001f);
    }
}
