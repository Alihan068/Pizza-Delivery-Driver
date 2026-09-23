using NUnit.Framework;

/// <summary>Pure coverage for the player's momentum top-speed boost: warm-up, gradual rise, cap, gradual fall and crash drain.</summary>
public sealed class MomentumBoostTests {
    const float Step = 0.02f;

    static MomentumBoost Create() => new MomentumBoost(1f, 1.5f, 4f, 1.5f, 0.5f);

    static float Run(MomentumBoost boost, float seconds, bool qualifying) {
        int steps = UnityEngine.Mathf.RoundToInt(seconds / Step);
        for (int i = 0; i < steps; i++) boost.Step(Step, qualifying);
        return boost.Multiplier;
    }

    /// <summary>Nothing happens during the one-second warm-up; afterwards the multiplier climbs gradually and caps at 1.5.</summary>
    [Test]
    public void Rises_AfterWarmupGraduallyToTheCap() {
        var boost = Create();
        Assert.AreEqual(1f, Run(boost, 1f, true), 0.0001f, "flat during warm-up");
        Assert.AreEqual(1.25f, Run(boost, 2f, true), 0.01f, "half-way two seconds into a four-second rise");
        Assert.AreEqual(1.5f, Run(boost, 3f, true), 0.0001f, "capped at the maximum");
        Assert.IsTrue(boost.IsBoosting);
    }

    /// <summary>Lifting off, braking or drifting drains gradually and restarts the warm-up.</summary>
    [Test]
    public void Falls_GraduallyWhenThePlayerStopsQualifying() {
        var boost = Create();
        Run(boost, 6f, true);
        Assert.AreEqual(1.25f, Run(boost, 0.75f, false), 0.01f, "half of a 1.5s fall");
        Assert.AreEqual(1f, Run(boost, 1f, false), 0.0001f, "back to normal, never below");
        Assert.AreEqual(0f, boost.QualifiedSeconds);
        Assert.AreEqual(1f, Run(boost, 0.9f, true), 0.0001f, "a new warm-up is required");
    }

    /// <summary>A crash drains the whole boost at the crash rate even if the player keeps the throttle straight.</summary>
    [Test]
    public void Crash_DrainsFullyBeforeANewWarmup() {
        var boost = Create();
        Run(boost, 6f, true);
        boost.NotifyCrash();
        Assert.AreEqual(1.26f, Run(boost, 0.24f, true), 0.005f, "crash rate (12 steps), not the slower normal fall");
        Assert.AreEqual(1f, Run(boost, 0.3f, true), 0.0001f);
        Assert.AreEqual(1f, Run(boost, 0.9f, true), 0.0001f, "warm-up restarts after the drain");
        Assert.Greater(Run(boost, 0.5f, true), 1f, "then it climbs again");
    }

    /// <summary>Invalid steps and a disabled maximum never change or break the multiplier.</summary>
    [Test]
    public void InvalidInput_IsSafe() {
        var boost = Create();
        boost.Step(float.NaN, true);
        boost.Step(-1f, true);
        Assert.AreEqual(1f, boost.Multiplier);
        var disabled = new MomentumBoost(1f, 0.8f, 4f, 1.5f, 0.5f);
        Assert.AreEqual(1f, Run(disabled, 10f, true), "a maximum below 1 disables the boost");
        boost.Reset();
        Assert.AreEqual(1f, boost.Multiplier);
    }
}
