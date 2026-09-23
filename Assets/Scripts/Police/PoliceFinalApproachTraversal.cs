using UnityEngine;

/// <summary>Measured latch for a committed final straight or arc-then-straight approach.</summary>
public sealed class PoliceFinalApproachTraversal {
    /// <summary>Traversal state.</summary>
    public enum State {
        /// <summary>No maneuver owns this latch; only an explicit lifecycle reset returns here.</summary>
        Inactive,
        /// <summary>Measured motion spends the original travel and active-session time limits.</summary>
        Active,
        /// <summary>The maneuver is permanently refused until its owner clears the lifecycle.</summary>
        Expired,
        /// <summary>The measured endpoint was reached; completion alone is not target standoff proof.</summary>
        Complete
    }
    /// <summary>Update outcome.</summary>
    public enum UpdateResult { Active, Released, Expired, InvalidInput }

    State state;
    PoliceFinalApproachGeometry.Plan plan;
    Vector2 lastPosition;
    float startClock;
    float lastClock;
    float progress;
    float measuredTravel;
    float maximumTravel;
    float maximumSeconds;

    /// <summary>Current latch state.</summary>
    public State CurrentState => state;
    /// <summary>Captured immutable plan.</summary>
    public PoliceFinalApproachGeometry.Plan Plan => plan;
    /// <summary>Measured path progress.</summary>
    public float Progress => progress;
    /// <summary>Remaining path length.</summary>
    public float Remaining => Mathf.Max(0f, plan.TotalLength - progress);
    /// <summary>Measured travel charged against the original bound.</summary>
    public float MeasuredTravel => measuredTravel;
    /// <summary>Original active-session deadline, unchanged by actual-pose replanning.</summary>
    public float Deadline => startClock + maximumSeconds;
    /// <summary>Unspent measured travel from the original maneuver bound.</summary>
    public float TravelRemaining => Mathf.Max(0f, maximumTravel - measuredTravel);

    /// <summary>Commits one finite plan with immutable travel and duration limits.</summary>
    public bool TryBegin(PoliceFinalApproachGeometry.Plan value, float clock, Vector2 position, float maximumTravel, float maximumSeconds) {
        if (state != State.Inactive || !Finite(clock) || clock < 0f || !Finite(position) || !Exact(position, value.start) || !FinitePositive(value.TotalLength) ||
            !FinitePositive(maximumTravel) || maximumTravel < value.TotalLength || !FinitePositive(maximumSeconds)) return false;
        plan = value; lastPosition = position; startClock = lastClock = clock;
        this.maximumTravel = maximumTravel; this.maximumSeconds = maximumSeconds; state = State.Active; return true;
    }

    /// <summary>Consumes one actual pose observation without restarting the original bounds.</summary>
    /// <param name="clock">Monotonic active-session clock, excluding paused game time.</param>
    /// <param name="position">Actual observed pivot, never a sampled or commanded path point.</param>
    /// <param name="forward">Actual nonzero body heading used for tangent tracking.</param>
    /// <param name="displacementSlack">Authored multiplier bounding projection gain by measured travel.</param>
    /// <param name="alignmentDegrees">Maximum heading error from the nearest path tangent.</param>
    /// <param name="paused">Leaves the measured transaction unchanged when true.</param>
    /// <param name="maxDeviation">Maximum cross-track error; does not inflate collision geometry.</param>
    /// <param name="acquisitionTolerance">Small numerical acquisition allowance, never stationarity progress.</param>
    /// <param name="maximumStep">Finite force/velocity-derived bound on this observation's displacement; zero admits only rest.</param>
    /// <returns>Invalid observations expire the latch; neither invalid input nor expiry can renew it.</returns>
    public UpdateResult Tick(float clock, Vector2 position, Vector2 forward, float displacementSlack, float alignmentDegrees, bool paused,
        float maxDeviation = 0f, float acquisitionTolerance = 0f, float maximumStep = 0f) {
        if (state == State.Expired) return UpdateResult.Expired;
        if (state != State.Active || !Finite(clock) || clock < lastClock || !Finite(position) || !Finite(forward) ||
            forward.sqrMagnitude <= 0f || !FinitePositive(displacementSlack) || !FiniteNonnegative(alignmentDegrees) ||
            !FiniteNonnegative(maxDeviation) || !FiniteNonnegative(acquisitionTolerance) || !FiniteNonnegative(maximumStep)) { Expire(); return UpdateResult.InvalidInput; }
        if (paused) return UpdateResult.Active;
        float step = Vector2.Distance(lastPosition, position);
        // A repeated actual pose earns no projection credit. Trigonometric roundoff at an arc
        // start must neither invent movement nor expire a newly committed stationary sample.
        float observed = step > 0f ? PoliceFinalApproachGeometry.ProjectProgress(plan, position) : progress;
        if (!FiniteNonnegative(step) || step > maximumStep || observed < progress - acquisitionTolerance ||
            observed - progress > step * displacementSlack + (step > 0f ? acquisitionTolerance : 0f) ||
            Vector2.Distance(position, plan.Sample(observed)) > maxDeviation + acquisitionTolerance ||
            Vector2.Angle(forward, PoliceFinalApproachGeometry.DirectionAt(plan, observed)) > alignmentDegrees) { Expire(); return UpdateResult.InvalidInput; }
        measuredTravel += step; progress = Mathf.Max(progress, observed); lastPosition = position; lastClock = clock;
        if (!Finite(measuredTravel) || measuredTravel > maximumTravel || clock - startClock > maximumSeconds) { Expire(); return UpdateResult.Expired; }
        if (progress >= plan.TotalLength && step > 0f) { state = State.Complete; return UpdateResult.Released; }
        return UpdateResult.Active;
    }

    /// <summary>Replaces future geometry at the last measured pose without renewing time or travel limits.</summary>
    public bool TryReplan(PoliceFinalApproachGeometry.Plan value) {
        if (state != State.Active || !Exact(value.start, lastPosition) || !FinitePositive(value.TotalLength) || value.TotalLength > TravelRemaining) return false;
        plan = value; progress = 0f;
        return true;
    }

    /// <summary>Samples an immutable lookahead from measured progress.</summary>
    public Vector2 SampleAhead(float distance) => plan.Sample(progress + Mathf.Max(0f, distance));
    /// <summary>Clears the latch on route, target, graph, life, or session invalidation.</summary>
    public void Reset() { state = State.Inactive; plan = default; lastPosition = Vector2.zero; startClock = lastClock = progress = measuredTravel = 0f; maximumTravel = maximumSeconds = 0f; }
    void Expire() { state = State.Expired; }
    static bool Exact(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
