using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Road-width and corner prefilters plus complete spawn/swept-envelope clearance checks. The full
/// checks require an injected static-obstacle query; width/radius estimates alone do not certify
/// collision clearance. Coordinates are map-local, +Y forward, heading CCW from +Y.
/// </summary>
public static class RoadClearanceChecker {
    /// <summary>Whether a vehicle's clearance width fits within an edge's authored usable width, with a safety margin.</summary>
    /// <param name="edge">Edge to check against.</param>
    /// <param name="vehicleClearanceWidth">Vehicle's footprint width.</param>
    /// <param name="safetyMargin">Extra clearance required beyond the vehicle's own width.</param>
    public static bool FitsEdgeWidth(RoadEdgeRecord edge, float vehicleClearanceWidth, float safetyMargin) {
        if (edge == null || !IsFinite(edge.usableWidth) || !IsFinite(vehicleClearanceWidth) ||
            vehicleClearanceWidth <= 0f || !IsFinite(safetyMargin) || safetyMargin < 0f) return false;
        return vehicleClearanceWidth + safetyMargin <= edge.usableWidth;
    }

    /// <summary>
    /// Estimates the sharpest corner radius along an edge's full polyline (from-node, interior
    /// points, to-node) using a circular-arc approximation at each interior vertex.
    /// </summary>
    /// <returns>The smallest estimated corner radius, or <see cref="float.PositiveInfinity"/> when the edge has no sharp corner.</returns>
    public static float EstimateSharpestCornerRadius(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode) {
        if (fromNode == null || edge == null || toNode == null) return 0f;

        var points = new List<Vector2> { new Vector2(fromNode.x, fromNode.y) };
        if (edge.orderedPoints != null) points.AddRange(edge.orderedPoints);
        points.Add(new Vector2(toNode.x, toNode.y));

        float sharpest = float.PositiveInfinity;
        for (int i = 1; i < points.Count - 1; i++) {
            Vector2 incoming = points[i] - points[i - 1];
            Vector2 outgoing = points[i + 1] - points[i];
            if (incoming.sqrMagnitude <= 0.0001f || outgoing.sqrMagnitude <= 0.0001f) continue;

            float turnAngleDegrees = Vector2.Angle(incoming, outgoing);
            if (turnAngleDegrees < 0.5f) continue; // effectively straight, no radius constraint

            float shorterSegmentLength = Mathf.Min(incoming.magnitude, outgoing.magnitude);
            float radius = shorterSegmentLength / (2f * Mathf.Sin(turnAngleDegrees * Mathf.Deg2Rad / 2f));
            if (radius < sharpest) sharpest = radius;
        }
        return sharpest;
    }

    /// <summary>Whether a vehicle's minimum turning radius is small enough to take every corner along an edge.</summary>
    public static bool FitsTurningRadius(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode, float vehicleMinimumTurningRadius) {
        return EstimateSharpestCornerRadius(fromNode, edge, toNode) >= vehicleMinimumTurningRadius;
    }

    /// <summary>Legacy width prefilter only. Use <see cref="IsSpawnClear"/> to check length, offset, bounds and obstacles.</summary>
    public static bool FitsSpawnFootprint(VehicleSpawnRecord spawn, RoadEdgeRecord edge, float safetyMargin) {
        if (spawn == null || edge == null) return false;
        return FitsEdgeWidth(edge, spawn.clearanceWidth, safetyMargin);
    }

    /// <summary>
    /// Checks a spawn's width/length envelope at its arc-length attachment, oriented along its
    /// edge. Local offset is measured from vehicle pivot to collider center. Safety margin adds
    /// to both full dimensions (half on each side). Missing geometry/query fails closed.
    /// </summary>
    public static bool IsSpawnClear(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode,
        VehicleSpawnRecord spawn, Vector2 localOffset, float safetyMargin, Rect localBounds,
        IReadOnlyList<NoSpawnRegion> exclusions, IAreaClearanceQuery query) {
        if (spawn == null || edge == null || spawn.edgeId != edge.edgeId ||
            !IsFinite(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f) return false;
        Vector2 footprint = new Vector2(spawn.clearanceWidth, spawn.clearanceLength);
        if (!TryPadFootprint(edge, footprint, localOffset, safetyMargin, out var padded) ||
            !TryGetPoints(fromNode, edge, toNode, out var points, out float length) || spawn.distanceAlongEdge > length) return false;

        float remaining = spawn.distanceAlongEdge;
        for (int i = 1; i < points.Count; i++) {
            Vector2 delta = points[i] - points[i - 1];
            float segmentLength = delta.magnitude;
            if (segmentLength == 0f) continue;
            if (remaining <= segmentLength || i == points.Count - 1) {
                Vector2 pivot = points[i - 1] + delta * Mathf.Clamp01(remaining / segmentLength);
                float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(delta);
                return RoadFootprintClearance.IsPoseClear(pivot, padded, localOffset, heading, localBounds, exclusions, query);
            }
            remaining -= segmentLength;
        }
        return false;
    }

    /// <summary>
    /// Checks the complete polyline against bounds, exclusions and static geometry using continuous
    /// swept rectangles, including conservative rotations at interior vertices. Duplicate points
    /// are skipped. This verifies the chosen envelope, not a physically feasible steering curve.
    /// </summary>
    public static bool IsEdgeSweepClear(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode,
        Vector2 footprint, Vector2 localOffset, float safetyMargin, Rect localBounds,
        IReadOnlyList<NoSpawnRegion> exclusions, IAreaClearanceQuery query) {
        if (!TryPadFootprint(edge, footprint, localOffset, safetyMargin, out var padded) ||
            !TryGetPoints(fromNode, edge, toNode, out var points, out _)) return false;
        bool haveHeading = false;
        float previousHeading = 0f;
        for (int i = 1; i < points.Count; i++) {
            Vector2 delta = points[i] - points[i - 1];
            if (delta.sqrMagnitude == 0f) continue;
            float heading = MapNavigationCoordinates.DirectionToHeadingDegrees(delta);
            if (haveHeading && !RoadFootprintClearance.IsSweepClear(points[i - 1], points[i - 1], padded,
                localOffset, previousHeading, heading, localBounds, exclusions, query)) return false;
            if (!RoadFootprintClearance.IsSweepClear(points[i - 1], points[i], padded, localOffset,
                heading, heading, localBounds, exclusions, query)) return false;
            haveHeading = true;
            previousHeading = heading;
        }
        return haveHeading;
    }

    static bool TryPadFootprint(RoadEdgeRecord edge, Vector2 footprint, Vector2 localOffset, float safetyMargin, out Vector2 padded) {
        padded = footprint + Vector2.one * safetyMargin;
        if (!IsFinite(footprint.x) || !IsFinite(footprint.y) || footprint.x <= 0f || footprint.y <= 0f ||
            !IsFinite(localOffset.x) || !IsFinite(localOffset.y) || !IsFinite(padded.x) || !IsFinite(padded.y)) return false;
        return FitsEdgeWidth(edge, footprint.x + 2f * Mathf.Abs(localOffset.x), safetyMargin);
    }

    static bool TryGetPoints(RoadNodeRecord fromNode, RoadEdgeRecord edge, RoadNodeRecord toNode,
        out List<Vector2> points, out float length) {
        points = null;
        length = 0f;
        if (fromNode == null || edge == null || toNode == null || string.IsNullOrWhiteSpace(edge.edgeId) ||
            string.IsNullOrWhiteSpace(fromNode.nodeId) || string.IsNullOrWhiteSpace(toNode.nodeId) ||
            edge.fromNodeId != fromNode.nodeId || edge.toNodeId != toNode.nodeId) return false;
        points = new List<Vector2> { new Vector2(fromNode.x, fromNode.y) };
        if (edge.orderedPoints != null) points.AddRange(edge.orderedPoints);
        points.Add(new Vector2(toNode.x, toNode.y));
        for (int i = 0; i < points.Count; i++) {
            if (!IsFinite(points[i].x) || !IsFinite(points[i].y)) return false;
            if (i > 0) length += Vector2.Distance(points[i - 1], points[i]);
        }
        return IsFinite(length) && length > 0f;
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
