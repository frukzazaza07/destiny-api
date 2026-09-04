using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed record PromptVariantSelection(
    string? ExperimentId,
    string VariantId,
    string? Version,
    string AdditionalSystemInstruction)
{
    public static PromptVariantSelection Control { get; } = new(
        null,
        "CONTROL",
        null,
        string.Empty);

    public string? CacheDiscriminator => ExperimentId is null
        ? null
        : $"{ExperimentId}:{Version ?? VariantId}";
}

public sealed record LlmWorkerDefinition(
    string Id,
    Uri Endpoint,
    string Model,
    LlmWorkerProvider Provider,
    int Priority,
    int MaxConcurrency,
    int TimeoutSeconds,
    string? ApiKey,
    string ApiKeyHeader);

public sealed record InferencePlan(
    string TierId,
    string CacheModelVersion,
    PromptVariantSelection PromptVariant,
    IReadOnlyList<LlmWorkerDefinition> Workers);

public interface IInferenceRouter
{
    InferencePlan Resolve(
        TarotReadingDto request,
        ClassificationResult classification,
        string? requestedTier = null,
        string? requestedPromptVariant = null);
}

public sealed class InferenceRouter(IOptions<LlmOptions> options) : IInferenceRouter
{
    private readonly LlmOptions _options = options.Value;

    public InferencePlan Resolve(
        TarotReadingDto request,
        ClassificationResult classification,
        string? requestedTier = null,
        string? requestedPromptVariant = null)
    {
        requestedTier ??= ReadOptionalRequestValue(request, "ModelTier");
        requestedPromptVariant ??= ReadOptionalRequestValue(request, "PromptVariant");

        var tier = ResolveTier(requestedTier);
        var promptVariant = ResolvePromptVariant(
            request,
            classification,
            requestedPromptVariant);

        return new InferencePlan(
            tier.Id,
            tier.CacheModelVersion,
            promptVariant,
            tier.Workers);
    }

    public static string BuildPromptAssignmentKey(
        TarotReadingDto request,
        ClassificationResult classification)
    {
        var parts = new List<string>
        {
            classification.Domain.ToString(),
            classification.Intent.ToUpperInvariant(),
            request.Spread.ToUpperInvariant(),
            request.Locale.ToUpperInvariant()
        };
        parts.AddRange(request.Cards.Select(card =>
            $"{card.Position.ToUpperInvariant()}:{card.CardId.ToUpperInvariant()}:{card.Orientation}"));
        return string.Join("|", parts);
    }

    private ResolvedTier ResolveTier(string? requestedTier)
    {
        var configuredTiers = _options.Tiers ?? [];
        if (configuredTiers.Count == 0)
        {
            var legacyWorkers = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? Array.Empty<LlmWorkerDefinition>()
                :
                [
                    BuildWorker(
                        new LlmWorkerOptions
                        {
                            Id = "legacy",
                            Endpoint = _options.Endpoint,
                            Model = _options.Model,
                            MaxConcurrency = _options.MaxConcurrency
                        },
                        "DEEP",
                        _options.Model,
                        0)
                ];

            return new ResolvedTier(
                "DEEP",
                _options.Model,
                legacyWorkers);
        }

        var selectedId = string.IsNullOrWhiteSpace(requestedTier)
            ? _options.DefaultTier
            : requestedTier;
        var selected = configuredTiers.SingleOrDefault(tier =>
            string.Equals(tier.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            throw new ArgumentException(
                $"Unknown LLM model tier '{selectedId}'.",
                nameof(requestedTier));
        }

        if (string.IsNullOrWhiteSpace(selected.Id))
        {
            throw new InvalidOperationException("Configured LLM tiers require a non-empty id.");
        }

        var tierModel = FirstNonEmpty(selected.Model, _options.Model);
        var cacheModelVersion = FirstNonEmpty(selected.CacheModelVersion, tierModel);
        if (string.IsNullOrWhiteSpace(cacheModelVersion))
        {
            throw new InvalidOperationException(
                $"LLM tier '{selected.Id}' requires a cache model version or model.");
        }

        var configuredWorkers = selected.Workers ?? [];
        if (configuredWorkers.Count == 0 && !string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            configuredWorkers =
            [
                new LlmWorkerOptions
                {
                    Id = $"{selected.Id}-default",
                    Endpoint = _options.Endpoint,
                    Model = tierModel,
                    MaxConcurrency = _options.MaxConcurrency
                }
            ];
        }

        var workers = configuredWorkers
            .Where(worker => worker.Enabled)
            .Select((worker, index) => BuildWorker(worker, selected.Id, tierModel, index))
            .ToArray();
        var duplicateWorker = workers
            .GroupBy(worker => worker.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateWorker is not null)
        {
            throw new InvalidOperationException(
                $"LLM tier '{selected.Id}' has duplicate worker id '{duplicateWorker.Key}'.");
        }

        return new ResolvedTier(selected.Id.ToUpperInvariant(), cacheModelVersion, workers);
    }

    private PromptVariantSelection ResolvePromptVariant(
        TarotReadingDto request,
        ClassificationResult classification,
        string? requestedPromptVariant)
    {
        var experiment = _options.PromptExperiment ?? new PromptExperimentOptions();
        if (!experiment.Enabled)
        {
            return PromptVariantSelection.Control;
        }

        if (string.IsNullOrWhiteSpace(experiment.Id))
        {
            throw new InvalidOperationException("An enabled prompt experiment requires an id.");
        }

        var variants = (experiment.Variants ?? [])
            .Where(variant => variant.Weight > 0)
            .ToArray();
        if (variants.Length == 0)
        {
            throw new InvalidOperationException(
                $"Prompt experiment '{experiment.Id}' requires at least one positive-weight variant.");
        }

        var duplicateVariant = variants
            .GroupBy(variant => variant.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateVariant is not null)
        {
            throw new InvalidOperationException(
                $"Prompt experiment '{experiment.Id}' has a missing or duplicate variant id.");
        }

        PromptVariantOptions selected;
        if (!string.IsNullOrWhiteSpace(requestedPromptVariant))
        {
            selected = variants.SingleOrDefault(variant =>
                    string.Equals(variant.Id, requestedPromptVariant, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"Unknown prompt variant '{requestedPromptVariant}'.",
                    nameof(requestedPromptVariant));
        }
        else
        {
            var totalWeight = variants.Sum(variant => (long)variant.Weight);
            var assignmentKey = BuildPromptAssignmentKey(request, classification);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(assignmentKey));
            var bucket = (long)(BinaryPrimitives.ReadUInt64BigEndian(hash) % (ulong)totalWeight);
            selected = variants[^1];
            long upperBound = 0;
            foreach (var variant in variants)
            {
                upperBound += variant.Weight;
                if (bucket < upperBound)
                {
                    selected = variant;
                    break;
                }
            }
        }

        return new PromptVariantSelection(
            experiment.Id,
            selected.Id,
            string.IsNullOrWhiteSpace(selected.Version) ? selected.Id : selected.Version,
            selected.AdditionalSystemInstruction?.Trim() ?? string.Empty);
    }

    private LlmWorkerDefinition BuildWorker(
        LlmWorkerOptions worker,
        string tierId,
        string tierModel,
        int index)
    {
        var endpoint = FirstNonEmpty(worker.Endpoint, _options.Endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException(
                $"LLM worker '{WorkerId(worker, tierId, index)}' requires an absolute endpoint URI.");
        }

        var model = FirstNonEmpty(worker.Model, tierModel, _options.Model);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"LLM worker '{WorkerId(worker, tierId, index)}' requires a model.");
        }

        if (worker.MaxConcurrency <= 0)
        {
            throw new InvalidOperationException(
                $"LLM worker '{WorkerId(worker, tierId, index)}' requires positive concurrency.");
        }

        return new LlmWorkerDefinition(
            WorkerId(worker, tierId, index),
            endpointUri,
            model,
            worker.Provider,
            worker.Priority,
            worker.MaxConcurrency,
            Math.Max(1, worker.TimeoutSeconds ?? _options.TimeoutSeconds),
            worker.ApiKey,
            string.IsNullOrWhiteSpace(worker.ApiKeyHeader)
                ? "Authorization"
                : worker.ApiKeyHeader);
    }

    private static string WorkerId(LlmWorkerOptions worker, string tierId, int index) =>
        string.IsNullOrWhiteSpace(worker.Id)
            ? $"{tierId}-worker-{index + 1}"
            : worker.Id;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string? ReadOptionalRequestValue(object request, string propertyName)
    {
        var value = request.GetType().GetProperty(propertyName)?.GetValue(request);
        return value switch
        {
            null => null,
            string text => text,
            _ => value.ToString()
        };
    }

    private sealed record ResolvedTier(
        string Id,
        string CacheModelVersion,
        IReadOnlyList<LlmWorkerDefinition> Workers);
}

internal sealed class InferenceWorkerPool(
    int failureThreshold,
    TimeSpan circuitCooldown)
{
    private readonly ConcurrentDictionary<string, WorkerRuntime> _workers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _failureThreshold = Math.Max(1, failureThreshold);
    private readonly TimeSpan _circuitCooldown = circuitCooldown <= TimeSpan.Zero
        ? TimeSpan.FromSeconds(1)
        : circuitCooldown;
    private long _selectionSequence;

    public IReadOnlyList<LlmWorkerDefinition> OrderCandidates(
        string tierId,
        IReadOnlyList<LlmWorkerDefinition> workers,
        bool bypassLocalWorkers,
        bool enableCloudFallback,
        bool allowCloudForRequest)
    {
        var now = DateTimeOffset.UtcNow;
        var sequence = Interlocked.Increment(ref _selectionSequence);
        var available = workers
            .Where(worker => !bypassLocalWorkers
                || worker.Provider != LlmWorkerProvider.LOCAL_GPU)
            .Where(worker => worker.Provider != LlmWorkerProvider.CLOUD_GPU
                || (enableCloudFallback && allowCloudForRequest))
            .Select(worker => (Worker: worker, Runtime: GetRuntime(tierId, worker)))
            .Where(candidate => !candidate.Runtime.IsCircuitOpen(now))
            .GroupBy(candidate => new
            {
                Provider = candidate.Worker.Provider == LlmWorkerProvider.CLOUD_GPU ? 1 : 0,
                candidate.Worker.Priority
            })
            .OrderBy(group => group.Key.Provider)
            .ThenBy(group => group.Key.Priority)
            .SelectMany(group => RotateLeastLoaded(group.ToArray(), sequence))
            .Select(candidate => candidate.Worker)
            .ToArray();

        return available;
    }

    public async ValueTask<InferenceWorkerLease> AcquireAsync(
        string tierId,
        LlmWorkerDefinition worker,
        CancellationToken cancellationToken)
    {
        var runtime = GetRuntime(tierId, worker);
        await runtime.Concurrency.WaitAsync(cancellationToken);
        Interlocked.Increment(ref runtime.InFlight);
        return new InferenceWorkerLease(runtime);
    }

    public void MarkSuccess(string tierId, LlmWorkerDefinition worker) =>
        GetRuntime(tierId, worker).MarkSuccess();

    public void MarkFailure(string tierId, LlmWorkerDefinition worker) =>
        GetRuntime(tierId, worker).MarkFailure(_failureThreshold, _circuitCooldown);

    private WorkerRuntime GetRuntime(string tierId, LlmWorkerDefinition worker)
    {
        var key = $"{tierId}|{worker.Id}|{worker.Endpoint}|{worker.Model}";
        return _workers.GetOrAdd(key, _ => new WorkerRuntime(worker.MaxConcurrency));
    }

    private static IEnumerable<(LlmWorkerDefinition Worker, WorkerRuntime Runtime)> RotateLeastLoaded(
        IReadOnlyList<(LlmWorkerDefinition Worker, WorkerRuntime Runtime)> candidates,
        long sequence)
    {
        var remaining = candidates.ToList();
        while (remaining.Count > 0)
        {
            var minimumLoad = remaining.Min(candidate => candidate.Runtime.Load);
            var equallyLoaded = remaining
                .Where(candidate => Math.Abs(candidate.Runtime.Load - minimumLoad) < 0.000001)
                .OrderBy(candidate => candidate.Worker.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var start = (int)((ulong)sequence % (ulong)equallyLoaded.Length);
            for (var index = 0; index < equallyLoaded.Length; index++)
            {
                var candidate = equallyLoaded[(start + index) % equallyLoaded.Length];
                yield return candidate;
                remaining.Remove(candidate);
            }
        }
    }

    internal sealed class WorkerRuntime(int maximumConcurrency)
    {
        private int _consecutiveFailures;
        private long _circuitOpenUntilUtcTicks;

        public SemaphoreSlim Concurrency { get; } = new(maximumConcurrency, maximumConcurrency);
        public int InFlight;
        public double Load => (double)Volatile.Read(ref InFlight) / maximumConcurrency;

        public bool IsCircuitOpen(DateTimeOffset now) =>
            Volatile.Read(ref _circuitOpenUntilUtcTicks) > now.UtcTicks;

        public void MarkSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _circuitOpenUntilUtcTicks, 0);
        }

        public void MarkFailure(int threshold, TimeSpan cooldown)
        {
            if (Interlocked.Increment(ref _consecutiveFailures) < threshold)
            {
                return;
            }

            Interlocked.Exchange(
                ref _circuitOpenUntilUtcTicks,
                DateTimeOffset.UtcNow.Add(cooldown).UtcTicks);
        }
    }
}

internal sealed class InferenceWorkerLease(
    InferenceWorkerPool.WorkerRuntime runtime) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Decrement(ref runtime.InFlight);
        runtime.Concurrency.Release();
    }
}
