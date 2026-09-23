using UnityEngine;

/// <summary>
/// Pure "keep your foot down" speed reward for the player. After the player has held throttle for
/// the authored warm-up without braking, handbraking, drifting or steering hard, the top-speed
/// multiplier climbs gradually toward the authored maximum. Any of those — or a crash — makes it
/// fall back gradually toward 1; a crash falls faster and restarts the warm-up. The multiplier never
/// jumps, so the car's top speed and acceleration change smoothly rather than snapping.
/// </summary>
public sealed class MomentumBoost {
    readonly float warmupSeconds;
    readonly float maximumMultiplier;
    readonly float riseRate;
    readonly float fallRate;
    readonly float crashFallRate;
    float qualifiedSeconds;
    bool crashPending;

    /// <summary>Creates the boost from authored timings.</summary>
    /// <param name="warmupSeconds">Seconds of qualifying driving before the multiplier starts rising.</param>
    /// <param name="maximumMultiplier">Highest multiplier; values below 1 disable the boost.</param>
    /// <param name="riseSeconds">Seconds to climb from 1 to the maximum once warmed up.</param>
    /// <param name="fallSeconds">Seconds to fall from the maximum back to 1 after the player stops qualifying.</param>
    /// <param name="crashFallSeconds">Seconds to fall from the maximum back to 1 after a crash.</param>
    public MomentumBoost(float warmupSeconds, float maximumMultiplier, float riseSeconds, float fallSeconds, float crashFallSeconds) {
        this.warmupSeconds = Finite(warmupSeconds) ? Mathf.Max(0f, warmupSeconds) : 1f;
        this.maximumMultiplier = Finite(maximumMultiplier) ? Mathf.Max(1f, maximumMultiplier) : 1f;
        float span = this.maximumMultiplier - 1f;
        riseRate = span / Mathf.Max(0.01f, Finite(riseSeconds) ? riseSeconds : 4f);
        fallRate = span / Mathf.Max(0.01f, Finite(fallSeconds) ? fallSeconds : 1.5f);
        crashFallRate = span / Mathf.Max(0.01f, Finite(crashFallSeconds) ? crashFallSeconds : 0.5f);
    }

    /// <summary>Current top-speed multiplier, always within [1, maximum].</summary>
    public float Multiplier { get; private set; } = 1f;

    /// <summary>Seconds the current qualifying streak has lasted.</summary>
    public float QualifiedSeconds => qualifiedSeconds;

    /// <summary>True while the multiplier is above 1.</summary>
    public bool IsBoosting => Multiplier > 1.0001f;

    /// <summary>Reports a crash; the multiplier then drains to 1 at the crash rate before a new warm-up can start.</summary>
    public void NotifyCrash() {
        crashPending = true;
    }

    /// <summary>Drops the boost immediately, for a new life or session.</summary>
    public void Reset() {
        Multiplier = 1f;
        qualifiedSeconds = 0f;
        crashPending = false;
    }

    /// <summary>Advances the boost by one physics step.</summary>
    /// <param name="deltaTime">Step length; non-positive or non-finite steps change nothing.</param>
    /// <param name="qualifying">True while the player holds throttle straight ahead without brake, handbrake or drift.</param>
    /// <returns>The multiplier after this step.</returns>
    public float Step(float deltaTime, bool qualifying) {
        if (!Finite(deltaTime) || deltaTime <= 0f) return Multiplier;
        if (crashPending) {
            // A crash drains the whole boost at the crash rate before a new warm-up can begin.
            qualifiedSeconds = 0f;
            Multiplier = Mathf.Max(1f, Multiplier - crashFallRate * deltaTime);
            if (Multiplier <= 1f) crashPending = false;
            return Multiplier;
        }
        if (!qualifying) {
            qualifiedSeconds = 0f;
            Multiplier = Mathf.Max(1f, Multiplier - fallRate * deltaTime);
            return Multiplier;
        }
        qualifiedSeconds += deltaTime;
        if (qualifiedSeconds > warmupSeconds)
            Multiplier = Mathf.Min(maximumMultiplier, Multiplier + riseRate * deltaTime);
        return Multiplier;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
