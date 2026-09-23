#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;

[Serializable] sealed class TrafficBenchmarkSummaryDto {
    public TrafficBenchmarkMetadataDto metadata; public int samples; public bool frameSamplesAvailable; public float durationSeconds;
    public TrafficBenchmarkFrameDto frameMs; public float maxGC; public float totalGC; public TrafficBenchmarkBudgetDto capacities;
    public TrafficBenchmarkPopulationDto population; public TrafficBenchmarkCounterDto[] counters;
}
[Serializable] sealed class TrafficBenchmarkMetadataDto { public string scene; public string seed; public string resolution; public string scenario; }
[Serializable] sealed class TrafficBenchmarkFrameDto { public float median; public float p95; public float p99; public float max; }
[Serializable] sealed class TrafficBenchmarkBudgetDto {
    public bool available; public int maxCivilian; public int maxPolice; public int maxTotal;
    public int maxWreckSlots; public int maxTotalPhysicsObjects;
}
[Serializable] sealed class TrafficBenchmarkPopulationDto {
    public int maxCapViolations; public int maxActiveCivilian; public int maxActivePolice;
    public int maxPendingCivilian; public int maxPendingPolice; public int maxWrecks; public int maxReservedWrecks;
}
[Serializable] sealed class TrafficBenchmarkCounterDto {
    public string name; public string status; public bool available; public string unit;
    public int capacity; public int samplesSeen; public float min; public float max; public float mean;
}
#endif
