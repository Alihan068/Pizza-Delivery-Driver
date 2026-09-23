using UnityEngine;

/// <summary>
/// Pure measured-motion latch for one certified road turn. It owns no route query, Unity component,
/// or physics command: a future controller supplies a stable cursor token, measured outgoing
/// progress and heading, then consumes the explicit active/released/expired outcome.
/// </summary>
public sealed class PoliceTurnTraversal {
    /// <summary>State retained for one accepted turn until release, expiry, or an explicit reset.</summary>
    public enum State {
        /// <summary>No turn is currently latched.</summary>
        Inactive,
        /// <summary>A turn is latched and remains subject to measured travel and active-clock bounds.</summary>
        Active,
        /// <summary>A bound was exceeded or an invalid update occurred; reset is required before another begin.</summary>
        Expired
    }

    /// <summary>Result from one measured update without exposing mutation commands to callers.</summary>
    public enum UpdateResult {
        /// <summary>The turn remains latched.</summary>
        Active,
        /// <summary>Measured outgoing progress and heading released the turn.</summary>
        Released,
        /// <summary>A travel or duration limit has permanently expired this latch.</summary>
        Expired,
        /// <summary>Input was invalid or out of clock order; the latch fails closed as expired.</summary>
        InvalidInput
    }

    /// <summary>Finite limits copied at begin time so later authoring changes cannot restart an active turn.</summary>
    public readonly struct Limits {
        /// <summary>Measured outgoing route distance required before release is eligible.</summary>
        public readonly float exitDistance;
        /// <summary>Maximum absolute angle from the certified outgoing tangent at release.</summary>
        public readonly float exitAlignmentDegrees;
        /// <summary>Maximum accumulated measured pivot displacement while latched.</summary>
        public readonly float maximumTravel;
        /// <summary>Maximum active-clock duration while latched.</summary>
        public readonly float maximumActiveSeconds;

        /// <summary>Creates immutable per-turn limits supplied by the future driving-settings integration.</summary>
        /// <param name="exitDistance">Positive measured outgoing distance required before release may be considered.</param>
        /// <param name="exitAlignmentDegrees">Positive maximum heading error at release, bounded by 180 degrees.</param>
        /// <param name="maximumTravel">Positive measured travel bound, at least the requested exit distance.</param>
        /// <param name="maximumActiveSeconds">Positive active-session duration bound.</param>
        public Limits(float exitDistance, float exitAlignmentDegrees, float maximumTravel, float maximumActiveSeconds) {
            this.exitDistance = exitDistance;
            this.exitAlignmentDegrees = exitAlignmentDegrees;
            this.maximumTravel = maximumTravel;
            this.maximumActiveSeconds = maximumActiveSeconds;
        }

        /// <summary>Returns true only for finite positive bounds with travel at least as large as the requested exit.</summary>
        public bool IsValid() => FinitePositive(exitDistance) && FinitePositive(exitAlignmentDegrees) && exitAlignmentDegrees <= 180f &&
            FinitePositive(maximumTravel) && maximumTravel >= exitDistance && FinitePositive(maximumActiveSeconds);
    }

    State state;
    int token = -1;
    float absoluteArc;
    float cap;
    Vector2 outgoing;
    Limits limits;
    float startClock;
    float lastClock;
    Vector2 lastPosition;
    float measuredTravel;

    /// <summary>Current latch state; expiry remains visible until an explicit reset.</summary>
    public State CurrentState => state;
    /// <summary>Stable cursor token captured at begin, or -1 while inactive.</summary>
    public int Token => token;
    /// <summary>Absolute route arc captured from the certified bend.</summary>
    public float AbsoluteArc => absoluteArc;
    /// <summary>Corner speed cap retained after the vertex until release or expiry.</summary>
    public float Cap => cap;
    /// <summary>Maximum measured travel retained by the active turn transaction.</summary>
    public float MaximumTravel => limits.maximumTravel;
    /// <summary>Normalized certified outgoing tangent, or zero while inactive.</summary>
    public Vector2 OutgoingDirection => outgoing;
    /// <summary>Accumulated measured pivot displacement, never commanded-speed distance.</summary>
    public float MeasuredTravel => measuredTravel;

    /// <summary>
    /// Latches one certified turn exactly once. Expired state deliberately cannot begin again until
    /// the controller explicitly invalidates/reset its route state.
    /// </summary>
    /// <param name="turnToken">Cursor-binding-local token identifying the certified bend occurrence.</param>
    /// <param name="turnAbsoluteArc">Finite nonnegative absolute arc of that bend.</param>
    /// <param name="outgoingDirection">Finite nonzero certified tangent after the bend.</param>
    /// <param name="cornerCap">Finite nonnegative speed cap retained through exit.</param>
    /// <param name="turnLimits">Finite copied release/travel/duration limits.</param>
    /// <param name="clock">Monotonic active-session clock at the measured begin pose.</param>
    /// <param name="position">Measured pivot position at begin.</param>
    /// <returns>True only when an inactive latch was established.</returns>
    public bool TryBegin(int turnToken, float turnAbsoluteArc, Vector2 outgoingDirection, float cornerCap, Limits turnLimits,
        float clock, Vector2 position) {
        if (state != State.Inactive || turnToken < 0 || !FiniteNonnegative(turnAbsoluteArc) || !FiniteNonnegative(cornerCap) ||
            !turnLimits.IsValid() || !FiniteNonnegative(clock) || !Finite(position) || !FinitePositive(outgoingDirection.sqrMagnitude)) return false;
        Vector2 normalized = outgoingDirection.normalized;
        if (!FinitePositive(normalized.sqrMagnitude)) return false;
        token = turnToken; absoluteArc = turnAbsoluteArc; cap = cornerCap; outgoing = normalized; limits = turnLimits;
        startClock = clock; lastClock = clock; lastPosition = position; measuredTravel = 0f; state = State.Active;
        return true;
    }

    /// <summary>
    /// Consumes a measured physics-step observation. Paused observations cannot release, extend,
    /// restart, or replace the active measurement baseline, so resumed movement is charged against
    /// the same original travel bound. Invalid input permanently expires the current latch.
    /// </summary>
    /// <param name="clock">Monotonic active-session clock.</param>
    /// <param name="position">Measured pivot position.</param>
    /// <param name="outgoingProgress">Finite nonnegative measured route arc progress after the selected vertex; callers clamp pre-vertex values to zero.</param>
    /// <param name="forward">Measured vehicle forward direction.</param>
    /// <param name="paused">Explicit pause gate from the controller.</param>
    /// <returns>Explicit state-transition outcome.</returns>
    public UpdateResult Tick(float clock, Vector2 position, float outgoingProgress, Vector2 forward, bool paused) {
        if (state == State.Expired) return UpdateResult.Expired;
        if (state != State.Active || !Finite(clock) || clock < lastClock || !Finite(position) || !FiniteNonnegative(outgoingProgress) || !FinitePositive(forward.sqrMagnitude)) {
            Expire();
            return UpdateResult.InvalidInput;
        }
        if (paused) return UpdateResult.Active;
        float stepDistance = Vector2.Distance(lastPosition, position);
        if (!FiniteNonnegative(stepDistance) || !Finite(clock - startClock)) { Expire(); return UpdateResult.InvalidInput; }
        measuredTravel += stepDistance;
        lastClock = clock;
        lastPosition = position;
        if (!Finite(measuredTravel) || measuredTravel > limits.maximumTravel || clock - startClock > limits.maximumActiveSeconds) {
            Expire();
            return UpdateResult.Expired;
        }
        Vector2 normalizedForward = forward.normalized;
        if (!FinitePositive(normalizedForward.sqrMagnitude)) { Expire(); return UpdateResult.InvalidInput; }
        if (stepDistance > 0f && outgoingProgress >= limits.exitDistance && Vector2.Angle(normalizedForward, outgoing) <= limits.exitAlignmentDegrees) {
            Reset();
            return UpdateResult.Released;
        }
        return UpdateResult.Active;
    }

    /// <summary>Clears active or expired state after route, target, life, or graph invalidation.</summary>
    public void Reset() {
        state = State.Inactive; token = -1; absoluteArc = 0f; cap = 0f; outgoing = Vector2.zero; limits = default;
        startClock = 0f; lastClock = 0f; lastPosition = Vector2.zero; measuredTravel = 0f;
    }

    void Expire() { state = State.Expired; }
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
