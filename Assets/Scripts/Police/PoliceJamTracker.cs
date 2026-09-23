using UnityEngine;

/// <summary>
/// Pure physical-jam detector for one pursuing vehicle. Progress is the net world displacement from
/// a moving anchor over an authored window, never a single-frame speed or displacement sample: a car
/// grinding along a wall keeps producing small per-frame motion and speed jitter (the reckless
/// lateral wiggle does exactly that), which would reset a per-frame test every step and let the unit
/// stay pinned forever. The tracker only observes; deciding what to do about a jam belongs to the
/// caller.
/// </summary>
public sealed class PoliceJamTracker {
    Vector2 anchor;
    float heldSeconds;
    bool hasAnchor;

    /// <summary>Seconds the vehicle has stayed within the displacement threshold of its anchor.</summary>
    public float HeldSeconds => heldSeconds;

    /// <summary>True once an anchor has been captured and not yet invalidated.</summary>
    public bool HasAnchor => hasAnchor;

    /// <summary>Current anchor position; meaningless while <see cref="HasAnchor"/> is false.</summary>
    public Vector2 Anchor => anchor;

    /// <summary>Drops the anchor and the accumulated hold time.</summary>
    public void Reset() {
        hasAnchor = false;
        heldSeconds = 0f;
        anchor = Vector2.zero;
    }

    /// <summary>
    /// Observes one step and reports whether the vehicle is jammed.
    /// </summary>
    /// <param name="deltaTime">Active step; non-positive or non-finite steps are ignored.</param>
    /// <param name="position">Current world position.</param>
    /// <param name="throttling">Whether the vehicle is actually commanded to drive forward. A deliberate brake or reverse is never a jam.</param>
    /// <param name="windowSeconds">Seconds without net progress that count as jammed.</param>
    /// <param name="minimumDisplacement">Net distance from the anchor that counts as real progress and re-anchors.</param>
    /// <returns>True on the step the hold time reaches the window, and on every later step until progress or a reset.</returns>
    public bool Observe(float deltaTime, Vector2 position, bool throttling, float windowSeconds, float minimumDisplacement) {
        if (!throttling || !Finite(position)) {
            Reset();
            return false;
        }
        if (!hasAnchor) {
            anchor = position;
            hasAnchor = true;
            heldSeconds = 0f;
            return false;
        }
        if (Vector2.Distance(position, anchor) > Mathf.Max(0.01f, minimumDisplacement)) {
            anchor = position;
            heldSeconds = 0f;
            return false;
        }
        if (Finite(deltaTime) && deltaTime > 0f) heldSeconds += deltaTime;
        return heldSeconds >= Mathf.Max(0.05f, windowSeconds);
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
}
