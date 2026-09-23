using NUnit.Framework;
using UnityEngine;

/// <summary>Pure coverage for the slip-angle drift grip model and the new optional slide-recovery tuning value.</summary>
public sealed class DriftModelTests {
    const float Hold = 30f, Exit = 8f, MinSpeed = 2f, DriftGrip = 1.3f, NormalGrip = 10f;

    static float Grip(float slip, float speed, float throttle, float throttleHold = 0.6f) =>
        VehicleDrivingMath.DriftTargetGrip(slip, Hold, Exit, speed, MinSpeed, throttle, throttleHold, DriftGrip, NormalGrip);

    /// <summary>A wide slide keeps drift grip; grip returns gradually as the slip angle closes.</summary>
    [Test]
    public void Grip_FollowsSlipAngleInsteadOfSwitching() {
        Assert.AreEqual(DriftGrip, Grip(45f, 6f, 0f), 0.001f, "beyond the hold angle the slide stays loose");
        Assert.AreEqual(DriftGrip, Grip(30f, 6f, 0f), 0.001f);
        float mid = Grip(19f, 6f, 0f);
        Assert.Greater(mid, DriftGrip);
        Assert.Less(mid, NormalGrip, "half-way through the angle range the grip is partial, not snapped");
        Assert.AreEqual(NormalGrip, Grip(8f, 6f, 0f), 0.001f, "at the exit angle full grip is the target");
        Assert.Greater(Grip(12f, 6f, 0f), Grip(25f, 6f, 0f), "grip rises monotonically as the angle closes");
    }

    /// <summary>Held throttle keeps the rear loose; lifting off lets it bite.</summary>
    [Test]
    public void Throttle_HoldsTheSlideAndLiftingOffRegrips() {
        float lifted = Grip(15f, 6f, 0f);
        float floored = Grip(15f, 6f, 1f);
        Assert.Less(floored, lifted, "throttle keeps grip lower");
        Assert.AreEqual(DriftGrip, Grip(15f, 6f, 1f, 1f), 0.001f, "full hold at full throttle stops recovery entirely");
        Assert.AreEqual(lifted, Grip(15f, 6f, 1f, 0f), 0.001f, "zero hold means throttle has no effect");
    }

    /// <summary>Bleeding off speed recovers the slide even at a wide angle.</summary>
    [Test]
    public void Speed_BleedingOffRecoversTheSlide() {
        Assert.AreEqual(NormalGrip, Grip(45f, 1.5f, 0f), 0.001f, "below the minimum drift speed the car regrips");
        Assert.AreEqual(DriftGrip, Grip(45f, 4.5f, 0f), 0.001f, "at twice the minimum speed a wide slide stays loose");
        Assert.AreEqual(Grip(45f, 3f, 0f), Mathf.Lerp(DriftGrip, NormalGrip, 0.5f), 0.01f);
    }

    /// <summary>After the handbrake is released a wide, throttled slide still regains full grip within the release time.</summary>
    [Test]
    public void Release_TimeLimitEndsAWideThrottledSlide() {
        float Released(float seconds) => VehicleDrivingMath.DriftTargetGrip(45f, Hold, Exit, 6f, MinSpeed, 1f, 0.6f,
            DriftGrip, NormalGrip, seconds, 0.5f);
        Assert.AreEqual(DriftGrip, Released(0f), 0.001f, "right after release the slide is still loose");
        Assert.AreEqual(Mathf.Lerp(DriftGrip, NormalGrip, 0.5f), Released(0.25f), 0.01f, "half-way through the release time");
        Assert.AreEqual(NormalGrip, Released(0.5f), 0.001f, "full grip by the release time even at 45 degrees and full throttle");
        Assert.AreEqual(DriftGrip, VehicleDrivingMath.DriftTargetGrip(45f, Hold, Exit, 6f, MinSpeed, 1f, 0.6f,
            DriftGrip, NormalGrip, 5f, 0f), 0.001f, "a zero release time disables the limit");
    }

    /// <summary>Saves written before the slide-recovery field existed keep the vehicle default; a set value is clamped.</summary>
    [Test]
    public void SlideRecoveryTuning_OldSavesUseTheVehicleDefault() {
        var old = JsonUtility.FromJson<VehicleSaveData>("{\"hasCustomTuning\":true,\"tunedDriftGrip\":2.0}");
        Assert.LessOrEqual(old.tunedGripRecoverTime, 0f, "JsonUtility reads a missing field as 0, which counts as never set");
        Assert.AreEqual(0.7f, VehicleTuningRules.ResolveOptionalPlayerValue(true, true, old.tunedGripRecoverTime, 0.7f, 0.2f, 1.5f), 0.0001f);
        Assert.AreEqual(1.1f, VehicleTuningRules.ResolveOptionalPlayerValue(true, true, 1.1f, 0.7f, 0.2f, 1.5f), 0.0001f);
        Assert.AreEqual(1.5f, VehicleTuningRules.ResolveOptionalPlayerValue(true, true, 9f, 0.7f, 0.2f, 1.5f), 0.0001f, "clamped to the vehicle envelope");
        Assert.AreEqual(0.7f, VehicleTuningRules.ResolveOptionalPlayerValue(false, true, 1.1f, 0.7f, 0.2f, 1.5f), 0.0001f, "Advanced Tuning off uses the default");
        Assert.AreEqual(0.7f, VehicleTuningRules.ResolveOptionalPlayerValue(true, true, float.NaN, 0.7f, 0.2f, 1.5f), 0.0001f);
    }
}
