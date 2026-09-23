using NUnit.Framework;
using UnityEngine;

/// <summary>Pure gates for bounded curved final approaches and their measured lifecycle.</summary>
public sealed class PoliceCurvedFinalApproachTests {
    /// <summary>Repeated native-coordinate entry observations cannot invent progress or expire from trigonometric roundoff.</summary>
    [Test]
    public void Traversal_RepeatedNativeEntryPose_EarnsNoProgress() {
        Vector2 start = new Vector2(-46f, -8.01f);
        Vector2 forward = (Vector2)(Quaternion.Euler(0f, 0f, -180f) * Vector3.up);
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(start, forward, new Vector2(-58f, -10.5f), 2.5f, 16f, out var plan));
        var traversal = new PoliceFinalApproachTraversal();
        Assert.IsTrue(traversal.TryBegin(plan, 3.2f, start, 16f, 8f));
        for (int i = 0; i < 3; i++) Assert.AreEqual(PoliceFinalApproachTraversal.UpdateResult.Active,
            traversal.Tick(3.2f + i * 0.02f, start, forward, 1.5f, 8f, false, 2f, 0.05f, 0.1f));
        Assert.AreEqual(0f, traversal.Progress);
        Assert.AreEqual(0f, traversal.MeasuredTravel);
        Assert.AreEqual(11.2f, traversal.Deadline);
    }

    /// <summary>Aligned targets retain the existing straight-only final approach behavior.</summary>
    [Test]
    public void AlignedTarget_RetainsStraightRegression() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.left, new Vector2(-6f, 0f), 1f, 20f, out var plan));
        Assert.AreEqual(PoliceFinalApproachGeometry.Kind.Straight, plan.kind);
        Assert.AreEqual(0f, plan.arcLength);
    }

    /// <summary>Perpendicular westbound target receives a forward-only tangent arc instead of stopping at the road projection.</summary>
    [Test]
    public void PerpendicularTarget_UsesForwardArcThenStraight() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(new Vector2(-46f, -9.3f), Vector2.down,
            new Vector2(-58f, -10.5f), 1.2f, 20f, out var plan));
        Assert.AreEqual(PoliceFinalApproachGeometry.Kind.ArcThenStraight, plan.kind);
        Assert.GreaterOrEqual(plan.radius, 1.2f);
        Assert.That(Vector2.Distance(plan.Sample(plan.TotalLength), plan.target), Is.LessThan(0.001f));
    }

    /// <summary>Mirroring the target mirrors the selected turn while preserving forward travel.</summary>
    [Test]
    public void MirroredTarget_MirrorsTurnSign() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.up, new Vector2(-5f, 2f), 1f, 20f, out var left));
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.up, new Vector2(5f, 2f), 1f, 20f, out var right));
        Assert.AreEqual(-left.turnSign, right.turnSign);
        Assert.That(Vector2.Distance(left.Sample(left.TotalLength), left.target), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(right.Sample(right.TotalLength), right.target), Is.LessThan(0.001f));
    }

    /// <summary>Targets outside the bounded search distance are refused without inventing a graph edge.</summary>
    [Test]
    public void OutOfBoundTarget_IsRefused() {
        Assert.IsFalse(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.down, new Vector2(-58f, -10.5f), 1.2f, 20f, out _));
    }

    /// <summary>Measured traversal cannot restart its original travel or duration budget after a refresh.</summary>
    [Test]
    public void Traversal_UsesMeasuredProgressAndImmutableBudget() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.down, new Vector2(-5f, -2f), 1f, 20f, out var plan));
        var traversal = new PoliceFinalApproachTraversal();
        Assert.IsTrue(traversal.TryBegin(plan, 1f, plan.start, plan.TotalLength + 1f, 10f));
        Assert.AreEqual(PoliceFinalApproachTraversal.UpdateResult.Active,
            traversal.Tick(2f, plan.Sample(0.1f), PoliceFinalApproachGeometry.DirectionAt(plan, 0.1f), 1.5f, 8f, false, 0.05f, 0.01f, 0.2f));
        float before = traversal.MeasuredTravel;
        float deadline = traversal.Deadline;
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(plan.Sample(0.1f), PoliceFinalApproachGeometry.DirectionAt(plan, 0.1f),
            plan.target + Vector2.left * 0.1f, 1f, traversal.TravelRemaining, out var future));
        Assert.IsTrue(traversal.TryReplan(future));
        Assert.AreEqual(deadline, traversal.Deadline);
        Assert.AreEqual(before, traversal.MeasuredTravel);
        Assert.AreEqual(PoliceFinalApproachTraversal.UpdateResult.Active,
            traversal.Tick(3f, future.Sample(0.1f), PoliceFinalApproachGeometry.DirectionAt(future, 0.1f), 1.5f, 8f, false, 0.05f, 0.01f, 0.2f));
        Assert.GreaterOrEqual(traversal.MeasuredTravel, before);
    }

    /// <summary>Lifecycle reset clears the committed geometry and refuses stale continuation.</summary>
    [Test]
    public void Traversal_ResetClearsLifecycle() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.down, new Vector2(-5f, -2f), 1f, 20f, out var plan));
        var traversal = new PoliceFinalApproachTraversal();
        Assert.IsTrue(traversal.TryBegin(plan, 0f, plan.start, plan.TotalLength + 1f, 10f));
        traversal.Reset();
        Assert.AreEqual(PoliceFinalApproachTraversal.State.Inactive, traversal.CurrentState);
        Assert.AreEqual(0f, traversal.Progress);
    }

    /// <summary>Expired transactions cannot be renewed by repeated TryBegin or future-plan replacement.</summary>
    [Test]
    public void Traversal_ExpiryCannotRenew() {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.down, new Vector2(-5f, -2f), 1f, 20f, out var plan));
        var traversal = new PoliceFinalApproachTraversal();
        Assert.IsTrue(traversal.TryBegin(plan, 0f, plan.start, 10f, 1f));
        Assert.AreEqual(PoliceFinalApproachTraversal.UpdateResult.Expired,
            traversal.Tick(1.1f, plan.start, plan.entryDirection, 1.5f, 8f, false));
        Assert.IsFalse(traversal.TryBegin(plan, 1.1f, plan.start, 10f, 1f));
        Assert.IsFalse(traversal.TryReplan(plan));
        Assert.AreEqual(1f, traversal.Deadline);
    }

    /// <summary>An off-path pose cannot earn projected distance; a large jump is not a physics-step observation.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public void Traversal_RejectsCrossTrackAndUnmeasuredJump(bool crossTrack) {
        Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(Vector2.zero, Vector2.up, new Vector2(5f, 3f), 2f, 20f, out var plan));
        var traversal = new PoliceFinalApproachTraversal();
        Assert.IsTrue(traversal.TryBegin(plan, 0f, plan.start, 10f, 10f));
        Vector2 position = crossTrack ? plan.Sample(0.1f) + Vector2.left : plan.Sample(2f);
        Vector2 tangent = PoliceFinalApproachGeometry.DirectionAt(plan, PoliceFinalApproachGeometry.ProjectProgress(plan, position));
        Assert.AreEqual(PoliceFinalApproachTraversal.UpdateResult.InvalidInput,
            traversal.Tick(0.02f, position, tangent, 1.5f, 8f, false, 0.1f, 0.01f, crossTrack ? 2f : 0.2f));
        Assert.AreEqual(0f, traversal.Progress);
        Assert.AreEqual(PoliceFinalApproachTraversal.State.Expired, traversal.CurrentState);
    }

    /// <summary>A collider placed on the rotating outer arc between sample points blocks the conservative full envelope.</summary>
    [Test]
    public void ArcEnvelope_RejectsBetweenChordBlockerInRealPhysics() {
        using (var fixture = new PursuitFixture()) {
            fixture.ConfigureStraightInitialRoute();
            Assert.IsTrue(PoliceFinalApproachGeometry.TrySolve(new Vector2(4f, 0f), Vector2.up,
                new Vector2(12f, 5f), 3f, 20f, out var plan));
            Vector2 padded = fixture.Profile.colliderSize + Vector2.one * fixture.Binding.navigationSettings.clearanceMargin;
            int work = 64;
            Assert.IsTrue(PoliceFinalApproachGeometry.IsRemainingClear(plan, 0f, padded, Vector2.zero,
                fixture.Binding.navigation.localBounds, fixture.Binding.staticClearance, ref work));
            float progress = plan.arcLength * 0.37f;
            Vector2 tangent = PoliceFinalApproachGeometry.DirectionAt(plan, progress);
            Vector2 outward = new Vector2(tangent.y, -tangent.x) * plan.turnSign;
            fixture.CreateBlocker(plan.Sample(progress) + outward * (padded.magnitude * 0.5f), false, Vector2.one * 0.02f);
            work = 64;
            Assert.IsFalse(PoliceFinalApproachGeometry.IsRemainingClear(plan, 0f, padded, Vector2.zero,
                fixture.Binding.navigation.localBounds, fixture.Binding.staticClearance, ref work));
        }
    }
}
