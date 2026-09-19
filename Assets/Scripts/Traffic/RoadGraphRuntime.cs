using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Indexed runtime view of one <see cref="MapNavigationDocument"/>'s road graph: id lookups,
/// per-node outgoing-edge adjacency, and precomputed arc lengths. Shared edit commands invalidate
/// every view of their document; the next lookup rebuilds it once, without per-frame map scans.
/// </summary>
public class RoadGraphRuntime {
    readonly MapNavigationDocument document;
    long observedRevision;
    readonly Dictionary<string, RoadNodeRecord> nodesById = new Dictionary<string, RoadNodeRecord>();
    readonly Dictionary<string, RoadEdgeRecord> edgesById = new Dictionary<string, RoadEdgeRecord>();
    readonly Dictionary<string, JunctionRecord> junctionsById = new Dictionary<string, JunctionRecord>();
    readonly Dictionary<string, List<RoadEdgeRecord>> outgoingEdgesByNode = new Dictionary<string, List<RoadEdgeRecord>>();
    readonly Dictionary<string, float> arcLengthByEdge = new Dictionary<string, float>();

    static readonly IReadOnlyList<RoadEdgeRecord> EmptyEdgeList = new List<RoadEdgeRecord>();

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
        nodesById.Clear();
        edgesById.Clear();
        junctionsById.Clear();
        outgoingEdgesByNode.Clear();
        arcLengthByEdge.Clear();
        observedRevision = NavigationDocumentRevision.Get(document);
        if (document == null) return;

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
                arcLengthByEdge[edge.edgeId] = ComputeArcLength(edge);
                if (!outgoingEdgesByNode.TryGetValue(edge.fromNodeId, out var list)) {
                    list = new List<RoadEdgeRecord>();
                    outgoingEdgesByNode[edge.fromNodeId] = list;
                }
                list.Add(edge);
            }
        }
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

    /// <summary>Edges leaving a node, in authoring order. Never null.</summary>
    public IReadOnlyList<RoadEdgeRecord> GetOutgoingEdges(string nodeId) {
        EnsureCurrent();
        if (!string.IsNullOrEmpty(nodeId) && outgoingEdgesByNode.TryGetValue(nodeId, out var list)) return list;
        return EmptyEdgeList;
    }

    void EnsureCurrent() {
        if (observedRevision != NavigationDocumentRevision.Get(document)) Rebuild();
    }

    float ComputeArcLength(RoadEdgeRecord edge) {
        return RoadEdgeGeometry.ComputeLength(GetNode(edge.fromNodeId), edge, GetNode(edge.toNodeId));
    }
}
