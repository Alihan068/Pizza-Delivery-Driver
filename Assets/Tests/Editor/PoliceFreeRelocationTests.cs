using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Pure EditMode coverage for R05 free placement gates and atomic relocation seams.</summary>
public sealed class PoliceFreeRelocationTests {
    /// <summary>Reinforcements enter inside the managed radius, never beyond its far boundary.</summary>
    [TestCase(6f, true)]
    [TestCase(10f, false)]
    [TestCase(20f, false)]
    public void DestinationMustFitInsideManagementRadius(float distance, bool accepted) {
        var context = Context();
        context.destinations = new[] { new PoliceFreePlacementPlanner.Destination { position = Vector2.up * distance } };
        Assert.AreEqual(accepted, new PoliceFreePlacementPlanner().TryPlan(context).IsFound);
    }

    static readonly PolicePrefabGeometry Geometry = new PolicePrefabGeometry(new Vector2(2f, 2f), Vector2.zero,
        new Vector2(4f, 2f), new Vector2(1f, 0f));

    [Test]
    public void ReferenceRadius_IsThreeTimesAuthoredHalfDiagonal() {
        var settings = new PolicePlacementSettings { authoredCameraHorizontalWorldSize = 16f, authoredCameraAspect = 16f / 9f };
        Assert.AreEqual(Mathf.Sqrt(8f * 8f + 4.5f * 4.5f) * 3f, settings.relocationRadius, .0001f);
    }

    [TestCase(9.9f, false)]
    [TestCase(10f, false)]
    [TestCase(10.1f, true)]
    public void SourceThreeTimesRadius_InsideEqualOutside(float remainingDistance, bool expected) {
        var context = Context();
        context.relocationRadius = 10f;
        var source = Source(7, new Vector2(0f, -(Geometry.WholeSurfaceRadius + remainingDistance)));
        Assert.AreEqual(expected, PoliceFreePlacementPlanner.IsEligibleSource(source, context));
    }

    [Test]
    public void SourceGates_RejectVisibleDwellCooldownRecoveryAndStoppedPlayer() {
        var context = Context();
        var source = Source(1, new Vector2(0f, -30f));
        context.cameraBounds = new Rect(-40f, -40f, 80f, 80f);
        Assert.IsFalse(PoliceFreePlacementPlanner.IsEligibleSource(source, context));
        context.cameraBounds = new Rect(-2f, -2f, 4f, 4f); source.offscreenDwellComplete = false;
        Assert.IsFalse(PoliceFreePlacementPlanner.IsEligibleSource(source, context));
        source.offscreenDwellComplete = true; source.relocationCooldownComplete = false;
        Assert.IsFalse(PoliceFreePlacementPlanner.IsEligibleSource(source, context));
        source.relocationCooldownComplete = true; source.hasContactOrArrestOrRecoveryOrRamming = true;
        Assert.IsFalse(PoliceFreePlacementPlanner.IsEligibleSource(source, context));
        source.hasContactOrArrestOrRecoveryOrRamming = false; context.playerVelocity = Vector2.zero;
        Assert.IsFalse(PoliceFreePlacementPlanner.IsEligibleSource(source, context));
    }

    [Test]
    public void FullRendererEnvelope_IsUsedForVisibility() {
        var candidate = new PoliceSpawnCandidate { position = new Vector2(1.5f, 0f), headingDegrees = 0f };
        Assert.IsTrue(PoliceSpawnSafetyPolicy.IsWholeFootprintVisible(candidate, new Rect(-1f, -1f, 2f, 2f), 0f, Geometry));
    }

    [Test]
    public void GraphlessPlacement_UsesFreshStaticAndDynamicQueriesAndPath() {
        var context = Context();
        context.sources = new[] { Source(4, new Vector2(0f, -30f)) };
        context.destinations = new[] { new PoliceFreePlacementPlanner.Destination { position = new Vector2(0f, 6f) } };
        context.reachability = (p, h) => new PoliceFreePathPlanner.Result(PoliceFreePathPlanner.Status.Found, 1, 4, 99, 1, 1, 1);
        var result = new PoliceFreePlacementPlanner().TryPlan(context);
        Assert.IsTrue(result.IsFound);
        Assert.AreEqual(4, result.source.lifeId);
    }

    [Test]
    public void StaticOrDynamicClearanceFailure_RejectsCandidate() {
        var context = Context();
        context.worldClearance = new ClearQuery(false);
        Assert.IsFalse(new PoliceFreePlacementPlanner().TryPlan(context).IsFound);
        context.worldClearance = new ClearQuery(true);
        context.localStaticClearance = new ClearQuery(false);
        Assert.IsFalse(new PoliceFreePlacementPlanner().TryPlan(context).IsFound);
    }

    [Test]
    public void PendingPath_DefersAndNeverAuthorizesTeleport() {
        var context = Context();
        context.reachability = (p, h) => new PoliceFreePathPlanner.Result(PoliceFreePathPlanner.Status.Pending, 1, 1, 99, 1, 0, 1);
        var result = new PoliceFreePlacementPlanner().TryPlan(context);
        Assert.AreEqual(PoliceFreePlacementPlanner.Status.Pending, result.status);
    }

    [Test]
    public void CandidateOrdering_IsLongestRemainingThenLifeId() {
        var context = Context();
        context.sources = new[] { Source(2, new Vector2(0f, -30f)), Source(1, new Vector2(0f, -30f)) };
        context.reachability = (p, h) => new PoliceFreePathPlanner.Result(PoliceFreePathPlanner.Status.Found, 1, 1, 99, 1, 1, 1);
        var result = new PoliceFreePlacementPlanner().TryPlan(context);
        Assert.AreEqual(1, result.source.lifeId);
    }

    [Test]
    public void AtomicCommit_RollsBackAndPreservesIdentityOnBindFailure() {
        var state = new IdentityState { lifeId = 42, hp = 73f, slot = 3, heat = 9f, position = Vector2.one };
        Vector2 oldPosition = state.position;
        bool committed = PoliceFreePlacementPlanner.TryCommitAtomic(() => true, () => state.position = new Vector2(9f, 9f),
            () => false, () => state.position = oldPosition);
        Assert.IsFalse(committed);
        Assert.AreEqual(oldPosition, state.position);
        Assert.AreEqual(42, state.lifeId); Assert.AreEqual(73f, state.hp); Assert.AreEqual(3, state.slot); Assert.AreEqual(9f, state.heat);
    }

    static PoliceFreePlacementPlanner.Context Context() {
        return new PoliceFreePlacementPlanner.Context {
            playerPosition = Vector2.zero, playerVelocity = Vector2.up, cameraBounds = new Rect(-2f, -2f, 4f, 4f), cameraMargin = 0f,
            relocationRadius = 10f, minimumDistance = 0f, reactionSeconds = 0f, playerSurfaceRadius = 0f, playerFootprint = Vector2.zero,
            mapOriginWorld = Vector2.zero, localMapBounds = new Rect(-100f, -100f, 200f, 200f), noSpawnRegions = new List<NoSpawnRegion>(),
            worldClearance = new ClearQuery(true), localStaticClearance = new ClearQuery(true), sources = new[] { Source(1, new Vector2(0f, -30f)) },
            destinations = new[] { new PoliceFreePlacementPlanner.Destination { position = new Vector2(0f, 6f), headingDegrees = 0f } }, maxCandidates = 8,
            reachability = (p, h) => new PoliceFreePathPlanner.Result(PoliceFreePathPlanner.Status.Found, 1, 1, 99, 1, 1, 1)
        };
    }

    static PoliceFreePlacementPlanner.Source Source(int lifeId, Vector2 position) => new PoliceFreePlacementPlanner.Source {
        lifeId = lifeId, position = position, geometry = Geometry, isBehind = true, offscreenDwellComplete = true,
        relocationCooldownComplete = true
    };

    sealed class ClearQuery : IAreaClearanceQuery {
        readonly bool clear;
        public ClearQuery(bool value) { clear = value; }
        public bool IsAreaClear(Vector2 center, Vector2 footprint, float headingDegrees) => clear;
    }

    sealed class IdentityState {
        public int lifeId;
        public float hp;
        public int slot;
        public float heat;
        public Vector2 position;
    }
}
