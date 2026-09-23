using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>Cached, budgeted spatial projection index over complete directed road-edge polylines.</summary>
public sealed class RoadSegmentSpatialIndex {
    /// <summary>Terminal state returned by index construction.</summary>
    public enum BuildStatus {
        /// <summary>A complete index was published.</summary>
        Built,
        /// <summary>Input or geometry was invalid.</summary>
        InvalidInput,
        /// <summary>The caller-owned build budget was exhausted.</summary>
        BudgetExceeded
    }

    /// <summary>Terminal state returned by a projection query.</summary>
    public enum QueryStatus {
        /// <summary>One or more candidates were returned.</summary>
        Found,
        /// <summary>The valid query found no segment within its radius.</summary>
        NoCandidates,
        /// <summary>Query input was invalid.</summary>
        InvalidInput,
        /// <summary>The caller-owned query budget was exhausted.</summary>
        BudgetExceeded,
        /// <summary>The held index no longer matches its graph version.</summary>
        StaleGraph
    }

    /// <summary>Stable projection result for one directed normalized polyline segment.</summary>
    public readonly struct ProjectionCandidate {
        /// <summary>Directed authored edge identifier.</summary>
        public readonly string edgeId;
        /// <summary>Zero-based segment index in the edge's normalized full polyline.</summary>
        public readonly int segmentIndex;
        /// <summary>Arc distance from the edge start to the projected point.</summary>
        public readonly float distanceAlongEdge;
        /// <summary>Closest point on the segment.</summary>
        public readonly Vector2 point;
        /// <summary>Unit direction from the segment start toward its end.</summary>
        public readonly Vector2 direction;
        /// <summary>Squared distance from the query point.</summary>
        public readonly float distanceSquared;

        /// <summary>Creates a projection candidate.</summary>
        public ProjectionCandidate(string edgeId, int segmentIndex, float distanceAlongEdge, Vector2 point, Vector2 direction, float distanceSquared) {
            this.edgeId = edgeId;
            this.segmentIndex = segmentIndex;
            this.distanceAlongEdge = distanceAlongEdge;
            this.point = point;
            this.direction = direction;
            this.distanceSquared = distanceSquared;
        }
    }

    /// <summary>Complete projection query result with no partial candidates on failure.</summary>
    public sealed class QueryResult {
        /// <summary>Query terminal status.</summary>
        public QueryStatus status;
        /// <summary>Stable sorted candidates; empty unless status is Found.</summary>
        public IReadOnlyList<ProjectionCandidate> candidates;
    }

    sealed class SegmentRecord {
        public string edgeId;
        public int segmentIndex;
        public Vector2 start;
        public Vector2 end;
        public Vector2 direction;
        public float startDistance;
        public float length;
    }

    struct Cell : IEquatable<Cell> {
        public int x;
        public int y;
        public bool Equals(Cell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => x * 397 ^ y;
    }

    sealed class CacheEntry {
        public long version;
        public float cellSize;
        public RoadSegmentSpatialIndex index;
    }

    static readonly ConditionalWeakTable<RoadGraphRuntime, CacheEntry> cache = new ConditionalWeakTable<RoadGraphRuntime, CacheEntry>();
    static int successfulBuildCount;
    readonly RoadGraphRuntime graph;
    readonly float cellSize;
    readonly Dictionary<Cell, List<SegmentRecord>> segmentsByCell;
    readonly List<SegmentRecord> allSegments;

    /// <summary>Number of successfully published index builds in this process.</summary>
    public static int SuccessfulBuildCount => successfulBuildCount;

    /// <summary>Graph version captured when this index was built.</summary>
    public long GraphVersion { get; }

    /// <summary>Cell size captured when this index was built.</summary>
    public float CellSize => cellSize;

    RoadSegmentSpatialIndex(RoadGraphRuntime graph, float cellSize, Dictionary<Cell, List<SegmentRecord>> segmentsByCell,
        List<SegmentRecord> allSegments) {
        this.graph = graph;
        GraphVersion = graph.Version;
        this.cellSize = cellSize;
        this.segmentsByCell = segmentsByCell;
        this.allSegments = allSegments;
    }

    /// <summary>Returns a successful cached index or builds and publishes one atomically.</summary>
    /// <param name="graph">Graph whose cached full polylines are indexed.</param>
    /// <param name="cellSize">Finite positive map-local cell size.</param>
    /// <param name="budget">Caller-owned construction budget.</param>
    /// <param name="index">Published complete index, or null on failure.</param>
    /// <param name="status">Build outcome.</param>
    /// <returns>True only when a complete index is available.</returns>
    public static bool TryGetOrBuild(RoadGraphRuntime graph, float cellSize, RoadPathQuery.SearchBudget budget,
        out RoadSegmentSpatialIndex index, out BuildStatus status) {
        index = null;
        status = BuildStatus.InvalidInput;
        if (graph == null || budget == null || budget.maximumWork <= 0 || !IsFinitePositive(cellSize)) return false;
        long version = graph.Version;
        var entry = cache.GetValue(graph, key => new CacheEntry());
        if (entry.index != null && entry.version == version && entry.cellSize == cellSize) {
            index = entry.index;
            status = BuildStatus.Built;
            return true;
        }

        var cells = new Dictionary<Cell, List<SegmentRecord>>();
        var allSegments = new List<SegmentRecord>();
        foreach (var edge in graph.Edges) {
            if (!budget.TryConsume(1)) { status = BuildStatus.BudgetExceeded; return false; }
            if (edge == null) { status = BuildStatus.InvalidInput; return false; }
            var geometry = graph.GetGeometry(edge.edgeId);
            if (geometry == null || geometry.Points == null || geometry.CumulativeLengths == null || geometry.Points.Count < 2 ||
                geometry.CumulativeLengths.Count != geometry.Points.Count || !IsFinite(geometry.Length) || geometry.Length <= 0f) {
                status = BuildStatus.InvalidInput;
                return false;
            }
            for (int segmentIndex = 0; segmentIndex < geometry.Points.Count - 1; segmentIndex++) {
                if (!budget.TryConsume(1)) { status = BuildStatus.BudgetExceeded; return false; }
                Vector2 start = geometry.Points[segmentIndex];
                Vector2 end = geometry.Points[segmentIndex + 1];
                Vector2 delta = end - start;
                float length = delta.magnitude;
                if (!IsFinite(start) || !IsFinite(end) || !IsFinite(length) || length <= 0f) {
                    status = BuildStatus.InvalidInput;
                    return false;
                }
                var segment = new SegmentRecord {
                    edgeId = edge.edgeId,
                    segmentIndex = segmentIndex,
                    start = start,
                    end = end,
                    direction = delta / length,
                    startDistance = geometry.CumulativeLengths[segmentIndex],
                    length = length
                };
                allSegments.Add(segment);
                if (!TryGetCellRange(start, end, cellSize, out int minX, out int maxX, out int minY, out int maxY)) {
                    status = BuildStatus.InvalidInput;
                    return false;
                }
                long cellCount = (long)maxX - minX + 1L;
                long rowCount = (long)maxY - minY + 1L;
                if (cellCount <= 0L || rowCount <= 0L) {
                    status = BuildStatus.BudgetExceeded;
                    return false;
                }
                long requiredCells = cellCount > long.MaxValue / rowCount ? long.MaxValue : cellCount * rowCount;
                if (requiredCells > budget.RemainingWork) {
                    status = BuildStatus.BudgetExceeded;
                    return false;
                }
                for (int x = minX; ; x++) {
                    for (int y = minY; ; y++) {
                        if (!budget.TryConsume(1)) { status = BuildStatus.BudgetExceeded; return false; }
                        var cell = new Cell { x = x, y = y };
                        if (!cells.TryGetValue(cell, out var list)) {
                            list = new List<SegmentRecord>();
                            cells[cell] = list;
                        }
                        list.Add(segment);
                        if (y == maxY) break;
                    }
                    if (x == maxX) break;
                }
            }
        }
        index = new RoadSegmentSpatialIndex(graph, cellSize, cells, allSegments);
        entry.version = version;
        entry.cellSize = cellSize;
        entry.index = index;
        successfulBuildCount++;
        status = BuildStatus.Built;
        return true;
    }

    /// <summary>Projects a point onto indexed full segments within a finite search radius.</summary>
    /// <param name="position">Finite map-local query position.</param>
    /// <param name="searchRadius">Finite nonnegative radius.</param>
    /// <param name="budget">Caller-owned aggregate query budget.</param>
    /// <returns>Explicit query status and stable sorted candidates.</returns>
    public QueryResult Query(Vector2 position, float searchRadius, RoadPathQuery.SearchBudget budget) {
        var empty = new List<ProjectionCandidate>();
        if (graph.Version != GraphVersion) return NewQueryResult(QueryStatus.StaleGraph, empty);
        if (budget == null || budget.maximumWork <= 0 || !IsFinite(position) || !IsFinite(searchRadius) || searchRadius < 0f) return NewQueryResult(QueryStatus.InvalidInput, empty);
        if (!TryGetCellRange(position - Vector2.one * searchRadius, position + Vector2.one * searchRadius, cellSize, out int minX, out int maxX, out int minY, out int maxY)) return NewQueryResult(QueryStatus.InvalidInput, empty);
        long cellCount = (long)maxX - minX + 1L; long rowCount = (long)maxY - minY + 1L;
        if (cellCount <= 0L || rowCount <= 0L) return NewQueryResult(QueryStatus.InvalidInput, empty);
        long requiredCells = cellCount > long.MaxValue / rowCount ? long.MaxValue : cellCount * rowCount;
        if (requiredCells > budget.RemainingWork) return NewQueryResult(QueryStatus.BudgetExceeded, empty);
        var seen = new HashSet<SegmentRecord>();
        var candidates = new List<ProjectionCandidate>();
        float radiusSquared = searchRadius * searchRadius;
        if (!IsFinite(radiusSquared)) return NewQueryResult(QueryStatus.InvalidInput, empty);
        for (int x = minX; ; x++) {
            for (int y = minY; ; y++) {
                if (!budget.TryConsume(1)) return NewQueryResult(QueryStatus.BudgetExceeded, empty);
                if (!segmentsByCell.TryGetValue(new Cell { x = x, y = y }, out var segments)) {
                    if (y == maxY) break;
                    continue;
                }
                foreach (var segment in segments) {
                    if (!budget.TryConsume(1)) return NewQueryResult(QueryStatus.BudgetExceeded, empty);
                    if (!seen.Add(segment)) continue;
                    Vector2 delta = segment.end - segment.start;
                    float denominator = delta.sqrMagnitude;
                    if (!IsFinite(denominator) || denominator <= 0f) return NewQueryResult(QueryStatus.InvalidInput, empty);
                    float t = Mathf.Clamp01(Vector2.Dot(position - segment.start, delta) / denominator);
                    Vector2 point = segment.start + delta * t;
                    float distanceSquared = (position - point).sqrMagnitude;
                    float distanceAlongEdge = segment.startDistance + segment.length * t;
                    if (!IsFinite(t) || !IsFinite(point) || !IsFinite(distanceSquared) || !IsFinite(distanceAlongEdge)) return NewQueryResult(QueryStatus.InvalidInput, empty);
                    if (distanceSquared <= radiusSquared) candidates.Add(new ProjectionCandidate(segment.edgeId, segment.segmentIndex, distanceAlongEdge, point, segment.direction, distanceSquared));
                }
                if (y == maxY) break;
            }
            if (x == maxX) break;
        }
        candidates.Sort(CompareCandidates);
        return candidates.Count == 0 ? NewQueryResult(QueryStatus.NoCandidates, empty) : NewQueryResult(QueryStatus.Found, candidates);
    }

    /// <summary>Projects a point onto every unique indexed segment without scanning empty cells.</summary>
    /// <param name="position">Finite map-local query position.</param>
    /// <param name="budget">Caller-owned aggregate query budget.</param>
    /// <returns>Explicit query status and stable sorted candidates.</returns>
    public QueryResult QueryAll(Vector2 position, RoadPathQuery.SearchBudget budget) {
        var empty = new List<ProjectionCandidate>();
        if (graph.Version != GraphVersion) return NewQueryResult(QueryStatus.StaleGraph, empty);
        if (budget == null || budget.maximumWork <= 0 || !IsFinite(position)) return NewQueryResult(QueryStatus.InvalidInput, empty);
        var candidates = new List<ProjectionCandidate>();
        foreach (var segment in allSegments) {
            if (!budget.TryConsume(1)) return NewQueryResult(QueryStatus.BudgetExceeded, empty);
            Vector2 delta = segment.end - segment.start;
            float denominator = delta.sqrMagnitude;
            if (!IsFinite(denominator) || denominator <= 0f) return NewQueryResult(QueryStatus.InvalidInput, empty);
            float t = Mathf.Clamp01(Vector2.Dot(position - segment.start, delta) / denominator);
            Vector2 point = segment.start + delta * t;
            float distanceSquared = (position - point).sqrMagnitude;
            float distanceAlongEdge = segment.startDistance + segment.length * t;
            if (!IsFinite(t) || !IsFinite(point) || !IsFinite(distanceSquared) || !IsFinite(distanceAlongEdge))
                return NewQueryResult(QueryStatus.InvalidInput, empty);
            candidates.Add(new ProjectionCandidate(segment.edgeId, segment.segmentIndex, distanceAlongEdge, point,
                segment.direction, distanceSquared));
        }
        candidates.Sort(CompareCandidates);
        return candidates.Count == 0 ? NewQueryResult(QueryStatus.NoCandidates, empty) : NewQueryResult(QueryStatus.Found, candidates);
    }

    static QueryResult NewQueryResult(QueryStatus status, IReadOnlyList<ProjectionCandidate> candidates) {
        return new QueryResult { status = status, candidates = candidates };
    }

    static int CompareCandidates(ProjectionCandidate first, ProjectionCandidate second) {
        int distance = first.distanceSquared.CompareTo(second.distanceSquared);
        if (distance != 0) return distance;
        int edge = string.CompareOrdinal(first.edgeId, second.edgeId);
        return edge != 0 ? edge : first.segmentIndex.CompareTo(second.segmentIndex);
    }

    static bool TryGetCellRange(Vector2 first, Vector2 second, float cellSize, out int minX, out int maxX, out int minY, out int maxY) {
        minX = maxX = minY = maxY = 0;
        if (!IsFinite(first) || !IsFinite(second) || !IsFinitePositive(cellSize)) return false;
        double rawMinX = Math.Floor(Math.Min(first.x, second.x) / cellSize);
        double rawMaxX = Math.Floor(Math.Max(first.x, second.x) / cellSize);
        double rawMinY = Math.Floor(Math.Min(first.y, second.y) / cellSize);
        double rawMaxY = Math.Floor(Math.Max(first.y, second.y) / cellSize);
        if (rawMinX < int.MinValue || rawMaxX > int.MaxValue || rawMinY < int.MinValue || rawMaxY > int.MaxValue) return false;
        minX = (int)rawMinX; maxX = (int)rawMaxX; minY = (int)rawMinY; maxY = (int)rawMaxY;
        return true;
    }

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
}
