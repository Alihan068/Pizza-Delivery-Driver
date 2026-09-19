using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides whether one vehicle can physically drive a bound route: every edge must fit its width,
/// every interior corner must fit its minimum turning radius (reusing
/// <see cref="RoadClearanceChecker"/>), and every edge-to-edge transition (including a loop's
/// closing seam) must be a turn the vehicle can make — no U-turns beyond the authored maximum, and
/// enough straight length on both sides to lay a circular arc of the vehicle's radius
/// (tangent length r·tan(θ/2)). A route that exists in data is not a route every profile may
/// enter; a heavy/wide profile is refused here so a manager can pick another pool entry.
/// </summary>
public static class RouteTransitionFilter {
    /// <summary>Vehicle limits the filter evaluates a route against.</summary>
    public readonly struct VehicleConstraints {
        /// <summary>Collider footprint width.</summary>
        public readonly float width;
        /// <summary>Extra clearance required beyond the width on every edge.</summary>
        public readonly float widthSafetyMargin;
        /// <summary>Smallest radius the vehicle can turn within.</summary>
        public readonly float minimumTurningRadius;
        /// <summary>Largest direction change (degrees) at any transition the vehicle is allowed to attempt.</summary>
        public readonly float maxTurnAngleDegrees;

        public VehicleConstraints(float width, float widthSafetyMargin, float minimumTurningRadius, float maxTurnAngleDegrees) {
            this.width = width;
            this.widthSafetyMargin = widthSafetyMargin;
            this.minimumTurningRadius = minimumTurningRadius;
            this.maxTurnAngleDegrees = maxTurnAngleDegrees;
        }
    }

    /// <summary>Checks a bound cursor's whole route against a vehicle's constraints.</summary>
    /// <param name="cursor">A bound <see cref="RouteCursor"/>; an unbound cursor fails.</param>
    /// <param name="constraints">Vehicle limits to test.</param>
    /// <param name="issue">First failure found, or null when the route is drivable.</param>
    /// <returns>True when every edge and transition is drivable.</returns>
    public static bool IsDrivable(RouteCursor cursor, VehicleConstraints constraints, out string issue) {
        issue = null;
        if (cursor == null || !cursor.IsBound) {
            issue = "route is not bound";
            return false;
        }
        if (!IsFinite(constraints.width) || constraints.width <= 0f || !IsFinite(constraints.minimumTurningRadius)
            || constraints.minimumTurningRadius < 0f || !IsFinite(constraints.maxTurnAngleDegrees)) {
            issue = "invalid vehicle constraints";
            return false;
        }

        int count = cursor.EdgeCount;
        for (int i = 0; i < count; i++) {
            var edge = cursor.EdgeAt(i);
            var polyline = cursor.PolylineAt(i);
            if (!RoadClearanceChecker.FitsEdgeWidth(edge, constraints.width, Mathf.Max(0f, constraints.widthSafetyMargin))) {
                issue = "edge " + edge.edgeId + " is too narrow (" + edge.usableWidth + ") for width " + constraints.width;
                return false;
            }
            // Interior corners of this edge.
            for (int v = 1; v < polyline.Count - 1; v++) {
                if (!TransitionFits(polyline[v - 1], polyline[v], polyline[v + 1], constraints, out float angle, out float needed, out float available)) {
                    issue = "edge " + edge.edgeId + " interior corner of " + angle.ToString("F0") + "° needs " + needed.ToString("F2")
                        + " units of straight on each side, has " + available.ToString("F2");
                    return false;
                }
            }
            // Transition into the next edge (loop seam included).
            int nextIndex = i + 1;
            if (nextIndex >= count) {
                if (!cursor.Loop) break;
                nextIndex = 0;
            }
            var nextPolyline = cursor.PolylineAt(nextIndex);
            if (!TransitionFits(polyline[polyline.Count - 2], polyline[polyline.Count - 1], nextPolyline[1], constraints,
                out float turnAngle, out float tangentNeeded, out float tangentAvailable)) {
                issue = "transition " + edge.edgeId + " -> " + cursor.EdgeAt(nextIndex).edgeId + " turns " + turnAngle.ToString("F0")
                    + "°; needs " + tangentNeeded.ToString("F2") + " units of straight on each side, has " + tangentAvailable.ToString("F2");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Whether a single vertex turn is drivable: angle within the maximum, and both adjacent
    /// segments at least as long as the arc's tangent length r·tan(θ/2).
    /// </summary>
    public static bool TransitionFits(Vector2 before, Vector2 vertex, Vector2 after, VehicleConstraints constraints,
        out float turnAngleDegrees, out float tangentNeeded, out float tangentAvailable) {
        Vector2 incoming = vertex - before;
        Vector2 outgoing = after - vertex;
        turnAngleDegrees = 0f;
        tangentNeeded = 0f;
        tangentAvailable = Mathf.Min(incoming.magnitude, outgoing.magnitude);
        if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f) return true;

        turnAngleDegrees = Vector2.Angle(incoming, outgoing);
        if (turnAngleDegrees < 0.5f) return true;
        if (turnAngleDegrees > constraints.maxTurnAngleDegrees) return false;

        // A U-turn-ish angle near 180 makes tan blow up; the max-angle gate above is what excludes it.
        tangentNeeded = constraints.minimumTurningRadius * Mathf.Tan(Mathf.Min(turnAngleDegrees, 179f) * 0.5f * Mathf.Deg2Rad);
        return tangentAvailable + 0.0001f >= tangentNeeded;
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
