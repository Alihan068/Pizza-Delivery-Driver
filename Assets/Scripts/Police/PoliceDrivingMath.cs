using UnityEngine;

/// <summary>Pure bounded calculations shared by police braking, steering, cornering, and sensing code.</summary>
public static class PoliceDrivingMath {
    /// <summary>Calculates force-limited braking with an optional crash envelope and comfort factor.</summary>
    /// <param name="motorSettings">Motor force and nominal braking settings.</param>
    /// <param name="mass">Positive rigidbody mass.</param>
    /// <param name="crashMode">Whether the crash force multiplier applies.</param>
    /// <param name="crashMultiplier">Positive crash-mode force multiplier.</param>
    /// <param name="comfort">Comfort factor in the exclusive-zero to one range.</param>
    /// <param name="deceleration">Calculated positive deceleration, or zero on failure.</param>
    /// <returns>True when the finite positive braking result was calculated.</returns>
    public static bool TryBrakeDeceleration(NpcMotorSettings motorSettings, float mass, bool crashMode,
        float crashMultiplier, float comfort, out float deceleration) {
        deceleration = 0f;
        if (motorSettings == null || !FinitePositive(mass) || !FinitePositive(motorSettings.brakeDeceleration) ||
            !FinitePositive(motorSettings.maxBrakeForce) || !FinitePositive(crashMultiplier) ||
            !Finite(comfort) || comfort <= 0f || comfort > 1f) return false;
        float result = Mathf.Min(motorSettings.brakeDeceleration, motorSettings.maxBrakeForce / mass) *
            (crashMode ? crashMultiplier : 1f) * comfort;
        if (!FinitePositive(result)) return false;
        deceleration = result;
        return true;
    }

    /// <summary>Calculates stopping distance plus reaction and safety gap without truncating overflow.</summary>
    /// <param name="speed">Nonnegative forward speed.</param>
    /// <param name="deceleration">Positive available deceleration.</param>
    /// <param name="reactionTime">Nonnegative reaction time.</param>
    /// <param name="gap">Positive safety gap.</param>
    /// <param name="maximumDistance">Positive caller-authorized maximum range.</param>
    /// <param name="range">Required finite range, or zero on failure.</param>
    /// <returns>True when the required range fits within the caller bound.</returns>
    public static bool TrySweepDistance(float speed, float deceleration, float reactionTime, float gap,
        float maximumDistance, out float range) {
        range = 0f;
        if (!FiniteNonnegative(speed) || !FinitePositive(deceleration) || !FiniteNonnegative(reactionTime) ||
            !FinitePositive(gap) || !FinitePositive(maximumDistance)) return false;
        double result = (double)speed * speed / (2d * deceleration) + (double)speed * reactionTime + gap;
        if (double.IsNaN(result) || double.IsInfinity(result) || result > maximumDistance || result > float.MaxValue) return false;
        range = (float)result;
        return Finite(range);
    }

    /// <summary>Calculates the speed permitted after a bounded braking distance.</summary>
    /// <param name="targetSpeed">Nonnegative target speed at the endpoint.</param>
    /// <param name="distance">Nonnegative distance available for braking.</param>
    /// <param name="deceleration">Positive available deceleration.</param>
    /// <param name="speed">Finite permitted speed, or zero on failure.</param>
    /// <returns>True when the finite square-root result was calculated.</returns>
    public static bool TryAllowedSpeed(float targetSpeed, float distance, float deceleration, out float speed) {
        speed = 0f;
        if (!FiniteNonnegative(targetSpeed) || !FiniteNonnegative(distance) || !FinitePositive(deceleration)) return false;
        double value = (double)targetSpeed * targetSpeed + 2d * deceleration * distance;
        if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue * (double)float.MaxValue) return false;
        double root = System.Math.Sqrt(value);
        if (double.IsNaN(root) || double.IsInfinity(root) || root > float.MaxValue) return false;
        float result = (float)root;
        if (!FiniteNonnegative(result)) return false;
        speed = result;
        return true;
    }

    /// <summary>Calculates curvature, yaw-limited steering, and speed cap for a forward aim delta.</summary>
    /// <param name="forward">Finite nonzero vehicle-forward vector.</param>
    /// <param name="aimDelta">Finite nonzero vector toward the forward target.</param>
    /// <param name="forwardSpeed">Nonnegative current forward speed.</param>
    /// <param name="motorSettings">Motor speed, yaw, and turning-radius limits.</param>
    /// <param name="steering">Signed steering command, or zero on failure.</param>
    /// <param name="speedCap">Finite speed cap, or zero on failure.</param>
    /// <remarks>Unit-direction differences within four binary32 roundoff units are treated as exactly straight to remove Quaternion representation noise only; gameplay angle and clearance tolerances remain unchanged.</remarks>
    /// <returns>True when the target lies ahead and its curvature is physically feasible.</returns>
    public static bool TrySteering(Vector2 forward, Vector2 aimDelta, float forwardSpeed, NpcMotorSettings motorSettings,
        out float steering, out float speedCap) {
        steering = 0f;
        speedCap = 0f;
        if (motorSettings == null || !Finite(forward) || !Finite(aimDelta) || !FiniteNonnegative(forwardSpeed) ||
            !FinitePositive(forward.sqrMagnitude) || !FinitePositive(aimDelta.sqrMagnitude) || !FinitePositive(motorSettings.maxSpeed) ||
            !FinitePositive(motorSettings.turnRate) || !FinitePositive(motorSettings.minimumTurningRadius)) return false;
        Vector2 normalizedForward = forward.normalized;
        Vector2 normalizedAim = aimDelta.normalized;
        if (!FinitePositive(normalizedForward.sqrMagnitude) || !FinitePositive(normalizedAim.sqrMagnitude) || Vector2.Dot(normalizedForward, aimDelta) <= 0f) return false;
        double unitDirectionDeltaX = (double)normalizedForward.x - normalizedAim.x;
        double unitDirectionDeltaY = (double)normalizedForward.y - normalizedAim.y;
        double unitDirectionDeltaSquared = unitDirectionDeltaX * unitDirectionDeltaX + unitDirectionDeltaY * unitDirectionDeltaY;
        double unitRoundoff = 4d / 8388608d;
        float curvature = unitDirectionDeltaSquared <= unitRoundoff * unitRoundoff ? 0f :
            2f * Cross(normalizedForward, aimDelta) / aimDelta.sqrMagnitude;
        if (!Finite(curvature) || Mathf.Abs(curvature) > 1f / motorSettings.minimumTurningRadius) return false;
        float turnRateRadians = motorSettings.turnRate * Mathf.Deg2Rad;
        if (!FinitePositive(turnRateRadians)) return false;
        float calculatedSpeedCap = Mathf.Min(motorSettings.maxSpeed, Mathf.Abs(curvature) <= 0f ? motorSettings.maxSpeed : turnRateRadians / Mathf.Abs(curvature));
        if (!FinitePositive(calculatedSpeedCap)) return false;
        float calculatedSteering = 0f;
        if (forwardSpeed > 0f) {
            float denominator = Mathf.Min(turnRateRadians, forwardSpeed / motorSettings.minimumTurningRadius);
            if (!FinitePositive(denominator)) return false;
            calculatedSteering = Mathf.Clamp(curvature * forwardSpeed / denominator, -1f, 1f);
        }
        if (!Finite(calculatedSteering) || !Finite(calculatedSpeedCap)) return false;
        steering = calculatedSteering;
        speedCap = calculatedSpeedCap;
        return true;
    }

    /// <summary>Interpolates cruise speed toward a corner cap without allowing a larger radius to raise the cap.</summary>
    /// <param name="cruiseSpeed">Nonnegative current cruise speed.</param>
    /// <param name="cornerSpeed">Nonnegative authored full-slowdown corner speed.</param>
    /// <param name="absoluteAngle">Nonnegative absolute turn angle in degrees.</param>
    /// <param name="fullSlowdownAngle">Positive angle reaching the full slowdown cap.</param>
    /// <param name="speed">Bounded corner target speed, or zero on failure.</param>
    /// <returns>True when all inputs are finite and the bounded interpolation succeeds.</returns>
    public static bool TryCornerTargetSpeed(float cruiseSpeed, float cornerSpeed, float absoluteAngle,
        float fullSlowdownAngle, out float speed) {
        speed = 0f;
        if (!FiniteNonnegative(cruiseSpeed) || !FiniteNonnegative(cornerSpeed) || !FiniteNonnegative(absoluteAngle) ||
            !FinitePositive(fullSlowdownAngle)) return false;
        float t = Mathf.Clamp01(absoluteAngle / fullSlowdownAngle);
        float result = Mathf.Lerp(cruiseSpeed, Mathf.Min(cruiseSpeed, cornerSpeed), t);
        if (!FiniteNonnegative(result)) return false;
        speed = result;
        return true;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
