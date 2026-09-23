using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded structural validation for one <see cref="MapNavigationDocument"/>. Every check is a
/// flat scan or a dictionary lookup — never a recursive graph traversal — so a cyclic graph can
/// never make this hang, and one bad record never stops the rest of the document from being
/// checked. <see cref="Validate"/> always returns its full issue list; it never throws for bad
/// content, so a runtime import failure is an explicit, reportable result, not a crash.
/// </summary>
public static class TrafficMapValidator {
    /// <summary>Runs every structural check against a document.</summary>
    /// <param name="document">Document to validate. Null itself is reported, not thrown.</param>
    /// <param name="limits">Size limits to enforce. Null falls back to the authored defaults.</param>
    /// <returns>Every issue found, in check order. Empty when the document is structurally valid.</returns>
    public static List<TrafficValidationIssue> Validate(MapNavigationDocument document, TrafficValidationLimits limits) {
        var issues = new List<TrafficValidationIssue>();
        if (document == null) {
            issues.Add(new TrafficValidationIssue("NullDocument", string.Empty, "Document is null."));
            return issues;
        }
        limits ??= new TrafficValidationLimits();

        var nodeIds = new HashSet<string>();
        ValidateNodes(document, limits, issues, nodeIds);
        var edgeIds = new HashSet<string>();
        ValidateEdges(document, limits, issues, nodeIds, edgeIds);
        ValidateJunctions(document, issues);
        ValidateSpawns(document, issues, edgeIds);
        ValidateRoutes(document, limits, issues);
        ValidatePoliceEntries(document, issues);
        ValidateDifficultyProfileBindings(document, issues);

        if (document.edges == null || document.edges.Count == 0) {
            issues.Add(new TrafficValidationIssue("ZeroEdgeCount", string.Empty, "Document has no edges."));
        }

        return issues;
    }

    static void ValidateNodes(MapNavigationDocument document, TrafficValidationLimits limits, List<TrafficValidationIssue> issues, HashSet<string> nodeIds) {
        if (document.nodes == null) return;
        if (document.nodes.Count > limits.maxNodes) {
            issues.Add(new TrafficValidationIssue("TooManyNodes", string.Empty, "Node count " + document.nodes.Count + " exceeds limit " + limits.maxNodes + "."));
        }
        foreach (var node in document.nodes) {
            if (node == null || string.IsNullOrEmpty(node.nodeId)) {
                issues.Add(new TrafficValidationIssue("InvalidNodeId", string.Empty, "Node has a null/empty id."));
                continue;
            }
            if (!nodeIds.Add(node.nodeId)) {
                issues.Add(new TrafficValidationIssue("DuplicateNodeId", node.nodeId, "Duplicate node id " + node.nodeId + "."));
            }
            if (float.IsNaN(node.x) || float.IsInfinity(node.x) || float.IsNaN(node.y) || float.IsInfinity(node.y)) {
                issues.Add(new TrafficValidationIssue("NonFiniteNodePosition", node.nodeId, "Node " + node.nodeId + " has a NaN/Infinity coordinate."));
                continue;
            }
            if (!IsWithinBounds(document.localBounds, node.x, node.y)) {
                issues.Add(new TrafficValidationIssue("NodeOutOfBounds", node.nodeId, "Node " + node.nodeId + " lies outside localBounds."));
            }
        }
    }

    static void ValidateEdges(MapNavigationDocument document, TrafficValidationLimits limits, List<TrafficValidationIssue> issues,
        HashSet<string> nodeIds, HashSet<string> edgeIds) {
        if (document.edges == null) return;
        if (document.edges.Count > limits.maxEdges) {
            issues.Add(new TrafficValidationIssue("TooManyEdges", string.Empty, "Edge count " + document.edges.Count + " exceeds limit " + limits.maxEdges + "."));
        }
        foreach (var edge in document.edges) {
            if (edge == null || string.IsNullOrEmpty(edge.edgeId)) {
                issues.Add(new TrafficValidationIssue("InvalidEdgeId", string.Empty, "Edge has a null/empty id."));
                continue;
            }
            if (!edgeIds.Add(edge.edgeId)) {
                issues.Add(new TrafficValidationIssue("DuplicateEdgeId", edge.edgeId, "Duplicate edge id " + edge.edgeId + "."));
            }
            if (string.IsNullOrEmpty(edge.fromNodeId) || !nodeIds.Contains(edge.fromNodeId)) {
                issues.Add(new TrafficValidationIssue("DanglingEdgeFromNode", edge.edgeId, "Edge " + edge.edgeId + " references an unknown fromNodeId."));
            }
            if (string.IsNullOrEmpty(edge.toNodeId) || !nodeIds.Contains(edge.toNodeId)) {
                issues.Add(new TrafficValidationIssue("DanglingEdgeToNode", edge.edgeId, "Edge " + edge.edgeId + " references an unknown toNodeId."));
            }
            if (edge.fromNodeId == edge.toNodeId && !string.IsNullOrEmpty(edge.fromNodeId)) {
                issues.Add(new TrafficValidationIssue("ZeroLengthEdge", edge.edgeId, "Edge " + edge.edgeId + " starts and ends at the same node."));
            }
            if (edge.orderedPoints != null) {
                if (edge.orderedPoints.Count > limits.maxPointsPerEdge) {
                    issues.Add(new TrafficValidationIssue("TooManyEdgePoints", edge.edgeId, "Edge " + edge.edgeId + " has more interior points than the limit."));
                }
                foreach (var point in edge.orderedPoints) {
                    if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.y)) {
                        issues.Add(new TrafficValidationIssue("NonFiniteEdgePoint", edge.edgeId, "Edge " + edge.edgeId + " has a NaN/Infinity interior point."));
                        break;
                    }
                }
            }
        }
    }

    static void ValidateJunctions(MapNavigationDocument document, List<TrafficValidationIssue> issues) {
        var seen = new HashSet<string>();
        var edges = new Dictionary<string, RoadEdgeRecord>();
        if (document.edges != null) {
            foreach (var edge in document.edges) {
                if (edge != null && !string.IsNullOrWhiteSpace(edge.edgeId)) edges[edge.edgeId] = edge;
            }
        }
        if (document.junctions != null) foreach (var junction in document.junctions) {
            if (junction == null || string.IsNullOrEmpty(junction.junctionId)) {
                issues.Add(new TrafficValidationIssue("InvalidJunctionId", string.Empty, "Junction has a null/empty id."));
                continue;
            }
            if (!seen.Add(junction.junctionId)) {
                issues.Add(new TrafficValidationIssue("DuplicateJunctionId", junction.junctionId, "Duplicate junction id " + junction.junctionId + "."));
            }
            if (junction.allowedTransitions == null) {
                issues.Add(new TrafficValidationIssue("MissingJunctionTransitions", junction.junctionId, "Junction transition list is null."));
                continue;
            }
            foreach (var transition in junction.allowedTransitions) {
                if (transition == null || string.IsNullOrWhiteSpace(transition.fromEdgeId) ||
                    string.IsNullOrWhiteSpace(transition.toEdgeId) ||
                    !edges.TryGetValue(transition.fromEdgeId, out var incoming) ||
                    !edges.TryGetValue(transition.toEdgeId, out var outgoing)) {
                    issues.Add(new TrafficValidationIssue("DanglingJunctionTransition", junction.junctionId, "Transition must reference two existing edges."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(incoming.toNodeId) || incoming.toNodeId != outgoing.fromNodeId ||
                    (!string.IsNullOrEmpty(incoming.endJunctionId) && incoming.endJunctionId != junction.junctionId) ||
                    (!string.IsNullOrEmpty(outgoing.startJunctionId) && outgoing.startJunctionId != junction.junctionId)) {
                    issues.Add(new TrafficValidationIssue("DisconnectedJunctionTransition", junction.junctionId, "Transition edges must meet at the same node and junction."));
                }
            }
        }
        foreach (var edge in edges.Values) {
            if ((!string.IsNullOrEmpty(edge.startJunctionId) && !seen.Contains(edge.startJunctionId)) ||
                (!string.IsNullOrEmpty(edge.endJunctionId) && !seen.Contains(edge.endJunctionId))) {
                issues.Add(new TrafficValidationIssue("DanglingEdgeJunction", edge.edgeId, "Edge references an unknown junction."));
            }
        }
    }

    static void ValidateSpawns(MapNavigationDocument document, List<TrafficValidationIssue> issues, HashSet<string> edgeIds) {
        if (document.spawnPoints == null) return;
        var seen = new HashSet<string>();
        foreach (var spawn in document.spawnPoints) {
            if (spawn == null || string.IsNullOrEmpty(spawn.spawnId)) {
                issues.Add(new TrafficValidationIssue("InvalidSpawnId", string.Empty, "Spawn has a null/empty id."));
                continue;
            }
            if (!seen.Add(spawn.spawnId)) {
                issues.Add(new TrafficValidationIssue("DuplicateSpawnId", spawn.spawnId, "Duplicate spawn id " + spawn.spawnId + "."));
            }
            if (string.IsNullOrEmpty(spawn.edgeId) || !edgeIds.Contains(spawn.edgeId)) {
                issues.Add(new TrafficValidationIssue("DanglingSpawnEdge", spawn.spawnId, "Spawn " + spawn.spawnId + " references an unknown edgeId."));
                continue;
            }
            if (float.IsNaN(spawn.distanceAlongEdge) || float.IsInfinity(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f) {
                issues.Add(new TrafficValidationIssue("InvalidSpawnDistance", spawn.spawnId, "Spawn " + spawn.spawnId + " has an invalid distanceAlongEdge."));
            }
        }
    }

    static void ValidateRoutes(MapNavigationDocument document, TrafficValidationLimits limits, List<TrafficValidationIssue> issues) {
        if (document.civilianRoutes == null) return;
        if (document.civilianRoutes.Count > limits.maxRoutes) {
            issues.Add(new TrafficValidationIssue("TooManyRoutes", string.Empty, "Route count " + document.civilianRoutes.Count + " exceeds limit " + limits.maxRoutes + "."));
        }
        var seen = new HashSet<string>();
        foreach (var route in document.civilianRoutes) {
            if (route == null || string.IsNullOrEmpty(route.routeId)) {
                issues.Add(new TrafficValidationIssue("InvalidRouteId", string.Empty, "Route has a null/empty id."));
                continue;
            }
            if (!seen.Add(route.routeId)) {
                issues.Add(new TrafficValidationIssue("DuplicateRouteId", route.routeId, "Duplicate route id " + route.routeId + "."));
            }
            // Delegates to the same contiguity/closure logic S02.3 already proved correct, instead
            // of re-walking the edge chain a second, differently-written way.
            if (!CivilianRouteValidator.IsValid(document, route, out string issue)) {
                issues.Add(new TrafficValidationIssue("OpenOrDisconnectedRoute", route.routeId, "Route " + route.routeId + ": " + issue));
            }
        }
    }

    static void ValidatePoliceEntries(MapNavigationDocument document, List<TrafficValidationIssue> issues) {
        if (document.policeEntries == null || document.policeEntries.Count == 0) return;
        var spawnIds = new HashSet<string>();
        if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints)
            if (spawn != null && !string.IsNullOrEmpty(spawn.spawnId)) spawnIds.Add(spawn.spawnId);

        var entryIds = new HashSet<string>();
        foreach (var entry in document.policeEntries) {
            if (entry == null || string.IsNullOrEmpty(entry.entryId)) {
                issues.Add(new TrafficValidationIssue("InvalidPoliceEntryId", string.Empty, "Police entry has a null/empty id."));
                continue;
            }
            if (!entryIds.Add(entry.entryId))
                issues.Add(new TrafficValidationIssue("DuplicatePoliceEntryId", entry.entryId, "Duplicate police entry id " + entry.entryId + "."));
            if (string.IsNullOrEmpty(entry.spawnId) || !spawnIds.Contains(entry.spawnId))
                issues.Add(new TrafficValidationIssue("DanglingPoliceEntrySpawn", entry.entryId, "Police entry references an unknown spawnId."));
            if (entry.allowedRoles == null || !entry.allowedRoles.Contains(VehicleRole.Police))
                issues.Add(new TrafficValidationIssue("PoliceEntryDisallowsPolice", entry.entryId, "Police entry must allow the Police vehicle role."));
        }
    }

    static void ValidateDifficultyProfileBindings(MapNavigationDocument document, List<TrafficValidationIssue> issues) {
        if (document.difficultyProfileBindings == null || document.difficultyProfileBindings.Count == 0) return;
        var difficultyIds = new HashSet<string>();
        foreach (var binding in document.difficultyProfileBindings) {
            if (binding == null || string.IsNullOrEmpty(binding.difficultyId)) {
                issues.Add(new TrafficValidationIssue("InvalidDifficultyTrafficBinding", string.Empty, "Difficulty traffic binding has no difficulty id."));
                continue;
            }
            if (!difficultyIds.Add(binding.difficultyId))
                issues.Add(new TrafficValidationIssue("DuplicateDifficultyTrafficBinding", binding.difficultyId, "Duplicate difficulty traffic binding."));
            if (string.IsNullOrEmpty(binding.civilianPopulationProfileId))
                issues.Add(new TrafficValidationIssue("MissingCivilianPopulationProfile", binding.difficultyId, "Difficulty traffic binding has no civilian population profile."));
        }
    }

    static bool IsWithinBounds(Rect bounds, float x, float y) {
        if (bounds.width <= 0f && bounds.height <= 0f) return true; // unset bounds: no constraint authored yet
        return x >= bounds.xMin && x <= bounds.xMax && y >= bounds.yMin && y <= bounds.yMax;
    }
}
