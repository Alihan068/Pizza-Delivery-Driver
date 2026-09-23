using UnityEngine;

/// <summary>Pure bounded state and reserve math for the police ram decision.</summary>
public sealed class PoliceRamDecision {
    float coneDegrees;
    float maximumSurfaceGap;
    float maximumActiveSeconds;
    float cooldownSeconds;
    float startClock;
    float lastValidClock;
    float activeElapsed;
    float cooldownUntil;
    bool configured;
    bool active;
    bool separationRequired;
    bool hasValidClock;
    bool cooldownUnrepresentable;

    /// <summary>True while a bounded ram command owns the decision state.</summary>
    public bool IsActive => active;
    /// <summary>Remaining active seconds before the bounded ram times out.</summary>
    public float RemainingActiveSeconds => active ? Mathf.Max(0f, maximumActiveSeconds - activeElapsed) : 0f;
    /// <summary>Earliest clock at which a cancelled ram may attempt again.</summary>
    public float CooldownUntil => cooldownUntil;
    /// <summary>True until a fresh positive physical separation is observed.</summary>
    public bool SeparationRequired => separationRequired;

    /// <summary>Captures the four validated ram settings without retaining the authored object.</summary>
    /// <param name="settings">Validated driving settings containing the bounded ram values.</param>
    /// <returns>True when the settings are valid and the detached configuration was captured.</returns>
    public bool TryConfigure(PoliceDrivingSettings settings) {
        if (settings == null || !settings.IsValid(out _) || !FinitePositive(settings.ramConeDegrees) ||
            settings.ramConeDegrees >= 90f || !FinitePositive(settings.ramMaximumSurfaceGap) ||
            settings.ramMaximumSurfaceGap > settings.maxSweepDistance || !FinitePositive(settings.ramMaximumActiveSeconds) ||
            !FinitePositive(settings.ramCooldownSeconds)) {
            configured = false;
            coneDegrees = 0f;
            maximumSurfaceGap = 0f;
            maximumActiveSeconds = 0f;
            cooldownSeconds = 0f;
            ResetState();
            return false;
        }
        coneDegrees = settings.ramConeDegrees;
        maximumSurfaceGap = settings.ramMaximumSurfaceGap;
        maximumActiveSeconds = settings.ramMaximumActiveSeconds;
        cooldownSeconds = settings.ramCooldownSeconds;
        configured = true;
        ResetState();
        return true;
    }

    /// <summary>Clears active, cooldown, and separation state while retaining the captured configuration.</summary>
    public void ResetForNewLife() {
        ResetState();
    }

    /// <summary>Checks finite forward geometry, target cone membership, and measured surface-gap bounds.</summary>
    /// <param name="actualForward">Measured finite forward vector of the police body.</param>
    /// <param name="targetDelta">Measured vector from police to the live target.</param>
    /// <param name="actualSurfaceGap">Measured positive surface separation.</param>
    /// <param name="separationTolerance">Minimum gap required to avoid sustained contact admission.</param>
    /// <returns>True only when the target is ahead, inside the authored cone, and within ram range.</returns>
    public bool IsEligible(Vector2 actualForward, Vector2 targetDelta, float actualSurfaceGap, float separationTolerance) {
        if (!configured || !Finite(actualForward) || !Finite(targetDelta) || !Finite(actualSurfaceGap) ||
            !FiniteNonnegative(separationTolerance) || !FinitePositive(actualForward.sqrMagnitude) ||
            !FinitePositive(targetDelta.sqrMagnitude) || actualSurfaceGap <= separationTolerance ||
            actualSurfaceGap > maximumSurfaceGap) return false;
        Vector2 forward = actualForward.normalized;
        Vector2 targetDirection = targetDelta.normalized;
        float alignment = Vector2.Dot(forward, targetDirection);
        float minimumAlignment = Mathf.Cos(coneDegrees * Mathf.Deg2Rad);
        return Finite(alignment) && alignment > 0f && alignment >= minimumAlignment;
    }

    /// <summary>Performs the cheap time, speed, recovery, and cooldown gate before a begin attempt.</summary>
    /// <param name="clock">Current finite monotonic session clock.</param>
    /// <param name="speed">Measured nonnegative forward speed magnitude.</param>
    /// <param name="stoppedThreshold">Existing finite nonnegative stopped threshold.</param>
    /// <param name="recoveryActive">True while crash recovery owns motion.</param>
    /// <returns>True when the decision may attempt a separated admission.</returns>
    public bool CanAttempt(float clock, float speed, float stoppedThreshold, bool recoveryActive) {
        if (!configured || active || recoveryActive || !FiniteNonnegative(clock) || !FiniteNonnegative(speed) ||
            !FiniteNonnegative(stoppedThreshold)) return false;
        if (hasValidClock && clock < lastValidClock) return false;
        if (!separationRequired) return true;
        if (cooldownUnrepresentable) return false;
        return clock >= cooldownUntil && speed <= stoppedThreshold;
    }

    /// <summary>Starts one bounded ram interval after the cheap gate and a fresh physical separation proof.</summary>
    /// <param name="clock">Current finite monotonic session clock.</param>
    /// <param name="dt">Positive finite active tick duration.</param>
    /// <param name="speed">Measured nonnegative speed used by the caller's gate.</param>
    /// <param name="stoppedThreshold">Existing finite nonnegative stopped threshold.</param>
    /// <param name="separated">Fresh positive non-overlapped separation observation.</param>
    /// <param name="recoveryActive">True while crash recovery owns motion.</param>
    /// <returns>True when the interval was admitted with time remaining.</returns>
    public bool TryBegin(float clock, float dt, float speed, float stoppedThreshold, bool separated, bool recoveryActive) {
        if (!FinitePositive(dt) || !separated || !CanAttempt(clock, speed, stoppedThreshold, recoveryActive)) return false;
        if (!Finite((double)clock + dt) || dt >= maximumActiveSeconds) return false;
        startClock = clock;
        lastValidClock = clock;
        activeElapsed = dt;
        hasValidClock = true;
        active = true;
        separationRequired = false;
        return true;
    }

    /// <summary>Accounts one active tick without renewing the original interval or accepting clock regression.</summary>
    /// <param name="clock">Current finite monotonic session clock.</param>
    /// <param name="dt">Positive finite active tick duration.</param>
    /// <returns>True while the original bounded interval remains active.</returns>
    public bool Advance(float clock, float dt) {
        if (!active) return false;
        if (!Finite(clock) || !FinitePositive(dt) || !hasValidClock || clock < lastValidClock) {
            Cancel(clock);
            return false;
        }
        double accumulated = (double)activeElapsed + dt;
        double clockElapsed = (double)clock - startClock;
        double elapsed = System.Math.Max(accumulated, clockElapsed);
        if (!Finite(elapsed) || elapsed >= maximumActiveSeconds) {
            Cancel(clock);
            return false;
        }
        activeElapsed = (float)elapsed;
        lastValidClock = clock;
        return true;
    }

    /// <summary>Cancels once into cooldown and latches the requirement for a fresh physical separation.</summary>
    /// <param name="clock">Current clock, or any value whose finite portion can advance the safe deadline.</param>
    /// <returns>True only for the first transition into cooldown.</returns>
    public bool Cancel(float clock) {
        if (!configured || separationRequired) return false;
        float validClock = hasValidClock ? lastValidClock : 0f;
        if (Finite(clock)) validClock = Mathf.Max(validClock, clock);
        if (!Finite(validClock)) validClock = float.MaxValue;
        double deadline = (double)validClock + cooldownSeconds;
        float representableDeadline = deadline > float.MaxValue ? float.MaxValue : (float)deadline;
        cooldownUnrepresentable = !Finite(deadline) || deadline > float.MaxValue || representableDeadline <= validClock;
        cooldownUntil = cooldownUnrepresentable ? float.MaxValue : (float)deadline;
        active = false;
        activeElapsed = 0f;
        lastValidClock = validClock;
        hasValidClock = true;
        separationRequired = true;
        return true;
    }

    /// <summary>Calculates the full double-precision ram reserve and rejects overflow or excessive range.</summary>
    /// <param name="speed">Current finite nonnegative speed.</param>
    /// <param name="acceleration">Positive bounded acceleration.</param>
    /// <param name="braking">Positive bounded braking.</param>
    /// <param name="remainingSeconds">Positive remaining active interval.</param>
    /// <param name="reactionTime">Finite nonnegative reaction time.</param>
    /// <param name="dt">Positive finite command step.</param>
    /// <param name="stopGap">Positive required post-contact gap.</param>
    /// <param name="targetCastGap">Positive fresh target cast gap.</param>
    /// <param name="maximumDistance">Positive maximum allowed reserve cast length.</param>
    /// <param name="length">Calculated full reserve length.</param>
    /// <param name="peakSpeed">Calculated peak speed over the reserve horizon.</param>
    /// <returns>True when the complete reserve is finite, representable, and within the authored maximum.</returns>
    public static bool TryReserve(float speed, float acceleration, float braking, float remainingSeconds,
        float reactionTime, float dt, float stopGap, float targetCastGap, float maximumDistance,
        out float length, out float peakSpeed) {
        length = 0f;
        peakSpeed = 0f;
        if (!FiniteNonnegative(speed) || !FinitePositive(acceleration) || !FinitePositive(braking) ||
            !FinitePositive(remainingSeconds) || !FiniteNonnegative(reactionTime) || !FinitePositive(dt) ||
            !FinitePositive(stopGap) || !FinitePositive(targetCastGap) || !FinitePositive(maximumDistance)) return false;
        double horizon = (double)remainingSeconds + reactionTime + dt;
        double velocity = speed + (double)acceleration * horizon;
        double driveDistance = velocity * horizon;
        double stopDistance = velocity * velocity / (2d * braking) + velocity * dt + stopGap;
        double reserve = System.Math.Max(targetCastGap, driveDistance) + stopDistance;
        if (!Finite(horizon) || !Finite(velocity) || !Finite(driveDistance) || !Finite(stopDistance) ||
            !Finite(reserve) || reserve > maximumDistance || reserve > float.MaxValue || velocity > float.MaxValue) return false;
        length = (float)reserve;
        peakSpeed = (float)velocity;
        return Finite(length) && Finite(peakSpeed);
    }

    /// <summary>Calculates finite lateral drift allowance for one fixed-heading command step.</summary>
    /// <param name="lateralSpeed">Finite lateral speed magnitude or signed speed.</param>
    /// <param name="grip">Finite grip in the inclusive zero-to-one range.</param>
    /// <param name="dt">Positive finite command step.</param>
    /// <param name="distance">Calculated nonnegative lateral allowance.</param>
    /// <returns>True when the allowance is finite; zero grip permits only zero lateral speed.</returns>
    public static bool TryLateralAllowance(float lateralSpeed, float grip, float dt, out float distance) {
        distance = 0f;
        if (!Finite(lateralSpeed) || !Finite(grip) || grip < 0f || grip > 1f || !FinitePositive(dt)) return false;
        if (grip == 0f) return lateralSpeed == 0f;
        double result = System.Math.Abs((double)lateralSpeed) * (1d - grip) * dt / grip;
        if (!Finite(result) || result > float.MaxValue) return false;
        distance = (float)result;
        return FiniteNonnegative(distance);
    }

    void ResetState() {
        startClock = 0f;
        lastValidClock = 0f;
        activeElapsed = 0f;
        cooldownUntil = 0f;
        active = false;
        separationRequired = false;
        hasValidClock = false;
        cooldownUnrepresentable = false;
    }

    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
