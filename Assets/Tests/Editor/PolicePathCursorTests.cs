using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Focused S08.3a tests for bounded open-span police route cursor behavior.</summary>
public sealed class PolicePathCursorTests {
    /// <summary>Confirms a clipped single span preserves its start and exact graph-length endpoint.</summary>
    [Test]
    public void Cursor_SinglePartialSpanPreservesEndpoints() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 2f, 8f) }, out var graph);
        Assert.IsTrue(cursor.IsBound);
        Assert.AreEqual(new RoadPathQuery.EdgeAnchor("e0", 2f), cursor.CurrentAnchor);
        Assert.AreEqual(6f, cursor.RemainingDistance, 0.0001f);
        int work = 128;
        Assert.IsTrue(cursor.TrySampleAhead(100f, ref work, out var endpoint));
        Assert.AreEqual(new Vector2(8f, 0f), endpoint);
        Assert.AreEqual(graph.GetArcLength("e0"), 10f, 0.0001f);
    }

    /// <summary>Confirms repeated edge IDs remain separate route occurrences and ties choose least forward distance.</summary>
    [Test]
    public void Cursor_RepeatedEdgeOccurrenceDoesNotJumpToLaterCrossing() {
        var cursor = Bind(LoopSpans(), out _);
        int advanceWork = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(2f, 0f), ref advanceWork));
        Assert.AreEqual("e0", cursor.CurrentAnchor.edgeId);
        Assert.AreEqual(2f, cursor.CurrentAnchor.distanceAlongEdge, 0.0001f);
        int sampleWork = 128;
        Assert.IsTrue(cursor.TrySampleAhead(8f, ref sampleWork, out var firstCorner));
        Assert.AreEqual(new Vector2(10f, 0f), firstCorner);
    }

    /// <summary>Confirms a same-edge zero-length route is valid, complete, and samples its endpoint.</summary>
    [Test]
    public void Cursor_ZeroLengthSpanStartsCompleted() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 5f, 5f) }, out _);
        Assert.IsTrue(cursor.IsBound);
        Assert.IsTrue(cursor.IsComplete);
        Assert.AreEqual(0f, cursor.RemainingDistance, 0.0001f);
        Assert.AreEqual(5f, cursor.CurrentAnchor.distanceAlongEdge, 0.0001f);
        int sampleWork = 128;
        Assert.IsTrue(cursor.TrySampleAhead(10f, ref sampleWork, out var point));
        Assert.AreEqual(new Vector2(5f, 0f), point);
        int advanceWork = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        Assert.IsTrue(cursor.IsComplete);
    }

    /// <summary>Confirms stationary valid measurements are accepted without creating progress.</summary>
    [Test]
    public void Cursor_StationaryMeasurementsAreAcceptedWithoutProgress() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _);
        for (int attempt = 0; attempt < 10; attempt++) {
            int work = 128;
            Assert.IsTrue(cursor.Advance(new Vector2(0f, 0f), ref work));
            Assert.AreEqual(0f, cursor.ProgressDistance, 0.0001f);
        }
    }

    /// <summary>Confirms a jump beyond the bounded projection window cannot teleport the cursor.</summary>
    [Test]
    public void Cursor_LargeMeasuredJumpBeyondWindowDoesNotTeleport() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _, new PolicePathCursor.TrackingLimits(6f, 2f, 1.5f, 0.05f));
        int work = 128;
        Assert.IsFalse(cursor.Advance(new Vector2(10f, 0f), ref work));
        Assert.AreEqual(0f, cursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms an off-route measurement is rejected without committing partial progress.</summary>
    [Test]
    public void Cursor_OffRouteMeasurementLeavesProgressUnchanged() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _);
        int work = 128;
        float progress = cursor.ProgressDistance;
        Assert.IsFalse(cursor.Advance(new Vector2(3f, 3f), ref work));
        Assert.AreEqual(progress, cursor.ProgressDistance, 0.0001f);
        Assert.AreEqual(0f, cursor.CurrentAnchor.distanceAlongEdge, 0.0001f);
    }

    /// <summary>Confirms a budget-limited projection commits no progress, then selects the closer later candidate when retried.</summary>
    [Test]
    public void Cursor_BudgetExhaustionCommitsNoPartialProgress() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 8f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) }, out _);
        int work = 1;
        Assert.IsFalse(cursor.Advance(new Vector2(10f, 1f), ref work));
        Assert.AreEqual(0, work);
        Assert.AreEqual(0f, cursor.ProgressDistance, 0.0001f);
        int retryWork = 8;
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 1f), ref retryWork));
        Assert.AreEqual(3f, cursor.ProgressDistance, 0.0001f);
        Assert.AreEqual("e1", cursor.CurrentAnchor.edgeId);
    }

    /// <summary>Confirms a graph version change invalidates the bound cursor safely.</summary>
    [Test]
    public void Cursor_StaleGraphVersionFailsSafely() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out var graph);
        graph.Rebuild();
        int work = 128;
        Assert.IsFalse(cursor.Advance(new Vector2(2f, 0f), ref work));
        Assert.IsFalse(cursor.IsBound);
        Assert.AreEqual(0f, cursor.RemainingDistance, 0.0001f);
    }

    /// <summary>Confirms invalid and disconnected spans clear an older route instead of replacing it partially.</summary>
    [Test]
    public void Cursor_InvalidOrDisconnectedBindClearsOldRoute() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out var graph);
        var budget = new RoadPathQuery.SearchBudget(128);
        Assert.IsFalse(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 8f, 2f) }, Limits(), budget));
        Assert.IsFalse(cursor.IsBound);
        Assert.IsFalse(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e2", 0f, 10f) }, Limits(), budget));
        Assert.IsFalse(cursor.IsBound);
    }

    /// <summary>Confirms lookahead clamps to the endpoint and exposes the next corner geometry.</summary>
    [Test]
    public void Cursor_LookaheadClampsAndReturnsNextTurn() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 2f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) }, out _);
        int work = 128;
        Assert.IsTrue(cursor.TrySampleAhead(100f, ref work, out var endpoint));
        Assert.AreEqual(new Vector2(10f, 10f), endpoint);
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found,
            cursor.TryGetNextTurn(20f, ref work, out var turnPoint, out var incoming, out var outgoing, out var distanceAhead));
        Assert.AreEqual(new Vector2(10f, 0f), turnPoint);
        Assert.AreEqual(Vector2.right, incoming);
        Assert.AreEqual(Vector2.up, outgoing);
        Assert.AreEqual(8f, distanceAhead, 0.0001f);
    }

    /// <summary>Confirms reset removes old progress and a new bind starts from its own first span.</summary>
    [Test]
    public void Cursor_ResetAndNewBindDoNotRetainOldPath() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 2f, 8f) }, out var graph);
        cursor.Reset();
        Assert.IsFalse(cursor.IsBound);
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e1", 3f, 7f) }, Limits(), new RoadPathQuery.SearchBudget(128)));
        Assert.AreEqual("e1", cursor.CurrentAnchor.edgeId);
        Assert.AreEqual(3f, cursor.CurrentAnchor.distanceAlongEdge, 0.0001f);
        Assert.AreEqual(4f, cursor.RemainingDistance, 0.0001f);
    }

    /// <summary>Confirms partial spans and every corner advance by their exact flattened route distance.</summary>
    [Test]
    public void Cursor_PartialFirstThenSecondAndThirdSegmentsRemainMonotonic() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 2f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f),
            new RoadPathQuery.PathSpan("e2", 0f, 10f)
        }, out _);
        int work = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref work));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 0f), ref work));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 5f), ref work));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 10f), ref work));
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 10f), ref work));
        Assert.AreEqual(23f, cursor.ProgressDistance, 0.0001f);
        Assert.AreEqual("e2", cursor.CurrentAnchor.edgeId);
    }

    /// <summary>Confirms an interior bend uses the graph's exact arc length and accumulated route distance.</summary>
    [Test]
    public void Cursor_InteriorPolylineBendIsUpcomingTurn() {
        var document = MakeBendDocument();
        var graph = new RoadGraphRuntime(document);
        float bendLength = graph.GetArcLength("bend");
        var cursor = new PolicePathCursor();
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("bend", 0f, bendLength) }, Limits(), new RoadPathQuery.SearchBudget(128)));
        int work = 128;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found,
            cursor.TryGetNextTurn(bendLength, ref work, out var point, out _, out _, out var distance));
        Assert.AreEqual(new Vector2(5f, 5f), point);
        Assert.AreEqual(7.0710678f, distance, 0.0001f);
    }

    /// <summary>Confirms a committed rounded-corner handoff uses the profile radius and remains monotonic.</summary>
    [Test]
    public void Cursor_CommittedCornerHandoffAcceptsBoundedRoundedPosition() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int approachWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref approachWork));
        Assert.IsTrue(cursor.Advance(new Vector2(9f, 0f), ref approachWork));
        int handoffWork = 8;
        Assert.IsTrue(cursor.TryAdvanceCommittedCorner(new Vector2(12.2f, 0.5f), 0, 10f, Vector2.up,
            2.5f, 16f, ref handoffWork));
        Assert.AreEqual(10.5f, cursor.ProgressDistance, 0.0001f);
        Assert.AreEqual("e1", cursor.CurrentAnchor.edgeId);
        Assert.AreEqual(7, handoffWork);
    }

    /// <summary>Confirms the committed corner handoff rejects a position outside its radius envelope.</summary>
    [Test]
    public void Cursor_CommittedCornerHandoffRejectsUnboundedPosition() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int approachWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref approachWork));
        Assert.IsTrue(cursor.Advance(new Vector2(9f, 0f), ref approachWork));
        float progress = cursor.ProgressDistance;
        int handoffWork = 8;
        Assert.IsFalse(cursor.TryAdvanceCommittedCorner(new Vector2(12.2f, 2f), 0, 10f, Vector2.up,
            2.5f, 16f, ref handoffWork));
        Assert.AreEqual(progress, cursor.ProgressDistance, 0.0001f);
        Assert.AreEqual(8, handoffWork);
    }

    /// <summary>Confirms a same-edge split is accepted while an exact gap is rejected.</summary>
    [Test]
    public void Cursor_SameEdgeSplitAllowedAndGapRejected() {
        var graph = new RoadGraphRuntime(MakeDocument());
        var cursor = new PolicePathCursor();
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 0f, 5f), new RoadPathQuery.PathSpan("e0", 5f, 10f) }, Limits(), new RoadPathQuery.SearchBudget(128)));
        Assert.IsFalse(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 0f, 5f), new RoadPathQuery.PathSpan("e0", 6f, 10f) }, Limits(), new RoadPathQuery.SearchBudget(128)));
    }

    /// <summary>Confirms coincident but differently identified nodes do not satisfy a cross-edge transition.</summary>
    [Test]
    public void Cursor_CoincidentDisconnectedNodesAreRejected() {
        var document = MakeDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "b-copy", x = 10f, y = 0f });
        document.edges.Find(edge => edge.edgeId == "e1").fromNodeId = "b-copy";
        var graph = new RoadGraphRuntime(document);
        Assert.AreEqual(10f, graph.GetArcLength("e0"), 0.0001f);
        Assert.AreEqual(10f, graph.GetArcLength("e1"), 0.0001f);
        var cursor = new PolicePathCursor();
        Assert.IsFalse(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) }, Limits(), new RoadPathQuery.SearchBudget(128)));
    }

    /// <summary>Confirms sample and turn operations return zero outputs when their shared budget is exhausted.</summary>
    [Test]
    public void Cursor_SampleAndTurnBudgetFailureClearsOutputs() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) }, out _);
        int sampleWork = 0;
        Assert.IsFalse(cursor.TrySampleAhead(2f, ref sampleWork, out var point));
        Assert.AreEqual(Vector2.zero, point);
        int turnWork = 0;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.BudgetExceeded,
            cursor.TryGetNextTurn(20f, ref turnWork, out var turn, out var incoming, out var outgoing, out var distance));
        Assert.AreEqual(Vector2.zero, turn);
        Assert.AreEqual(Vector2.zero, incoming);
        Assert.AreEqual(Vector2.zero, outgoing);
        Assert.AreEqual(0f, distance);
    }

    /// <summary>Confirms a tiny positive route is not pre-completed by tolerance and completes only at its endpoint.</summary>
    [Test]
    public void Cursor_TinyPositiveLengthCompletesOnlyAtExactEnd() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 5f, 5.001f) }, out _);
        Assert.IsFalse(cursor.IsComplete);
        int work = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(5.001f, 0f), ref work));
        Assert.IsTrue(cursor.IsComplete);
        Assert.AreEqual(0f, cursor.RemainingDistance, 0f);
    }

    /// <summary>Confirms a positive route followed by a zero-length final span reaches its terminal anchor only at the endpoint.</summary>
    [Test]
    public void Cursor_FinalZeroSpanUsesTerminalAnchorWhenComplete() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 0f) }, out _);
        int firstWork = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref firstWork));
        Assert.IsFalse(cursor.IsComplete);
        int finalWork = 128;
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 0f), ref finalWork));
        Assert.IsTrue(cursor.IsComplete);
        Assert.AreEqual("e1", cursor.CurrentAnchor.edgeId);
        Assert.AreEqual(0f, cursor.CurrentAnchor.distanceAlongEdge, 0f);
    }

    /// <summary>Confirms a whole repeated square route reaches the final repeated edge occurrence exactly.</summary>
    [Test]
    public void Cursor_WholeRepeatedLoopAdvancesEveryFiveUnits() {
        var cursor = Bind(LoopSpans(), out _);
        var points = new[] {
            new Vector2(5f, 0f), new Vector2(10f, 0f), new Vector2(10f, 5f), new Vector2(10f, 10f),
            new Vector2(5f, 10f), new Vector2(0f, 10f), new Vector2(0f, 5f), new Vector2(0f, 0f),
            new Vector2(5f, 0f), new Vector2(10f, 0f)
        };
        foreach (var point in points) {
            int work = 64;
            Assert.IsTrue(cursor.Advance(point, ref work));
        }
        Assert.AreEqual(50f, cursor.ProgressDistance, 0.0001f);
        Assert.IsTrue(cursor.IsComplete);
        Assert.AreEqual("e0", cursor.CurrentAnchor.edgeId);
        Assert.AreEqual(10f, cursor.CurrentAnchor.distanceAlongEdge, 0.0001f);
    }

    /// <summary>Confirms a crossing measurement chooses the first of two equally exact candidates.</summary>
    [Test]
    public void Cursor_RealCrossingTieChoosesLeastForwardCandidate() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "start", x = -1f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "end", x = 0f, y = -1f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "crossing", fromNodeId = "start", toNodeId = "end", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var graph = new RoadGraphRuntime(document);
        Assert.AreEqual(6f, graph.GetArcLength("crossing"), 0.0001f);
        var cursor = new PolicePathCursor();
        var limits = new PolicePathCursor.TrackingLimits(6f, 0.1f, 6f, 0.05f);
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("crossing", 0f, 6f) }, limits, new RoadPathQuery.SearchBudget(128)));
        int work = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(0f, 0f), ref work));
        Assert.AreEqual(1f, cursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms a stale graph produces the explicit stale turn status and default outputs.</summary>
    [Test]
    public void Cursor_StaleGraphTurnQueryReturnsStaleGraph() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) }, out var graph);
        graph.Rebuild();
        int work = 128;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.StaleGraph,
            cursor.TryGetNextTurn(20f, ref work, out var point, out var incoming, out var outgoing, out var distance));
        Assert.AreEqual(Vector2.zero, point);
        Assert.AreEqual(Vector2.zero, incoming);
        Assert.AreEqual(Vector2.zero, outgoing);
        Assert.AreEqual(0f, distance);
    }

    /// <summary>Confirms binding requires exactly enough work for its known spans and geometry visits.</summary>
    [Test]
    public void Cursor_BindBudgetRequiresMeasuredWork() {
        var graph = new RoadGraphRuntime(MakeDocument());
        var spans = new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f) };
        var successfulBudget = new RoadPathQuery.SearchBudget(128);
        var successfulCursor = new PolicePathCursor();
        Assert.IsTrue(successfulCursor.TryBind(graph, graph.Version, spans, Limits(), successfulBudget));
        int consumedWork = successfulBudget.ConsumedWork;
        Assert.Greater(consumedWork, 0);
        var insufficientCursor = new PolicePathCursor();
        Assert.IsFalse(insufficientCursor.TryBind(graph, graph.Version, spans, Limits(), new RoadPathQuery.SearchBudget(consumedWork - 1)));
        Assert.IsFalse(insufficientCursor.IsBound);
    }

    /// <summary>Confirms measurements on the full edge outside a clipped span are rejected.</summary>
    [Test]
    public void Cursor_ClippedEndpointsRejectFullEdgeMeasurements() {
        var limits = new PolicePathCursor.TrackingLimits(6f, 0.1f, 1.5f, 0.05f);
        var startCursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 2f, 8f) }, out _ , limits);
        int startWork = 128;
        Assert.IsFalse(startCursor.Advance(new Vector2(1f, 0f), ref startWork));
        Assert.AreEqual(0f, startCursor.ProgressDistance, 0.0001f);
        var endCursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 2f, 8f) }, out _ , limits);
        int endWork = 128;
        Assert.IsFalse(endCursor.Advance(new Vector2(9f, 0f), ref endWork));
        Assert.AreEqual(0f, endCursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms NaN inputs and invalid limits fail without mutating a valid cursor.</summary>
    [Test]
    public void Cursor_InvalidNaNInputAndLimitsAreNonMutating() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out var graph);
        int advanceWork = 12;
        Assert.IsFalse(cursor.Advance(new Vector2(float.NaN, 0f), ref advanceWork));
        Assert.AreEqual(12, advanceWork);
        Assert.AreEqual(0f, cursor.ProgressDistance, 0.0001f);
        int sampleWork = 12;
        Assert.IsFalse(cursor.TrySampleAhead(float.NaN, ref sampleWork, out var point));
        Assert.AreEqual(Vector2.zero, point);
        Assert.AreEqual(12, sampleWork);
        int turnWork = 12;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.InvalidInput,
            cursor.TryGetNextTurn(float.NaN, ref turnWork, out _, out _, out _, out _));
        Assert.AreEqual(12, turnWork);
        var invalidCursor = new PolicePathCursor();
        Assert.IsFalse(invalidCursor.TryBind(graph, graph.Version, new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) },
            new PolicePathCursor.TrackingLimits(float.NaN, 0.1f, 1.5f, 0.05f), new RoadPathQuery.SearchBudget(128)));
        Assert.IsFalse(invalidCursor.IsBound);
    }

    /// <summary>Confirms an authored self-loop may repeat as a full-end to full-start occurrence.</summary>
    [Test]
    public void Cursor_AuthoredSelfLoopMayRepeat() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "loop", x = 0f, y = 0f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "loopEdge", fromNodeId = "loop", toNodeId = "loop", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> { new Vector2(1f, 0f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        var graph = new RoadGraphRuntime(document);
        var cursor = new PolicePathCursor();
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] {
            new RoadPathQuery.PathSpan("loopEdge", 0f, 2f), new RoadPathQuery.PathSpan("loopEdge", 0f, 2f)
        }, Limits(), new RoadPathQuery.SearchBudget(128)));
    }

    /// <summary>Confirms binding owns its span values instead of observing caller mutations.</summary>
    [Test]
    public void Cursor_BindingOwnsCallerSpanValues() {
        var spans = new List<RoadPathQuery.PathSpan> {
            new RoadPathQuery.PathSpan("e0", 0f, 10f)
        };
        var cursor = Bind(spans, out _);
        spans[0] = new RoadPathQuery.PathSpan("e0", 2f, 8f);
        int work = 8;
        Assert.IsTrue(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f)
        }, ref work));
    }

    /// <summary>Confirms a measured partial anchor matches the complete remaining clipped suffix.</summary>
    [Test]
    public void Cursor_MeasuredPartialSuffixMatchesExactly() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        int work = 8;
        Assert.IsTrue(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 5f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, ref work));
    }

    /// <summary>Confirms endpoint, start, order and directed edge changes fail closed.</summary>
    [Test]
    public void Cursor_ChangedRemainingSuffixDetailsAreRejected() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        int endpointWork = 8;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 5f, 9f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, ref endpointWork));
        int startWork = 8;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 4f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, ref startWork));
        int orderWork = 8;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e1", 0f, 10f), new RoadPathQuery.PathSpan("e0", 5f, 10f)
        }, ref orderWork));
    }

    /// <summary>Confirms a repeated route occurrence cannot be replaced by an earlier coincident occurrence.</summary>
    [Test]
    public void Cursor_RepeatedGeometryUsesTheBoundOccurrenceSuffix() {
        var cursor = Bind(LoopSpans(), out _);
        foreach (var point in new[] {
            new Vector2(5f, 0f), new Vector2(10f, 0f), new Vector2(10f, 5f), new Vector2(10f, 10f),
            new Vector2(5f, 10f), new Vector2(0f, 10f), new Vector2(0f, 5f), new Vector2(0f, 0f), new Vector2(5f, 0f)
        }) {
            int work = 64;
            Assert.IsTrue(cursor.Advance(point, ref work));
        }
        Assert.AreEqual(45f, cursor.ProgressDistance);
        Assert.AreEqual("e0", cursor.CurrentAnchor.edgeId);
        int correctWork = 4;
        Assert.IsTrue(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 5f, 10f)
        }, ref correctWork));
        int workRemaining = 64;
        Assert.IsFalse(cursor.MatchesRemainingSpans(LoopSpans(), ref workRemaining));
    }

    /// <summary>Confirms completion matches only its canonical final zero-length remainder.</summary>
    [Test]
    public void Cursor_CompletedRouteMatchesCanonicalZeroRemainder() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _);
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 0f), ref advanceWork));
        int work = 4;
        Assert.IsTrue(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 10f, 10f)
        }, ref work));
        int changedWork = 4;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 10f, 9f)
        }, ref changedWork));
    }

    /// <summary>Confirms stale graph invalidation is fail-closed for suffix continuation.</summary>
    [Test]
    public void Cursor_StaleGraphSuffixContinuationFailsSafely() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out var graph);
        graph.Rebuild();
        int work = 8;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f)
        }, ref work));
        Assert.IsFalse(cursor.IsBound);
        Assert.AreEqual(0f, cursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms an almost sufficient suffix budget fails without changing measured progress.</summary>
    [Test]
    public void Cursor_SuffixBudgetExhaustionIsTransactional() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        float progress = cursor.ProgressDistance;
        int work = 3;
        Assert.IsFalse(cursor.MatchesRemainingSpans(new[] {
            new RoadPathQuery.PathSpan("e0", 5f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, ref work));
        Assert.AreEqual(progress, cursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms turn tokens distinguish repeated occurrences and survive tracking within a binding.</summary>
    [Test]
    public void Cursor_TurnTokensAreOccurrenceStableAcrossTracking() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f),
            new RoadPathQuery.PathSpan("e2", 0f, 10f), new RoadPathQuery.PathSpan("e3", 0f, 10f),
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int firstWork = 64;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found,
            cursor.TryGetNextTurn(100f, ref firstWork, out var firstPoint, out var firstIncoming, out var firstOutgoing, out _, out int firstToken));
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        int trackedWork = 64;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found,
            cursor.TryGetNextTurn(100f, ref trackedWork, out _, out _, out _, out _, out int trackedToken));
        Assert.AreEqual(firstToken, trackedToken);
        foreach (var point in new[] {
            new Vector2(10f, 0f), new Vector2(10f, 5f), new Vector2(10f, 10f), new Vector2(5f, 10f),
            new Vector2(0f, 10f), new Vector2(0f, 5f), new Vector2(0f, 0f), new Vector2(5f, 0f)
        }) {
            int work = 64;
            Assert.IsTrue(cursor.Advance(point, ref work));
        }
        Assert.AreEqual(45f, cursor.ProgressDistance);
        int repeatedWork = 64;
        Assert.AreEqual(PolicePathCursor.TurnQueryStatus.Found,
            cursor.TryGetNextTurn(100f, ref repeatedWork, out var secondPoint, out var secondIncoming, out var secondOutgoing, out _, out int secondToken));
        Assert.AreEqual(firstPoint, secondPoint);
        Assert.AreEqual(firstIncoming, secondIncoming);
        Assert.AreEqual(firstOutgoing, secondOutgoing);
        Assert.AreNotEqual(firstToken, secondToken);
    }

    /// <summary>Confirms interior geometry and edge seams expose actual cached endpoint tangents.</summary>
    [Test]
    public void Cursor_EndpointTangentsUseActualPositiveGeometry() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        Assert.IsTrue(cursor.TryGetEndpointTangents(out var first, out var last));
        Assert.AreEqual(Vector2.right, first);
        Assert.AreEqual(Vector2.up, last);
    }

    /// <summary>Confirms a zero-length route does not invent endpoint tangent directions.</summary>
    [Test]
    public void Cursor_ZeroLengthRouteHasNoEndpointTangents() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 5f, 5f) }, out _);
        Assert.IsFalse(cursor.TryGetEndpointTangents(out var first, out var last));
        Assert.AreEqual(Vector2.zero, first);
        Assert.AreEqual(Vector2.zero, last);
    }

    /// <summary>Confirms an interior polyline bend returns its actual vertex and outgoing exit point.</summary>
    [Test]
    public void Cursor_CorridorCertifiesInteriorSingleBend() {
        var graph = new RoadGraphRuntime(MakeBendDocument());
        var cursor = new PolicePathCursor();
        float length = graph.GetArcLength("bend");
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] {
            new RoadPathQuery.PathSpan("bend", 0f, length)
        }, Limits(), new RoadPathQuery.SearchBudget(128)));
        int work = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.SingleBend,
            cursor.TryGetSingleBendCorridor(length, 1f, -1, ref work, out var corridor));
        Assert.AreEqual(0, corridor.token);
        Assert.AreEqual(new Vector2(5f, 5f), corridor.vertex);
        Assert.AreEqual(new Vector2(5.7071066f, 4.2928934f), corridor.exitPoint);
    }

    /// <summary>Confirms an edge seam is certified as a bend with the correct directed seam directions.</summary>
    [Test]
    public void Cursor_CorridorCertifiesSeamSingleBend() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 5f),
            new RoadPathQuery.PathSpan("e1", 5f, 10f)
        }, out _);
        int work = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.SingleBend,
            cursor.TryGetSingleBendCorridor(12f, 1f, -1, ref work, out var corridor));
        Assert.AreEqual(new Vector2(10f, 0f), corridor.vertex);
        Assert.AreEqual(Vector2.right, corridor.incomingDirection);
        Assert.AreEqual(Vector2.up, corridor.outgoingDirection);
        Assert.AreEqual(new Vector2(10f, 2f), corridor.lookaheadPoint);
    }

    /// <summary>Confirms a latched turn remains certifiable after its vertex during outgoing scanning.</summary>
    [Test]
    public void Cursor_CorridorLatchSupportsPostVertexOutgoingScan() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 5f),
            new RoadPathQuery.PathSpan("e1", 5f, 10f)
        }, out _);
        int initialWork = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.SingleBend,
            cursor.TryGetSingleBendCorridor(12f, 2f, -1, ref initialWork, out var initial));
        int advanceWork = 64;
        Assert.IsTrue(cursor.Advance(new Vector2(5f, 0f), ref advanceWork));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 0f), ref advanceWork));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 5f), ref advanceWork));
        Assert.IsTrue(cursor.Advance(new Vector2(10f, 7f), ref advanceWork));
        Assert.AreEqual(17f, cursor.ProgressDistance);
        int latchWork = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.SingleBend,
            cursor.TryGetSingleBendCorridor(2f, 2f, initial.token, ref latchWork, out var latched));
        Assert.AreEqual(initial.token, latched.token);
        Assert.AreEqual(new Vector2(10f, 2f), latched.exitPoint);
    }

    /// <summary>Confirms a second bend hidden beyond the first endpoint is rejected.</summary>
    [Test]
    public void Cursor_CorridorRejectsHiddenSecondBend() {
        var cursor = Bind(LoopSpans(), out _);
        int work = 128;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.UnsupportedMultipleBends,
            cursor.TryGetSingleBendCorridor(25f, 1f, -1, ref work, out var corridor));
        Assert.AreEqual(default(PolicePathCursor.SingleBendCorridor), corridor);
    }

    /// <summary>Confirms a tiny non-collinear second bend is not treated as a collinear seam.</summary>
    [Test]
    public void Cursor_CorridorRejectsTinyHiddenSecondBend() {
        var graph = new RoadGraphRuntime(MakeTinySecondBendDocument());
        var cursor = new PolicePathCursor();
        float length = graph.GetArcLength("tiny");
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, new[] {
            new RoadPathQuery.PathSpan("tiny", 0f, length)
        }, Limits(), new RoadPathQuery.SearchBudget(128)));
        int work = 128;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.UnsupportedMultipleBends,
            cursor.TryGetSingleBendCorridor(length, 1f, -1, ref work, out _));
    }

    /// <summary>Confirms the selected bend requires enough remaining route for its full exit corridor.</summary>
    [Test]
    public void Cursor_CorridorRejectsInsufficientOutgoingLength() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _);
        int work = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.Straight,
            cursor.TryGetSingleBendCorridor(10f, 2f, -1, ref work, out _));
        var graph = new RoadGraphRuntime(MakeBendDocument());
        var bendCursor = new PolicePathCursor();
        float bendLength = graph.GetArcLength("bend");
        Assert.IsTrue(bendCursor.TryBind(graph, graph.Version, new[] {
            new RoadPathQuery.PathSpan("bend", 0f, bendLength)
        }, Limits(), new RoadPathQuery.SearchBudget(128)));
        int bendWork = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.InsufficientLength,
            bendCursor.TryGetSingleBendCorridor(bendLength, bendLength, -1, ref bendWork, out _));
    }

    /// <summary>Confirms stale and out-of-binding latch tokens fail without silently switching turns.</summary>
    [Test]
    public void Cursor_CorridorRejectsStaleAndInvalidLatchTokens() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out var graph);
        int invalidWork = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.InvalidInput,
            cursor.TryGetSingleBendCorridor(12f, 1f, 99, ref invalidWork, out _));
        graph.Rebuild();
        int staleWork = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.StaleGraph,
            cursor.TryGetSingleBendCorridor(12f, 1f, 0, ref staleWork, out _));
    }

    /// <summary>Confirms a true no-turn route returns a sampled straight corridor.</summary>
    [Test]
    public void Cursor_CorridorReportsTrueStraightRoute() {
        var cursor = Bind(new[] { new RoadPathQuery.PathSpan("e0", 0f, 10f) }, out _);
        int work = 64;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.Straight,
            cursor.TryGetSingleBendCorridor(4f, 2f, -1, ref work, out var corridor));
        Assert.AreEqual(-1, corridor.token);
        Assert.AreEqual(new Vector2(4f, 0f), corridor.exitPoint);
    }

    /// <summary>Confirms exhausted corridor work returns default output and preserves progress.</summary>
    [Test]
    public void Cursor_CorridorBudgetExhaustionIsTransactional() {
        var cursor = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        float progress = cursor.ProgressDistance;
        int work = 0;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.BudgetExceeded,
            cursor.TryGetSingleBendCorridor(12f, 1f, -1, ref work, out var corridor));
        Assert.AreEqual(default(PolicePathCursor.SingleBendCorridor), corridor);
        Assert.AreEqual(progress, cursor.ProgressDistance, 0.0001f);
    }

    /// <summary>Confirms one unit below a successful bounded corridor budget fails transactionally.</summary>
    [Test]
    public void Cursor_CorridorOneLessThanRequiredWorkFailsClosed() {
        var successful = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int sufficientWork = 128;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.SingleBend,
            successful.TryGetSingleBendCorridor(12f, 1f, -1, ref sufficientWork, out _));
        int requiredWork = 128 - sufficientWork;
        Assert.Greater(requiredWork, 1);

        var insufficient = Bind(new[] {
            new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f)
        }, out _);
        int oneLessWork = requiredWork - 1;
        float progress = insufficient.ProgressDistance;
        Assert.AreEqual(PolicePathCursor.CorridorQueryStatus.BudgetExceeded,
            insufficient.TryGetSingleBendCorridor(12f, 1f, -1, ref oneLessWork, out var corridor));
        Assert.AreEqual(default(PolicePathCursor.SingleBendCorridor), corridor);
        Assert.AreEqual(progress, insufficient.ProgressDistance, 0.0001f);
    }

    static PolicePathCursor Bind(IReadOnlyList<RoadPathQuery.PathSpan> spans, out RoadGraphRuntime graph,
        PolicePathCursor.TrackingLimits limits = null) {
        graph = new RoadGraphRuntime(MakeDocument());
        var cursor = new PolicePathCursor();
        Assert.IsTrue(cursor.TryBind(graph, graph.Version, spans, limits ?? Limits(), new RoadPathQuery.SearchBudget(128)));
        return cursor;
    }

    static PolicePathCursor.TrackingLimits Limits() => new PolicePathCursor.TrackingLimits(6f, 2f, 1.5f, 0.05f);

    static IReadOnlyList<RoadPathQuery.PathSpan> LoopSpans() => new[] {
        new RoadPathQuery.PathSpan("e0", 0f, 10f), new RoadPathQuery.PathSpan("e1", 0f, 10f),
        new RoadPathQuery.PathSpan("e2", 0f, 10f), new RoadPathQuery.PathSpan("e3", 0f, 10f),
        new RoadPathQuery.PathSpan("e0", 0f, 10f)
    };

    static MapNavigationDocument MakeDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "c", x = 10f, y = 10f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "d", x = 0f, y = 10f });
        document.edges.Add(Edge("e0", "a", "b"));
        document.edges.Add(Edge("e1", "b", "c"));
        document.edges.Add(Edge("e2", "c", "d"));
        document.edges.Add(Edge("e3", "d", "a"));
        return document;
    }

    static MapNavigationDocument MakeBendDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 10f, y = 0f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "bend", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> { new Vector2(5f, 5f) }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        return document;
    }

    static MapNavigationDocument MakeTinySecondBendDocument() {
        var document = new MapNavigationDocument();
        document.nodes.Add(new RoadNodeRecord { nodeId = "a", x = 0f, y = 0f });
        document.nodes.Add(new RoadNodeRecord { nodeId = "b", x = 15f, y = 5f });
        document.edges.Add(new RoadEdgeRecord { edgeId = "tiny", fromNodeId = "a", toNodeId = "b", usableWidth = 4f, speedLimit = 8f,
            orderedPoints = new List<Vector2> {
                new Vector2(5f, 5f), new Vector2(10f, 5.001f)
            }, allowedRoles = new List<VehicleRole> { VehicleRole.Police } });
        return document;
    }

    static RoadEdgeRecord Edge(string id, string from, string to) => new RoadEdgeRecord {
        edgeId = id, fromNodeId = from, toNodeId = to, usableWidth = 4f, speedLimit = 8f,
        orderedPoints = new List<Vector2>(), allowedRoles = new List<VehicleRole> { VehicleRole.Police }
    };
}
