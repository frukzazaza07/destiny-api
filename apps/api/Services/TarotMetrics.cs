using System.Diagnostics.Metrics;

namespace TarotDestiny.Api.Services;

public sealed class TarotMetrics : IDisposable
{
    public const string MeterName = "TarotDestiny.Api";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _readingRequests;
    private readonly Counter<long> _classifierAccepted;
    private readonly Counter<long> _classifierRejected;
    private readonly Counter<long> _classifierRemoteSucceeded;
    private readonly Counter<long> _classifierFallbackUsed;
    private readonly Histogram<double> _classifierLatency;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _cacheSkipped;
    private readonly Counter<long> _llmCalls;
    private readonly Counter<long> _llmAvoided;
    private readonly Counter<long> _llmFailures;
    private readonly Histogram<double> _llmLatency;
    private long _readingRequestCount;
    private long _classifierAcceptedCount;
    private long _classifierRejectedCount;
    private long _classifierRemoteSucceededCount;
    private long _classifierFallbackUsedCount;
    private long _classifierDurationCount;
    private long _classifierDurationTotalMilliseconds;
    private long _cacheHitCount;
    private long _cacheMissCount;
    private long _cacheSkippedCount;
    private long _llmCallCount;
    private long _llmAvoidedCount;
    private long _llmFailureCount;
    private long _llmDurationCount;
    private long _llmDurationTotalMilliseconds;

    public TarotMetrics()
    {
        _readingRequests = _meter.CreateCounter<long>("tarot.readings.requests");
        _classifierAccepted = _meter.CreateCounter<long>("tarot.classifier.accepted");
        _classifierRejected = _meter.CreateCounter<long>("tarot.classifier.rejected");
        _classifierRemoteSucceeded = _meter.CreateCounter<long>("tarot.classifier.remote_succeeded");
        _classifierFallbackUsed = _meter.CreateCounter<long>("tarot.classifier.fallback_used");
        _classifierLatency = _meter.CreateHistogram<double>("tarot.classifier.duration", "ms");
        _cacheHits = _meter.CreateCounter<long>("tarot.cache.hits");
        _cacheMisses = _meter.CreateCounter<long>("tarot.cache.misses");
        _cacheSkipped = _meter.CreateCounter<long>("tarot.cache.skipped");
        _llmCalls = _meter.CreateCounter<long>("tarot.llm.calls");
        _llmAvoided = _meter.CreateCounter<long>("tarot.llm.avoided");
        _llmFailures = _meter.CreateCounter<long>("tarot.llm.failures");
        _llmLatency = _meter.CreateHistogram<double>("tarot.llm.duration", "ms");
    }

    public void ReadingRequested() { _readingRequests.Add(1); Interlocked.Increment(ref _readingRequestCount); }
    public void ClassifierAccepted() { _classifierAccepted.Add(1); Interlocked.Increment(ref _classifierAcceptedCount); }
    public void ClassifierRejected() { _classifierRejected.Add(1); Interlocked.Increment(ref _classifierRejectedCount); }
    public void ClassifierRemoteSucceeded() { _classifierRemoteSucceeded.Add(1); Interlocked.Increment(ref _classifierRemoteSucceededCount); }
    public void ClassifierFallbackUsed() { _classifierFallbackUsed.Add(1); Interlocked.Increment(ref _classifierFallbackUsedCount); }
    public void CacheHit() { _cacheHits.Add(1); Interlocked.Increment(ref _cacheHitCount); }
    public void CacheMiss() { _cacheMisses.Add(1); Interlocked.Increment(ref _cacheMissCount); }
    public void CacheSkipped() { _cacheSkipped.Add(1); Interlocked.Increment(ref _cacheSkippedCount); }
    public void LlmCalled() { _llmCalls.Add(1); Interlocked.Increment(ref _llmCallCount); }
    public void LlmAvoided() { _llmAvoided.Add(1); Interlocked.Increment(ref _llmAvoidedCount); }
    public void LlmFailed() { _llmFailures.Add(1); Interlocked.Increment(ref _llmFailureCount); }

    public void RecordClassifierLatency(TimeSpan duration)
    {
        _classifierLatency.Record(duration.TotalMilliseconds);
        Interlocked.Increment(ref _classifierDurationCount);
        Interlocked.Add(ref _classifierDurationTotalMilliseconds, (long)duration.TotalMilliseconds);
    }

    public void RecordLlmLatency(TimeSpan duration)
    {
        _llmLatency.Record(duration.TotalMilliseconds);
        Interlocked.Increment(ref _llmDurationCount);
        Interlocked.Add(ref _llmDurationTotalMilliseconds, (long)duration.TotalMilliseconds);
    }

    public TarotMetricSnapshot Snapshot()
    {
        var durationCount = Interlocked.Read(ref _llmDurationCount);
        var durationTotal = Interlocked.Read(ref _llmDurationTotalMilliseconds);
        var classifierDurationCount = Interlocked.Read(ref _classifierDurationCount);
        var classifierDurationTotal = Interlocked.Read(ref _classifierDurationTotalMilliseconds);
        return new TarotMetricSnapshot(
            Interlocked.Read(ref _readingRequestCount),
            Interlocked.Read(ref _classifierAcceptedCount),
            Interlocked.Read(ref _classifierRejectedCount),
            Interlocked.Read(ref _classifierRemoteSucceededCount),
            Interlocked.Read(ref _classifierFallbackUsedCount),
            classifierDurationCount == 0 ? 0 : (double)classifierDurationTotal / classifierDurationCount,
            Interlocked.Read(ref _cacheHitCount),
            Interlocked.Read(ref _cacheMissCount),
            Interlocked.Read(ref _cacheSkippedCount),
            Interlocked.Read(ref _llmCallCount),
            Interlocked.Read(ref _llmAvoidedCount),
            Interlocked.Read(ref _llmFailureCount),
            durationCount == 0 ? 0 : (double)durationTotal / durationCount);
    }

    public void Dispose() => _meter.Dispose();
}

public sealed record TarotMetricSnapshot(
    long ReadingRequests,
    long ClassifierAccepted,
    long ClassifierRejected,
    long ClassifierRemoteSucceeded,
    long ClassifierFallbackUsed,
    double AverageClassifierLatencyMs,
    long CacheHits,
    long CacheMisses,
    long CacheSkipped,
    long LlmCalls,
    long LlmAvoided,
    long LlmFailures,
    double AverageLlmLatencyMs);
