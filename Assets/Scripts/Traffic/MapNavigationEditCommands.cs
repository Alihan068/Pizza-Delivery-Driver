using System.Collections.Generic;

/// <summary>
/// Minimal, shared create/move-node and connect-edge edit operations for a
/// <see cref="MapNavigationDocument"/>. This is the single geometry-editing authority both S07's
/// authoring pass and S11's fuller edit/Undo commands build on — neither creates a second one.
/// Every operation is atomic: if any part of it would be invalid, nothing about the document is
/// changed at all, not even partially.
/// </summary>
public static class MapNavigationEditCommands {
    /// <summary>Creates a finite-position node with a unique ID and invalidates indexed views. Invalid input leaves the draft unchanged.</summary>
    public static bool TryCreateNode(MapNavigationDocument document, string nodeId, float x, float y) {
        if (document == null || string.IsNullOrWhiteSpace(nodeId) || document.nodes == null || !IsFinite(x) || !IsFinite(y)) return false;
        foreach (var node in document.nodes) {
            if (node != null && node.nodeId == nodeId) return false;
        }
        document.nodes.Add(new RoadNodeRecord { nodeId = nodeId, x = x, y = y });
        NavigationDocumentRevision.Changed(document);
        return true;
    }

    /// <summary>
    /// Moves a node. Every edge touching it moves its endpoint with it in the same operation, and
    /// every spawn on an affected edge keeps its normalized position along that edge
    /// (u = distance / oldLength, newDistance = u * newLength) per contracts §2. Rejected — with the
    /// node put back exactly where it was — if the move would collapse any connected edge or produce
    /// non-finite geometry. Successful edits invalidate every indexed view of this document.
    /// </summary>
    public static bool TryMoveNode(MapNavigationDocument document, string nodeId, float newX, float newY) {
        if (document == null || document.nodes == null || !IsFinite(newX) || !IsFinite(newY)) return false;

        RoadNodeRecord node = FindNode(document, nodeId);
        if (node == null) return false;

        var affectedEdges = new List<RoadEdgeRecord>();
        var oldLengthByEdgeId = new Dictionary<string, float>();
        if (document.edges != null) {
            foreach (var edge in document.edges) {
                if (edge == null) continue;
                if (edge.fromNodeId != nodeId && edge.toNodeId != nodeId) continue;
                if (string.IsNullOrWhiteSpace(edge.edgeId) || oldLengthByEdgeId.ContainsKey(edge.edgeId)) return false;
                float oldLength = RoadEdgeGeometry.ComputeLength(FindNode(document, edge.fromNodeId), edge, FindNode(document, edge.toNodeId));
                if (!IsFinite(oldLength) || oldLength <= 0.0001f) return false;
                oldLengthByEdgeId[edge.edgeId] = oldLength;
                affectedEdges.Add(edge);
            }
        }

        if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) {
            if (spawn == null || string.IsNullOrEmpty(spawn.edgeId) || !oldLengthByEdgeId.TryGetValue(spawn.edgeId, out float length)) continue;
            if (!IsFinite(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f || spawn.distanceAlongEdge > length) return false;
        }

        float oldX = node.x, oldY = node.y;
        node.x = newX;
        node.y = newY;

        foreach (var edge in affectedEdges) {
            float newLength = RoadEdgeGeometry.ComputeLength(FindNode(document, edge.fromNodeId), edge, FindNode(document, edge.toNodeId));
            if (!IsFinite(newLength) || newLength <= 0.0001f) {
                node.x = oldX; // atomic reject: put the node back exactly, nothing else was touched yet
                node.y = oldY;
                return false;
            }
        }

        // Geometry is valid for every affected edge; now renormalize their spawns.
        if (document.spawnPoints != null) {
            foreach (var edge in affectedEdges) {
                if (!oldLengthByEdgeId.TryGetValue(edge.edgeId, out float oldLength) || oldLength <= 0.0001f) continue;
                float newLength = RoadEdgeGeometry.ComputeLength(FindNode(document, edge.fromNodeId), edge, FindNode(document, edge.toNodeId));
                foreach (var spawn in document.spawnPoints) {
                    if (spawn == null || spawn.edgeId != edge.edgeId) continue;
                    float u = spawn.distanceAlongEdge / oldLength;
                    spawn.distanceAlongEdge = u * newLength;
                }
            }
        }

        NavigationDocumentRevision.Changed(document);
        return true;
    }

    /// <summary>Connects existing nodes with a finite, positive-length edge and positive width/speed. Invalid or duplicate input is rejected atomically; success invalidates indexed views.</summary>
    public static bool TryConnectEdge(MapNavigationDocument document, string edgeId, string fromNodeId, string toNodeId, float usableWidth, float speedLimit) {
        if (document == null || string.IsNullOrWhiteSpace(edgeId) || document.nodes == null || document.edges == null ||
            !IsFinite(usableWidth) || usableWidth <= 0f || !IsFinite(speedLimit) || speedLimit <= 0f) return false;
        if (FindNode(document, fromNodeId) == null || FindNode(document, toNodeId) == null) return false;
        foreach (var edge in document.edges) {
            if (edge != null && edge.edgeId == edgeId) return false;
        }
        var newEdge = new RoadEdgeRecord { edgeId = edgeId, fromNodeId = fromNodeId, toNodeId = toNodeId, usableWidth = usableWidth, speedLimit = speedLimit };
        float length = RoadEdgeGeometry.ComputeLength(FindNode(document, fromNodeId), newEdge, FindNode(document, toNodeId));
        if (!IsFinite(length) || length <= 0.0001f) return false;
        document.edges.Add(newEdge);
        NavigationDocumentRevision.Changed(document);
        return true;
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static RoadNodeRecord FindNode(MapNavigationDocument document, string nodeId) {
        if (document.nodes == null) return null;
        foreach (var node in document.nodes) {
            if (node != null && node.nodeId == nodeId) return node;
        }
        return null;
    }
}
