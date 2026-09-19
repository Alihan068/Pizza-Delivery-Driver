using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Checks oriented vehicle envelopes against map bounds, exclusion rectangles and an injected
/// static-obstacle query. All coordinates use the same map-local space; the query adapter applies
/// the map-to-scene transform. Heading is CCW from +Y, and footprint is (width, length).
/// </summary>
public static class RoadFootprintClearance {
    /// <summary>
    /// Checks the entire rectangle, including its rotated local collider offset. Missing queries,
    /// non-finite input and unset/invalid bounds fail closed. Touching an exclusion is blocked.
    /// The query must check the entire supplied rectangle against the intended blocking geometry.
    /// </summary>
    public static bool IsPoseClear(Vector2 pivot, Vector2 footprint, Vector2 localOffset, float headingDegrees,
        Rect localBounds, IReadOnlyList<NoSpawnRegion> exclusions, IAreaClearanceQuery query) {
        if (query == null || !IsFinite(pivot) || !IsFinite(localOffset) || !IsFinite(headingDegrees) ||
            !IsFinite(footprint) || footprint.x <= 0f || footprint.y <= 0f || !IsValidRect(localBounds)) return false;

        Vector2 right = Rotate(Vector2.right, headingDegrees);
        Vector2 forward = Rotate(Vector2.up, headingDegrees);
        Vector2 center = pivot + right * localOffset.x + forward * localOffset.y;
        Vector2 half = footprint * 0.5f;
        Vector2 extent = new Vector2(Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y,
            Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y);
        if (!IsFinite(center) || !IsFinite(extent) || center.x - extent.x < localBounds.xMin ||
            center.x + extent.x > localBounds.xMax || center.y - extent.y < localBounds.yMin ||
            center.y + extent.y > localBounds.yMax) return false;

        if (exclusions != null) foreach (var exclusion in exclusions) {
            if (exclusion == null || !IsValidRect(exclusion.area)) return false;
            if (Overlaps(center, half, right, forward, exclusion.area)) return false;
        }
        return query.IsAreaClear(center, footprint, headingDegrees);
    }

    /// <summary>
    /// Checks a continuous swept envelope without sampling gaps. Fixed-heading translation uses
    /// an enclosing oriented box (exact for motion along either local axis). A changing heading
    /// uses a conservative box enclosing every rotation about the moving pivot, including offset.
    /// This may reject tight but feasible turns; it never claims a drivable steering trajectory.
    /// </summary>
    public static bool IsSweepClear(Vector2 startPivot, Vector2 endPivot, Vector2 footprint, Vector2 localOffset,
        float startHeadingDegrees, float endHeadingDegrees, Rect localBounds,
        IReadOnlyList<NoSpawnRegion> exclusions, IAreaClearanceQuery query) {
        if (!IsFinite(startPivot) || !IsFinite(endPivot) || !IsFinite(footprint) || !IsFinite(localOffset) ||
            footprint.x <= 0f || footprint.y <= 0f || !IsFinite(startHeadingDegrees) || !IsFinite(endHeadingDegrees)) return false;

        Vector2 delta = endPivot - startPivot;
        Vector2 middle = startPivot * 0.5f + endPivot * 0.5f;
        if (Mathf.DeltaAngle(startHeadingDegrees, endHeadingDegrees) == 0f) {
            Vector2 localDelta = Rotate(delta, -startHeadingDegrees);
            Vector2 sweptSize = footprint + new Vector2(Mathf.Abs(localDelta.x), Mathf.Abs(localDelta.y));
            return IsPoseClear(middle, sweptSize, localOffset, startHeadingDegrees, localBounds, exclusions, query);
        }

        float radius = localOffset.magnitude + footprint.magnitude * 0.5f;
        Vector2 rotatingSize = new Vector2(Mathf.Abs(delta.x) + 2f * radius, Mathf.Abs(delta.y) + 2f * radius);
        return IsPoseClear(middle, rotatingSize, Vector2.zero, 0f, localBounds, exclusions, query);
    }

    // Separating-axis test for an oriented footprint against an axis-aligned exclusion rectangle.
    static bool Overlaps(Vector2 center, Vector2 half, Vector2 right, Vector2 forward, Rect area) {
        Vector2 delta = area.center - center;
        Vector2 areaHalf = area.size * 0.5f;
        if (Mathf.Abs(delta.x) > areaHalf.x + Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y) return false;
        if (Mathf.Abs(delta.y) > areaHalf.y + Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y) return false;
        if (Mathf.Abs(Vector2.Dot(delta, right)) > half.x + Mathf.Abs(right.x) * areaHalf.x + Mathf.Abs(right.y) * areaHalf.y) return false;
        if (Mathf.Abs(Vector2.Dot(delta, forward)) > half.y + Mathf.Abs(forward.x) * areaHalf.x + Mathf.Abs(forward.y) * areaHalf.y) return false;
        return true;
    }

    static Vector2 Rotate(Vector2 vector, float degrees) {
        float radians = degrees * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
        return new Vector2(cosine * vector.x - sine * vector.y, sine * vector.x + cosine * vector.y);
    }

    static bool IsValidRect(Rect rect) => IsFinite(rect.position) && IsFinite(rect.size) &&
        IsFinite(rect.xMax) && IsFinite(rect.yMax) && rect.width > 0f && rect.height > 0f;

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
