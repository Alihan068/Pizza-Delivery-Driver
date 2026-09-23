using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded deterministic pose-lattice A* over free-drive line and arc primitives.</summary>
public sealed class PoliceFreePathPlanner {
    /// <summary>Terminal state of a bounded free-drive search.</summary>
    public enum Status { Pending, Found, NoPath, BudgetExceeded, InvalidInput }

    /// <summary>Primitive used by a free-drive path.</summary>
    public enum PrimitiveKind { Straight, Arc }

    /// <summary>Immutable continuous pose primitive.</summary>
    public readonly struct Primitive {
        /// <summary>Creates a primitive.</summary>
        public Primitive(PrimitiveKind kind, Vector2 startPivot, float startHeadingDegrees, Vector2 endPivot,
            float endHeadingDegrees, float turnRadius, float deltaHeadingDegrees, float length) {
            this.kind = kind;
            this.startPivot = startPivot;
            this.startHeadingDegrees = startHeadingDegrees;
            this.endPivot = endPivot;
            this.endHeadingDegrees = endHeadingDegrees;
            this.turnRadius = turnRadius;
            this.deltaHeadingDegrees = deltaHeadingDegrees;
            this.length = length;
        }

        /// <summary>Primitive type.</summary>
        public readonly PrimitiveKind kind;
        /// <summary>Continuous start pivot.</summary>
        public readonly Vector2 startPivot;
        /// <summary>Continuous start heading.</summary>
        public readonly float startHeadingDegrees;
        /// <summary>Continuous end pivot.</summary>
        public readonly Vector2 endPivot;
        /// <summary>Continuous end heading.</summary>
        public readonly float endHeadingDegrees;
        /// <summary>Turn radius for an arc, or zero for a straight.</summary>
        public readonly float turnRadius;
        /// <summary>Heading delta for an arc, or zero for a straight.</summary>
        public readonly float deltaHeadingDegrees;
        /// <summary>Traversal length.</summary>
        public readonly float length;
    }

    /// <summary>Immutable search input with all geometry and bounded-work facts frozen.</summary>
    public readonly struct Request {
        /// <summary>Creates a search request.</summary>
        public Request(int requestId, int unitLifeId, int targetLifeId, int geometryRevision, Vector2 startPivot,
            float startHeadingDegrees, Vector2 targetPivot, PoliceFreeNavigationGeometry.Footprint footprint,
            PoliceFreeNavigationQuery query, float goalRadius = .75f, float turnRadius = 4f,
            float primitiveLength = 1f, float maxSweepAngle = 10f, float maxSweepLength = .25f,
            float turnCost = .1f, float cellSize = .75f, int headingBins = 64, int maxStates = 8192,
            int requestWorkLimit = 32768, float clearanceMargin = .1f) {
            this.requestId = requestId;
            this.unitLifeId = unitLifeId;
            this.targetLifeId = targetLifeId;
            this.geometryRevision = geometryRevision;
            this.startPivot = startPivot;
            this.startHeadingDegrees = startHeadingDegrees;
            this.targetPivot = targetPivot;
            this.footprint = footprint;
            this.query = query;
            this.goalRadius = goalRadius;
            this.turnRadius = turnRadius;
            this.primitiveLength = primitiveLength;
            this.maxSweepAngle = maxSweepAngle;
            this.maxSweepLength = maxSweepLength;
            this.turnCost = turnCost;
            this.cellSize = cellSize;
            this.headingBins = headingBins;
            this.maxStates = maxStates;
            this.requestWorkLimit = requestWorkLimit;
            this.clearanceMargin = clearanceMargin;
        }

        /// <summary>Request identity.</summary>
        public readonly int requestId;
        /// <summary>Unit life identity.</summary>
        public readonly int unitLifeId;
        /// <summary>Target life identity.</summary>
        public readonly int targetLifeId;
        /// <summary>Geometry revision.</summary>
        public readonly int geometryRevision;
        /// <summary>Actual start pivot.</summary>
        public readonly Vector2 startPivot;
        /// <summary>Actual start heading.</summary>
        public readonly float startHeadingDegrees;
        /// <summary>Target pivot.</summary>
        public readonly Vector2 targetPivot;
        /// <summary>Vehicle footprint.</summary>
        public readonly PoliceFreeNavigationGeometry.Footprint footprint;
        /// <summary>Injected full-footprint clearance query.</summary>
        public readonly PoliceFreeNavigationQuery query;
        /// <summary>Goal pivot tolerance.</summary>
        public readonly float goalRadius;
        /// <summary>Minimum turning radius.</summary>
        public readonly float turnRadius;
        /// <summary>Nominal primitive length.</summary>
        public readonly float primitiveLength;
        /// <summary>Maximum validation arc heading change.</summary>
        public readonly float maxSweepAngle;
        /// <summary>Maximum validation envelope length.</summary>
        public readonly float maxSweepLength;
        /// <summary>Arc turn cost per radian.</summary>
        public readonly float turnCost;
        /// <summary>Visited position cell size.</summary>
        public readonly float cellSize;
        /// <summary>Heading quantization count.</summary>
        public readonly int headingBins;
        /// <summary>Maximum stored states.</summary>
        public readonly int maxStates;
        /// <summary>Total request work cap.</summary>
        public readonly int requestWorkLimit;
        /// <summary>Conservative footprint clearance margin.</summary>
        public readonly float clearanceMargin;
    }

    /// <summary>Search result; primitive data is copy-safe and only exposed for Found.</summary>
    public sealed class Result {
        /// <summary>Creates a result.</summary>
        public Result(Status status, int requestId, int unitLifeId, int targetLifeId, int geometryRevision,
            int consumedWork, int totalWork, Primitive[] primitives = null, string reason = null) {
            this.status = status;
            this.requestId = requestId;
            this.unitLifeId = unitLifeId;
            this.targetLifeId = targetLifeId;
            this.geometryRevision = geometryRevision;
            this.consumedWork = consumedWork;
            this.totalWork = totalWork;
            primitiveData = primitives;
            this.reason = reason;
        }

        /// <summary>Search status.</summary>
        public readonly Status status;
        /// <summary>Request identity.</summary>
        public readonly int requestId;
        /// <summary>Unit identity.</summary>
        public readonly int unitLifeId;
        /// <summary>Target identity.</summary>
        public readonly int targetLifeId;
        /// <summary>Geometry revision.</summary>
        public readonly int geometryRevision;
        /// <summary>Consumed node, goal, and envelope work.</summary>
        public readonly int consumedWork;
        /// <summary>Total request work cap.</summary>
        public readonly int totalWork;
        /// <summary>Terminal diagnostic reason.</summary>
        public readonly string reason;
        /// <summary>Copy-safe primitives; empty for every non-Found result.</summary>
        internal readonly Primitive[] primitiveData;

        /// <summary>Returns a defensive copy of the Found primitive chain.</summary>
        public IReadOnlyList<Primitive> Primitives => status == Status.Found && primitiveData != null
            ? (Primitive[])primitiveData.Clone() : Array.Empty<Primitive>();
    }

    struct Key : IEquatable<Key> {
        public Key(Vector2 pivot, float heading, float cell, int bins) {
            x = Mathf.RoundToInt(pivot.x / cell);
            y = Mathf.RoundToInt(pivot.y / cell);
            int bin = Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / 360f * bins);
            headingBin = bin % bins;
        }

        public bool Equals(Key other) => x == other.x && y == other.y && headingBin == other.headingBin;
        public override bool Equals(object obj) => obj is Key && Equals((Key)obj);
        public override int GetHashCode() => ((x * 397) ^ y) * 397 ^ headingBin;

        readonly int x;
        readonly int y;
        readonly int headingBin;
    }

    struct State {
        public Vector2 pivot;
        public float heading;
        public float g;
        public float h;
        public int parent;
        public Primitive primitive;
        public Key key;
        public int order;
    }

    struct HeapItem {
        public int state;
        public float f;
        public float h;
        public int order;
    }

    Request request;
    readonly List<State> states = new List<State>();
    readonly List<HeapItem> heap = new List<HeapItem>();
    readonly Dictionary<Key, float> best = new Dictionary<Key, float>();
    int consumed;
    int currentState = -1;
    int neighborIndex;
    int pendingSegmentIndex;
    int pendingSegmentCount;
    Primitive pendingPrimitive;
    bool validatingPrimitive;
    bool goalPending;
    bool goalEnvelopePending;
    bool started;
    bool finished;
    Result result;
    bool connectionPending;
    bool validatingConnection;
    Primitive[] connection;
    int connectionIndex;

    /// <summary>Starts a new search from the supplied actual pose.</summary>
    public Result Begin(Request value) {
        Cancel();
        request = value;
        if (!IsValid(value)) return Finish(Status.InvalidInput, "InvalidInput");

        var key = new Key(value.startPivot, value.startHeadingDegrees, value.cellSize, value.headingBins);
        var state = new State {
            pivot = value.startPivot,
            heading = value.startHeadingDegrees,
            g = 0f,
            h = Vector2.Distance(value.startPivot, value.targetPivot),
            parent = -1,
            key = key,
            order = 0
        };
        states.Add(state);
        best[key] = 0f;
        Push(0, state.h, state.h, 0);
        return Snapshot();
    }

    /// <summary>Advances the search by at most the supplied deterministic work grant.</summary>
    public Result Step(int workGrant) {
        if (finished) return result;
        int grant = Mathf.Max(0, workGrant);
        while (grant > 0 && !finished) {
            if (consumed >= request.requestWorkLimit) {
                Finish(Status.BudgetExceeded, "RequestWorkLimit");
                break;
            }

            if (!started) {
                started = true;
                var check = request.query.CheckPose(request.startPivot, request.startHeadingDegrees,
                    request.footprint, request.clearanceMargin, NewQueryBudget());
                SpendQuery(check, ref grant);
                AcceptStart(check);
                continue;
            }

            if (goalEnvelopePending) {
                var check = request.query.CheckPose(states[currentState].pivot, states[currentState].heading,
                    request.footprint, request.clearanceMargin, NewQueryBudget());
                SpendQuery(check, ref grant);
                goalEnvelopePending = false;
                AcceptGoal(check);
                continue;
            }

            if (goalPending) {
                goalPending = false;
                if (!SpendBookkeeping(ref grant)) break;
                goalEnvelopePending = true;
                continue;
            }

            if (connectionPending) {
                connectionPending = false;
                if (!SpendBookkeeping(ref grant)) break;
                State from = states[currentState];
                float distance = Vector2.Distance(from.pivot, request.targetPivot);
                if (PoliceFinalApproachGeometry.TrySolve(from.pivot,
                    PoliceFreeNavigationGeometry.Rotate(Vector2.up, from.heading), request.targetPivot,
                    request.turnRadius, distance + 2f * Mathf.PI * request.turnRadius, out var approach)) {
                    float exitHeading = Vector2.SignedAngle(Vector2.up, approach.exitDirection);
                    Primitive line = new Primitive(PrimitiveKind.Straight, approach.arcExit, exitHeading,
                        approach.target, exitHeading, 0f, 0f, approach.straightLength);
                    if (approach.kind == PoliceFinalApproachGeometry.Kind.Straight) connection = new[] { line };
                    else {
                        Primitive arc = MakeArc(approach.start, from.heading, approach.radius,
                            approach.turnSign * approach.arcLength / approach.radius * Mathf.Rad2Deg, approach.arcLength);
                        connection = approach.straightLength > 0.0001f ? new[] { arc, line } : new[] { arc };
                    }
                    connectionIndex = 0;
                    validatingConnection = true;
                    BeginPrimitive(connection[0]);
                }
                continue;
            }

            if (validatingPrimitive) {
                ProcessPendingEnvelope(ref grant);
                continue;
            }

            if (currentState < 0) {
                if (heap.Count == 0) {
                    Finish(Status.NoPath, "FrontierEmpty");
                    break;
                }
                if (!SpendBookkeeping(ref grant)) break;
                if (!Pop(out currentState)) {
                    Finish(Status.NoPath, "FrontierEmpty");
                    break;
                }
                neighborIndex = 0;
                if (Vector2.Distance(states[currentState].pivot, request.targetPivot) <= request.goalRadius)
                    goalPending = true;
                else connectionPending = true;
                continue;
            }

            if (neighborIndex >= 5) {
                currentState = -1;
                continue;
            }

            BeginPrimitive(MakeNeighbor(states[currentState], neighborIndex));
        }
        return finished ? result : Snapshot();
    }

    /// <summary>Cancels the search and clears all frontier, validation, and visited state.</summary>
    public void Cancel() {
        states.Clear();
        heap.Clear();
        best.Clear();
        currentState = -1;
        neighborIndex = 0;
        pendingSegmentIndex = 0;
        pendingSegmentCount = 0;
        pendingPrimitive = default;
        validatingPrimitive = false;
        goalPending = false;
        goalEnvelopePending = false;
        consumed = 0;
        started = false;
        finished = false;
        result = null;
        connectionPending = false;
        validatingConnection = false;
        connection = null;
        connectionIndex = 0;
    }

    /// <summary>Returns the most recent result without advancing work.</summary>
    public Result Current => finished ? result : Snapshot();

    void BeginPrimitive(Primitive primitive) {
        pendingPrimitive = primitive;
        pendingSegmentIndex = 0;
        pendingSegmentCount = GetSegmentCount(primitive);
        validatingPrimitive = true;
    }

    void ProcessPendingEnvelope(ref int grant) {
        Primitive segment = MakeSegment(pendingPrimitive, pendingSegmentIndex, pendingSegmentCount);
        var budget = NewQueryBudget();
        var check = segment.kind == PrimitiveKind.Straight
            ? request.query.CheckLine(segment.startPivot, segment.endPivot, request.footprint,
                segment.startHeadingDegrees, request.clearanceMargin, budget)
            : request.query.CheckArc(segment.startPivot, segment.startHeadingDegrees, segment.turnRadius,
                segment.deltaHeadingDegrees, request.footprint, request.clearanceMargin, budget);
        SpendQuery(check, ref grant);
        if (!AcceptEnvelope(check)) {
            validatingPrimitive = false;
            if (validatingConnection) { validatingConnection = false; connection = null; }
            else neighborIndex++;
            return;
        }

        pendingSegmentIndex++;
        if (pendingSegmentIndex < pendingSegmentCount) return;

        validatingPrimitive = false;
        if (validatingConnection) {
            connectionIndex++;
            if (connectionIndex < connection.Length) BeginPrimitive(connection[connectionIndex]);
            else FinishFound(currentState, connection);
            return;
        }
        AddNeighborState(pendingPrimitive);
        neighborIndex++;
    }

    void AddNeighborState(Primitive primitive) {
        State from = states[currentState];
        float g = from.g + primitive.length + request.turnCost *
            Mathf.Abs(primitive.deltaHeadingDegrees) * Mathf.Deg2Rad;
        var next = new State {
            pivot = primitive.endPivot,
            heading = primitive.endHeadingDegrees,
            g = g,
            h = Vector2.Distance(primitive.endPivot, request.targetPivot),
            parent = currentState,
            primitive = primitive,
            key = new Key(primitive.endPivot, primitive.endHeadingDegrees, request.cellSize, request.headingBins),
            order = states.Count
        };
        if (best.TryGetValue(next.key, out var old) && g >= old - 0.000001f) return;
        if (states.Count >= request.maxStates) {
            Finish(Status.BudgetExceeded, "MaxStates");
            return;
        }
        best[next.key] = g;
        int index = states.Count;
        states.Add(next);
        Push(index, g + next.h, next.h, next.order);
    }

    Primitive MakeNeighbor(State from, int index) {
        if (index == 0) return MakeStraight(from, request.primitiveLength);
        float radius = index <= 2 ? request.turnRadius : request.turnRadius * 2f;
        float sign = index == 1 || index == 3 ? 1f : -1f;
        float delta = sign * request.primitiveLength / radius * Mathf.Rad2Deg;
        return MakeArc(from.pivot, from.heading, radius, delta, request.primitiveLength);
    }

    Primitive MakeStraight(State from, float length) {
        Vector2 end = from.pivot + PoliceFreeNavigationGeometry.Rotate(Vector2.up * length, from.heading);
        return new Primitive(PrimitiveKind.Straight, from.pivot, from.heading, end, from.heading,
            0f, 0f, length);
    }

    Primitive MakeArc(Vector2 startPivot, float startHeading, float radius, float delta, float length) {
        float radians = delta * Mathf.Deg2Rad;
        Vector2 forward = PoliceFreeNavigationGeometry.Rotate(Vector2.up, startHeading);
        Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 center = startPivot + left * Mathf.Sign(radians) * radius;
        Vector2 offset = startPivot - center;
        float startAngle = Mathf.Atan2(offset.y, offset.x);
        float endAngle = startAngle + radians;
        Vector2 end = center + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;
        return new Primitive(PrimitiveKind.Arc, startPivot, startHeading, end, startHeading + delta,
            radius, delta, length);
    }

    Primitive MakeSegment(Primitive primitive, int index, int count) {
        float startT = index / (float)count;
        float endT = (index + 1) / (float)count;
        if (primitive.kind == PrimitiveKind.Straight) {
            Vector2 lineStart = Vector2.Lerp(primitive.startPivot, primitive.endPivot, startT);
            Vector2 lineEnd = Vector2.Lerp(primitive.startPivot, primitive.endPivot, endT);
            return new Primitive(PrimitiveKind.Straight, lineStart, primitive.startHeadingDegrees, lineEnd,
                primitive.startHeadingDegrees, 0f, 0f, primitive.length / count);
        }

        float startDelta = primitive.deltaHeadingDegrees * startT;
        float delta = primitive.deltaHeadingDegrees * (endT - startT);
        Primitive start = MakeArc(primitive.startPivot, primitive.startHeadingDegrees, primitive.turnRadius,
            startDelta, primitive.length * startT);
        return MakeArc(start.endPivot, start.endHeadingDegrees, primitive.turnRadius, delta,
            primitive.length / count);
    }

    int GetSegmentCount(Primitive primitive) {
        double lengthSegments = Math.Ceiling(primitive.length / request.maxSweepLength);
        double angleSegments = primitive.kind == PrimitiveKind.Straight
            ? 1d : Math.Ceiling(Math.Abs(primitive.deltaHeadingDegrees) / request.maxSweepAngle);
        double required = Math.Max(1d, Math.Max(lengthSegments, angleSegments));
        long cap = request.requestWorkLimit == int.MaxValue ? int.MaxValue : (long)request.requestWorkLimit + 1L;
        if (required >= cap) return (int)cap;
        return Mathf.Max(1, (int)required);
    }

    void AcceptStart(PoliceFreeNavigationQuery.Result check) {
        if (!AcceptRevision(check)) return;
        if (check.status == PoliceFreeNavigationQuery.Status.Blocked) {
            Finish(Status.NoPath, "StartBlocked");
            return;
        }
        AcceptEnvelope(check);
    }

    void AcceptGoal(PoliceFreeNavigationQuery.Result check) {
        if (!AcceptRevision(check)) return;
        if (check.status == PoliceFreeNavigationQuery.Status.Clear) {
            FinishFound(currentState);
            return;
        }
        if (check.status == PoliceFreeNavigationQuery.Status.Blocked) {
            currentState = -1;
            return;
        }
        AcceptFailure(check);
    }

    bool AcceptEnvelope(PoliceFreeNavigationQuery.Result check) {
        if (!AcceptRevision(check)) return false;
        if (check.status == PoliceFreeNavigationQuery.Status.Clear) return true;
        if (check.status == PoliceFreeNavigationQuery.Status.Blocked) return false;
        AcceptFailure(check);
        return false;
    }

    bool AcceptRevision(PoliceFreeNavigationQuery.Result check) {
        if (check.geometryRevision == request.geometryRevision) return true;
        Finish(Status.InvalidInput, "GeometryRevision");
        return false;
    }

    void AcceptFailure(PoliceFreeNavigationQuery.Result check) {
        if (check.status == PoliceFreeNavigationQuery.Status.Budget) {
            Finish(Status.BudgetExceeded, "QueryBudget");
            return;
        }
        Finish(Status.InvalidInput, check.reason.ToString());
    }

    void SpendQuery(PoliceFreeNavigationQuery.Result check, ref int grant) {
        if (check.consumedWork <= 0) return;
        consumed = Mathf.Min(request.requestWorkLimit, consumed + check.consumedWork);
        grant = Mathf.Max(0, grant - check.consumedWork);
    }

    bool SpendBookkeeping(ref int grant) {
        if (grant <= 0 || consumed >= request.requestWorkLimit) return false;
        consumed++;
        grant--;
        return true;
    }

    PoliceFreeNavigationQuery.SearchBudget NewQueryBudget() =>
        new PoliceFreeNavigationQuery.SearchBudget(request.requestWorkLimit - consumed);

    void FinishFound(int index, Primitive[] tail = null) {
        var path = new List<Primitive>();
        while (index >= 0) {
            if (states[index].parent >= 0) path.Add(states[index].primitive);
            index = states[index].parent;
        }
        path.Reverse();
        if (tail != null) path.AddRange(tail);
        Finish(Status.Found, null, path.ToArray());
    }

    Result Finish(Status status, string why, Primitive[] path = null) {
        finished = true;
        result = new Result(status, request.requestId, request.unitLifeId, request.targetLifeId,
            request.geometryRevision, consumed, request.requestWorkLimit, path, why);
        return result;
    }

    Result Snapshot() => new Result(Status.Pending, request.requestId, request.unitLifeId, request.targetLifeId,
        request.geometryRevision, consumed, request.requestWorkLimit);

    void Push(int state, float f, float h, int order) {
        heap.Add(new HeapItem { state = state, f = f, h = h, order = order });
        int index = heap.Count - 1;
        while (index > 0) {
            int parent = (index - 1) / 2;
            if (Less(heap[parent], heap[index])) break;
            Swap(index, parent);
            index = parent;
        }
    }

    bool Pop(out int state) {
        while (heap.Count > 0) {
            var top = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0) {
                int index = 0;
                while (true) {
                    int left = index * 2 + 1;
                    if (left >= heap.Count) break;
                    int right = left + 1;
                    int child = right < heap.Count && Less(heap[right], heap[left]) ? right : left;
                    if (Less(heap[index], heap[child])) break;
                    Swap(index, child);
                    index = child;
                }
            }

            State candidate = states[top.state];
            if (Mathf.Abs(candidate.g + candidate.h - top.f) < 0.000001f &&
                best.TryGetValue(candidate.key, out var current) &&
                Mathf.Abs(current - candidate.g) < 0.000001f) {
                state = top.state;
                return true;
            }
        }
        state = -1;
        return false;
    }

    bool Less(HeapItem a, HeapItem b) => a.f < b.f - 0.000001f ||
        (Mathf.Abs(a.f - b.f) <= 0.000001f &&
            (a.h < b.h - 0.000001f ||
                (Mathf.Abs(a.h - b.h) <= 0.000001f && a.order < b.order)));

    void Swap(int a, int b) {
        var value = heap[a];
        heap[a] = heap[b];
        heap[b] = value;
    }

    static bool IsValid(Request value) {
        return value.query != null && value.query.GeometryRevision == value.geometryRevision &&
            value.goalRadius >= 0f && IsFinite(value.goalRadius) &&
            value.turnRadius > 0f && IsFinite(value.turnRadius) && value.turnRadius <= float.MaxValue * .5f &&
            value.primitiveLength > 0f && IsFinite(value.primitiveLength) &&
            value.maxSweepAngle > 0f && IsFinite(value.maxSweepAngle) &&
            value.maxSweepLength > 0f && IsFinite(value.maxSweepLength) &&
            value.turnCost >= 0f && IsFinite(value.turnCost) &&
            value.cellSize > 0f && IsFinite(value.cellSize) &&
            value.headingBins > 0 && value.maxStates > 0 && value.requestWorkLimit > 0 &&
            value.clearanceMargin >= 0f && IsFinite(value.clearanceMargin) &&
            IsFinite(value.startPivot) && IsFinite(value.startHeadingDegrees) &&
            IsFinite(value.targetPivot) && IsValidFootprint(value.footprint);
    }

    static bool IsValidFootprint(PoliceFreeNavigationGeometry.Footprint footprint) =>
        IsFinite(footprint.size) && IsFinite(footprint.offset) &&
        footprint.size.x > 0f && footprint.size.y > 0f;

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
