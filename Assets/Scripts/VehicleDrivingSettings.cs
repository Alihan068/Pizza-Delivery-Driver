using System;
using UnityEngine;

/// <summary>
/// Authored tuning for one vehicle's arcade driving model.
/// </summary>
/// <remarks>
/// This is embedded in <see cref="VehicleData"/> so a vehicle's driving behavior travels with its
/// content asset. It contains no runtime state and must never be modified during a shift.
/// </remarks>
[Serializable]
public class VehicleDrivingSettings {

    [Header("Physics")]
    /// <summary>Light Rigidbody2D damping used alongside the custom forward and lateral motor.</summary>
    [Min(0f)] public float linearDamping = 0.15f;

    [Header("Longitudinal movement")]
    [Min(0.1f)] public float accelerationTime = 1.2f;
    [Min(0.1f)] public float brakeTime = 0.65f;
    [Min(0.1f)] public float coastTime = 2.5f;
    [Range(0.05f, 0.8f)] public float reverseSpeedFraction = 0.35f;
    [Min(0f)] public float reverseEntrySpeed = 0.15f;
    [Min(0f)] public float reverseEntryDelay = 0.25f;

    [Header("Steering")]
    [Min(0f)] public float steeringGain = 0.55f;
    [Min(0.02f)] public float steeringResponseTime = 0.15f;
    [Range(0f, 1f)] public float steeringHighSpeedScale = 0.6f;

    [Header("Grip and handbrake")]
    [Min(0f)] public float normalGrip = 10f;
    [Min(0f)] public float driftGrip = 1.3f;
    [Min(0f)] public float gripEnterTime = 0.08f;
    [Min(0f)] public float gripRecoverTime = 0.7f;
    [Min(0f)] public float handbrakeDeceleration = 2f;
    [Range(0f, 0.5f)] public float handlingRecoveryAssist = 0.25f;
    /// <summary>Steering multiplier while the handbrake is held during a drift attempt.</summary>
    [Min(1f)] public float driftSteeringMultiplier = 1.35f;

    [Header("Player Tuning Bounds")]
    /// <summary>Lowest drift grip value available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerDriftGripMin = 0.8f;

    /// <summary>Highest drift grip value available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerDriftGripMax = 3f;

    /// <summary>Lowest drift steering multiplier available to the player in Advanced Tuning.</summary>
    [Min(1f)] public float playerDriftSteeringMultiplierMin = 1f;

    /// <summary>Highest drift steering multiplier available to the player in Advanced Tuning.</summary>
    [Min(1f)] public float playerDriftSteeringMultiplierMax = 2f;

    /// <summary>Fastest grip-entry response time available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerGripEnterTimeMin = 0f;

    /// <summary>Slowest grip-entry response time available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerGripEnterTimeMax = 0.5f;
    /// <summary>Shortest grip-recovery time (snappiest slide exit) available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerGripRecoverTimeMin = 0.2f;
    /// <summary>Longest grip-recovery time (longest-lasting slide) available to the player in Advanced Tuning.</summary>
    [Min(0f)] public float playerGripRecoverTimeMax = 1.5f;

    [Header("Drift detection")]
    [Range(0f, 1f)] public float driftMinimumSpeedFraction = 0.25f;
    [Min(0f)] public float driftEnterAngle = 12f;
    [Min(0f)] public float driftExitAngle = 8f;
    [Range(0f, 180f)] public float driftMaximumAngle = 75f;
    [Min(0f)] public float driftEnterDwell = 0f;
    [Min(0f)] public float driftExitDwell = 0.25f;
    /// <summary>Requires the authored handbrake command before normal cornering can enter drift.</summary>
    public bool driftRequiresHandbrake = true;
    /// <summary>Slip angle (degrees) at or above which a drifting car keeps its drift grip after the handbrake is released; grip returns as the angle closes toward the exit angle.</summary>
    [Range(1f, 90f)] public float driftHoldAngle = 30f;
    /// <summary>How strongly held throttle keeps a released drift loose (0 = throttle has no effect, 1 = full throttle stops grip from returning).</summary>
    [Range(0f, 1f)] public float driftThrottleHold = 0.6f;
    /// <summary>Seconds after the handbrake is released by which a drift fully regains grip, whatever the angle or throttle (0 disables the limit).</summary>
    [Min(0f)] public float driftReleaseRecoverySeconds = 0.5f;
    [Header("Momentum boost")]
    /// <summary>Seconds of straight, full-commitment driving before top speed starts to climb.</summary>
    [Min(0f)] public float momentumWarmupSeconds = 0f;
    /// <summary>Highest top-speed multiplier the momentum boost can reach; 1 disables it.</summary>
    [Min(1f)] public float momentumMaximumMultiplier = 1.5f;
    /// <summary>Seconds to climb from normal to maximum top speed once warmed up.</summary>
    [Min(0.01f)] public float momentumRiseSeconds = 4f;
    /// <summary>Seconds to fall from maximum back to normal after braking, drifting, lifting off or hard steering.</summary>
    [Min(0.01f)] public float momentumFallSeconds = 1.5f;
    /// <summary>Seconds to fall from maximum back to normal after a crash.</summary>
    [Min(0.01f)] public float momentumCrashFallSeconds = 0.5f;
    /// <summary>Minimum throttle input that counts as keeping the foot down.</summary>
    [Range(0f, 1f)] public float momentumMinimumThrottle = 0.5f;
    /// <summary>Largest steering input that still counts as driving straight.</summary>
    [Range(0f, 1f)] public float momentumStraightSteeringLimit = 0.5f;

    /// <summary>Returns a safe copy of these settings for runtime use.</summary>
    /// <returns>A detached settings object with the same authored values.</returns>
    public VehicleDrivingSettings Clone() {
        return new VehicleDrivingSettings {
            linearDamping = linearDamping,
            accelerationTime = accelerationTime,
            brakeTime = brakeTime,
            coastTime = coastTime,
            reverseSpeedFraction = reverseSpeedFraction,
            reverseEntrySpeed = reverseEntrySpeed,
            reverseEntryDelay = reverseEntryDelay,
            steeringGain = steeringGain,
            steeringResponseTime = steeringResponseTime,
            steeringHighSpeedScale = steeringHighSpeedScale,
            normalGrip = normalGrip,
            driftGrip = driftGrip,
            gripEnterTime = gripEnterTime,
            gripRecoverTime = gripRecoverTime,
            handbrakeDeceleration = handbrakeDeceleration,
            handlingRecoveryAssist = handlingRecoveryAssist,
            driftSteeringMultiplier = driftSteeringMultiplier,
            playerDriftGripMin = playerDriftGripMin,
            playerDriftGripMax = playerDriftGripMax,
            playerDriftSteeringMultiplierMin = playerDriftSteeringMultiplierMin,
            playerDriftSteeringMultiplierMax = playerDriftSteeringMultiplierMax,
            playerGripEnterTimeMin = playerGripEnterTimeMin,
            playerGripEnterTimeMax = playerGripEnterTimeMax,
            playerGripRecoverTimeMin = playerGripRecoverTimeMin,
            playerGripRecoverTimeMax = playerGripRecoverTimeMax,
            driftMinimumSpeedFraction = driftMinimumSpeedFraction,
            driftEnterAngle = driftEnterAngle,
            driftExitAngle = driftExitAngle,
            driftMaximumAngle = driftMaximumAngle,
            driftEnterDwell = driftEnterDwell,
            driftExitDwell = driftExitDwell,
            driftRequiresHandbrake = driftRequiresHandbrake,
            driftHoldAngle = driftHoldAngle,
            driftThrottleHold = driftThrottleHold,
            driftReleaseRecoverySeconds = driftReleaseRecoverySeconds,            momentumWarmupSeconds = momentumWarmupSeconds,
            momentumMaximumMultiplier = momentumMaximumMultiplier,
            momentumRiseSeconds = momentumRiseSeconds,
            momentumFallSeconds = momentumFallSeconds,
            momentumCrashFallSeconds = momentumCrashFallSeconds,
            momentumMinimumThrottle = momentumMinimumThrottle,
            momentumStraightSteeringLimit = momentumStraightSteeringLimit
        };
    }
}
