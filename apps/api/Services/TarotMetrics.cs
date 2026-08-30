using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using TarotDestiny.Api.Contracts;

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
    private readonly Counter<long> _classifierRequests;
    private readonly Counter<long> _classifierIntentDistribution;
    private readonly Histogram<double> _classifierConfidence;
    private readonly Counter<long> _classifierInvalidResponses;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _cacheSkipped;
    private readonly Counter<long> _cacheStores;
    private readonly Counter<long> _cachePreflightRequests;
    private readonly Counter<long> _cachePreflightEligible;
    private readonly Counter<long> _cachePreflightRejected;
    private readonly Counter<long> _llmCalls;
    private readonly Counter<long> _llmAvoided;
    private readonly Counter<long> _llmFailures;
    private readonly Histogram<double> _llmLatency;
    private readonly Counter<long> _startupWarmupRuns;
    private readonly Counter<long> _startupWarmupEvents;
    private readonly Histogram<double> _startupWarmupDuration;
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
    private readonly ConcurrentDictionary<string, PromptVariantAggregate> _promptVariants =
        new(StringComparer.OrdinalIgnoreCase);

    public TarotMetrics()
    {
        _readingRequests = _meter.CreateCounter<long>("tarot.readings.requests");
        _classifierAccepted = _meter.CreateCounter<long>("tarot.classifier.accepted");
        _classifierRejected = _meter.CreateCounter<long>("tarot.classifier.rejected");
        _classifierRemoteSucceeded = _meter.CreateCounter<long>("tarot.classifier.remote_succeeded");
        _classifierFallbackUsed = _meter.CreateCounter<long>("tarot.classifier.fallback_used");
        _classifierLatency = _meter.CreateHistogram<double>("tarot.classifier.duration", "ms");
        _classifierRequests = _meter.CreateCounter<long>("tarot.classifier.requests");
        _classifierIntentDistribution = _meter.CreateCounter<long>("tarot.classifier.intent_distribution");
        _classifierConfidence = _meter.CreateHistogram<double>("tarot.classifier.confidence");
        _classifierInvalidResponses = _meter.CreateCounter<long>("tarot.classifier.invalid_response");
        _cacheHits = _meter.CreateCounter<long>("tarot.cache.hits");
        _cacheMisses = _meter.CreateCounter<long>("tarot.cache.misses");
        _cacheSkipped = _meter.CreateCounter<long>("tarot.cache.skipped");
        _cacheStores = _meter.CreateCounter<long>("tarot.cache.store");
        _cachePreflightRequests = _meter.CreateCounter<long>("tarot.cache.preflight.requests");
        _cachePreflightEligible = _meter.CreateCounter<long>("tarot.cache.preflight.eligible");
        _cachePreflightRejected = _meter.CreateCounter<long>("tarot.cache.preflight.rejected");
        _llmCalls = _meter.CreateCounter<long>("tarot.llm.calls");
        _llmAvoided = _meter.CreateCounter<long>("tarot.llm.avoided");
        _llmFailures = _meter.CreateCounter<long>("tarot.llm.failures");
        _llmLatency = _meter.CreateHistogram<double>("tarot.llm.duration", "ms");
        _startupWarmupRuns = _meter.CreateCounter<long>("tarot.startup_warmup.runs");
        _startupWarmupEvents = _meter.CreateCounter<long>("tarot.startup_warmup.events");
        _startupWarmupDuration = _meter.CreateHistogram<double>("tarot.startup_warmup.duration", "ms");
    }

    public void ReadingRequested() { _readingRequests.Add(1); Interlocked.Increment(ref _readingRequestCount); }
    public void ClassifierAccepted() { _classifierAccepted.Add(1); Interlocked.Increment(ref _classifierAcceptedCount); }
    public void ClassifierRejected() { _classifierRejected.Add(1); Interlocked.Increment(ref _classifierRejectedCount); }
    public void ClassifierRemoteSucceeded() { _classifierRemoteSucceeded.Add(1); Interlocked.Increment(ref _classifierRemoteSucceededCount); }
    public void ClassifierFallbackUsed() { _classifierFallbackUsed.Add(1); Interlocked.Increment(ref _classifierFallbackUsedCount); }
    public void ClassifierObserved(ClassificationResult result)
    {
        _classifierRequests.Add(1);
        _classifierConfidence.Record(result.Confidence);
        _classifierIntentDistribution.Add(1,
            new KeyValuePair<string, object?>("intent", result.Intent),
            new KeyValuePair<string, object?>("domain", result.Domain.ToString()));
    }
    public void ClassifierInvalidResponse() => _classifierInvalidResponses.Add(1);
    public void CacheHit(Domain.ReadingMode mode) { _cacheHits.Add(1, new KeyValuePair<string, object?>("reading_mode", mode.ToString())); Interlocked.Increment(ref _cacheHitCount); }
    public void CacheMiss(Domain.ReadingMode mode) { _cacheMisses.Add(1, new KeyValuePair<string, object?>("reading_mode", mode.ToString())); Interlocked.Increment(ref _cacheMissCount); }
    public void CacheSkipped() { _cacheSkipped.Add(1); Interlocked.Increment(ref _cacheSkippedCount); }
    public void CacheStored(Domain.ReadingMode mode, bool success) =>
        _cacheStores.Add(1,
            new KeyValuePair<string, object?>("reading_mode", mode.ToString()),
            new KeyValuePair<string, object?>("result", success ? "success" : "failure"));
    public void CachePreflightRequested(Domain.ReadingMode mode) =>
        _cachePreflightRequests.Add(1, new KeyValuePair<string, object?>("reading_mode", mode.ToString()));
    public void CachePreflightEligible(Domain.ReadingMode mode) =>
        _cachePreflightEligible.Add(1, new KeyValuePair<string, object?>("reading_mode", mode.ToString()));
    public void CachePreflightRejected(Domain.ReadingMode mode, string reason) =>
        _cachePreflightRejected.Add(
            1,
            new KeyValuePair<string, object?>("reading_mode", mode.ToString()),
            new KeyValuePair<string, object?>("reason", reason));
    public void LlmCalled() { _llmCalls.Add(1); Interlocked.Increment(ref _llmCallCount); }
    public void LlmAvoided() { _llmAvoided.Add(1); Interlocked.Increment(ref _llmAvoidedCount); }
    public void LlmFailed() { _llmFailures.Add(1); Interlocked.Increment(ref _llmFailureCount); }
    public void StartupWarmupRun() => _startupWarmupRuns.Add(1);
    public void StartupWarmupEvent(string eventName, long count = 1) =>
        _startupWarmupEvents.Add(count, new KeyValuePair<string, object?>("event", eventName));
    public void RecordStartupWarmupDuration(TimeSpan duration) =>
        _startupWarmupDuration.Record(duration.TotalMilliseconds);

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

    public void PromptVariantSucceeded(string? experimentId, string variantId, double qualityScore)
    {
        var key = $"{experimentId ?? "NONE"}|{variantId}";
        _promptVariants.GetOrAdd(key, _ => new PromptVariantAggregate(experimentId, variantId))
            .Success(qualityScore);
    }

    public void PromptVariantFailed(string? experimentId, string variantId)
    {
        var key = $"{experimentId ?? "NONE"}|{variantId}";
        _promptVariants.GetOrAdd(key, _ => new PromptVariantAggregate(experimentId, variantId))
            .Failure();
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
            durationCount == 0 ? 0 : (double)durationTotal / durationCount,
            _promptVariants.Values
                .Select(aggregate => aggregate.Snapshot())
                .OrderBy(item => item.ExperimentId)
                .ThenBy(item => item.VariantId)
                .ToArray());
    }

    public void Dispose() => _meter.Dispose();

    private sealed class PromptVariantAggregate(string? experimentId, string variantId)
    {
        private long _successes;
        private long _failures;
        private long _qualityScoreTenThousands;
        public void Success(double score)
        {
            Interlocked.Increment(ref _successes);
            Interlocked.Add(ref _qualityScoreTenThousands, (long)(Math.Clamp(score, 0, 1) * 10_000));
        }
        public void Failure() => Interlocked.Increment(ref _failures);
        public PromptVariantMetricSnapshot Snapshot()
        {
            var successes = Interlocked.Read(ref _successes);
            var totalScore = Interlocked.Read(ref _qualityScoreTenThousands);
            return new PromptVariantMetricSnapshot(
                experimentId,
                variantId,
                successes,
                Interlocked.Read(ref _failures),
                successes == 0 ? 0 : Math.Round((double)totalScore / successes / 10_000, 4));
        }
    }
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
    double AverageLlmLatencyMs,
    IReadOnlyList<PromptVariantMetricSnapshot> PromptVariants);

public sealed record PromptVariantMetricSnapshot(
    string? ExperimentId,
    string VariantId,
    long Successes,
    long Failures,
    double AverageQualityScore);
