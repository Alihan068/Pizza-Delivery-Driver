using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared editor-independent mutation authority for a <see cref="MapNavigationDocument"/>.
/// Every operation validates all affected references and geometry before changing the DTO, so a
/// rejected edit leaves the draft unchanged. Node positions remain the endpoint authority; Unity
/// authoring and future runtime adapters must use this class rather than a second graph mutator.
/// </summary>
public static class MapNavigationEditCommands {
    const float MinimumEdgeLength = 0.0001f;

    /// <summary>Creates a finite-position node with a unique stable identifier.</summary>
    public static bool TryCreateNode(MapNavigationDocument document, string nodeId, float x, float y) => TryCreateNode(document, nodeId, x, y, out _);

    /// <summary>Creates a node and returns an explicit rejection reason without partial mutation.</summary>
    public static bool TryCreateNode(MapNavigationDocument document, string nodeId, float x, float y, out string issue) {
        issue = null;
        if (document == null || string.IsNullOrWhiteSpace(nodeId) || document.nodes == null || !Finite(x) || !Finite(y)) return Reject("Invalid node input.", out issue);
        if (FindNode(document, nodeId) != null) return Reject("Node id already exists.", out issue);
        document.nodes.Add(new RoadNodeRecord { nodeId = nodeId, x = x, y = y });
        Changed(document);
        return true;
    }

    /// <summary>
    /// Moves a node and renormalizes every spawn attached to an incident edge. If any affected edge
    /// would become non-finite or zero length, the node and dependent values remain unchanged.
    /// </summary>
    public static bool TryMoveNode(MapNavigationDocument document, string nodeId, float newX, float newY) => TryMoveNode(document, nodeId, newX, newY, out _);

    /// <summary>Moves a node while returning an explicit atomic rejection reason.</summary>
    public static bool TryMoveNode(MapNavigationDocument document, string nodeId, float newX, float newY, out string issue) {
        issue = null;
        if (document == null || document.nodes == null || !Finite(newX) || !Finite(newY)) return Reject("Invalid node move input.", out issue);
        RoadNodeRecord node = FindNode(document, nodeId);
        if (node == null) return Reject("Node was not found.", out issue);
        var affected = new List<RoadEdgeRecord>();
        var oldLengths = new Dictionary<string, float>();
        if (document.edges != null) foreach (var edge in document.edges) {
            if (edge == null || (edge.fromNodeId != nodeId && edge.toNodeId != nodeId)) continue;
            if (string.IsNullOrWhiteSpace(edge.edgeId) || oldLengths.ContainsKey(edge.edgeId)) return Reject("Incident edge ids are not unique.", out issue);
            float length = Length(document, edge);
            if (!UsableLength(length)) return Reject("An incident edge has invalid geometry.", out issue);
            oldLengths.Add(edge.edgeId, length);
            affected.Add(edge);
        }
        if (!ValidateSpawns(document, oldLengths, out issue)) return false;
        float oldX = node.x, oldY = node.y;
        node.x = newX; node.y = newY;
        var newLengths = new Dictionary<string, float>();
        foreach (var edge in affected) {
            float length = Length(document, edge);
            if (!UsableLength(length)) { node.x = oldX; node.y = oldY; return Reject("The move would collapse an incident edge.", out issue); }
            newLengths.Add(edge.edgeId, length);
        }
        if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) {
            if (spawn == null || !oldLengths.TryGetValue(spawn.edgeId, out float oldLength)) continue;
            spawn.distanceAlongEdge = spawn.distanceAlongEdge / oldLength * newLengths[spawn.edgeId];
        }
        Changed(document);
        return true;
    }

    /// <summary>Connects two existing nodes with a directed edge containing no interior points.</summary>
    public static bool TryConnectEdge(MapNavigationDocument document, string edgeId, string fromNodeId, string toNodeId, float usableWidth, float speedLimit) => TryConnectEdge(document, edgeId, fromNodeId, toNodeId, usableWidth, speedLimit, out _);

    /// <summary>Connects existing nodes after validating uniqueness, access values and positive arc length.</summary>
    public static bool TryConnectEdge(MapNavigationDocument document, string edgeId, string fromNodeId, string toNodeId, float usableWidth, float speedLimit, out string issue) {
        issue = null;
        if (document == null || string.IsNullOrWhiteSpace(edgeId) || document.nodes == null || document.edges == null || !Finite(usableWidth) || usableWidth <= 0f || !Finite(speedLimit) || speedLimit <= 0f) return Reject("Invalid edge input.", out issue);
        RoadNodeRecord from = FindNode(document, fromNodeId), to = FindNode(document, toNodeId);
        if (from == null || to == null) return Reject("Edge endpoint node was not found.", out issue);
        if (FindEdge(document, edgeId) != null) return Reject("Edge id already exists.", out issue);
        var edge = new RoadEdgeRecord { edgeId = edgeId, fromNodeId = fromNodeId, toNodeId = toNodeId, usableWidth = usableWidth, speedLimit = speedLimit };
        if (!UsableLength(RoadEdgeGeometry.ComputeLength(from, edge, to))) return Reject("Edge endpoints produce zero length.", out issue);
        document.edges.Add(edge); Changed(document); return true;
    }

    /// <summary>Deletes an unreferenced node. Connected edges are rejected instead of guessed away.</summary>
    public static bool TryDeleteNode(MapNavigationDocument document, string nodeId) => TryDeleteNode(document, nodeId, out _);

    /// <summary>Deletes a node only when no edge still depends on it.</summary>
    public static bool TryDeleteNode(MapNavigationDocument document, string nodeId, out string issue) {
        issue = null;
        if (document == null || document.nodes == null) return Reject("Document or node list is missing.", out issue);
        RoadNodeRecord node = FindNode(document, nodeId);
        if (node == null) return Reject("Node was not found.", out issue);
        if (document.edges != null) foreach (var edge in document.edges) if (edge != null && (edge.fromNodeId == nodeId || edge.toNodeId == nodeId)) return Reject("Node is referenced by an edge.", out issue);
        document.nodes.Remove(node); Changed(document); return true;
    }

    /// <summary>Deletes an edge only when its route, spawn and junction references are absent.</summary>
    public static bool TryDeleteEdge(MapNavigationDocument document, string edgeId) => TryDeleteEdge(document, edgeId, out _);

    /// <summary>Rejects unsafe edge deletion rather than creating dangling or guessed references.</summary>
    public static bool TryDeleteEdge(MapNavigationDocument document, string edgeId, out string issue) {
        issue = null;
        if (document == null || document.edges == null) return Reject("Document or edge list is missing.", out issue);
        RoadEdgeRecord edge = FindEdge(document, edgeId);
        if (edge == null) return Reject("Edge was not found.", out issue);
        if (!EdgeUnreferenced(document, edgeId, out issue)) return false;
        document.edges.Remove(edge); Changed(document); return true;
    }

    /// <summary>
    /// Splits an edge at an arc distance using caller-supplied node and edge ids. Route and spawn
    /// attachments are rewritten in order; unsafe junction transitions reject the whole operation.
    /// </summary>
    public static bool TrySplitEdge(MapNavigationDocument document, string edgeId, float distanceAlongEdge, string splitNodeId, string firstEdgeId, string secondEdgeId) => TrySplitEdge(document, edgeId, distanceAlongEdge, splitNodeId, firstEdgeId, secondEdgeId, out _);

    /// <summary>Performs an atomic edge split and returns a reason for any invalid or unsafe input.</summary>
    public static bool TrySplitEdge(MapNavigationDocument document, string edgeId, float distanceAlongEdge, string splitNodeId, string firstEdgeId, string secondEdgeId, out string issue) {
        issue = null;
        if (document == null || document.edges == null || document.nodes == null) return Reject("Document graph lists are missing.", out issue);
        RoadEdgeRecord original = FindEdge(document, edgeId);
        RoadNodeRecord from = original == null ? null : FindNode(document, original.fromNodeId), to = original == null ? null : FindNode(document, original.toNodeId);
        float length = original == null ? 0f : RoadEdgeGeometry.ComputeLength(from, original, to);
        if (original == null || from == null || to == null || !UsableLength(length) || !Finite(distanceAlongEdge) || distanceAlongEdge <= 0f || distanceAlongEdge >= length) return Reject("Split distance or source edge is invalid.", out issue);
        if (string.IsNullOrWhiteSpace(splitNodeId) || string.IsNullOrWhiteSpace(firstEdgeId) || string.IsNullOrWhiteSpace(secondEdgeId) || splitNodeId == firstEdgeId || splitNodeId == secondEdgeId || firstEdgeId == secondEdgeId || FindNode(document, splitNodeId) != null || FindEdge(document, firstEdgeId) != null || FindEdge(document, secondEdgeId) != null) return Reject("Split ids must be new and distinct.", out issue);
        if (!CanRewriteJunctions(document, edgeId, out issue)) return false;
        if (!ValidateSourceSpawns(document, edgeId, length, out issue)) return false;
        if (!TryPointAtDistance(from, original, to, distanceAlongEdge, out Vector2 splitPoint, out int segmentIndex, out bool splitAtVertex)) return Reject("Unable to locate split arc distance.", out issue);

        var first = CloneEdge(original, firstEdgeId, original.fromNodeId, splitNodeId);
        var second = CloneEdge(original, secondEdgeId, splitNodeId, original.toNodeId);
        first.endJunctionId = string.Empty;
        second.startJunctionId = string.Empty;
        first.orderedPoints = new List<Vector2>(); second.orderedPoints = new List<Vector2>();
        if (original.orderedPoints != null) for (int i = 0; i < original.orderedPoints.Count; i++) {
            if (i < segmentIndex) first.orderedPoints.Add(original.orderedPoints[i]);
            else if (!splitAtVertex || i > segmentIndex) second.orderedPoints.Add(original.orderedPoints[i]);
        }
        var rewrittenRoutes = new Dictionary<CivilianRouteRecord, List<string>>();
        if (document.civilianRoutes != null) foreach (var route in document.civilianRoutes) if (route != null && route.edgeIds != null && route.edgeIds.Contains(edgeId)) {
            var ids = new List<string>(); foreach (var id in route.edgeIds) { if (id == edgeId) { ids.Add(firstEdgeId); ids.Add(secondEdgeId); } else ids.Add(id); } rewrittenRoutes.Add(route, ids);
        }

        document.nodes.Add(new RoadNodeRecord { nodeId = splitNodeId, x = splitPoint.x, y = splitPoint.y });
        document.edges.Remove(original); document.edges.Add(first); document.edges.Add(second);
        if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) if (spawn != null && spawn.edgeId == edgeId) {
            if (spawn.distanceAlongEdge <= distanceAlongEdge) spawn.edgeId = firstEdgeId;
            else { spawn.edgeId = secondEdgeId; spawn.distanceAlongEdge -= distanceAlongEdge; }
        }
        foreach (var pair in rewrittenRoutes) pair.Key.edgeIds = pair.Value;
        RewriteJunctions(document, edgeId, firstEdgeId, secondEdgeId);
        Changed(document); return true;
    }

    /// <summary>Replaces a route's edge order and loop flag after validating every edge and adjacency.</summary>
    public static bool TryOrderRoute(MapNavigationDocument document, string routeId, IReadOnlyList<string> orderedEdgeIds, bool closeLoop, out string issue) {
        issue = null; CivilianRouteRecord route = FindRoute(document, routeId);
        if (route == null || orderedEdgeIds == null || orderedEdgeIds.Count == 0) return Reject("Route or edge order is missing.", out issue);
        var candidate = new CivilianRouteRecord { routeId = route.routeId, edgeIds = new List<string>(orderedEdgeIds), loop = closeLoop };
        if (!CivilianRouteValidator.IsValid(document, candidate, out issue)) return false;
        route.edgeIds = candidate.edgeIds; route.loop = closeLoop; Changed(document); return true;
    }

    /// <summary>Alias for callers that describe route ordering as setting an explicit order.</summary>
    public static bool TrySetRouteOrder(MapNavigationDocument document, string routeId, IReadOnlyList<string> orderedEdgeIds, bool closeLoop, out string issue) => TryOrderRoute(document, routeId, orderedEdgeIds, closeLoop, out issue);

    /// <summary>Sets the loop flag only after validating the route's current edge order.</summary>
    public static bool TryCloseRoute(MapNavigationDocument document, string routeId, bool closeLoop, out string issue) {
        CivilianRouteRecord route = FindRoute(document, routeId); if (route == null) return Reject("Route was not found.", out issue);
        return TryOrderRoute(document, routeId, route.edgeIds, closeLoop, out issue);
    }

    /// <summary>Adds a route with one existing edge, preserving the same route validation used for reordering.</summary>
    public static bool TryAddRoute(MapNavigationDocument document, string routeId, string edgeId, bool closeLoop, out string issue) {
        issue = null;
        if (document == null || document.civilianRoutes == null || string.IsNullOrWhiteSpace(routeId) || FindRoute(document, routeId) != null || FindEdge(document, edgeId) == null)
            return Reject("Route id or initial edge is invalid.", out issue);
        var route = new CivilianRouteRecord { routeId = routeId, edgeIds = new List<string> { edgeId }, loop = closeLoop };
        if (!CivilianRouteValidator.IsValid(document, route, out issue)) return false;
        document.civilianRoutes.Add(route); Changed(document); return true;
    }

    /// <summary>Adds a detached spawn record attached to a valid edge and arc distance.</summary>
    public static bool TryAddSpawn(MapNavigationDocument document, VehicleSpawnRecord spawn, out string issue) {
        issue = null; RoadEdgeRecord edge = FindEdge(document, spawn == null ? null : spawn.edgeId);
        CivilianRouteRecord route = FindRoute(document, spawn == null ? null : spawn.routeId);
        if (document == null || document.spawnPoints == null || spawn == null || string.IsNullOrWhiteSpace(spawn.spawnId) || edge == null || FindSpawn(document, spawn.spawnId) != null ||
            !ValidSpawnRole(spawn.role) || !Finite(spawn.clearanceWidth) || spawn.clearanceWidth <= 0f || !Finite(spawn.clearanceLength) || spawn.clearanceLength <= 0f ||
            (spawn.role == VehicleRole.Civilian && (route == null || route.edgeIds == null || !route.edgeIds.Contains(spawn.edgeId))) ||
            (spawn.role == VehicleRole.Police && !string.IsNullOrEmpty(spawn.routeId))) return Reject("Spawn requires a valid role, positive footprint and matching route attachment.", out issue);
        float length = Length(document, edge); if (!Finite(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f || spawn.distanceAlongEdge > length) return Reject("Spawn distance is outside the edge.", out issue);
        document.spawnPoints.Add(CloneSpawn(spawn)); Changed(document); return true;
    }

    /// <summary>Moves an existing spawn along its current edge without changing its attachment identity.</summary>
    public static bool TryMoveSpawn(MapNavigationDocument document, string spawnId, float distanceAlongEdge, out string issue) {
        issue = null;
        VehicleSpawnRecord spawn = FindSpawn(document, spawnId);
        RoadEdgeRecord edge = FindEdge(document, spawn == null ? null : spawn.edgeId);
        if (spawn == null || edge == null || !Finite(distanceAlongEdge) || distanceAlongEdge < 0f || distanceAlongEdge > Length(document, edge)) return Reject("Spawn distance is outside its edge.", out issue);
        spawn.distanceAlongEdge = distanceAlongEdge; Changed(document); return true;
    }

    /// <summary>Changes a route's vehicle pool and target count without changing its edge order.</summary>
    public static bool TryEditRoutePoolAndTarget(MapNavigationDocument document, string routeId, string vehiclePoolId, int targetCount, out string issue) {
        CivilianRouteRecord route = FindRoute(document, routeId); issue = null;
        if (route == null || string.IsNullOrWhiteSpace(vehiclePoolId) || targetCount < 0 || FindPool(document, vehiclePoolId) == null) return Reject("Route pool or target is invalid.", out issue);
        route.vehiclePoolId = vehiclePoolId; route.targetCount = targetCount; Changed(document); return true;
    }

    /// <summary>Replaces an existing pool's weighted entries using detached primitive DTO data.</summary>
    public static bool TryEditPool(MapNavigationDocument document, string poolId, IReadOnlyList<VehiclePoolEntry> entries, out string issue) {
        issue = null;
        VehiclePoolRecord pool = FindPool(document, poolId);
        if (pool == null || entries == null || entries.Count == 0) return Reject("Pool is missing or empty.", out issue);
        var replacement = new List<VehiclePoolEntry>();
        var profileIds = new HashSet<string>();
        foreach (var entry in entries) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.vehicleProfileId) || !profileIds.Add(entry.vehicleProfileId) || !Finite(entry.weight) || entry.weight <= 0f) return Reject("Pool contains an invalid or duplicate weighted entry.", out issue);
            replacement.Add(new VehiclePoolEntry { vehicleProfileId = entry.vehicleProfileId, weight = entry.weight });
        }
        pool.entries = replacement; Changed(document); return true;
    }

    /// <summary>Updates a route's pool reference without changing its target or edge order.</summary>
    public static bool TryEditRoutePool(MapNavigationDocument document, string routeId, string vehiclePoolId, out string issue) {
        CivilianRouteRecord route = FindRoute(document, routeId); issue = null;
        if (route == null || string.IsNullOrWhiteSpace(vehiclePoolId) || FindPool(document, vehiclePoolId) == null) return Reject("Route or pool is invalid.", out issue);
        route.vehiclePoolId = vehiclePoolId; Changed(document); return true;
    }

    /// <summary>Updates a route target count without changing its pool or edge order.</summary>
    public static bool TryEditRouteTarget(MapNavigationDocument document, string routeId, int targetCount, out string issue) {
        CivilianRouteRecord route = FindRoute(document, routeId); issue = null;
        if (route == null || targetCount < 0) return Reject("Route or target is invalid.", out issue);
        route.targetCount = targetCount; Changed(document); return true;
    }

    /// <summary>Adds a police entry whose spawn and role data are valid and detached from the caller.</summary>
    public static bool TryAddPoliceEntry(MapNavigationDocument document, string entryId, string spawnId, IReadOnlyList<VehicleRole> allowedRoles, out string issue) {
        issue = null;
        VehicleSpawnRecord spawn = FindSpawn(document, spawnId);
        if (document == null || document.policeEntries == null || string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(spawnId) || spawn == null || spawn.role != VehicleRole.Police || FindPoliceEntry(document, entryId) != null || allowedRoles == null || allowedRoles.Count == 0 || !ContainsRole(allowedRoles, VehicleRole.Police)) return Reject("Police entry requires a unique id and a Police-role spawn.", out issue);
        document.policeEntries.Add(new PoliceEntryRecord { entryId = entryId, spawnId = spawnId, allowedRoles = new List<VehicleRole>(allowedRoles) }); Changed(document); return true;
    }

    /// <summary>Edits an existing police entry after validating its spawn and Police role access.</summary>
    public static bool TryEditPoliceEntry(MapNavigationDocument document, string entryId, string spawnId, IReadOnlyList<VehicleRole> allowedRoles, out string issue) {
        issue = null;
        PoliceEntryRecord entry = FindPoliceEntry(document, entryId);
        VehicleSpawnRecord spawn = FindSpawn(document, spawnId);
        if (entry == null || spawn == null || spawn.role != VehicleRole.Police || allowedRoles == null || allowedRoles.Count == 0 || !ContainsRole(allowedRoles, VehicleRole.Police)) return Reject("Police entry requires a valid Police-role spawn.", out issue);
        entry.spawnId = spawnId; entry.allowedRoles = new List<VehicleRole>(allowedRoles); Changed(document); return true;
    }

    /// <summary>Atomically updates one edge's finite positive width and speed without changing any other document data.</summary>
    public static bool TryEditEdgeSettings(MapNavigationDocument document, string edgeId, float usableWidth, float speedLimit, out string issue) {
        issue = null;
        if (document == null || string.IsNullOrWhiteSpace(edgeId) || !Finite(usableWidth) || usableWidth <= 0f || !Finite(speedLimit) || speedLimit <= 0f) return Reject("Width and speed limit must be finite and positive.", out issue);
        RoadEdgeRecord edge = FindEdge(document, edgeId);
        if (edge == null) return Reject("Edge was not found.", out issue);
        edge.usableWidth = usableWidth;
        edge.speedLimit = speedLimit;
        Changed(document);
        return true;
    }

    static bool CanRewriteJunctions(MapNavigationDocument document, string edgeId, out string issue) {
        issue = null; if (document.junctions == null) return true;
        foreach (var junction in document.junctions) if (junction != null && junction.allowedTransitions != null) foreach (var transition in junction.allowedTransitions) if (transition != null && transition.fromEdgeId == edgeId && transition.toEdgeId == edgeId) return Reject("A self-transition cannot be safely rewritten after a split.", out issue);
        return true;
    }

    static void RewriteJunctions(MapNavigationDocument document, string oldId, string firstId, string secondId) {
        if (document.junctions == null) return; foreach (var junction in document.junctions) if (junction != null && junction.allowedTransitions != null) foreach (var transition in junction.allowedTransitions) if (transition != null) { if (transition.fromEdgeId == oldId) transition.fromEdgeId = secondId; if (transition.toEdgeId == oldId) transition.toEdgeId = firstId; }
    }

    static bool EdgeUnreferenced(MapNavigationDocument document, string edgeId, out string issue) {
        issue = null;
        if (document.civilianRoutes != null) foreach (var route in document.civilianRoutes) if (route != null && route.edgeIds != null && route.edgeIds.Contains(edgeId)) return Reject("Edge is referenced by a civilian route.", out issue);
        if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) if (spawn != null && spawn.edgeId == edgeId) return Reject("Edge is referenced by a spawn.", out issue);
        if (document.junctions != null) foreach (var junction in document.junctions) if (junction != null && junction.allowedTransitions != null) foreach (var transition in junction.allowedTransitions) if (transition != null && (transition.fromEdgeId == edgeId || transition.toEdgeId == edgeId)) return Reject("Edge is referenced by a junction transition.", out issue);
        return true;
    }

    static bool TryPointAtDistance(RoadNodeRecord from, RoadEdgeRecord edge, RoadNodeRecord to, float distance, out Vector2 point, out int segmentIndex, out bool splitAtVertex) {
        point = default; segmentIndex = 0; splitAtVertex = false; var points = new List<Vector2> { new Vector2(from.x, from.y) }; if (edge.orderedPoints != null) points.AddRange(edge.orderedPoints); points.Add(new Vector2(to.x, to.y)); float remaining = distance;
        for (int i = 0; i < points.Count - 1; i++) { float segment = Vector2.Distance(points[i], points[i + 1]); if (!Finite(segment) || segment <= 0f) return false; if (remaining <= segment) { point = Vector2.Lerp(points[i], points[i + 1], remaining / segment); segmentIndex = i; splitAtVertex = remaining == segment; return true; } remaining -= segment; }
        return false;
    }

    static bool ValidateSpawns(MapNavigationDocument document, Dictionary<string, float> lengths, out string issue) {
        issue = null; if (document.spawnPoints == null) return true; foreach (var spawn in document.spawnPoints) if (spawn != null && lengths.TryGetValue(spawn.edgeId, out float length) && (!Finite(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f || spawn.distanceAlongEdge > length)) return Reject("A spawn has invalid distance on an incident edge.", out issue); return true;
    }
    static bool ValidateSourceSpawns(MapNavigationDocument document, string edgeId, float length, out string issue) {
        issue = null; if (document.spawnPoints != null) foreach (var spawn in document.spawnPoints) if (spawn != null && spawn.edgeId == edgeId && (!Finite(spawn.distanceAlongEdge) || spawn.distanceAlongEdge < 0f || spawn.distanceAlongEdge > length)) return Reject("A spawn has invalid distance on the source edge.", out issue); return true;
    }
    static RoadEdgeRecord CloneEdge(RoadEdgeRecord source, string id, string from, string to) => new RoadEdgeRecord { edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = source.usableWidth, speedLimit = source.speedLimit, allowedRoles = source.allowedRoles == null ? new List<VehicleRole>() : new List<VehicleRole>(source.allowedRoles), startJunctionId = source.startJunctionId, endJunctionId = source.endJunctionId };
    static VehicleSpawnRecord CloneSpawn(VehicleSpawnRecord source) => new VehicleSpawnRecord { spawnId = source.spawnId, edgeId = source.edgeId, distanceAlongEdge = source.distanceAlongEdge, routeId = source.routeId, role = source.role, clearanceWidth = source.clearanceWidth, clearanceLength = source.clearanceLength };
    static bool ContainsRole(IReadOnlyList<VehicleRole> roles, VehicleRole role) { for (int i = 0; i < roles.Count; i++) if (roles[i] == role) return true; return false; }
    static bool ValidSpawnRole(VehicleRole role) => role == VehicleRole.Civilian || role == VehicleRole.Police;
    static float Length(MapNavigationDocument document, RoadEdgeRecord edge) => edge == null ? 0f : RoadEdgeGeometry.ComputeLength(FindNode(document, edge.fromNodeId), edge, FindNode(document, edge.toNodeId));
    static bool UsableLength(float value) => Finite(value) && value > MinimumEdgeLength;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Reject(string message, out string issue) { issue = message; return false; }
    static void Changed(MapNavigationDocument document) => NavigationDocumentRevision.Changed(document);
    static RoadNodeRecord FindNode(MapNavigationDocument document, string id) => document?.nodes == null ? null : document.nodes.Find(item => item != null && item.nodeId == id);
    static RoadEdgeRecord FindEdge(MapNavigationDocument document, string id) => document?.edges == null ? null : document.edges.Find(item => item != null && item.edgeId == id);
    static CivilianRouteRecord FindRoute(MapNavigationDocument document, string id) => document?.civilianRoutes == null ? null : document.civilianRoutes.Find(item => item != null && item.routeId == id);
    static VehicleSpawnRecord FindSpawn(MapNavigationDocument document, string id) => document?.spawnPoints == null ? null : document.spawnPoints.Find(item => item != null && item.spawnId == id);
    static VehiclePoolRecord FindPool(MapNavigationDocument document, string id) => document?.vehiclePools == null ? null : document.vehiclePools.Find(item => item != null && item.poolId == id);
    static PoliceEntryRecord FindPoliceEntry(MapNavigationDocument document, string id) => document?.policeEntries == null ? null : document.policeEntries.Find(item => item != null && item.entryId == id);
}
