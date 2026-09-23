using NUnit.Framework;
using UnityEngine;

/// <summary>Pure S09 contact lifecycle tests that do not enter Unity Play Mode or mutate scenes.</summary>
public sealed class PoliceContactLifecycleTests {
    static readonly Vector2 Origin = Vector2.zero;
    const int FirstPoliceLife = 101;
    const int SecondPoliceLife = 202;

    [Test]
    public void LosingTheLastContactResetsHoldAndAnchor() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 2f, 0.1f, 0.1f, 5f, false);
        tracker.QueueExit(FirstPoliceLife);
        tracker.CommitStep(new int[0], new Vector2(4f, 0f), Vector2.zero, 0.02f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(0, tracker.ContactCount);
        Assert.AreEqual(0f, tracker.HoldSeconds);
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, new Vector2(4f, 0f), Vector2.zero, 0.02f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(0.02f, tracker.HoldSeconds, 0.0001f);
    }

    [Test]
    public void MovingPlayerResetsHoldAndUsesNewAnchor() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 2f, 0.1f, 0.1f, 5f, false);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, new Vector2(1f, 0f), 0.02f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(0f, tracker.HoldSeconds);
        tracker.CommitStep(new[] { FirstPoliceLife }, new Vector2(0.01f, 0f), Vector2.zero, 0.02f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(0.02f, tracker.HoldSeconds, 0.0001f);
    }

    [Test]
    public void PausedStepAppliesContactsButDoesNotAdvanceHold() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 1f, 0.1f, 0.1f, 5f, true);
        Assert.AreEqual(0f, tracker.HoldSeconds);
        Assert.AreEqual(1, tracker.ContactCount);
    }

    [Test]
    public void ExitAndEnterOrderPreservesContinuousMultiPoliceContact() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 1f, 0.1f, 0.1f, 5f, false);
        tracker.QueueExit(FirstPoliceLife);
        tracker.QueueEnter(SecondPoliceLife);
        tracker.CommitStep(new[] { SecondPoliceLife }, Origin, Vector2.zero, 1f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(1, tracker.ContactCount);
        Assert.AreEqual(2f, tracker.HoldSeconds, 0.0001f);

        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife, SecondPoliceLife }, Origin, Vector2.zero, 1f, 0.1f, 0.1f, 5f, false);
        Assert.AreEqual(2, tracker.ContactCount);
    }

    [Test]
    public void PendingRequestCanBePeekedAndRejectedThenRearmed() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        Assert.IsTrue(tracker.TryPeekArrestRequest());
        Assert.IsFalse(tracker.TryFinalizeArrestRequest(new int[0], Origin, Vector2.zero, true, true));
        Assert.IsFalse(tracker.HasPendingArrestRequest);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        Assert.IsTrue(tracker.HasPendingArrestRequest);
        Assert.IsTrue(tracker.TryFinalizeArrestRequest(new[] { FirstPoliceLife }, Origin, Vector2.zero, true, true));
        Assert.IsTrue(tracker.ArrestFinalized);
        Assert.IsFalse(tracker.TryFinalizeArrestRequest(new[] { FirstPoliceLife }, Origin, Vector2.zero, true, true));
    }

    [Test]
    public void DeathWinsPendingArrestRaceWithoutFinalizingArrest() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        var result = PoliceArrestRace.Resolve(tracker, new[] { FirstPoliceLife }, Origin, Vector2.zero, true, false);
        Assert.AreEqual(PoliceArrestRaceResult.Wrecked, result);
        Assert.IsFalse(tracker.ArrestFinalized);
    }

    [Test]
    public void FinalizedArrestEmitsOnceAndDoesNotAdvanceTime() {
        var tracker = StartedTracker();
        Enter(tracker, FirstPoliceLife);
        tracker.CommitStep(new[] { FirstPoliceLife }, Origin, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        float holdAtThreshold = tracker.HoldSeconds;
        Assert.AreEqual(PoliceArrestRaceResult.Arrested,
            PoliceArrestRace.Resolve(tracker, new[] { FirstPoliceLife }, Origin, Vector2.zero, true, true));
        Assert.AreEqual(holdAtThreshold, tracker.HoldSeconds, 0.0001f);
        Assert.AreEqual(PoliceArrestRaceResult.None,
            PoliceArrestRace.Resolve(tracker, new[] { FirstPoliceLife }, Origin, Vector2.zero, true, true));
    }

    static PoliceContactTracker StartedTracker() {
        var tracker = new PoliceContactTracker();
        tracker.Begin(Origin);
        return tracker;
    }

    static void Enter(PoliceContactTracker tracker, int lifeId) => tracker.QueueEnter(lifeId);
}
