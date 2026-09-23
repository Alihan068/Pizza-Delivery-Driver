using NUnit.Framework;

/// <summary>Pure bounded statistic tests for the passive S12 benchmark recorder.</summary>
public sealed class TrafficBenchmarkRecorderTests {
    /// <summary>Confirms percentile interpolation uses only the bounded sample prefix.</summary>
    [Test]
    public void PercentileUsesBoundedPrefix() {
        float[] samples = { 5f, 1f, 3f, 999f };
        Assert.That(TrafficBenchmarkStatistics.Percentile(samples, 3, 0.5d), Is.EqualTo(3f));
        Assert.That(TrafficBenchmarkStatistics.Percentile(samples, 3, 0.95d), Is.EqualTo(4.8d).Within(0.0001d));
    }

    /// <summary>Confirms histogram mapping clamps high samples and rejects invalid bounds.</summary>
    [Test]
    public void HistogramBucketIsBounded() {
        Assert.That(TrafficBenchmarkStatistics.HistogramBucket(0.2f, 1f, 4), Is.EqualTo(0));
        Assert.That(TrafficBenchmarkStatistics.HistogramBucket(2.1f, 1f, 4), Is.EqualTo(2));
        Assert.That(TrafficBenchmarkStatistics.HistogramBucket(99f, 1f, 4), Is.EqualTo(3));
        Assert.That(TrafficBenchmarkStatistics.HistogramBucket(2f, 0f, 4), Is.EqualTo(-1));
    }
}
