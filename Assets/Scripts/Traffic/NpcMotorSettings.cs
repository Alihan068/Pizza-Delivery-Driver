using UnityEngine;

/// <summary>Authored motor tuning for one <see cref="NpcVehicleProfile"/>. S04 is what actually drives a Rigidbody2D from this data.</summary>
[System.Serializable]
public class NpcMotorSettings {
    [Tooltip("Speed this vehicle tries to hold when the road ahead is clear.")]
    public float cruiseSpeed = 6f;

    [Tooltip("Absolute top speed regardless of throttle.")]
    public float maxSpeed = 9f;

    [Tooltip("Top speed while reversing.")]
    public float reverseSpeed = 3f;

    [Tooltip("Forward acceleration.")]
    public float acceleration = 4f;

    [Tooltip("Deceleration while braking.")]
    public float brakeDeceleration = 8f;

    [Tooltip("Maximum forward force the motor may apply.")]
    public float maxEngineForce = 12f;

    [Tooltip("Maximum braking force the motor may apply.")]
    public float maxBrakeForce = 16f;

    [Tooltip("Maximum turn rate, degrees per second.")]
    public float turnRate = 120f;

    [Tooltip("Smallest radius this vehicle can turn within.")]
    public float minimumTurningRadius = 3f;

    [Tooltip("Lateral grip retained while turning; higher resists sliding more.")]
    [Range(0f, 1f)] public float lateralGrip = 0.9f;

    [Tooltip("Seconds between forward-obstacle sensor checks.")]
    public float sensorInterval = 0.2f;

    [Tooltip("Seconds this vehicle takes to react to a new hazard.")]
    public float reactionTime = 0.3f;

    [Tooltip("Minimum following gap this vehicle keeps from the vehicle ahead.")]
    public float minimumGap = 2f;
}
