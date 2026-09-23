using UnityEngine;

/// <summary>Allocation-free pure calculations shared by the vehicle motor and editor tests.</summary>
public static class VehicleDrivingMath {

    /// <summary>Converts a time-to-speed target into acceleration.</summary>
    /// <param name="maximumSpeed">The target speed in world units per second.</param>
    /// <param name="time">The time used to reach the target.</param>
    /// <returns>A non-negative acceleration value.</returns>
    public static float AccelerationForTime(float maximumSpeed, float time) {
        return Mathf.Max(0f, maximumSpeed) / Mathf.Max(0.01f, time);
    }

    /// <summary>Applies exponential damping without depending on the render frame rate.</summary>
    /// <param name="value">Current scalar value.</param>
    /// <param name="rate">Damping rate in inverse seconds.</param>
    /// <param name="deltaTime">Physics step duration.</param>
    /// <returns>The damped value.</returns>
    public static float Damp(float value, float rate, float deltaTime) {
        return value * Mathf.Exp(-Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
    }

    /// <summary>Returns the steering response scale at a normalized speed.</summary>
    /// <param name="normalizedSpeed">Current speed divided by the effective maximum.</param>
    /// <param name="highSpeedScale">Scale used at maximum speed.</param>
    /// <returns>A scale between the authored high-speed value and one.</returns>
    public static float SteeringSpeedScale(float normalizedSpeed, float highSpeedScale) {
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedSpeed));
        return Mathf.Lerp(1f, Mathf.Clamp01(highSpeedScale), t);
    }

    /// <summary>Returns the highest valid speed component without changing lateral momentum.</summary>
    /// <param name="forwardSpeed">Current signed forward speed.</param>
    /// <param name="maximumForwardSpeed">Positive forward speed limit.</param>
    /// <param name="maximumReverseSpeed">Positive reverse speed limit.</param>
    /// <returns>A clamped signed forward speed.</returns>
    public static float ClampForwardSpeed(float forwardSpeed, float maximumForwardSpeed, float maximumReverseSpeed) {
        return Mathf.Clamp(forwardSpeed, -Mathf.Max(0f, maximumReverseSpeed), Mathf.Max(0f, maximumForwardSpeed));
    }

    /// <summary>
    /// Raw (pre-armor) collision damage. Contacts slower than the dead zone deal nothing; between the dead
    /// zone and the full-damage speed the damage fades in from zero so there is no jump at the threshold;
    /// from the full-damage speed up it is exactly the original base + factor * speed^exponent.
    /// </summary>
    /// <param name="impactSpeed">Closing speed into the contact surface.</param>
    /// <param name="deadZoneSpeed">Closing speed at or below which a contact deals no damage.</param>
    /// <param name="baseFullSpeed">Closing speed from which the full original damage applies.</param>
    /// <param name="damageBase">Flat damage of a real crash.</param>
    /// <param name="damageFactor">Multiplier of the speed-scaled term.</param>
    /// <param name="damageExponent">Exponent of the speed-scaled term.</param>
    /// <returns>Damage before armor; zero for a contact inside the dead zone.</returns>
    public static float CollisionDamage(float impactSpeed, float deadZoneSpeed, float baseFullSpeed,
        float damageBase, float damageFactor, float damageExponent) {
        if (float.IsNaN(impactSpeed) || impactSpeed <= Mathf.Max(0f, deadZoneSpeed)) return 0f;
        float weight = baseFullSpeed > deadZoneSpeed ? Mathf.InverseLerp(deadZoneSpeed, baseFullSpeed, impactSpeed) : 1f;
        return weight * (damageBase + damageFactor * Mathf.Pow(impactSpeed, damageExponent));
    }

    /// <summary>Calculates the speed closing into a collision surface.</summary>
    /// <param name="relativeVelocity">Relative velocity at the collision.</param>
    /// <param name="contactNormal">The contact surface normal.</param>
    /// <returns>The absolute normal component of the relative velocity.</returns>
    public static float CalculateClosingSpeed(Vector2 relativeVelocity, Vector2 contactNormal) {
        if (contactNormal.sqrMagnitude <= 0.0001f) return relativeVelocity.magnitude;
        return Mathf.Abs(Vector2.Dot(relativeVelocity, contactNormal.normalized));
    }

    /// <summary>
    /// Target lateral grip for a car that is drifting after the handbrake was released: a slip-angle
    /// tyre model instead of an on/off switch. A wide slide stays at drift grip and only regains grip
    /// as the slip angle closes toward the exit angle or the speed bleeds off, so the slide ends on
    /// its own and can be held with skill. Held throttle keeps the rear loose; lifting off lets it bite.
    /// </summary>
    /// <param name="slipAngle">Current absolute slip angle in degrees.</param>
    /// <param name="holdAngle">Slip angle at or above which drift grip is fully kept.</param>
    /// <param name="exitAngle">Slip angle at or below which full normal grip is the target.</param>
    /// <param name="speed">Absolute forward speed.</param>
    /// <param name="minimumDriftSpeed">Speed below which the slide is fully recovering.</param>
    /// <param name="throttle">Throttle input 0..1.</param>
    /// <param name="throttleHold">How strongly throttle suppresses recovery, 0..1.</param>
    /// <param name="driftGrip">Lowest grip while sliding.</param>
    /// <param name="normalGrip">Full grip when not sliding.</param>
    /// <param name="releasedSeconds">Seconds since the handbrake was released during this drift.</param>
    /// <param name="releaseRecoverySeconds">Seconds after release by which full grip is the target regardless of angle or throttle; zero or less disables the limit.</param>
    /// <returns>Grip between driftGrip and normalGrip.</returns>
    public static float DriftTargetGrip(float slipAngle, float holdAngle, float exitAngle, float speed,
        float minimumDriftSpeed, float throttle, float throttleHold, float driftGrip, float normalGrip,
        float releasedSeconds = 0f, float releaseRecoverySeconds = 0f) {
        float hold = Mathf.Max(holdAngle, exitAngle + 0.01f);
        float angleRecovery = Mathf.InverseLerp(hold, exitAngle, slipAngle);
        float slow = Mathf.Max(0.01f, minimumDriftSpeed);
        float speedRecovery = 1f - Mathf.InverseLerp(slow, slow * 2f, Mathf.Abs(speed));
        float recovery = Mathf.Max(angleRecovery, speedRecovery);
        recovery *= 1f - Mathf.Clamp01(throttleHold) * Mathf.Clamp01(throttle);
        // A wide slide keeps itself wide, so without a time limit it would only end once the car slowed down.
        if (releaseRecoverySeconds > 0f)
            recovery = Mathf.Max(recovery, Mathf.Clamp01(releasedSeconds / releaseRecoverySeconds));
        return Mathf.Lerp(driftGrip, normalGrip, Mathf.Clamp01(recovery));
    }
}
