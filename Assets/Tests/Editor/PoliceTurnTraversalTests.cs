using NUnit.Framework;
using UnityEngine;

/// <summary>Pure measured-clock and pose coverage for the corner latch, independent of cursor and physics fixtures.</summary>
public sealed class PoliceTurnTraversalTests {
    static readonly PoliceTurnTraversal.Limits limits = new PoliceTurnTraversal.Limits(1.5f, 8f, 16f, 8f);

    /// <summary>A certified turn retains its token, arc and cap until both measured exit conditions are true.</summary>
    [Test]
    public void BeginAndMeasuredExit_RetainsCapThenReleases() {
        var turn = NewTurn();
        Assert.IsTrue(turn.TryBegin(7, 12f, Vector2.right, 2f, limits, 3f, Vector2.zero));
        Assert.AreEqual(7, turn.Token); Assert.AreEqual(12f, turn.AbsoluteArc); Assert.AreEqual(2f, turn.Cap);
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Active, turn.Tick(4f, Vector2.right, 1.5f, Vector2.up, false));
        Assert.AreEqual(PoliceTurnTraversal.State.Active, turn.CurrentState); Assert.AreEqual(2f, turn.Cap);
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Released, turn.Tick(5f, Vector2.right * 2f, 1.5f, Vector2.right, false));
        Assert.AreEqual(PoliceTurnTraversal.State.Inactive, turn.CurrentState); Assert.AreEqual(-1, turn.Token);
        Assert.AreEqual(0f, turn.Cap); Assert.AreEqual(Vector2.zero, turn.OutgoingDirection);
    }

    /// <summary>Stationary measurements and paused observations cannot manufacture outgoing travel or release a turn.</summary>
    [Test]
    public void PauseAndStationarity_CannotReleaseOrRestartBounds() {
        var turn = NewTurn(); Assert.IsTrue(turn.TryBegin(1, 2f, Vector2.right, 2f, limits, 0f, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Active, turn.Tick(1f, Vector2.zero, 2f, Vector2.right, false));
        Assert.AreEqual(0f, turn.MeasuredTravel);
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Active, turn.Tick(2f, Vector2.up * 9f, 9f, Vector2.right, true));
        Assert.AreEqual(PoliceTurnTraversal.State.Active, turn.CurrentState); Assert.AreEqual(0f, turn.MeasuredTravel);
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Active, turn.Tick(3f, Vector2.up * 9f, 0f, Vector2.right, false));
        Assert.AreEqual(9f, turn.MeasuredTravel);
    }

    /// <summary>Travel and active duration each expire once and cannot be restarted without reset.</summary>
    [TestCase(17f, 1f)]
    [TestCase(1f, 9f)]
    public void Bounds_ExpireAndRemainLatched(float travel, float clock) {
        var turn = NewTurn(); Assert.IsTrue(turn.TryBegin(2, 3f, Vector2.right, 2f, limits, 0f, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Expired, turn.Tick(clock, Vector2.right * travel, 0f, Vector2.up, false));
        Assert.AreEqual(PoliceTurnTraversal.State.Expired, turn.CurrentState);
        Assert.IsFalse(turn.TryBegin(3, 4f, Vector2.up, 1f, limits, clock, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.Expired, turn.Tick(clock + 1f, Vector2.zero, 0f, Vector2.up, false));
    }

    /// <summary>Clock regression, non-finite pose and invalid begin limits fail closed with stable observable state.</summary>
    [Test]
    public void InvalidInputs_FailClosed() {
        var turn = NewTurn();
        Assert.IsFalse(new PoliceTurnTraversal.Limits(0f, 8f, 16f, 8f).IsValid());
        Assert.IsFalse(new PoliceTurnTraversal.Limits(2f, 8f, 1f, 8f).IsValid());
        Assert.IsFalse(new PoliceTurnTraversal.Limits(1f, 8f, 16f, float.PositiveInfinity).IsValid());
        Assert.IsFalse(turn.TryBegin(-1, 0f, Vector2.right, 1f, limits, 0f, Vector2.zero));
        Assert.IsTrue(turn.TryBegin(4, 0f, Vector2.right, 1f, limits, 2f, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.InvalidInput, turn.Tick(1f, Vector2.zero, 0f, Vector2.right, false));
        Assert.AreEqual(PoliceTurnTraversal.State.Expired, turn.CurrentState);
        turn.Reset(); Assert.IsTrue(turn.TryBegin(5, 0f, Vector2.right, 1f, limits, 2f, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.InvalidInput, turn.Tick(3f, Vector2.right, 2f, Vector2.right * 0.000001f, false));
        turn.Reset(); Assert.IsTrue(turn.TryBegin(6, 0f, Vector2.right, 1f, limits, 2f, Vector2.zero));
        Assert.AreEqual(PoliceTurnTraversal.UpdateResult.InvalidInput, turn.Tick(3f, new Vector2(float.NaN, 0f), 0f, Vector2.right, false));
    }

    /// <summary>An explicit reset is the sole transition that permits a new token after expiry.</summary>
    [Test]
    public void Reset_AllowsNewTurnAfterExpiry() {
        var turn = NewTurn(); Assert.IsTrue(turn.TryBegin(5, 0f, Vector2.right, 1f, limits, 0f, Vector2.zero));
        turn.Tick(9f, Vector2.zero, 0f, Vector2.right, false);
        turn.Reset();
        Assert.IsTrue(turn.TryBegin(6, 1f, Vector2.up, 1f, limits, 10f, Vector2.one));
    }

    static PoliceTurnTraversal NewTurn() => new PoliceTurnTraversal();
}
