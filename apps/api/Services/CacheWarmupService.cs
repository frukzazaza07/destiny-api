using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface ICacheWarmupService
{
    CacheWarmupJobDto Enqueue(CacheWarmupRequestDto request);
    CacheWarmupJobDto? Get(Guid id);
    IReadOnlyList<CacheWarmupJobDto> List();
}

public sealed class CacheWarmupService(
    IServiceScopeFactory scopeFactory,
    ITarotCatalog catalog,
    ICacheLock cacheLock,
    IAnswerCache answerCache,
    IHostApplicationLifetime applicationLifetime,
    IOptions<StartupCacheWarmupOptions> startupOptions,
    IOptions<DeepSharedCacheOptions> deepCacheOptions,
    IOptions<TarotCacheOptions> cacheOptions,
    TarotMetrics metrics,
    ILogger<CacheWarmupService> logger) : BackgroundService, ICacheWarmupService
{
    private readonly StartupCacheWarmupOptions _startup = startupOptions.Value;
    private readonly DeepSharedCacheOptions _deepCache = deepCacheOptions.Value;
    private readonly TarotCacheOptions _cacheOptions = cacheOptions.Value;
    private readonly Channel<WarmupWorkItem> _queue =
        Channel.CreateBounded<WarmupWorkItem>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly ConcurrentDictionary<Guid, WarmupState> _jobs = new();

    public CacheWarmupJobDto Enqueue(CacheWarmupRequestDto request)
    {
        if (request.Locale is not null && !LocaleIds.IsSupported(request.Locale))
        {
            throw new ArgumentException("Warmup locale must be en or th.");
        }

        var work = new WarmupWorkItem(
            Guid.NewGuid(), request, null,
            request.Locale is null ? [LocaleIds.English, LocaleIds.Thai] : [request.Locale],
            [SpreadIds.Daily1], null, 1, 0, false);
        AddJob(work);
        if (!_queue.Writer.TryWrite(work))
        {
            _jobs.TryRemove(work.Id, out _);
            throw new InvalidOperationException("The cache warmup queue is full.");
        }

        return _jobs[work.Id].Snapshot();
    }

    public CacheWarmupJobDto? Get(Guid id) =>
        _jobs.TryGetValue(id, out var state) ? state.Snapshot() : null;

    public IReadOnlyList<CacheWarmupJobDto> List() =>
        _jobs.Values.OrderByDescending(job => job.CreatedAt).Take(50)
            .Select(job => job.Snapshot()).ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueTask = ProcessQueueAsync(stoppingToken);
        try
        {
            await RunStartupWarmupAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            metrics.StartupWarmupEvent("cancelled");
            logger.LogInformation("Startup cache warmup was cancelled");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Startup cache warmup failed without affecting API availability");
        }

        await queueTask;
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            await RunAsync(item, cancellationToken);
        }
    }

    private async Task RunStartupWarmupAsync(CancellationToken cancellationToken)
    {
        if (!_startup.Enabled) return;

        metrics.StartupWarmupRun();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            await WaitForApplicationStartedAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(_startup.DelaySeconds), cancellationToken);

            var lockIdentity = string.Join('|', "startup-warmup", _cacheOptions.CacheVersion,
                _cacheOptions.TaxonomyVersion, _cacheOptions.PromptVersion,
                _cacheOptions.InterpretationVersion);
            await using var lease = await cacheLock.TryAcquireAsync(lockIdentity, cancellationToken);
            if (lease is null)
            {
                metrics.StartupWarmupEvent("lock_contended");
                logger.LogInformation("Another API replica owns startup cache warmup for {CacheVersion}", _cacheOptions.CacheVersion);
                return;
            }
            metrics.StartupWarmupEvent("lock_acquired");

            if (_startup.RehydrateRedisFromPostgres)
            {
                await RehydrateRedisAsync(cancellationToken);
            }

            var remaining = _startup.MaxCombinationsPerStartup;
            foreach (var modeText in _startup.ReadingModes)
            {
                if (remaining <= 0 || !Enum.TryParse<ReadingMode>(modeText, true, out var mode)) continue;
                if (mode == ReadingMode.DEEP && (!_deepCache.Enabled || !_deepCache.WriteEnabled)) continue;
                var modeLimit = mode == ReadingMode.DEEP
                    ? Math.Min(remaining, _startup.MaxDeepGenerationsPerStartup)
                    : remaining;
                if (modeLimit <= 0) continue;

                var request = new CacheWarmupRequestDto(mode, _startup.Variants, modeLimit, 0, null, null);
                IReadOnlyList<string> approvedIntents = mode == ReadingMode.DEEP
                    ? _startup.ApprovedIntents.Intersect(_deepCache.ApprovedIntents, StringComparer.OrdinalIgnoreCase).ToArray()
                    : _startup.ApprovedIntents;
                var work = new WarmupWorkItem(
                    Guid.NewGuid(), request, approvedIntents, _startup.Locales,
                _startup.Spreads, _startup.CanonicalDeepQuestions,
                    _startup.MaxConcurrency, _startup.RetryCount, true);
                AddJob(work);
                await RunAsync(work, cancellationToken);
                remaining -= _jobs[work.Id].Requested;
            }
        }
        finally
        {
            metrics.RecordStartupWarmupDuration(DateTimeOffset.UtcNow - startedAt);
        }
    }

    private async Task RehydrateRedisAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IGeneratedAnswerStore>();
            var entries = await store.LoadCurrentEntriesAsync(
                _cacheOptions.CacheVersion, _cacheOptions.PromptVersion,
                _cacheOptions.InterpretationVersion,
                _startup.MaxCombinationsPerStartup, cancellationToken);
            var approvedEntries = entries.Where(IsApprovedPersistentEntry).ToArray();
            foreach (var entry in approvedEntries)
            {
                metrics.StartupWarmupEvent("entries_examined");
                var key = $"tarot:answer:{entry.CacheHash}";
                if (await answerCache.GetAsync(key, cancellationToken) is not null)
                {
                    metrics.StartupWarmupEvent("entries_already_present");
                    continue;
                }
                await answerCache.SetAsync(key, entry.Answers,
                    TimeSpan.FromDays(_cacheOptions.AnswerTtlDays), cancellationToken);
                metrics.StartupWarmupEvent("redis_rehydrated");
            }

            logger.LogInformation("Startup cache warmup examined {Count} approved persistent entries", approvedEntries.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            metrics.StartupWarmupEvent("failures");
            logger.LogWarning(exception, "Persistent cache rehydration failed; startup generation may continue");
        }
    }

    private bool IsApprovedPersistentEntry(PersistedAnswerCacheEntry entry) =>
        _startup.ApprovedIntents.Contains(entry.Intent, StringComparer.OrdinalIgnoreCase) &&
        _startup.Locales.Contains(entry.Locale, StringComparer.OrdinalIgnoreCase) &&
        _startup.ReadingModes.Contains(entry.ReadingMode, StringComparer.OrdinalIgnoreCase) &&
        _startup.Spreads.Contains(entry.SpreadId, StringComparer.OrdinalIgnoreCase);

    private void AddJob(WarmupWorkItem work)
    {
        var combinations = BuildCombinations(work).Count;
        if (work.Request.Offset >= combinations && combinations > 0)
        {
            throw new ArgumentException($"Warmup offset must be less than {combinations}.");
        }

        var requested = Math.Min(work.Request.MaxCombinations,
            Math.Max(0, combinations - work.Request.Offset));
        if (!_jobs.TryAdd(work.Id,
                new WarmupState(work.Id, work.Request.Offset, requested, combinations)))
        {
            throw new InvalidOperationException("Could not create the cache warmup job.");
        }
    }

    private async Task RunAsync(WarmupWorkItem work, CancellationToken cancellationToken)
    {
        var state = _jobs[work.Id];
        state.Start();
        try
        {
            var combinations = BuildCombinations(work).Skip(work.Request.Offset)
                .Take(work.Request.MaxCombinations).ToArray();
            await Parallel.ForEachAsync(combinations, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, work.MaxConcurrency)
            }, async (combination, token) =>
                await RunCombinationAsync(work, state, combination, token));
            state.Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.Cancel();
        }
    }

    private async Task RunCombinationAsync(
        WarmupWorkItem work, WarmupState state,
        WarmupCombination combination, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= work.RetryCount; attempt++)
        {
            try
            {
                for (var variant = 1; variant <= work.Request.Variants; variant++)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<ITarotReadingService>();
                    var response = await service.GenerateAsync(new TarotReadingDto(
                        combination.Question, combination.Spread, combination.Locale,
                        combination.Cards, work.Request.ReadingMode,
                        work.Request.ModelTier, variant), cancellationToken);
                    if (work.IsStartup)
                    {
                        metrics.StartupWarmupEvent(response.CacheStatus == CacheStatus.MISS
                            ? "entries_generated"
                            : response.CacheStatus == CacheStatus.SKIPPED && work.Request.ReadingMode == ReadingMode.DEEP
                                ? "entries_rejected_safety"
                                : "entries_already_present");
                    }
                    state.VariantGenerated();
                }

                state.CombinationCompleted();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < work.RetryCount)
            {
                logger.LogWarning(exception, "Cache warmup job {JobId} will retry one combination", work.Id);
                await Task.Delay(TimeSpan.FromSeconds(_startup.RetryDelaySeconds), cancellationToken);
            }
            catch (Exception exception)
            {
                if (work.IsStartup) metrics.StartupWarmupEvent("failures");
                state.CombinationFailed(exception.Message);
                logger.LogWarning(exception, "Cache warmup job {JobId} failed one combination", work.Id);
                return;
            }
        }
    }

    private IReadOnlyList<WarmupCombination> BuildCombinations(WarmupWorkItem work)
    {
        var locales = work.Locales.Where(LocaleIds.IsSupported)
            .Select(locale => locale.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase);
        var intents = (work.ApprovedIntents is null
                ? TarotIntents.All.Where(intent => intent != TarotIntents.PersonalCustom)
                : work.ApprovedIntents)
            .Where(intent => TarotIntents.All.Contains(intent, StringComparer.OrdinalIgnoreCase))
            .Where(intent => intent != TarotIntents.PersonalCustom)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var spreads = work.Spreads.Where(spread => SpreadIds.TryNormalize(spread, out _))
            .Select(SpreadIds.Normalize).Distinct(StringComparer.OrdinalIgnoreCase);
        var results = new List<WarmupCombination>();

        foreach (var locale in locales)
        foreach (var intent in intents)
        foreach (var spread in spreads)
        {
            var question = ResolveQuestion(work, locale, intent);
            if (question is null) continue;

            if (spread == SpreadIds.Daily1)
            {
                foreach (var card in catalog.AllCards)
                foreach (var orientation in Enum.GetValues<Orientation>())
                {
                    results.Add(new WarmupCombination(locale, spread, question,
                        [new SelectedCard("GUIDANCE", card.Id, orientation)]));
                }
                continue;
            }

            for (var index = 0; index < catalog.AllCards.Count; index++)
            for (var mask = 0; mask < 8; mask++)
            {
                var cards = ReadingPositions.Destiny3.Select((position, cardIndex) =>
                    new SelectedCard(position,
                        catalog.AllCards[(index + cardIndex) % catalog.AllCards.Count].Id,
                        (mask & (1 << cardIndex)) == 0 ? Orientation.UPRIGHT : Orientation.REVERSED))
                    .ToArray();
                results.Add(new WarmupCombination(locale, spread, question, cards));
            }
        }

        return results;
    }

    private static string? ResolveQuestion(WarmupWorkItem work, string locale, string intent)
    {
        if (work.Request.ReadingMode == ReadingMode.STANDARD) return $"topic:{intent}";
        return work.CanonicalDeepQuestions is not null &&
               work.CanonicalDeepQuestions.TryGetValue(locale, out var localeQuestions) &&
               localeQuestions.TryGetValue(intent, out var question) &&
               !string.IsNullOrWhiteSpace(question)
            ? question : null;
    }

    private Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        if (applicationLifetime.ApplicationStarted.IsCancellationRequested) return Task.CompletedTask;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        applicationLifetime.ApplicationStarted.Register(() => started.TrySetResult());
        cancellationToken.Register(() => started.TrySetCanceled(cancellationToken));
        return started.Task;
    }

    private sealed record WarmupWorkItem(
        Guid Id, CacheWarmupRequestDto Request, IReadOnlyList<string>? ApprovedIntents,
        IReadOnlyList<string> Locales, IReadOnlyList<string> Spreads,
        IReadOnlyDictionary<string, Dictionary<string, string>>? CanonicalDeepQuestions,
        int MaxConcurrency, int RetryCount, bool IsStartup);

    private sealed record WarmupCombination(
        string Locale, string Spread, string Question, IReadOnlyList<SelectedCard> Cards);

    private sealed class WarmupState(Guid id, int offset, int requested, int total)
    {
        private readonly object _gate = new();
        private string _status = "QUEUED";
        private int _completed;
        private int _variants;
        private int _failed;
        private string? _lastError;
        private DateTimeOffset? _completedAt;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public int Requested => requested;
        public void Start() { lock (_gate) _status = "RUNNING"; }
        public void VariantGenerated() { lock (_gate) _variants++; }
        public void CombinationCompleted() { lock (_gate) _completed++; }
        public void CombinationFailed(string error) { lock (_gate) { _failed++; _lastError = error; } }
        public void Complete() { lock (_gate) { _status = "COMPLETED"; _completedAt = DateTimeOffset.UtcNow; } }
        public void Cancel() { lock (_gate) { _status = "CANCELLED"; _completedAt = DateTimeOffset.UtcNow; } }
        public CacheWarmupJobDto Snapshot()
        {
            lock (_gate)
            {
                return new CacheWarmupJobDto(id, _status, offset, requested, total,
                    _completed, _variants, _failed, _lastError, CreatedAt, _completedAt);
            }
        }
    }
}
