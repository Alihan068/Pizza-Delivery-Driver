using System;

/// <summary>Fixed acceptance workload, not gameplay tuning or a configurable automation framework.</summary>
public static class S12BenchmarkPlan {
    /// <summary>Approved authored map identity; no scene-name or primary-map fallback.</summary>
    public const string MapId = "4c46aef37a1f4f468fc0915175730643";
    /// <summary>Approved modifier suppressing police without suppressing civilian simulation.</summary>
    public const string PeacefulId = "b2d4f6a8c0e2447f9b3d5e7a9c1e3f5a";
    /// <summary>Approved modifier for the first empty traffic workload.</summary>
    public const string NoTrafficId = "a1c3e5f7b9d1436f8a2c4e6f8b0d2c4e";
    /// <summary>Five lifecycle windows, one identical baseline repeat, and one normal-police window.</summary>
    public const int CaseCount = 7;
    /// <summary>Process-local frame cap; Update samples do not prove rendered frames.</summary>
    public const int FrameCap = 60;
    /// <summary>Read-back launch dimensions; mismatches fail rather than change machine preferences.</summary>
    public const int Width = 1280, Height = 720;
    /// <summary>Absolute wall-clock safety bounds, in seconds.</summary>
    public const double ReadyTimeout = 30, CleanupTimeout = 15, TotalTimeout = 1500;
    /// <summary>Recorder capacity, sufficient for the single soak at the declared frame cap.</summary>
    public const int MaximumSamples = 100000;

    /// <summary>Returns a new immutable case; out-of-range indexes cannot silently extend the run.</summary>
    public static S12BenchmarkCase GetCase(int index) {
        switch (index) {
            case 0: return new S12BenchmarkCase("empty-peaceful", 3, 60, false, true, true);
            case 1: return new S12BenchmarkCase("civilian-cap-peaceful", 5, 60, false, true, false);
            case 2: return new S12BenchmarkCase("lifecycle-10min-configured", 10, 60, false, true, false);
            case 3: return new S12BenchmarkCase("baseline-peaceful-a", 3, 60, false, true, false);
            case 4: return new S12BenchmarkCase("peaceful-freeplay", 3, 600, true, true, false);
            case 5: return new S12BenchmarkCase("baseline-peaceful-b", 3, 60, false, true, false);
            case 6: return new S12BenchmarkCase("normal-police", 3, 60, false, false, false);
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>Inclusive deadline check; malformed or reversed clocks fail closed.</summary>
    public static bool Expired(double started, double now, double limit) {
        return double.IsNaN(started) || double.IsNaN(now) || double.IsNaN(limit) ||
            double.IsInfinity(started) || double.IsInfinity(now) || double.IsInfinity(limit) ||
            limit <= 0 || now < started || now - started >= limit;
    }
}

/// <summary>One stationary workload; duration selection is independent of the measured window.</summary>
public readonly struct S12BenchmarkCase {
    /// <summary>Honest workload label included in each output.</summary>
    public readonly string Label;
    /// <summary>Selected timer configuration, including the ignored timed setting during FREEPLAY.</summary>
    public readonly int Minutes;
    /// <summary>Required continuous realtime measurement window.</summary>
    public readonly float Seconds;
    /// <summary>Authored mode/modifier choices; these never override frozen rules.</summary>
    public readonly bool Freeplay, Peaceful, Empty;
    /// <summary>Creates one fixed-plan entry without reading Unity state.</summary>
    public S12BenchmarkCase(string label, int minutes, float seconds, bool freeplay, bool peaceful, bool empty) {
        Label = label; Minutes = minutes; Seconds = seconds;
        Freeplay = freeplay; Peaceful = peaceful; Empty = empty;
    }
}
