using UnityEngine;

/// <summary>Pure helpers relating a junction's authored conflict zone to the polyline a vehicle follows through it (map-local coordinates).</summary>
public static class JunctionGeometry {
    /// <summary>
    /// Distance from a point inside the zone, travelling along a direction, until the zone's
    /// boundary is crossed. Zero when the point is already outside the zone or the direction is
    /// degenerate. Used both backwards from a junction node (stop line on the approach edge) and
    /// forwards from it (where the exit edge leaves the zone).
    /// </summary>
    public static float DistanceToZoneBoundary(Rect zone, Vector2 point, Vector2 direction) {
        if (!zone.Contains(point)) return 0f;
        if (direction.sqrMagnitude <= 0.000001f) return 0f;
        direction.Normalize();
        float best = float.PositiveInfinity;
        if (direction.x > 0.000001f) best = Mathf.Min(best, (zone.xMax - point.x) / direction.x);
        else if (direction.x < -0.000001f) best = Mathf.Min(best, (zone.xMin - point.x) / direction.x);
        if (direction.y > 0.000001f) best = Mathf.Min(best, (zone.yMax - point.y) / direction.y);
        else if (direction.y < -0.000001f) best = Mathf.Min(best, (zone.yMin - point.y) / direction.y);
        return float.IsPositiveInfinity(best) ? 0f : Mathf.Max(0f, best);
    }
}
