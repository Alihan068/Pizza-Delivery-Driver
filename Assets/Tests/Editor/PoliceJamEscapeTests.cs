using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Focused pure coverage for the police obstacle-jam fix: a wiggle-immune jam detector, the shared
/// expiring jam memory, its effect on bounded clearance queries, and the new chase tuning contract.
/// </summary>
public sealed class PoliceJamEscapeTests {
    const float Step = 0.02f;
    const float Window = 1f;
    const float MinimumDisplacement = 0.35f;

    sealed class FixedSource : PoliceFreeNavigationQuery.IClearanceSource {
        readonly PoliceFreeNavigationQuery.Status status;
        public FixedSource(PoliceFreeNavigationQuery.Status status) { this.status = status; }
        public PoliceFreeNavigationQuery.SourceKind Kind => PoliceFreeNavigationQuery.SourceKind.Static;
        public PoliceFreeNavigationQuery.SourceResult Check(Vector2 center, Vector2 size, float headingDegrees) =>
            new PoliceFreeNavigationQuery.SourceResult(status, status == PoliceFreeNavigationQuery.Status.Clear
                ? PoliceFreeNavigationQuery.Reason.None : PoliceFreeNavigationQuery.Reason.SourceBlocked);
    }

    /// <summary>The reckless lateral wiggle used to reset the old per-frame test every step; net displacement still reports the jam.</summary>
    [Test]
    public void Tracker_ReportsJamDespiteWigglingAgainstAWall() {
        var tracker = new PoliceJamTracker();
        Vector2 pinned = new Vector2(12f, -3f);
        bool jammed = false;
        float elapsed = 0f;
        for (int i = 0; i < 100 && !jammed; i++) {
            elapsed += Step;
            // Grinding on the wall: sub-threshold lateral slide plus per-frame jitter.
            Vector2 sample = pinned + new Vector2(0.12f * Mathf.Sin(elapsed * 2.1f * Mathf.PI), 0.01f * Mathf.Cos(elapsed * 9f));
            jammed = tracker.Observe(Step, sample, true, Window, MinimumDisplacement);
        }
        Assert.IsTrue(jammed, "a wiggling but pinned car must be detected as jammed");
        Assert.GreaterOrEqual(tracker.HeldSeconds, Window);
        Assert.LessOrEqual(elapsed, Window + 4f * Step, "detection must land on the authored window, not later");
    }

    /// <summary>Real forward progress re-anchors the tracker, so a car driving a detour is never called jammed.</summary>
    [Test]
    public void Tracker_ProgressResetsAndBrakingClearsTheAnchor() {
        var tracker = new PoliceJamTracker();
        Vector2 position = Vector2.zero;
        for (int i = 0; i < 300; i++) {
            position += new Vector2(0.06f, 0f); // 3 m/s at a 0.02 s step
            Assert.IsFalse(tracker.Observe(Step, position, true, Window, MinimumDisplacement), "moving car at step " + i);
        }
        // Crawling below the threshold still counts as jammed: 0.2 m per window re-anchors at most
        // once before the hold time completes.
        Vector2 crawl = position;
        bool jammed = false;
        for (int i = 0; i < 200 && !jammed; i++) {
            crawl += new Vector2(0.004f, 0f); // 0.2 m per one-second window
            jammed = tracker.Observe(Step, crawl, true, Window, MinimumDisplacement);
        }
        Assert.IsTrue(jammed, "sub-threshold crawling against geometry is a jam");

        // A deliberate brake or reverse is never a jam and drops the anchor.
        Assert.IsFalse(tracker.Observe(Step, crawl, false, Window, MinimumDisplacement));
        Assert.IsFalse(tracker.HasAnchor);
        Assert.AreEqual(0f, tracker.HeldSeconds);

        // Non-finite input and non-positive steps are safe.
        Assert.IsFalse(tracker.Observe(Step, new Vector2(float.NaN, 0f), true, Window, MinimumDisplacement));
        Assert.IsFalse(tracker.Observe(0f, crawl, true, Window, MinimumDisplacement));
        Assert.IsFalse(tracker.Observe(-1f, crawl, true, Window, MinimumDisplacement));
    }

    /// <summary>Recorded spots expire, refresh in place instead of flushing the ring, and reject invalid input.</summary>
    [Test]
    public void Memory_RecordsRefreshesAndExpires() {
        var memory = new PoliceJamMemory(5f);
        Assert.AreEqual(0, memory.ActiveCount);
        Assert.IsTrue(memory.Record(new Vector2(4f, 4f), 1.5f));
        Assert.AreEqual(1, memory.ActiveCount);
        Assert.IsTrue(memory.Contains(new Vector2(4.4f, 4f)));
        Assert.IsFalse(memory.Contains(new Vector2(9f, 4f)));

        // A second jam in the same pocket refreshes the entry rather than consuming a slot.
        memory.SetClock(2f);
        Assert.IsTrue(memory.Record(new Vector2(4.3f, 4f), 1.5f));
        Assert.AreEqual(1, memory.ActiveCount);
        memory.SetClock(6f);
        Assert.AreEqual(1, memory.ActiveCount, "refreshed entry outlives the original lifetime");
        memory.SetClock(7.5f);
        Assert.AreEqual(0, memory.ActiveCount, "entries always expire");
        Assert.IsFalse(memory.Contains(new Vector2(4f, 4f)));

        // Capacity is bounded and the clock never moves backwards.
        for (int i = 0; i < PoliceJamMemory.Capacity + 6; i++) memory.Record(new Vector2(100f + i * 10f, 0f), 1f);
        Assert.AreEqual(PoliceJamMemory.Capacity, memory.ActiveCount);
        memory.SetClock(1f);
        Assert.AreEqual(7.5f, memory.Clock);
        Assert.IsFalse(memory.Record(new Vector2(float.NaN, 0f), 1f));
        Assert.IsFalse(memory.Record(Vector2.zero, 0f));
        memory.Clear();
        Assert.AreEqual(0, memory.ActiveCount);
    }

    /// <summary>A live jam spot blocks bounded clearance queries for every unit, and expiry restores the route.</summary>
    [Test]
    public void Query_BlocksEnvelopesOverALiveJamSpotOnly() {
        var bounds = new Rect(-50f, -50f, 100f, 100f);
        var footprint = new PoliceFreeNavigationGeometry.Footprint(new Vector2(2f, 4f), Vector2.zero);
        var query = new PoliceFreeNavigationQuery(new FixedSource(PoliceFreeNavigationQuery.Status.Clear), bounds);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Clear,
            query.CheckPose(new Vector2(6f, 0f), 0f, footprint, 0.1f, new PoliceFreeNavigationQuery.SearchBudget(1)).status);

        var memory = new PoliceJamMemory(10f);
        memory.Record(new Vector2(6f, 0f), 1.2f);
        query.JamMemory = memory;
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Blocked,
            query.CheckPose(new Vector2(6f, 0f), 0f, footprint, 0.1f, new PoliceFreeNavigationQuery.SearchBudget(1)).status,
            "the remembered pocket is blocked for every unit");
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Clear,
            query.CheckPose(new Vector2(20f, 0f), 0f, footprint, 0.1f, new PoliceFreeNavigationQuery.SearchBudget(1)).status,
            "unrelated geometry stays drivable");

        memory.SetClock(11f);
        Assert.AreEqual(PoliceFreeNavigationQuery.Status.Clear,
            query.CheckPose(new Vector2(6f, 0f), 0f, footprint, 0.1f, new PoliceFreeNavigationQuery.SearchBudget(1)).status,
            "an expired spot never keeps the map closed");
    }

    /// <summary>Contact pressure keeps driving through a first hit, survives a brief gap, and releases only on a real break-away.</summary>
    [Test]
    public void Pin_HoldsContactThroughBumpsAndReleasesOnBreakAway() {
        var pin = new PolicePinPressure(new PolicePinPressure.Settings(0.35f, 1.25f, 0.6f, 4f, 0.75f, 3f, 35f));
        Assert.IsFalse(pin.Observe(Step, false, 3f), "no contact, no pressure");
        Assert.IsTrue(pin.Observe(Step, true, 0f), "the first real hit engages contact pressure");

        // A bump that briefly opens a small gap must not end the pin the way a cancelled ram did.
        for (int i = 0; i < 25; i++) Assert.IsTrue(pin.Observe(Step, false, 0.9f), "brief separation at step " + i);
        Assert.IsTrue(pin.Observe(Step, true, 0f));
        Assert.Greater(pin.ActiveSeconds, 0.4f);

        // A genuine break-away past the release gap ends it after the grace window, then cools down.
        bool held = true;
        for (int i = 0; i < 40 && held; i++) held = pin.Observe(Step, false, 3f);
        Assert.IsFalse(held);
        Assert.Greater(pin.CooldownRemaining, 0f);
        Assert.IsFalse(pin.Observe(Step, true, 0f), "cooldown blocks an immediate re-pin");
        for (int i = 0; i < 40; i++) pin.Observe(Step, false, 5f);
        Assert.IsTrue(pin.Observe(Step, true, 0f), "after the cooldown the next hit pins again");

        // Bounded: one episode can never hold the player forever.
        bool stillHeld = true;
        int steps = 0;
        while (stillHeld && steps < 1000) { stillHeld = pin.Observe(Step, true, 0f); steps++; }
        Assert.IsFalse(stillHeld);
        Assert.LessOrEqual(steps * Step, 4f + 10f * Step);
    }

    /// <summary>Steering is damped and has a deadzone, so a pinning car does not bang-bang itself off the player.</summary>
    [Test]
    public void Pin_SteeringIsProportionalWithADeadzone() {
        var pin = new PolicePinPressure(new PolicePinPressure.Settings(0.35f, 1.25f, 0.6f, 4f, 0.75f, 3f, 35f));
        Assert.AreEqual(0f, pin.Steering(2f), "inside the deadzone");
        Assert.AreEqual(0f, pin.Steering(-2f));
        Assert.AreEqual(0.5f, pin.Steering(19f), 0.01f, "half lock at half the span");
        Assert.AreEqual(-0.5f, pin.Steering(-19f), 0.01f);
        Assert.AreEqual(1f, pin.Steering(60f), "saturates at full lock");
        Assert.AreEqual(0f, pin.Steering(float.NaN));
    }

    /// <summary>The new escape tuning survives cloning and is validated like every other authored field.</summary>
    [Test]
    public void ChaseSettings_CarryAndValidateEscapeTuning() {
        var settings = new PoliceFreeChaseSettings {
            jamWindowSeconds = 0.8f, jamMinimumDisplacement = 0.4f, forceThroughSeconds = 1.5f, replanGiveUpSeconds = 2.5f
        };
        Assert.IsTrue(settings.IsValid(out string reason), reason);
        PoliceFreeChaseSettings copy = settings.Clone();
        Assert.AreEqual(0.8f, copy.jamWindowSeconds);
        Assert.AreEqual(0.4f, copy.jamMinimumDisplacement);
        Assert.AreEqual(1.5f, copy.forceThroughSeconds);
        Assert.AreEqual(2.5f, copy.replanGiveUpSeconds);

        copy.jamWindowSeconds = 0f;
        Assert.IsFalse(copy.IsValid(out _));
        copy = settings.Clone();
        copy.replanGiveUpSeconds = float.NaN;
        Assert.IsFalse(copy.IsValid(out _));
        copy = settings.Clone();
        copy.forceThroughSeconds = -1f;
        Assert.IsFalse(copy.IsValid(out _));
        copy = settings.Clone();
        copy.forceThroughSeconds = 0f;
        Assert.IsTrue(copy.IsValid(out _), "zero force-through means detour immediately, which is valid tuning");

        copy = settings.Clone();
        Assert.AreEqual(settings.pinEngageGap, copy.pinEngageGap);
        Assert.AreEqual(settings.pinMaximumSeconds, copy.pinMaximumSeconds);
        copy.pinReleaseGap = copy.pinEngageGap - 0.1f;
        Assert.IsFalse(copy.IsValid(out _), "release gap below the engage gap is invalid tuning");
    }
}
