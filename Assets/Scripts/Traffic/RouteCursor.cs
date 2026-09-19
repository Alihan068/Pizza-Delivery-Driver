using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arc-length progress tracker over one civilian route's edge chain. Binds once to a
/// <see cref="RoadGraphRuntime"/> + <see cref="CivilianRouteRecord"/>, caches every edge's full
/// polyline (from-node, interior points, to-node) and cumulative lengths, then answers "where am I
/// on the route" and "what point is N units ahead" without re-scanning the map. A loop route wraps
/// its last edge back into its first purely as arc-length bookkeeping — the cursor never moves a
/// vehicle, so the seam is crossed by real driving. Allocation-free after binding.
/// </summary>
public sealed class RouteCursor {
    sealed class EdgeSample {
        public RoadEdgeRecord edge;
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<float> cumulative = new List<float>();
        public float length;
    }

    readonly List<EdgeSample> edges = new List<EdgeSample>();
    readonly List<float> edgeStartArc = new List<float>();
    bool loop;
    int edgeIndex;
    int segmentIndex;
    float distanceAlongEdge;
    int lapCount;

    /// <summary>True after a successful <see cref="TryBind"/> and until <see cref="Unbind"/>.</summary>
    public bool IsBound => edges.Count > 0;

    /// <summary>Route id this cursor is bound to, or null.</summary>
    public string RouteId { get; private set; }

    /// <summary>True when the bound route closes back on itself.</summary>
    public bool Loop => loop;

    /// <summary>Index into the route's edge list the cursor is currently on.</summary>
    public int EdgeIndex => edgeIndex;

    /// <summary>Arc-length distance from the current edge's from-node.</summary>
    public float DistanceAlongEdge => distanceAlongEdge;

    /// <summary>Number of times a loop cursor advanced from the last edge back into the first.</summary>
    public int LapCount => lapCount;

    /// <summary>Total arc length of the bound route across every edge.</summary>
    public float TotalLength { get; private set; }

    /// <summary>Monotonic arc position along the route including completed laps (laps × total length + distance into the route); 0 when unbound.</summary>
    public float ArcPosition => IsBound ? lapCount * TotalLength + edgeStartArc[edgeIndex] + distanceAlongEdge : 0f;

    /// <summary>The edge record the cursor is currently on, or null when unbound.</summary>
    public RoadEdgeRecord CurrentEdge => IsBound ? edges[edgeIndex].edge : null;

    /// <summary>True on a non-loop route once the cursor reached the final edge's end; always false on a loop.</summary>
    public bool ReachedEnd => IsBound && !loop && edgeIndex == edges.Count - 1
        && distanceAlongEdge >= edges[edgeIndex].length - 0.001f;

    /// <summary>
    /// Binds to a route, validating it fail-closed: every edge and node must exist in the graph,
    /// edges must chain contiguously, a loop must close, and no edge may have zero length.
    /// </summary>
    /// <param name="graph">Indexed graph the route's edges belong to.</param>
    /// <param name="route">Route record to follow.</param>
    /// <param name="issue">Human-readable failure reason, or null on success.</param>
    /// <returns>True when bound; false leaves the cursor unbound.</returns>
    public bool TryBind(RoadGraphRuntime graph, CivilianRouteRecord route, out string issue) {
        Unbind();
        issue = null;
        if (graph == null || route == null) {
            issue = "missing graph or route";
            return false;
        }
        if (route.edgeIds == null || route.edgeIds.Count == 0) {
            issue = "route has no edges";
            return false;
        }

        float total = 0f;
        RoadEdgeRecord previous = null;
        foreach (var edgeId in route.edgeIds) {
            var edge = graph.GetEdge(edgeId);
            if (edge == null) {
                issue = "dangling edge id " + edgeId;
                Unbind();
                return false;
            }
            var from = graph.GetNode(edge.fromNodeId);
            var to = graph.GetNode(edge.toNodeId);
            if (from == null || to == null) {
                issue = "edge " + edgeId + " references a missing node";
                Unbind();
                return false;
            }
            if (previous != null && previous.toNodeId != edge.fromNodeId) {
                issue = "gap between " + previous.edgeId + " and " + edge.edgeId;
                Unbind();
                return false;
            }

            var sample = new EdgeSample { edge = edge };
            sample.points.Add(new Vector2(from.x, from.y));
            if (edge.orderedPoints != null) {
                foreach (var point in edge.orderedPoints) {
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)) {
                        issue = "edge " + edgeId + " has a non-finite control point";
                        Unbind();
                        return false;
                    }
                    sample.points.Add(point);
                }
            }
            sample.points.Add(new Vector2(to.x, to.y));

            float running = 0f;
            sample.cumulative.Add(0f);
            for (int i = 1; i < sample.points.Count; i++) {
                running += Vector2.Distance(sample.points[i - 1], sample.points[i]);
                sample.cumulative.Add(running);
            }
            sample.length = running;
            if (sample.length <= 0.0001f) {
                issue = "edge " + edgeId + " has zero length";
                Unbind();
                return false;
            }
            edgeStartArc.Add(total);
            total += sample.length;
            edges.Add(sample);
            previous = edge;
        }

        if (route.loop && previous.toNodeId != edges[0].edge.fromNodeId) {
            issue = "loop does not connect its last edge back to its first";
            Unbind();
            return false;
        }

        loop = route.loop;
        RouteId = route.routeId;
        TotalLength = total;
        return true;
    }

    /// <summary>Drops the bound route and all cached geometry.</summary>
    public void Unbind() {
        edges.Clear();
        edgeStartArc.Clear();
        loop = false;
        RouteId = null;
        TotalLength = 0f;
        Reset();
    }

    /// <summary>Moves the cursor to the start of an edge without projecting a position.</summary>
    /// <param name="startEdgeIndex">Edge index to rest on; clamped into range.</param>
    public void Reset(int startEdgeIndex = 0) {
        edgeIndex = IsBound ? Mathf.Clamp(startEdgeIndex, 0, edges.Count - 1) : 0;
        segmentIndex = 0;
        distanceAlongEdge = 0f;
        lapCount = 0;
    }

    /// <summary>
    /// Places the cursor at the route point nearest to a map-local position, searching the whole
    /// route once. Meant for spawn placement, not per-step tracking — use <see cref="Advance"/> for that.
    /// </summary>
    /// <param name="localPosition">Position in map-local navigation coordinates.</param>
    /// <returns>False when unbound.</returns>
    public bool TryPlaceAt(Vector2 localPosition) {
        if (!IsBound) return false;
        float best = float.PositiveInfinity;
        int bestEdge = 0, bestSegment = 0;
        float bestDistance = 0f;
        for (int e = 0; e < edges.Count; e++) {
            var sample = edges[e];
            for (int s = 0; s < sample.points.Count - 1; s++) {
                float t = ProjectOntoSegment(localPosition, sample.points[s], sample.points[s + 1], out float sqrDistance);
                if (sqrDistance < best) {
                    best = sqrDistance;
                    bestEdge = e;
                    bestSegment = s;
                    bestDistance = sample.cumulative[s] + t * (sample.cumulative[s + 1] - sample.cumulative[s]);
                }
            }
        }
        edgeIndex = bestEdge;
        segmentIndex = bestSegment;
        distanceAlongEdge = bestDistance;
        lapCount = 0;
        return true;
    }

    /// <summary>
    /// Advances progress toward the nearest route point within a bounded forward window. The
    /// current segment may be re-projected backwards (a vehicle shoved back a little), but the
    /// search otherwise only walks forward through the route, crossing edge boundaries and wrapping
    /// on a loop, so a fast vehicle that skips a node in one step still lands on the right edge and
    /// a lap is counted exactly once per seam crossing.
    /// </summary>
    /// <param name="localPosition">Vehicle position in map-local navigation coordinates.</param>
    /// <param name="searchWindow">Arc length to look ahead for the nearest projection.</param>
    public void Advance(Vector2 localPosition, float searchWindow) {
        if (!IsBound) return;
        if (searchWindow < 0f || float.IsNaN(searchWindow)) searchWindow = 0f;

        int e = edgeIndex;
        int s = segmentIndex;
        int laps = lapCount;
        float walked = 0f;
        float best = float.PositiveInfinity;
        int bestEdge = edgeIndex, bestSegment = segmentIndex, bestLaps = lapCount;
        float bestDistance = distanceAlongEdge;

        // Segment containing current progress is included even for a zero window.
        while (true) {
            var sample = edges[e];
            float t = ProjectOntoSegment(localPosition, sample.points[s], sample.points[s + 1], out float sqrDistance);
            float candidate = sample.cumulative[s] + t * (sample.cumulative[s + 1] - sample.cumulative[s]);
            if (sqrDistance < best) {
                best = sqrDistance;
                bestEdge = e;
                bestSegment = s;
                bestLaps = laps;
                bestDistance = candidate;
            }

            float segmentEnd = sample.cumulative[s + 1];
            float startOnSegment = (e == edgeIndex && s == segmentIndex && laps == lapCount) ? distanceAlongEdge : sample.cumulative[s];
            walked += segmentEnd - startOnSegment;
            if (walked >= searchWindow) break;

            s++;
            if (s >= sample.points.Count - 1) {
                s = 0;
                e++;
                if (e >= edges.Count) {
                    if (!loop) break;
                    e = 0;
                    laps++;
                }
            }
            if (e == edgeIndex && s == segmentIndex && laps > lapCount) break; // full wrap: stop before revisiting
        }

        edgeIndex = bestEdge;
        segmentIndex = bestSegment;
        distanceAlongEdge = bestDistance;
        lapCount = bestLaps;
    }

    /// <summary>
    /// Returns the route point an arc-length distance ahead of the cursor, crossing edge boundaries
    /// and wrapping on a loop; a non-loop route clamps at its final node. The point always lies on
    /// the authored polyline, never on a chord that would cut through a corner.
    /// </summary>
    /// <param name="aheadDistance">Arc length ahead of current progress; negative is treated as zero.</param>
    /// <param name="tangent">Unit direction of the polyline at the sampled point.</param>
    public Vector2 SamplePoint(float aheadDistance, out Vector2 tangent) {
        tangent = Vector2.up;
        if (!IsBound) return Vector2.zero;
        if (aheadDistance < 0f || float.IsNaN(aheadDistance)) aheadDistance = 0f;

        int e = edgeIndex;
        float target = distanceAlongEdge + aheadDistance;
        int guard = edges.Count + 1;
        while (target > edges[e].length && guard-- > 0) {
            if (e == edges.Count - 1 && !loop) {
                target = edges[e].length;
                break;
            }
            target -= edges[e].length;
            e = (e + 1) % edges.Count;
        }

        var sample = edges[e];
        int s = Mathf.Clamp(SegmentIndexFor(sample, target), 0, sample.points.Count - 2);
        Vector2 a = sample.points[s];
        Vector2 b = sample.points[s + 1];
        float segmentLength = sample.cumulative[s + 1] - sample.cumulative[s];
        float t = segmentLength > 0f ? Mathf.Clamp01((target - sample.cumulative[s]) / segmentLength) : 0f;
        Vector2 direction = b - a;
        if (direction.sqrMagnitude > 0f) tangent = direction.normalized;
        return Vector2.LerpUnclamped(a, b, t);
    }

    /// <summary>
    /// Arc length from the cursor to the next polyline vertex whose direction change is at least
    /// <paramref name="minTurnAngleDegrees"/>, or <paramref name="maxSearch"/> when none is found
    /// within that distance. Lets a follower shorten its lookahead before a corner instead of
    /// aiming across it.
    /// </summary>
    public float DistanceToNextTurn(float minTurnAngleDegrees, float maxSearch) {
        TryGetNextTurn(minTurnAngleDegrees, maxSearch, out float distance, out _);
        return distance;
    }

    /// <summary>
    /// Finds the next polyline vertex (node or interior point, wrapping on a loop) whose direction
    /// change is at least <paramref name="minTurnAngleDegrees"/>. On a non-loop route the final
    /// node counts as a full stop (angle 180).
    /// </summary>
    /// <param name="minTurnAngleDegrees">Smallest direction change that counts as a turn.</param>
    /// <param name="maxSearch">Arc length to search ahead of the cursor.</param>
    /// <param name="distance">Arc length to the turn, or <paramref name="maxSearch"/> when none was found.</param>
    /// <param name="turnAngleDegrees">Unsigned direction change at the found turn, or 0 when none was found.</param>
    /// <returns>True when a qualifying turn lies within <paramref name="maxSearch"/>.</returns>
    public bool TryGetNextTurn(float minTurnAngleDegrees, float maxSearch, out float distance, out float turnAngleDegrees) {
        distance = maxSearch;
        turnAngleDegrees = 0f;
        if (!IsBound || maxSearch <= 0f) return false;

        int e = edgeIndex;
        int s = segmentIndex;
        float walked = edges[e].cumulative[s + 1] - distanceAlongEdge;
        int guard = 0;
        const int maxVertices = 4096;
        while (walked < maxSearch && guard++ < maxVertices) {
            var sample = edges[e];
            Vector2 incoming = sample.points[s + 1] - sample.points[s];
            int nextE = e, nextS = s + 1;
            if (nextS >= sample.points.Count - 1) {
                nextS = 0;
                nextE = e + 1;
                if (nextE >= edges.Count) {
                    if (!loop) {
                        distance = walked;
                        turnAngleDegrees = 180f; // route end: the follower must be able to stop here
                        return true;
                    }
                    nextE = 0;
                }
            }
            var next = edges[nextE];
            Vector2 outgoing = next.points[nextS + 1] - next.points[nextS];
            float angle = Vector2.Angle(incoming, outgoing);
            if (angle >= minTurnAngleDegrees) {
                distance = walked;
                turnAngleDegrees = angle;
                return true;
            }
            walked += next.cumulative[nextS + 1] - next.cumulative[nextS];
            e = nextE;
            s = nextS;
        }
        return false;
    }

    /// <summary>Arc length remaining on the current edge from the cursor to the edge's to-node.</summary>
    public float DistanceToEdgeEnd => IsBound ? Mathf.Max(0f, edges[edgeIndex].length - distanceAlongEdge) : 0f;

    /// <summary>The edge after the current one in route order (wrapping on a loop), or null on a non-loop's last edge or when unbound.</summary>
    public RoadEdgeRecord NextEdge {
        get {
            if (!IsBound) return null;
            int next = edgeIndex + 1;
            if (next >= edges.Count) {
                if (!loop) return null;
                next = 0;
            }
            return edges[next].edge;
        }
    }

    /// <summary>Number of edges in the bound route.</summary>
    public int EdgeCount => edges.Count;

    /// <summary>Edge record at a route index, or null when out of range.</summary>
    public RoadEdgeRecord EdgeAt(int index) => IsBound && index >= 0 && index < edges.Count ? edges[index].edge : null;

    /// <summary>Full polyline (from-node, interior points, to-node) of the edge at a route index; read-only by convention, null when out of range.</summary>
    public IReadOnlyList<Vector2> PolylineAt(int index) => IsBound && index >= 0 && index < edges.Count ? edges[index].points : null;

    static int SegmentIndexFor(EdgeSample sample, float distance) {
        for (int i = 0; i < sample.cumulative.Count - 1; i++) {
            if (distance <= sample.cumulative[i + 1]) return i;
        }
        return sample.cumulative.Count - 2;
    }

    static float ProjectOntoSegment(Vector2 point, Vector2 a, Vector2 b, out float sqrDistance) {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq) : 0f;
        sqrDistance = (a + ab * t - point).sqrMagnitude;
        return t;
    }
}
