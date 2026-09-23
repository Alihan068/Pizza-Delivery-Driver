#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>Why one bounded passive benchmark stopped.</summary>
public enum TrafficBenchmarkStopReason { None, Duration, Capacity, Explicit }
/// <summary>Immutable metadata identifying one bounded passive traffic benchmark run.</summary>
public readonly struct TrafficBenchmarkMetadata {
    /// <summary>Scene name captured by the caller.</summary>
    public readonly string scene;
    /// <summary>Deterministic seed captured by the caller.</summary>
    public readonly string seed;
    /// <summary>Requested resolution label captured by the caller.</summary>
    public readonly string resolution;
    /// <summary>Scenario label captured by the caller.</summary>
    public readonly string scenario;
    /// <summary>Creates benchmark metadata without reading global or Unity state.</summary>
    public TrafficBenchmarkMetadata(string scene, string seed, string resolution, string scenario) {
        this.scene = scene ?? string.Empty; this.seed = seed ?? string.Empty;
        this.resolution = resolution ?? string.Empty; this.scenario = scenario ?? string.Empty;
    }
}

/// <summary>Pure bounded statistics used by the passive recorder and focused tests.</summary>
public static class TrafficBenchmarkStatistics {
    /// <summary>Returns an interpolated percentile after sorting the bounded sample prefix.</summary>
    public static double Percentile(float[] samples, int count, double percentile) {
        if (samples == null || count <= 0 || count > samples.Length) return double.NaN;
        Array.Sort(samples, 0, count);
        double position = (count - 1) * Math.Max(0d, Math.Min(1d, percentile));
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        if (lower == upper) return samples[lower];
        return samples[lower] + (samples[upper] - samples[lower]) * (position - lower);
    }
    /// <summary>Maps one non-negative millisecond sample to a bounded histogram bucket.</summary>
    public static int HistogramBucket(float milliseconds, float bucketWidth, int bucketCount) {
        if (bucketWidth <= 0f || bucketCount <= 0 || float.IsNaN(milliseconds)) return -1;
        return Math.Min((int)Math.Floor(Math.Max(0f, milliseconds) / bucketWidth), bucketCount - 1);
    }
}

/// <summary>Passive, opt-in development profiler for bounded traffic sessions.</summary>
public sealed class TrafficBenchmarkRecorder : IDisposable {
    const int MaximumSamples = 100000, HistogramBuckets = 1000, CounterCapacity = 1;
    const float MaximumDurationSeconds = 3600f, HistogramBucketWidthMilliseconds = 1f;
    readonly CounterCapture[] counters = {
        new CounterCapture("Main Thread", "TimeNanoseconds"), new CounterCapture("GC Allocated In Frame", "Bytes"),
        new CounterCapture("Physics 2D", "TimeNanoseconds"), new CounterCapture("Texture Memory", "Bytes"),
        new CounterCapture("Managed Used Memory", "Bytes"), new CounterCapture("Total Used Memory", "Bytes")
    };
    DevelopmentTestProfile profile; TrafficBenchmarkMetadata metadata; TrafficSessionServices services;
    VehiclePopulationService population; float durationLimitSeconds; float[] frameMilliseconds; int[] frameHistogram;
    int sampleCount, capViolations; float elapsedSeconds; string runId; bool running, disposed;
    int maxActiveCivilian, maxActivePolice, maxPendingCivilian, maxPendingPolice, maxWrecks, maxReservedWrecks;
    /// <summary>True while bounded samples are being collected.</summary>
    public bool IsRunning => running;
    /// <summary>Last successfully written summary path, or null.</summary>
    public string LastOutputPath { get; private set; }
    /// <summary>Last stop or write error, or null.</summary>
    public string LastError { get; private set; }
    /// <summary>Last completed stop reason.</summary>
    public TrafficBenchmarkStopReason StopReason { get; private set; }

    /// <summary>Initializes only after profile, finite bounds, metadata, and live services validate.</summary>
    public bool Initialize(DevelopmentTestProfile validProfile, int boundedSamples, float boundedDurationSeconds,
                           TrafficBenchmarkMetadata runMetadata, TrafficSessionHost sessionHost, out string reason) {
        reason = null;
        if (disposed) return Fail("Recorder is disposed.", out reason);
        if (running) return Fail("Recorder is already running.", out reason);
        if (!validProfile.IsRequested || !validProfile.IsValid || string.IsNullOrEmpty(validProfile.RootPath))
            return Fail("A valid active development profile is required.", out reason);
        if (boundedSamples <= 0 || boundedSamples > MaximumSamples || float.IsNaN(boundedDurationSeconds) ||
            float.IsInfinity(boundedDurationSeconds) || boundedDurationSeconds <= 0f || boundedDurationSeconds > MaximumDurationSeconds)
            return Fail("Sample capacity or duration is outside the bounded range.", out reason);
        if (string.IsNullOrWhiteSpace(runMetadata.scene) || string.IsNullOrWhiteSpace(runMetadata.seed) ||
            string.IsNullOrWhiteSpace(runMetadata.resolution) || string.IsNullOrWhiteSpace(runMetadata.scenario))
            return Fail("Run metadata is incomplete.", out reason);
        if (sessionHost == null || sessionHost.Services == null || sessionHost.Services.Population == null)
            return Fail("A live traffic host with population services is required.", out reason);
        profile = validProfile; metadata = runMetadata; services = sessionHost.Services; population = services.Population;
        durationLimitSeconds = boundedDurationSeconds; runId = Guid.NewGuid().ToString("N");
        LastOutputPath = null; LastError = null; StopReason = TrafficBenchmarkStopReason.None;
        sampleCount = 0; capViolations = 0; elapsedSeconds = 0f; maxActiveCivilian = 0; maxActivePolice = 0;
        maxPendingCivilian = 0; maxPendingPolice = 0; maxWrecks = 0; maxReservedWrecks = 0;
        frameMilliseconds = new float[boundedSamples];
        frameHistogram = new int[HistogramBuckets]; InitializeCounters(); running = true; return true;
    }

    /// <summary>Collects one unscaled frame sample and bounded live population counters.</summary>
    public void Update() {
        if (!running || disposed) return;
        float delta = Mathf.Max(0f, Time.unscaledDeltaTime); elapsedSeconds += delta;
        if (sampleCount < frameMilliseconds.Length) {
            float milliseconds = delta * 1000f; frameMilliseconds[sampleCount++] = milliseconds;
            int bucket = TrafficBenchmarkStatistics.HistogramBucket(milliseconds, HistogramBucketWidthMilliseconds, frameHistogram.Length);
            if (bucket >= 0) frameHistogram[bucket]++;
        }
        int activeCivilian = population.ActiveCivilianCount, activePolice = population.ActivePoliceCount;
        int pendingCivilian = population.PendingCivilianCount, pendingPolice = population.PendingPoliceCount;
        int wrecks = population.OccupiedWreckCount, reservedWrecks = population.ReservedFutureWreckCount;
        maxActiveCivilian = Mathf.Max(maxActiveCivilian, activeCivilian); maxActivePolice = Mathf.Max(maxActivePolice, activePolice);
        maxPendingCivilian = Mathf.Max(maxPendingCivilian, pendingCivilian); maxPendingPolice = Mathf.Max(maxPendingPolice, pendingPolice);
        maxWrecks = Mathf.Max(maxWrecks, wrecks); maxReservedWrecks = Mathf.Max(maxReservedWrecks, reservedWrecks);
        int civilians = activeCivilian + pendingCivilian, police = activePolice + pendingPolice;
        PopulationBudgetData budget = services.Budget;
        int movingObjects = civilians + police;
        int physicsObjects = movingObjects + wrecks;
        if (budget != null && (civilians > budget.maxCivilianMoving || police > budget.maxPoliceMoving ||
            movingObjects > budget.maxTotalMoving || wrecks + reservedWrecks > budget.maxWreckSlots ||
            physicsObjects > budget.maxTotalPhysicsObjects)) capViolations++;
        for (int i = 0; i < counters.Length; i++) counters[i].Capture();
        if (sampleCount >= frameMilliseconds.Length) Stop(TrafficBenchmarkStopReason.Capacity, out _, out _);
        else if (elapsedSeconds >= durationLimitSeconds) Stop(TrafficBenchmarkStopReason.Duration, out _, out _);
    }

    /// <summary>Stops explicitly, disposes counters, and writes one unique JSON summary.</summary>
    public bool Stop(out string outputPath, out string reason) { return Stop(TrafficBenchmarkStopReason.Explicit, out outputPath, out reason); }
    bool Stop(TrafficBenchmarkStopReason stopReason, out string outputPath, out string reason) {
        outputPath = null; reason = null; StopReason = stopReason;
        if (!running) return Fail("Recorder is not running.", out reason);
        running = false; DisposeCounters();
        double median = TrafficBenchmarkStatistics.Percentile(frameMilliseconds, sampleCount, 0.50d);
        double p95 = TrafficBenchmarkStatistics.Percentile(frameMilliseconds, sampleCount, 0.95d);
        double p99 = TrafficBenchmarkStatistics.Percentile(frameMilliseconds, sampleCount, 0.99d);
        try {
            Directory.CreateDirectory(profile.RootPath); outputPath = Path.Combine(profile.RootPath, "traffic-benchmark-" + runId + ".json");
            File.WriteAllText(outputPath, BuildSummaryJson(median, p95, p99)); LastOutputPath = outputPath; LastError = null; return true;
        } catch (Exception exception) { LastError = "Benchmark summary write failed: " + exception.Message; reason = LastError; return false; }
    }
    /// <summary>Disposes every native profiler recorder and prevents further capture.</summary>
    public void Dispose() { if (disposed) return; running = false; DisposeCounters(); disposed = true; }

    void InitializeCounters() {
        for (int i = 0; i < counters.Length; i++) counters[i].ResetForInitialize();
        List<ProfilerRecorderHandle> available = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(available);
        for (int i = 0; i < counters.Length; i++) foreach (ProfilerRecorderHandle handle in available) {
            if (!handle.Valid) continue; ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name != counters[i].Name || description.UnitType.ToString() != counters[i].ExpectedUnit) continue;
            counters[i].Start(handle, description.UnitType.ToString()); break;
        }
    }
    string BuildSummaryJson(double median, double p95, double p99) {
        TrafficBenchmarkBudgetDto budget = new TrafficBenchmarkBudgetDto { available = services.Budget != null };
        if (budget.available) { budget.maxCivilian = services.Budget.maxCivilianMoving; budget.maxPolice = services.Budget.maxPoliceMoving;
            budget.maxTotal = services.Budget.maxTotalMoving; budget.maxWreckSlots = services.Budget.maxWreckSlots; budget.maxTotalPhysicsObjects = services.Budget.maxTotalPhysicsObjects; }
        TrafficBenchmarkCounterDto[] counterDtos = new TrafficBenchmarkCounterDto[counters.Length]; for (int i = 0; i < counters.Length; i++) counterDtos[i] = counters[i].ToDto();
        CounterCapture gc = counters[1];
        TrafficBenchmarkSummaryDto summary = new TrafficBenchmarkSummaryDto {
            metadata = new TrafficBenchmarkMetadataDto { scene = metadata.scene, seed = metadata.seed, resolution = metadata.resolution, scenario = metadata.scenario },
            samples = sampleCount, frameSamplesAvailable = sampleCount > 0, durationSeconds = elapsedSeconds,
            frameMs = new TrafficBenchmarkFrameDto { median = FiniteOrZero(median), p95 = FiniteOrZero(p95), p99 = FiniteOrZero(p99), max = sampleCount == 0 ? 0f : frameMilliseconds[sampleCount - 1] },
            maxGC = gc.Available ? FiniteOrZero(gc.Maximum) : 0f, totalGC = gc.Available ? FiniteOrZero(gc.Sum) : 0f,
            capacities = budget, population = new TrafficBenchmarkPopulationDto { maxCapViolations = capViolations, maxActiveCivilian = maxActiveCivilian,
                maxActivePolice = maxActivePolice, maxPendingCivilian = maxPendingCivilian, maxPendingPolice = maxPendingPolice,
                maxWrecks = maxWrecks, maxReservedWrecks = maxReservedWrecks }, counters = counterDtos
        };
        return JsonUtility.ToJson(summary, true);
    }
    void DisposeCounters() { for (int i = 0; i < counters.Length; i++) counters[i].Dispose(); }
    static float FiniteOrZero(double value) { return double.IsNaN(value) || double.IsInfinity(value) ? 0f : (float)value; }
    static bool Fail(string message, out string reason) { reason = message; return false; }

    sealed class CounterCapture {
        public readonly string Name, ExpectedUnit; public ProfilerRecorder Recorder; public bool Available; public string Unit; bool nativeDisposed;
        public double LastValue, Minimum, Maximum, Sum; public int SamplesSeen;
        public CounterCapture(string name, string expectedUnit) { Name = name; ExpectedUnit = expectedUnit; }
        public void ResetForInitialize() { if (Available && !nativeDisposed && Recorder.Valid) Recorder.Dispose(); Available = false; nativeDisposed = false; Unit = null; LastValue = 0d; Minimum = double.PositiveInfinity; Maximum = double.NegativeInfinity; Sum = 0d; SamplesSeen = 0; }
        public void Start(ProfilerRecorderHandle handle, string unit) { Recorder = new ProfilerRecorder(handle, CounterCapacity, ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame); Available = Recorder.Valid; nativeDisposed = false; Unit = unit; }
        public void Capture() { if (!Available || !Recorder.Valid) return; LastValue = Recorder.LastValueAsDouble; Minimum = Math.Min(Minimum, LastValue); Maximum = Math.Max(Maximum, LastValue); Sum += LastValue; SamplesSeen++; }
        public TrafficBenchmarkCounterDto ToDto() { bool measured = Available && SamplesSeen > 0; return new TrafficBenchmarkCounterDto { name = Name, status = measured ? "Measured" : "NotMeasured", available = Available, unit = Available ? Unit : null, capacity = CounterCapacity, samplesSeen = SamplesSeen, min = measured ? FiniteOrZero(Minimum) : 0f, max = measured ? FiniteOrZero(Maximum) : 0f, mean = measured ? FiniteOrZero(Sum / SamplesSeen) : 0f }; }
        public void Dispose() { if (Available && !nativeDisposed) Recorder.Dispose(); nativeDisposed = true; }
    }
}
#endif
