using UnityEngine;

/// <summary>Fixed straight connector geometry with monotonic measured progress; owns no physics or lifecycle.</summary>
public sealed class PoliceStraightConnector {
    Vector2 lastObservedPosition;
    PolicePathCursor.TrackingLimits limits;
    float alignmentDegrees;

    /// <summary>True after finite positive geometry and tracking limits have been captured.</summary>
    public bool IsBound { get; private set; }
    /// <summary>Immutable map-local start of the current line, or zero after reset.</summary>
    public Vector2 Start { get; private set; }
    /// <summary>Immutable map-local end of the current line, or zero after reset.</summary>
    public Vector2 End { get; private set; }
    /// <summary>Unit direction from start to end; never a commanded vehicle heading.</summary>
    public Vector2 Direction { get; private set; }
    /// <summary>Positive line length while bound.</summary>
    public float Length { get; private set; }
    /// <summary>Monotonic projection accepted from actual observed positions only.</summary>
    public float Progress { get; private set; }
    /// <summary>Nonnegative untraversed line distance, independent of stopping gaps.</summary>
    public float Remaining => Mathf.Max(0f, Length - Progress);

    /// <summary>Captures fixed geometry and a real observation without inventing initial progress; failure clears this helper.</summary>
    /// <param name="start">Certified line start.</param><param name="end">Certified line end.</param>
    /// <param name="observedPosition">Actual pivot observation, possibly still before a final connector.</param>
    /// <param name="tracking">Immutable measured-projection limits.</param><param name="maximumAlignment">Allowed heading error in degrees.</param>
    /// <returns>True for finite positive geometry and valid limits.</returns>
    public bool TryBind(Vector2 start, Vector2 end, Vector2 observedPosition,
        PolicePathCursor.TrackingLimits tracking, float maximumAlignment) {
        Reset();
        float length = Vector2.Distance(start, end);
        if (!Finite(start) || !Finite(end) || !Finite(observedPosition) || !Finite(length) || length <= 0f ||
            tracking == null || !tracking.IsValid(out _) || !Finite(maximumAlignment) || maximumAlignment < 0f || maximumAlignment > 180f) return false;
        Start = start; End = end; Length = length; Direction = (end - start) / length;
        lastObservedPosition = observedPosition; limits = tracking; alignmentDegrees = maximumAlignment; IsBound = true;
        return true;
    }

    /// <summary>Accepts one bounded real observation; invalid geometry or exhausted work never changes progress.</summary>
    /// <param name="position">Actual map-local pivot.</param><param name="forward">Actual nonzero forward direction.</param>
    /// <param name="work">Caller-owned finite budget, charged once for a valid observation.</param>
    /// <param name="crossedEnd">True only when the actual pivot reaches or crosses the end plane.</param>
    /// <returns>True when deviation, heading and measured-displacement bounds all hold.</returns>
    public bool TryAdvance(Vector2 position, Vector2 forward, ref int work, out bool crossedEnd) {
        crossedEnd = false;
        if (!IsBound || !Finite(position) || !Finite(forward) || !Finite(forward.sqrMagnitude) || forward.sqrMagnitude <= 0f) return false;
        Vector2 offset = position - Start;
        float lateral = Mathf.Abs(offset.x * Direction.y - offset.y * Direction.x);
        float projection = Vector2.Dot(offset, Direction);
        float displacement = Vector2.Distance(lastObservedPosition, position);
        float delta = projection - Progress;
        float allowance = Mathf.Min(limits.projectionWindow, displacement * limits.authoredDisplacementSlack + limits.acquisitionTolerance);
        if (!Finite(lateral) || lateral > limits.maxDeviation || !Finite(projection) || projection < -limits.acquisitionTolerance ||
            !Finite(displacement) || !Finite(delta) || delta < -limits.acquisitionTolerance || delta > allowance ||
            Vector2.Angle(forward, Direction) > alignmentDegrees || work < 1) return false;
        work--;
        Progress = Mathf.Max(Progress, Mathf.Min(Length, projection));
        lastObservedPosition = position;
        crossedEnd = projection >= Length;
        return true;
    }

    /// <summary>Samples the fixed line at measured progress plus a finite nonnegative lookahead, clamped to the endpoint.</summary>
    /// <param name="distance">Finite nonnegative lookahead supplied by validated controller settings.</param>
    /// <returns>The clamped map-local point; zero for invalid input or an unbound line.</returns>
    public Vector2 SampleAhead(float distance) => IsBound && Finite(distance) && distance >= 0f
        ? Start + Direction * Mathf.Min(Length, Progress + distance) : Vector2.zero;

    /// <summary>Clears geometry and measurements when the owning route or life is invalidated.</summary>
    public void Reset() {
        IsBound = false; Start = End = Direction = lastObservedPosition = Vector2.zero;
        Length = Progress = alignmentDegrees = 0f; limits = null;
    }
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
