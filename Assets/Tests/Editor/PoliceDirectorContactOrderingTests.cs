using NUnit.Framework;
using UnityEngine;

/// <summary>Pure ordering regressions for the Director-to-contact finalization contract.</summary>
public sealed class PoliceDirectorContactOrderingTests {
    const int PoliceLifeId = 501;

    [Test]
    public void CurrentPhysicsResultCancelsHoldBeforeLateFinalize() {
        var tracker = NewTracker();
        tracker.QueueEnter(PoliceLifeId);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);

        Vector2 currentCompletedPhysicsPosition = new Vector2(0.5f, 0f);
        Vector2 currentCompletedPhysicsVelocity = new Vector2(1f, 0f);
        tracker.CommitStep(new[] { PoliceLifeId }, currentCompletedPhysicsPosition,
            currentCompletedPhysicsVelocity, 0.02f, 0.1f, 0.1f, 5f, false);

        Assert.IsFalse(tracker.HasPendingArrestRequest);
        Assert.AreEqual(0f, tracker.HoldSeconds);
    }

    [Test]
    public void PendingThresholdIsCancelledByMovementBeforeLateFinalize() {
        var tracker = NewTracker();
        tracker.QueueEnter(PoliceLifeId);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        Assert.IsTrue(tracker.HasPendingArrestRequest);

        tracker.CommitStep(new[] { PoliceLifeId }, new Vector2(1f, 0f), Vector2.zero,
            0.02f, 0.1f, 0.1f, 5f, false);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero,
            0.02f, 0.1f, 0.1f, 5f, false);

        Assert.IsFalse(tracker.HasPendingArrestRequest);
        Assert.AreEqual(0f, tracker.HoldSeconds);
        Assert.AreEqual(PoliceArrestRaceResult.None,
            PoliceArrestRace.Resolve(tracker, new[] { PoliceLifeId }, new Vector2(1f, 0f), Vector2.zero, true, true));
    }

    [Test]
    public void PendingThresholdCannotBeSealedAfterLaterDeath() {
        var tracker = NewTracker();
        tracker.QueueEnter(PoliceLifeId);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);

        Assert.AreEqual(PoliceArrestRaceResult.Wrecked,
            PoliceArrestRace.Resolve(tracker, new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, true, false));
        Assert.IsFalse(tracker.ArrestFinalized);
    }

    [Test]
    public void PausedContactStepRetainsValidPendingRequestWithoutFinalizingIt() {
        var tracker = NewTracker();
        tracker.QueueEnter(PoliceLifeId);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, 5f, 0.1f, 0.1f, 5f, false);
        tracker.CommitStep(new[] { PoliceLifeId }, Vector2.zero, Vector2.zero, 0.02f, 0.1f, 0.1f, 5f, true);

        Assert.IsTrue(tracker.HasPendingArrestRequest);
        Assert.AreEqual(5f, tracker.HoldSeconds, 0.0001f);
    }

    static PoliceContactTracker NewTracker() {
        var tracker = new PoliceContactTracker();
        tracker.Begin(Vector2.zero);
        return tracker;
    }
}
