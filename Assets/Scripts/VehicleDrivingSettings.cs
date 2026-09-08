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
    [Min(0f)] public float driftGrip = 1.7f;
    [Min(0f)] public float gripEnterTime = 0.08f;
    [Min(0f)] public float gripRecoverTime = 0.22f;
    [Min(0f)] public float handbrakeDeceleration = 2f;
    [Range(0f, 0.5f)] public float handlingRecoveryAssist = 0.25f;
    /// <summary>Steering multiplier while the handbrake is held during a drift attempt.</summary>
    [Min(1f)] public float driftSteeringMultiplier = 1.35f;

    [Header("Drift detection")]
    [Range(0f, 1f)] public float driftMinimumSpeedFraction = 0.25f;
    [Min(0f)] public float driftEnterAngle = 12f;
    [Min(0f)] public float driftExitAngle = 8f;
    [Range(0f, 180f)] public float driftMaximumAngle = 75f;
    [Min(0f)] public float driftEnterDwell = 0f;
    [Min(0f)] public float driftExitDwell = 0.25f;
    /// <summary>Requires the authored handbrake command before normal cornering can enter drift.</summary>
    public bool driftRequiresHandbrake = true;

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
            driftMinimumSpeedFraction = driftMinimumSpeedFraction,
            driftEnterAngle = driftEnterAngle,
            driftExitAngle = driftExitAngle,
            driftMaximumAngle = driftMaximumAngle,
            driftEnterDwell = driftEnterDwell,
            driftExitDwell = driftExitDwell,
            driftRequiresHandbrake = driftRequiresHandbrake
        };
    }
}
