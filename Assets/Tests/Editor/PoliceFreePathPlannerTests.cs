using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused EditMode coverage for bounded free-drive planning and scheduling.</summary>
public sealed class PoliceFreePathPlannerTests {
    static readonly Rect Bounds = new Rect(-20f, -20f, 40f, 40f);
    static readonly PoliceFreeNavigationGeometry.Footprint Car =
        new PoliceFreeNavigationGeometry.Footprint(new Vector2(1f, 2f), Vector2.zero);

    /// <summary>Production-default primitives can reach goals requiring a left or right quarter turn.</summary>
    [Test]
    public void QuarterTurnGoals_LeftAndRightReachFound() {
        var left = NewPlanner(new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds),
            1, 10, new Vector2(-5f, 5f));
        var right = NewPlanner(new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds),
            2, 11, new Vector2(5f, 5f));

        Assert.AreEqual(PoliceFreePathPlanner.Status.Found, Run(left, 20000).status);
        Assert.AreEqual(PoliceFreePathPlanner.Status.Found, Run(right, 20000).status);
    }

    /// <summary>Arc neighbors retain primitive length and expose both full and half curvature.</summary>
    [Test]
    public void ArcNeighborsUsePrimitiveLengthAtBothCurvatures() {
        var planner = NewPlanner(new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds),
            3, 12, new Vector2(5f, 5f));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var states = (System.Collections.IList)typeof(PoliceFreePathPlanner).GetField("states", flags).GetValue(planner);
        var neighbor = typeof(PoliceFreePathPlanner).GetMethod("MakeNeighbor", flags);
        bool foundRadiusFour = false;
        bool foundRadiusEight = false;
        for (int index = 1; index <= 4; index++) {
            var primitive = (PoliceFreePathPlanner.Primitive)neighbor.Invoke(planner, new object[] { states[0], index });
            Assert.That(primitive.length, Is.EqualTo(1f).Within(0.0001f));
            foundRadiusFour |= Mathf.Abs(primitive.turnRadius - 4f) < 0.0001f;
            foundRadiusEight |= Mathf.Abs(primitive.turnRadius - 8f) < 0.0001f;
        }
        Assert.IsTrue(foundRadiusFour && foundRadiusEight);
    }

    /// <summary>A blocking wall is routed around by the full-footprint planner.</summary>
    [Test]
    public void ObstacleDetour_FindsRouteAroundWall() {
        var source = new RectObstacleSource(new Rect(-.75f, 5.5f, 1.5f, 3f));
        var planner = NewPlanner(new PoliceFreeNavigationQuery(source, Bounds), 4, 13, new Vector2(0f, 10f));
        var result = Run(planner, 40000);

        Assert.AreEqual(PoliceFreePathPlanner.Status.Found, result.status);
        bool detoured = false;
        foreach (var primitive in result.Primitives)
            detoured |= Mathf.Abs(primitive.endPivot.x) > 1.2f;
        Assert.IsTrue(detoured);
    }

    /// <summary>A gap narrower than the complete footprint is not treated as traversable.</summary>
    [Test]
    public void NarrowGap_FullFootprintIsBlocked() {
        var source = new RectObstacleSource(
            new Rect(-2f, -1f, 1.45f, 7f),
            new Rect(.55f, -1f, 1.45f, 7f));
        var planner = NewPlanner(new PoliceFreeNavigationQuery(source, new Rect(-2f, -6f, 4f, 12f)),
            5, 14, new Vector2(0f, 5f));

        Assert.AreEqual(PoliceFreePathPlanner.Status.NoPath, Run(planner, 50000).status);
    }

    /// <summary>One-unit grants advance at most one unit and preserve deterministic pending state.</summary>
    [Test]
    public void StepOne_ConsumesAtMostOneWorkDeterministically() {
        var queryA = new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds);
        var queryB = new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds);
        var first = new PoliceFreePathPlanner();
        var second = new PoliceFreePathPlanner();
        first.Begin(CreateRequest(6, 15, new Vector2(4f, 4f), queryA));
        second.Begin(CreateRequest(6, 15, new Vector2(4f, 4f), queryB));
        int previous = 0;

        for (int i = 0; i < 200; i++) {
            var firstResult = first.Step(1);
            var secondResult = second.Step(1);
            Assert.LessOrEqual(firstResult.consumedWork - previous, 1);
            Assert.AreEqual(firstResult.status, secondResult.status);
            Assert.AreEqual(firstResult.consumedWork, secondResult.consumedWork);
            previous = firstResult.consumedWork;
            if (firstResult.status != PoliceFreePathPlanner.Status.Pending) break;
        }
    }

    /// <summary>The request cap remains distinct from NoPath when work is exhausted.</summary>
    [Test]
    public void BudgetExceeded_IsDistinctFromNoPath() {
        var planner = new PoliceFreePathPlanner();
        planner.Begin(CreateRequest(7, 16, new Vector2(4f, 4f),
            new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds), requestWorkLimit: 1));
        Assert.AreEqual(PoliceFreePathPlanner.Status.BudgetExceeded, planner.Step(8).status);
    }

    /// <summary>Geometry and life identity mismatches are rejected before a search can run.</summary>
    [Test]
    public void RevisionMismatch_IsInvalidInput() {
        var query = new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds, 9);
        var planner = new PoliceFreePathPlanner();
        planner.Begin(CreateRequest(8, 17, new Vector2(2f, 2f), query, revision: 8));
        Assert.AreEqual(PoliceFreePathPlanner.Status.InvalidInput, planner.Step(1).status);
    }

    /// <summary>Every request float rejects NaN and infinity instead of entering the frontier.</summary>
    [Test]
    public void RequestValidation_RejectsEveryNonFiniteFloat() {
        var query = new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds);
        PoliceFreePathPlanner.Request[] requests = {
            CreateRequest(9, 18, new Vector2(2f, 2f), query, goalRadius: float.NaN),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, turnRadius: float.PositiveInfinity),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, primitiveLength: float.NaN),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, maxSweepAngle: float.PositiveInfinity),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, maxSweepLength: float.NaN),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, turnCost: float.PositiveInfinity),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, cellSize: float.NaN),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, clearanceMargin: float.PositiveInfinity),
            CreateRequest(9, 18, new Vector2(float.NaN, 2f), query),
            CreateRequest(9, 18, new Vector2(2f, float.PositiveInfinity), query),
            CreateRequest(9, 18, new Vector2(2f, 2f), query, startHeading: float.NaN),
            CreateRequest(9, 18, new Vector2(2f, 2f), query,
                footprint: new PoliceFreeNavigationGeometry.Footprint(new Vector2(float.NaN, 2f), Vector2.zero)),
            CreateRequest(9, 18, new Vector2(2f, 2f), query,
                footprint: new PoliceFreeNavigationGeometry.Footprint(Vector2.one, new Vector2(float.PositiveInfinity, 0f)))
        };

        foreach (var request in requests)
            Assert.AreEqual(PoliceFreePathPlanner.Status.InvalidInput, new PoliceFreePathPlanner().Begin(request).status);
    }

    /// <summary>A newer same-target request queues without resetting the old search and receives its result.</summary>
    [Test]
    public void Scheduler_PublishesIntermediateThenCompletesNewestSameTargetRequest() {
        var scheduler = new PoliceNavigationScheduler();
        scheduler.Register(19, new PoliceFreePathPlanner());
        var query = new PoliceFreeNavigationQuery(new RectObstacleSource(), Bounds, 4);
        scheduler.Submit(CreateRequest(10, 19, new Vector2(4f, 4f), query, targetLifeId: 20));
        scheduler.Step(1);
        scheduler.Submit(CreateRequest(11, 19, new Vector2(4.5f, 4f), query, targetLifeId: 20));

        bool sawIntermediate = false;
        bool sawNewest = false;
        for (int i = 0; i < 50000; i++) {
            scheduler.Step(1);
            if (!scheduler.TryGetResult(19, out var result)) continue;
            if (result.requestId == 10 && result.status == PoliceFreePathPlanner.Status.Found) {
                sawIntermediate = true;
                Assert.Greater(result.Primitives.Count, 0);
            }
            if (result.requestId == 11 && result.status == PoliceFreePathPlanner.Status.Found) {
                sawNewest = true;
                break;
            }
        }

        Assert.IsTrue(sawIntermediate);
        Assert.IsTrue(sawNewest);
    }

    static PoliceFreePathPlanner.Result Run(PoliceFreePathPlanner planner, int grants) {
        PoliceFreePathPlanner.Result result = planner.Current;
        for (int i = 0; i < grants && result.status == PoliceFreePathPlanner.Status.Pending; i++)
            result = planner.Step(32);
        return result;
    }

    static PoliceFreePathPlanner NewPlanner(PoliceFreeNavigationQuery query, int requestId, int targetLifeId,
        Vector2 target) {
        var planner = new PoliceFreePathPlanner();
        planner.Begin(CreateRequest(requestId, requestId, target, query, targetLifeId: targetLifeId));
        return planner;
    }

    static PoliceFreePathPlanner.Request CreateRequest(int requestId, int unitLifeId, Vector2 target,
        PoliceFreeNavigationQuery query, int targetLifeId = 1, int revision = -1,
        Vector2? startPivot = null, float startHeading = 0f, float goalRadius = .75f,
        float turnRadius = 4f, float primitiveLength = 1f, float maxSweepAngle = 10f,
        float maxSweepLength = .25f, float turnCost = .1f, float cellSize = .75f,
        int headingBins = 64, int maxStates = 8192, int requestWorkLimit = 32768,
        float clearanceMargin = .1f, PoliceFreeNavigationGeometry.Footprint? footprint = null) {
        return new PoliceFreePathPlanner.Request(requestId, unitLifeId, targetLifeId,
            revision < 0 ? query.GeometryRevision : revision, startPivot ?? Vector2.zero, startHeading,
            target, footprint ?? Car, query, goalRadius, turnRadius, primitiveLength, maxSweepAngle,
            maxSweepLength, turnCost, cellSize, headingBins, maxStates, requestWorkLimit, clearanceMargin);
    }

    sealed class RectObstacleSource : PoliceFreeNavigationQuery.IClearanceSource {
        readonly Rect[] obstacles;

        public RectObstacleSource(params Rect[] obstacles) { this.obstacles = obstacles ?? Array.Empty<Rect>(); }

        public PoliceFreeNavigationQuery.SourceKind Kind => PoliceFreeNavigationQuery.SourceKind.Static;

        public PoliceFreeNavigationQuery.SourceResult Check(Vector2 center, Vector2 size, float headingDegrees) {
            Vector2 right = PoliceFreeNavigationGeometry.Rotate(Vector2.right, headingDegrees);
            Vector2 forward = PoliceFreeNavigationGeometry.Rotate(Vector2.up, headingDegrees);
            Vector2 half = size * .5f;
            Vector2 extent = new Vector2(Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y,
                Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y);
            Rect envelope = new Rect(center - extent, extent * 2f);
            foreach (var obstacle in obstacles)
                if (envelope.xMin < obstacle.xMax && envelope.xMax > obstacle.xMin &&
                    envelope.yMin < obstacle.yMax && envelope.yMax > obstacle.yMin)
                    return new PoliceFreeNavigationQuery.SourceResult(
                        PoliceFreeNavigationQuery.Status.Blocked,
                        PoliceFreeNavigationQuery.Reason.SourceBlocked);
            return new PoliceFreeNavigationQuery.SourceResult(PoliceFreeNavigationQuery.Status.Clear,
                PoliceFreeNavigationQuery.Reason.None);
        }
    }
}
