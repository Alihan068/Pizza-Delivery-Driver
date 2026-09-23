using UnityEngine;

/// <summary>
/// Pure sustained-contact decision for a reckless police unit. A bounded ram ends the moment the
/// colliders actually touch, which on its own makes a unit disengage and realign after a single hit.
/// This policy takes over exactly there: while the unit stays in contact with (or within a short gap
/// of) the player it keeps full throttle and damped steering into the player, shoving them instead of
/// bouncing off. It releases when the player genuinely breaks away for a grace period, or after a
/// bounded pin so the player always gets a chance, followed by a short cooldown before the next pin.
/// Steering is proportional with a deadzone: at contact range the bearing swings wildly, and the
/// bang-bang steering used for distant pursuit would slide the car off the player.
/// </summary>
public sealed class PolicePinPressure {
    /// <summary>Authored bounds for one pin episode.</summary>
    public readonly struct Settings {
        /// <summary>Surface gap at or below which contact pressure may start even without an overlap.</summary>
        public readonly float engageGap;
        /// <summary>Surface gap above which the player counts as breaking away.</summary>
        public readonly float releaseGap;
        /// <summary>Seconds the player must stay beyond the release gap before the pin ends.</summary>
        public readonly float releaseGraceSeconds;
        /// <summary>Longest single pin episode.</summary>
        public readonly float maximumSeconds;
        /// <summary>Seconds after a completed pin before another may begin.</summary>
        public readonly float cooldownSeconds;
        /// <summary>Bearing error below which no steering is applied.</summary>
        public readonly float steeringDeadzoneDegrees;
        /// <summary>Bearing error at which steering reaches full lock.</summary>
        public readonly float fullLockDegrees;

        public Settings(float engageGap, float releaseGap, float releaseGraceSeconds, float maximumSeconds,
            float cooldownSeconds, float steeringDeadzoneDegrees, float fullLockDegrees) {
            this.engageGap = Mathf.Max(0f, engageGap);
            this.releaseGap = Mathf.Max(this.engageGap, releaseGap);
            this.releaseGraceSeconds = Mathf.Max(0f, releaseGraceSeconds);
            this.maximumSeconds = Mathf.Max(0.1f, maximumSeconds);
            this.cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            this.steeringDeadzoneDegrees = Mathf.Max(0f, steeringDeadzoneDegrees);
            this.fullLockDegrees = Mathf.Max(this.steeringDeadzoneDegrees + 1f, fullLockDegrees);
        }
    }

    readonly Settings settings;
    float activeSeconds;
    float lostSeconds;
    float cooldownRemaining;
    bool active;

    public PolicePinPressure(Settings settings) { this.settings = settings; }

    /// <summary>True while contact pressure owns the motor.</summary>
    public bool IsActive => active;

    /// <summary>Seconds the current pin episode has lasted.</summary>
    public float ActiveSeconds => activeSeconds;

    /// <summary>Seconds remaining before another pin may begin.</summary>
    public float CooldownRemaining => cooldownRemaining;

    /// <summary>Clears the episode and its cooldown, for a new life or an explicit stop.</summary>
    public void Reset() {
        active = false;
        activeSeconds = 0f;
        lostSeconds = 0f;
        cooldownRemaining = 0f;
    }

    /// <summary>
    /// Observes one step of contact evidence and reports whether contact pressure should drive now.
    /// </summary>
    /// <param name="deltaTime">Active step; non-positive or non-finite steps only re-report the current state.</param>
    /// <param name="contact">True when the colliders actually overlap or touch.</param>
    /// <param name="surfaceGap">Measured surface gap to the player; use 0 while overlapping and +infinity when unknown.</param>
    /// <param name="eligible">False disables engagement entirely (for example while the unit is wrecked or a session ended).</param>
    public bool Observe(float deltaTime, bool contact, float surfaceGap, bool eligible = true) {
        if (!Finite(deltaTime) || deltaTime < 0f) deltaTime = 0f;
        if (!eligible) {
            active = false;
            activeSeconds = 0f;
            lostSeconds = 0f;
            return false;
        }
        bool touching = contact || (Finite(surfaceGap) && surfaceGap <= settings.engageGap);
        bool holding = contact || (Finite(surfaceGap) && surfaceGap <= settings.releaseGap);
        if (!active) {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
            if (!touching || cooldownRemaining > 0f) return false;
            active = true;
            activeSeconds = 0f;
            lostSeconds = 0f;
            return true;
        }

        activeSeconds += deltaTime;
        lostSeconds = holding ? 0f : lostSeconds + deltaTime;
        if (activeSeconds >= settings.maximumSeconds || lostSeconds > settings.releaseGraceSeconds) {
            active = false;
            cooldownRemaining = settings.cooldownSeconds;
            activeSeconds = 0f;
            lostSeconds = 0f;
            return false;
        }
        return true;
    }

    /// <summary>Damped steering toward the player for one pin step; returns 0 inside the deadzone.</summary>
    /// <param name="bearingDegrees">Signed angle from the unit's forward direction to the player.</param>
    public float Steering(float bearingDegrees) {
        if (!Finite(bearingDegrees)) return 0f;
        float magnitude = Mathf.Abs(bearingDegrees);
        if (magnitude <= settings.steeringDeadzoneDegrees) return 0f;
        float span = Mathf.Max(0.01f, settings.fullLockDegrees - settings.steeringDeadzoneDegrees);
        return Mathf.Clamp((magnitude - settings.steeringDeadzoneDegrees) / span, 0f, 1f) * Mathf.Sign(bearingDegrees);
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
