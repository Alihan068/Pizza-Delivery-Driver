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

    /// <summary>Calculates the speed closing into a collision surface.</summary>
    /// <param name="relativeVelocity">Relative velocity at the collision.</param>
    /// <param name="contactNormal">The contact surface normal.</param>
    /// <returns>The absolute normal component of the relative velocity.</returns>
    public static float CalculateClosingSpeed(Vector2 relativeVelocity, Vector2 contactNormal) {
        if (contactNormal.sqrMagnitude <= 0.0001f) return relativeVelocity.magnitude;
        return Mathf.Abs(Vector2.Dot(relativeVelocity, contactNormal.normalized));
    }
}
