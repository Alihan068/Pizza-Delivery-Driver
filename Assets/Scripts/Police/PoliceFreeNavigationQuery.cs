using UnityEngine;

/// <summary>Typed, fail-closed clearance query for deterministic free-drive geometry checks.</summary>
public sealed class PoliceFreeNavigationQuery {
    /// <summary>Identifies the source category used by a query.</summary>
    public enum SourceKind {
        /// <summary>Authored or permanent world geometry.</summary>
        Static,
        /// <summary>Explicitly supplied transient or actor geometry.</summary>
        Dynamic
    }

    /// <summary>Typed outcome of one bounded clearance request.</summary>
    public enum Status {
        /// <summary>The source proved the envelope clear.</summary>
        Clear,
        /// <summary>The envelope intersects bounds or blocking geometry.</summary>
        Blocked,
        /// <summary>The source could not provide a trustworthy answer.</summary>
        Unavailable,
        /// <summary>The shared work budget was exhausted before the query ran.</summary>
        Budget,
        /// <summary>Alias for callers that use the longer budget terminology.</summary>
        BudgetExceeded = Budget
    }

    /// <summary>Explains a typed clearance outcome without collapsing saturation into blockage.</summary>
    public enum Reason {
        /// <summary>No additional reason.</summary>
        None,
        /// <summary>The envelope did not fit inside local bounds.</summary>
        Bounds,
        /// <summary>The source found blocking occupancy.</summary>
        SourceBlocked,
        /// <summary>The source was invalid or saturated.</summary>
        SourceUnavailable,
        /// <summary>Input geometry was malformed or non-finite.</summary>
        InvalidInput,
        /// <summary>No budget unit remained.</summary>
        BudgetExhausted
    }

    /// <summary>Mutable deterministic work allowance shared by a sequence of queries.</summary>
    public sealed class SearchBudget {
        /// <summary>Creates a budget with a non-negative work limit.</summary>
        public SearchBudget(int workLimit) { limit = Mathf.Max(0, workLimit); }

        /// <summary>Maximum number of query units available.</summary>
        public int Limit => limit;

        /// <summary>Number of units consumed so far.</summary>
        public int ConsumedWork => consumedWork;

        /// <summary>Returns whether no work remains.</summary>
        public bool IsExhausted => consumedWork >= limit;

        /// <summary>Consumes one unit if available.</summary>
        public bool TryConsume() {
            if (consumedWork >= limit) return false;
            consumedWork++;
            return true;
        }

        readonly int limit;
        int consumedWork;
    }

    /// <summary>Source-level result returned before query bounds and budget policy are applied.</summary>
    public readonly struct SourceResult {
        /// <summary>Creates a source result.</summary>
        public SourceResult(Status status, Reason reason) { this.status = status; this.reason = reason; }

        /// <summary>Source status.</summary>
        public readonly Status status;

        /// <summary>Source reason.</summary>
        public readonly Reason reason;
    }

    /// <summary>Clearance source injected into the pure query.</summary>
    public interface IClearanceSource {
        /// <summary>Source category.</summary>
        SourceKind Kind { get; }

        /// <summary>Checks one oriented envelope without owning the query budget.</summary>
        SourceResult Check(Vector2 center, Vector2 size, float headingDegrees);
    }

    /// <summary>Rich query result carrying status, provenance, revision and consumed work.</summary>
    public readonly struct Result {
        /// <summary>Creates a query result.</summary>
        public Result(Status status, Reason reason, SourceKind sourceKind, int geometryRevision, int consumedWork) {
            this.status = status; this.reason = reason; this.sourceKind = sourceKind;
            this.geometryRevision = geometryRevision; this.consumedWork = consumedWork;
        }

        /// <summary>Typed result status.</summary>
        public readonly Status status;

        /// <summary>Reason for the status.</summary>
        public readonly Reason reason;

        /// <summary>Static or dynamic source used by the query.</summary>
        public readonly SourceKind sourceKind;

        /// <summary>Geometry revision observed by the query.</summary>
        public readonly int geometryRevision;

        /// <summary>Total budget work consumed when producing this result.</summary>
        public readonly int consumedWork;

        /// <summary>Whether the envelope was proven clear.</summary>
        public bool IsClear => status == Status.Clear;
    }

    /// <summary>Creates a deterministic query around an injected source and local bounds.</summary>
    public PoliceFreeNavigationQuery(IClearanceSource source, Rect localBounds, int geometryRevision = 1) {
        this.source = source; this.localBounds = localBounds; this.geometryRevision = Mathf.Max(0, geometryRevision);
    }

    /// <summary>Creates a static-source compatibility adapter for the existing bool-only interface.</summary>
    public PoliceFreeNavigationQuery(IAreaClearanceQuery query, Rect localBounds, int geometryRevision = 1)
        : this(query == null ? null : new BoolClearanceSource(query), localBounds, geometryRevision) { }

    /// <summary>Current geometry revision attached to all new results.</summary>
    public int GeometryRevision => geometryRevision;

    /// <summary>
    /// Optional shared memory of spots where a police car physically jammed. When set, every
    /// envelope overlapping a live jam spot is reported Blocked, so all bounded path searches route
    /// around a pocket one unit already failed in. Expiry keeps this from shrinking the map.
    /// </summary>
    public PoliceJamMemory JamMemory { get; set; }

    internal Rect LocalBounds => localBounds;

    /// <summary>Invalidates geometry and deterministically advances the revision.</summary>
    public int InvalidateGeometry() {
        geometryRevision = geometryRevision == int.MaxValue ? 0 : geometryRevision + 1;
        return geometryRevision;
    }

    /// <summary>Checks one already-built oriented envelope, consuming exactly one work unit.</summary>
    public Result CheckEnvelope(Vector2 center, Vector2 size, float headingDegrees, SearchBudget budget) {
        if (budget == null || !IsFinite(center) || !IsFinite(size) || !IsFinite(headingDegrees) ||
            size.x <= 0f || size.y <= 0f)
            return new Result(Status.Unavailable, Reason.InvalidInput, Kind, geometryRevision, 0);
        if (!budget.TryConsume()) return new Result(Status.Budget, Reason.BudgetExhausted, Kind, geometryRevision, 0);
        var envelope = new PoliceFreeNavigationGeometry.Envelope(center, size, headingDegrees);
        if (!PoliceFreeNavigationGeometry.IsWithinBounds(envelope, localBounds))
            return new Result(Status.Blocked, Reason.Bounds, Kind, geometryRevision, 1);
        if (JamMemory != null && JamMemory.Intersects(center, size))
            return new Result(Status.Blocked, Reason.SourceBlocked, Kind, geometryRevision, 1);
        if (source == null) return new Result(Status.Unavailable, Reason.SourceUnavailable, Kind, geometryRevision, 1);
        SourceResult result = source.Check(center, size, headingDegrees);
        if (result.status == Status.Clear) return new Result(Status.Clear, Reason.None, Kind, geometryRevision, 1);
        if (result.status == Status.Blocked) return new Result(Status.Blocked, Reason.SourceBlocked, Kind, geometryRevision, 1);
        return new Result(Status.Unavailable, Reason.SourceUnavailable, Kind, geometryRevision, 1);
    }

    /// <summary>Checks one pose footprint, including its local collider offset.</summary>
    public Result CheckPose(Vector2 pivot, float headingDegrees, PoliceFreeNavigationGeometry.Footprint footprint,
        float margin, SearchBudget budget) {
        if (!PoliceFreeNavigationGeometry.TryGetLineEnvelope(pivot, pivot, footprint, headingDegrees, margin, out var envelope))
            return InvalidResult(budget);
        return CheckEnvelope(envelope.center, envelope.size, envelope.headingDegrees, budget);
    }

    /// <summary>Checks a fixed-heading analytic line sweep.</summary>
    public Result CheckLine(Vector2 startPivot, Vector2 endPivot, PoliceFreeNavigationGeometry.Footprint footprint,
        float headingDegrees, float margin, SearchBudget budget) {
        if (!PoliceFreeNavigationGeometry.TryGetLineEnvelope(startPivot, endPivot, footprint, headingDegrees, margin, out var envelope))
            return InvalidResult(budget);
        return CheckEnvelope(envelope.center, envelope.size, envelope.headingDegrees, budget);
    }

    /// <summary>Checks an analytic circular arc sweep with rotation and sagitta padding.</summary>
    public Result CheckArc(Vector2 startPivot, float startHeadingDegrees, float turnRadius, float deltaHeadingDegrees,
        PoliceFreeNavigationGeometry.Footprint footprint, float margin, SearchBudget budget) {
        if (!PoliceFreeNavigationGeometry.TryGetArcEnvelope(startPivot, startHeadingDegrees, turnRadius,
            deltaHeadingDegrees, footprint, margin, out var envelope)) return InvalidResult(budget);
        return CheckEnvelope(envelope.center, envelope.size, envelope.headingDegrees, budget);
    }

    /// <summary>Static PhysicsScene2D source using an explicit non-allocating overlap buffer.</summary>
    public sealed class PhysicsClearanceSource : IClearanceSource {
        /// <summary>Creates a PhysicsScene2D source with explicit source kind, filter and capacity.</summary>
        public PhysicsClearanceSource(PhysicsScene2D scene, Vector2 origin, SourceKind kind, int capacity, LayerMask layers) {
            this.scene = scene; this.origin = origin; Kind = kind; results = new Collider2D[Mathf.Max(1, capacity)];
            filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = layers };
        }

        /// <summary>Source category.</summary>
        public SourceKind Kind { get; }

        /// <summary>Checks PhysicsScene2D occupancy and fails closed on invalid or saturated queries.</summary>
        public SourceResult Check(Vector2 center, Vector2 size, float headingDegrees) {
            if (!scene.IsValid() || !IsFinite(center) || !IsFinite(size) || !IsFinite(headingDegrees) ||
                size.x <= 0f || size.y <= 0f) return new SourceResult(Status.Unavailable, Reason.SourceUnavailable);
            int count = scene.OverlapBox(center + origin, size, headingDegrees, filter, results);
            if (count >= results.Length) return new SourceResult(Status.Unavailable, Reason.SourceUnavailable);
            for (int i = 0; i < count; i++) if (results[i] != null) {
                if (Kind == SourceKind.Dynamic || results[i].attachedRigidbody == null ||
                    results[i].attachedRigidbody.bodyType == RigidbodyType2D.Static)
                    return new SourceResult(Status.Blocked, Reason.SourceBlocked);
            }
            return new SourceResult(Status.Clear, Reason.None);
        }

        readonly PhysicsScene2D scene;
        readonly Vector2 origin;
        readonly Collider2D[] results;
        readonly ContactFilter2D filter;
    }

    sealed class BoolClearanceSource : IClearanceSource {
        public BoolClearanceSource(IAreaClearanceQuery query) { this.query = query; }
        public SourceKind Kind => SourceKind.Static;
        public SourceResult Check(Vector2 center, Vector2 size, float headingDegrees) =>
            query == null ? new SourceResult(Status.Unavailable, Reason.SourceUnavailable) :
            query.IsAreaClear(center, size, headingDegrees)
                ? new SourceResult(Status.Clear, Reason.None)
                : new SourceResult(Status.Blocked, Reason.SourceBlocked);
        readonly IAreaClearanceQuery query;
    }

    Result InvalidResult(SearchBudget budget) => new Result(Status.Unavailable, Reason.InvalidInput, Kind,
        geometryRevision, budget == null ? 0 : budget.ConsumedWork);

    SourceKind Kind => source == null ? SourceKind.Static : source.Kind;
    readonly IClearanceSource source;
    readonly Rect localBounds;
    int geometryRevision;

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
