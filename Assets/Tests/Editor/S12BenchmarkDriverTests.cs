using System;
using System.IO;
using NUnit.Framework;

/// <summary>Two focused pure tests; no scenes, real profiles, prefs, or player launch.</summary>
public sealed class S12BenchmarkDriverTests {
    /// <summary>Malformed/unsupported opt-ins cannot fall back to production; no flag stays inert.</summary>
    [Test]
    public void Admission_RequiresDevelopmentStandaloneUniqueFlagAndFreshIsolatedGuid() {
        string root = Path.Combine(Path.GetTempPath(), "S12PureAdmissionNoIO");
        string[] valid = { "player", "-pizzaBenchmark", "stationary-v2", "-pizzaTestProfile", "0123456789abcdef0123456789abcdef" };
        Assert.That(S12BenchmarkGate.Validate(valid, root, true, false, out bool requested, out var profile, out _), Is.True);
        Assert.That(requested, Is.True);
        Assert.That(DevelopmentTestProfile.IsContainedPath(root, profile.RootPath), Is.True);
        Assert.That(S12BenchmarkGate.Validate(valid, root, false, false, out _, out _, out _), Is.False);
        Assert.That(S12BenchmarkGate.Validate(valid, root, true, true, out _, out _, out _), Is.False);
        string[][] rejected = {
            new[] { "-pizzaBenchmark", "stationary-v2" },
            new[] { "-pizzaBenchmark" },
            new[] { "-pizzaBenchmark", "other", "-pizzaTestProfile", valid[4] },
            new[] { "-pizzaBenchmark", "stationary-v2", "-pizzaBenchmark", "stationary-v2", "-pizzaTestProfile", valid[4] },
            new[] { "-pizzaBenchmark", "stationary-v2", "-pizzaTestProfile", "../real-career" },
            new[] { "-pizzaBenchmark", "stationary-v2", "-pizzaTestProfile", valid[4], "-pizzaTestProfile", valid[4] }
        };
        foreach (var args in rejected) {
            Assert.That(S12BenchmarkGate.Validate(args, root, true, false, out requested, out _, out _), Is.False);
            Assert.That(requested, Is.True);
        }
        Assert.That(S12BenchmarkGate.Validate(Array.Empty<string>(), root, false, true,
            out requested, out _, out _), Is.True);
        Assert.That(requested, Is.False);
    }

    /// <summary>Exactly one soak, five prescribed lifecycle configs, identical baselines and finite deadlines.</summary>
    [Test]
    public void Plan_PreservesFiveCyclesSingleSoakIdenticalBaselineAndAbsoluteDeadlines() {
        int[] minutes = { 3, 5, 10, 3, 3 };
        int soaks = 0;
        float seconds = 0;
        for (int i = 0; i < S12BenchmarkPlan.CaseCount; i++) {
            var item = S12BenchmarkPlan.GetCase(i);
            if (i < 5) { Assert.That(item.Minutes, Is.EqualTo(minutes[i])); Assert.That(item.Peaceful, Is.True); }
            if (item.Freeplay) { soaks++; Assert.That(i, Is.EqualTo(4)); }
            Assert.That(item.Seconds, Is.EqualTo(item.Freeplay ? 600 : 60));
            seconds += item.Seconds;
        }
        Assert.That(soaks, Is.EqualTo(1));
        var a = S12BenchmarkPlan.GetCase(3); var b = S12BenchmarkPlan.GetCase(5);
        Assert.That((a.Minutes, a.Seconds, a.Freeplay, a.Peaceful, a.Empty),
            Is.EqualTo((b.Minutes, b.Seconds, b.Freeplay, b.Peaceful, b.Empty)));
        Assert.That(seconds + S12BenchmarkPlan.CaseCount * (S12BenchmarkPlan.ReadyTimeout +
            2 * S12BenchmarkPlan.CleanupTimeout) + 2 * S12BenchmarkPlan.ReadyTimeout,
            Is.LessThan(S12BenchmarkPlan.TotalTimeout));
        Assert.Throws<ArgumentOutOfRangeException>(() => S12BenchmarkPlan.GetCase(S12BenchmarkPlan.CaseCount));
        foreach (double limit in new[] { S12BenchmarkPlan.ReadyTimeout, S12BenchmarkPlan.CleanupTimeout, S12BenchmarkPlan.TotalTimeout }) {
            Assert.That(S12BenchmarkPlan.Expired(10, 10 + limit - 0.001, limit), Is.False);
            Assert.That(S12BenchmarkPlan.Expired(10, 10 + limit, limit), Is.True);
        }
        Assert.That(S12BenchmarkPlan.Expired(10, 9, 30), Is.True);
        Assert.That(S12BenchmarkPlan.Expired(10, double.NaN, 30), Is.True);
        Assert.That(S12BenchmarkPlan.Expired(10, 11, double.PositiveInfinity), Is.True);
    }
}
