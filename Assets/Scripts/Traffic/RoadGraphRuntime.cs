using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>
/// Indexed runtime view of one <see cref="MapNavigationDocument"/>'s road graph: id lookups,
/// per-node outgoing-edge adjacency, and precomputed arc lengths. Shared edit commands invalidate
/// every view of their document; the next lookup rebuilds it once, without per-frame map scans.
/// </summary>
public class RoadGraphRuntime {
    /// <summary>Immutable-by-convention geometry snapshot cached for one indexed edge version.</summary>
    public sealed class EdgeGeometrySnapshot {
        readonly ReadOnlyCollection<Vector2> points;
        readonly ReadOnlyCollection<float> cumulativeLengths;

        internal EdgeGeometrySnapshot(List<Vector2> points, List<float> cumulativeLengths, float length) {
            this.points = new ReadOnlyCollection<Vector2>(points);
            this.cumulativeLengths = new ReadOnlyCollection<float>(cumulativeLengths);
            Length = length;
        }

        /// <summary>Normalized full polyline including authoritative start and end node positions.</summary>
        public IReadOnlyList<Vector2> Points => points;
        /// <summary>Cumulative arc lengths aligned with <see cref="Points"/>.</summary>
        public IReadOnlyList<float> CumulativeLengths => cumulativeLengths;
        /// <summary>Total normalized polyline length.</summary>
        public float Length { get; }
    }

    readonly MapNavigationDocument document;
    long observedRevision;
    long version;
    readonly Dictionary<string, RoadNodeRecord> nodesById = new Dictionary<string, RoadNodeRecord>();
    readonly Dictionary<string, RoadEdgeRecord> edgesById = new Dictionary<string, RoadEdgeRecord>();
    readonly Dictionary<string, JunctionRecord> junctionsById = new Dictionary<string, JunctionRecord>();
    readonly Dictionary<string, List<RoadEdgeRecord>> outgoingEdgesByNode = new Dictionary<string, List<RoadEdgeRecord>>();
    readonly Dictionary<string, float> arcLengthByEdge = new Dictionary<string, float>();
    readonly Dictionary<string, EdgeGeometrySnapshot> geometryByEdge = new Dictionary<string, EdgeGeometrySnapshot>();
    ReadOnlyCollection<RoadEdgeRecord> edgeSnapshot = new ReadOnlyCollection<RoadEdgeRecord>(new List<RoadEdgeRecord>());

    static readonly IReadOnlyList<RoadEdgeRecord> EmptyEdgeList = new List<RoadEdgeRecord>();

    /// <summary>Monotonically increasing indexed-geometry version; every explicit rebuild increments it.</summary>
    public long Version {
        get {
            EnsureCurrent();
            return version;
        }
    }

    /// <summary>Read-only snapshot of the valid indexed edge records for the current version.</summary>
    public IReadOnlyList<RoadEdgeRecord> Edges {
        get {
            EnsureCurrent();
            return edgeSnapshot;
        }
    }

    /// <summary>Builds every index from a document in one pass. Safe to call with a null/empty document; the runtime is simply empty.</summary>
    public RoadGraphRuntime(MapNavigationDocument document) {
        this.document = document;
        Rebuild();
    }

    /// <summary>
    /// Rebuilds all indexes together. Call after direct DTO writes outside the shared edit commands.
    /// Records are copied so unannounced source writes cannot mix new geometry with old cached costs.
    /// Returned records are read-only by convention; author changes through the source document.
    /// </summary>
    public void Rebuild() {
        version++;
        nodesById.Clear();
        edgesById.Clear();
        junctionsById.Clear();
        outgoingEdgesByNode.Clear();
        arcLengthByEdge.Clear();
        geometryByEdge.Clear();
        var rebuiltEdges = new List<RoadEdgeRecord>();
        observedRevision = NavigationDocumentRevision.Get(document);
        if (document == null) {
            edgeSnapshot = new ReadOnlyCollection<RoadEdgeRecord>(rebuiltEdges);
            return;
        }

        if (document.nodes != null) {
            foreach (var node in document.nodes) {
                if (node != null && !string.IsNullOrEmpty(node.nodeId)) {
                    nodesById[node.nodeId] = new RoadNodeRecord { nodeId = node.nodeId, x = node.x, y = node.y };
                }
            }
        }
        if (document.junctions != null) {
            foreach (var junction in document.junctions) {
                if (junction == null || string.IsNullOrEmpty(junction.junctionId)) continue;
                var copy = new JunctionRecord { junctionId = junction.junctionId, conflictZone = junction.conflictZone };
                if (junction.allowedTransitions == null) copy.allowedTransitions = null;
                else foreach (var transition in junction.allowedTransitions) {
                    copy.allowedTransitions.Add(transition == null ? null : new JunctionTransition {
                        fromEdgeId = transition.fromEdgeId, toEdgeId = transition.toEdgeId, priority = transition.priority
                    });
                }
                junctionsById[junction.junctionId] = copy;
            }
        }
        if (document.edges != null) {
            foreach (var source in document.edges) {
                if (source == null || string.IsNullOrEmpty(source.edgeId) || string.IsNullOrEmpty(source.fromNodeId)) continue;
                var edge = new RoadEdgeRecord {
                    edgeId = source.edgeId, fromNodeId = source.fromNodeId, toNodeId = source.toNodeId,
                    usableWidth = source.usableWidth, speedLimit = source.speedLimit,
                    startJunctionId = source.startJunctionId, endJunctionId = source.endJunctionId,
                    orderedPoints = source.orderedPoints == null ? null : new List<Vector2>(source.orderedPoints),
                    allowedRoles = source.allowedRoles == null ? null : new List<VehicleRole>(source.allowedRoles)
                };
                edgesById[edge.edgeId] = edge;
                rebuiltEdges.Add(edge);
                var geometry = BuildGeometry(edge);
                geometryByEdge[edge.edgeId] = geometry;
                arcLengthByEdge[edge.edgeId] = geometry.Length;
                if (!outgoingEdgesByNode.TryGetValue(edge.fromNodeId, out var list)) {
                    list = new List<RoadEdgeRecord>();
                    outgoingEdgesByNode[edge.fromNodeId] = list;
                }
                list.Add(edge);
            }
        }
        edgeSnapshot = new ReadOnlyCollection<RoadEdgeRecord>(rebuiltEdges);
    }

    /// <summary>Returns the current indexed node, or null. Treat the returned record as read-only.</summary>
    public RoadNodeRecord GetNode(string nodeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(nodeId) && nodesById.TryGetValue(nodeId, out var node) ? node : null;
    }

    /// <summary>Returns the current indexed edge, or null. Treat the returned record as read-only.</summary>
    public RoadEdgeRecord GetEdge(string edgeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(edgeId) && edgesById.TryGetValue(edgeId, out var edge) ? edge : null;
    }

    /// <summary>Returns the current indexed junction, or null. Treat its transitions as read-only.</summary>
    public JunctionRecord GetJunction(string junctionId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(junctionId) && junctionsById.TryGetValue(junctionId, out var junction) ? junction : null;
    }

    /// <summary>Returns arc length from the same document revision as the indexed geometry; unknown IDs return zero.</summary>
    public float GetArcLength(string edgeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(edgeId) && arcLengthByEdge.TryGetValue(edgeId, out var length) ? length : 0f;
    }

    /// <summary>Returns the cached normalized full polyline for one edge, or null for an unknown edge.</summary>
    public IReadOnlyList<Vector2> GetPolyline(string edgeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(edgeId) && geometryByEdge.TryGetValue(edgeId, out var geometry) ? geometry.Points : null;
    }

    /// <summary>Returns cached cumulative arc lengths aligned with <see cref="GetPolyline"/>, or null when unknown.</summary>
    public IReadOnlyList<float> GetCumulativeLengths(string edgeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(edgeId) && geometryByEdge.TryGetValue(edgeId, out var geometry) ? geometry.CumulativeLengths : null;
    }

    /// <summary>Returns the complete cached geometry snapshot for one edge, or null when unknown.</summary>
    public EdgeGeometrySnapshot GetGeometry(string edgeId) {
        EnsureCurrent();
        return !string.IsNullOrEmpty(edgeId) && geometryByEdge.TryGetValue(edgeId, out var geometry) ? geometry : null;
    }

    /// <summary>Edges leaving a node, in authoring order. Never null.</summary>
    public IReadOnlyList<RoadEdgeRecord> GetOutgoingEdges(string nodeId) {
        EnsureCurrent();
        if (!string.IsNullOrEmpty(nodeId) && outgoingEdgesByNode.TryGetValue(nodeId, out var list)) return list;
        return EmptyEdgeList;
    }

    void EnsureCurrent() {
        if (observedRevision != NavigationDocumentRevision.Get(document)) Rebuild();
    }

    EdgeGeometrySnapshot BuildGeometry(RoadEdgeRecord edge) {
        var fromNode = GetNodeWithoutEnsure(edge.fromNodeId);
        var toNode = GetNodeWithoutEnsure(edge.toNodeId);
        if (fromNode == null || toNode == null) return EmptyGeometry();
        var points = new List<Vector2>();
        AddDistinctPoint(points, new Vector2(fromNode.x, fromNode.y));
        if (edge.orderedPoints != null) foreach (var point in edge.orderedPoints) AddDistinctPoint(points, point);
        AddDistinctPoint(points, new Vector2(toNode.x, toNode.y));
        foreach (var point in points) if (!IsFinite(point)) return EmptyGeometry();

        var cumulative = new List<float>(points.Count) { 0f };
        float length = 0f;
        for (int i = 1; i < points.Count; i++) {
            length += Vector2.Distance(points[i - 1], points[i]);
            cumulative.Add(length);
        }
        return new EdgeGeometrySnapshot(points, cumulative, length);
    }

    static EdgeGeometrySnapshot EmptyGeometry() => new EdgeGeometrySnapshot(new List<Vector2>(), new List<float>(), 0f);

    static bool IsFinite(Vector2 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y);

    RoadNodeRecord GetNodeWithoutEnsure(string nodeId) {
        return !string.IsNullOrEmpty(nodeId) && nodesById.TryGetValue(nodeId, out var node) ? node : null;
    }

    static void AddDistinctPoint(List<Vector2> points, Vector2 point) {
        if (points.Count == 0 || points[points.Count - 1].x != point.x || points[points.Count - 1].y != point.y) points.Add(point);
    }
}
