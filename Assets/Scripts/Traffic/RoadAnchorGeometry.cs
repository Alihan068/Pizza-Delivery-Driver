using UnityEngine;

/// <summary>Small charged geometry adapter for directed graph anchors and clipped suffixes.</summary>
public static class RoadAnchorGeometry {
    /// <summary>Samples a cached normalized edge polyline at a finite arc distance.</summary>
    public static bool TryGetPoint(RoadGraphRuntime graph, string edgeId, float distance, RoadPathQuery.SearchBudget budget,
        out Vector2 point, out Vector2 direction) {
        point = Vector2.zero;
        direction = Vector2.zero;
        if (graph == null || budget == null || !budget.TryConsume(1)) return false;
        var geometry = graph.GetGeometry(edgeId);
        if (geometry == null || geometry.Points.Count < 2 || !Finite(distance) || distance < 0f || distance > geometry.Length) return false;
        for (int i = 1; i < geometry.CumulativeLengths.Count; i++) {
            if (!budget.TryConsume(1)) return false;
            float start = geometry.CumulativeLengths[i - 1];
            float end = geometry.CumulativeLengths[i];
            if (distance <= end) {
                float segmentLength = end - start;
                if (!Finite(segmentLength) || segmentLength <= 0f) return false;
                float t = Mathf.Clamp01((distance - start) / segmentLength);
                point = Vector2.Lerp(geometry.Points[i - 1], geometry.Points[i], t);
                direction = (geometry.Points[i] - geometry.Points[i - 1]).normalized;
                return Finite(point) && Finite(direction) && direction.sqrMagnitude > 0f;
            }
        }
        return false;
    }

    static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
