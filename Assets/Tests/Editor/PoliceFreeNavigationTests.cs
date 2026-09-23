using NUnit.Framework;
using UnityEngine;

/// <summary>Fast pure EditMode coverage for free-drive geometry and query provenance.</summary>
public sealed class PoliceFreeNavigationTests {
    static readonly Rect Bounds = new Rect(-10f, -10f, 20f, 20f);
    static readonly PoliceFreeNavigationGeometry.Footprint Car =
        new PoliceFreeNavigationGeometry.Footprint(new Vector2(2f, 4f), new Vector2(1f, 0.5f));

    /// <summary>Clear and blocked fixed-heading sweeps preserve full footprint semantics.</summary>
    [Test]
    public void LineSweep_ReportsClearAndBlocked() {
        var budget = new PoliceFreeNavigationQuery.SearchBudget(2);
        var clear = new PoliceFreeNavigationQuery(new FakeSource(PoliceFreeNavigationQuery.Status.Clear), Bounds, 7);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Clear, clear.CheckLine(Vector2.zero, Vector2.up * 2f, Car, 0f, 0f, budget).status);
        var blocked = new PoliceFreeNavigationQuery(new FakeSource(PoliceFreeNavigationQuery.Status.Blocked), Bounds, 7);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Blocked, blocked.CheckLine(Vector2.zero, Vector2.up, Car, 0f, 0f,
            new PoliceFreeNavigationQuery.SearchBudget(1)).status);
    }

    /// <summary>Offset, rotation and local bounds alter the submitted envelope conservatively.</summary>
    [Test]
    public void Geometry_RespectsOffsetRotationAndBounds() {
        Assert.IsTrue(PoliceFreeNavigationGeometry.TryGetLineEnvelope(Vector2.zero, Vector2.up, Car, 90f, 0f, out var envelope));
        Assert.Less(envelope.center.x, -0.4f);
        Assert.IsFalse(PoliceFreeNavigationGeometry.IsWithinBounds(envelope, new Rect(-1f, -1f, 1f, 1f)));
    }

    /// <summary>Arc math produces a padded analytic envelope and rejects invalid turns.</summary>
    [Test]
    public void Arc_UsesRotationAndSagittaPadding() {
        Assert.IsTrue(PoliceFreeNavigationGeometry.TryGetArcEnvelope(Vector2.zero, 0f, 4f, 30f, Car, 0f, out var arc));
        Assert.Greater(arc.size.x, Car.size.x);
        Assert.IsFalse(PoliceFreeNavigationGeometry.TryGetArcEnvelope(Vector2.zero, 0f, 0f, 30f, Car, 0f, out _));
    }

    /// <summary>Results carry source category and the current geometry revision.</summary>
    [Test]
    public void Result_CarriesSourceAndRevision() {
        var query = new PoliceFreeNavigationQuery(new FakeSource(PoliceFreeNavigationQuery.Status.Clear,
            PoliceFreeNavigationQuery.SourceKind.Dynamic), Bounds, 12);
        var result = query.CheckEnvelope(Vector2.zero, Vector2.one, 0f, new PoliceFreeNavigationQuery.SearchBudget(1));
        Assert.AreEqual(PoliceFreeNavigationQuery.SourceKind.Dynamic, result.sourceKind);
        Assert.AreEqual(12, result.geometryRevision);
        query.InvalidateGeometry();
        Assert.AreEqual(13, query.GeometryRevision);
    }

    /// <summary>Unavailable source and exhausted budgets fail closed with distinct statuses.</summary>
    [Test]
    public void UnavailableAndBudget_AreDistinct() {
        var unavailable = new PoliceFreeNavigationQuery((PoliceFreeNavigationQuery.IClearanceSource)null, Bounds);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Unavailable,
            unavailable.CheckEnvelope(Vector2.zero, Vector2.one, 0f, new PoliceFreeNavigationQuery.SearchBudget(1)).status);
        var budget = new PoliceFreeNavigationQuery.SearchBudget(0);
        var query = new PoliceFreeNavigationQuery(new FakeSource(PoliceFreeNavigationQuery.Status.Clear), Bounds);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Budget, query.CheckEnvelope(Vector2.zero, Vector2.one, 0f, budget).status);
    }

    /// <summary>Repeated equal queries consume equal work and preserve deterministic outcomes.</summary>
    [Test]
    public void BudgetConsumption_IsDeterministic() {
        var query = new PoliceFreeNavigationQuery(new FakeSource(PoliceFreeNavigationQuery.Status.Clear), Bounds, 3);
        var firstBudget = new PoliceFreeNavigationQuery.SearchBudget(2);
        var secondBudget = new PoliceFreeNavigationQuery.SearchBudget(2);
        var first = query.CheckEnvelope(Vector2.zero, Vector2.one, 0f, firstBudget);
        var second = query.CheckEnvelope(Vector2.zero, Vector2.one, 0f, secondBudget);
        Assert.AreEqual(first.status, second.status);
        Assert.AreEqual(firstBudget.ConsumedWork, secondBudget.ConsumedWork);
        Assert.AreEqual(1, first.consumedWork);
    }

    /// <summary>The bool-only clearance interface remains usable through the static adapter.</summary>
    [Test]
    public void BoolAdapter_MapsClearAndBlocked() {
        var clear = new PoliceFreeNavigationQuery(new BoolSource(true), Bounds);
        var blocked = new PoliceFreeNavigationQuery(new BoolSource(false), Bounds);
        Assert.IsTrue(clear.CheckEnvelope(Vector2.zero, Vector2.one, 0f, new PoliceFreeNavigationQuery.SearchBudget(1)).IsClear);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Blocked,
            blocked.CheckEnvelope(Vector2.zero, Vector2.one, 0f, new PoliceFreeNavigationQuery.SearchBudget(1)).status);
    }

    sealed class FakeSource : PoliceFreeNavigationQuery.IClearanceSource {
        public FakeSource(PoliceFreeNavigationQuery.Status status,
            PoliceFreeNavigationQuery.SourceKind kind = PoliceFreeNavigationQuery.SourceKind.Static) { this.status = status; Kind = kind; }
        public PoliceFreeNavigationQuery.SourceKind Kind { get; }
        public PoliceFreeNavigationQuery.SourceResult Check(Vector2 center, Vector2 size, float headingDegrees) =>
            new PoliceFreeNavigationQuery.SourceResult(status, PoliceFreeNavigationQuery.Reason.None);
        readonly PoliceFreeNavigationQuery.Status status;
    }

    sealed class BoolSource : IAreaClearanceQuery {
        public BoolSource(bool clear) { this.clear = clear; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => clear;
        readonly bool clear;
    }
}
