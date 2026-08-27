using System.Collections.Concurrent;
using System.Threading.Channels;
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
    ILogger<CacheWarmupService> logger) : BackgroundService, ICacheWarmupService
{
    private readonly Channel<(Guid Id, CacheWarmupRequestDto Request)> _queue =
        Channel.CreateBounded<(Guid, CacheWarmupRequestDto)>(new BoundedChannelOptions(20)
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

        var total = CountCombinations(request.Locale);
        if (request.Offset >= total)
        {
            throw new ArgumentException($"Warmup offset must be less than {total}.");
        }

        var id = Guid.NewGuid();
        var requested = Math.Min(request.MaxCombinations, total - request.Offset);
        var state = new WarmupState(id, request.Offset, requested, total);
        if (!_jobs.TryAdd(id, state) || !_queue.Writer.TryWrite((id, request)))
        {
            _jobs.TryRemove(id, out _);
            throw new InvalidOperationException("The cache warmup queue is full.");
        }

        return state.Snapshot();
    }

    public CacheWarmupJobDto? Get(Guid id) =>
        _jobs.TryGetValue(id, out var state) ? state.Snapshot() : null;

    public IReadOnlyList<CacheWarmupJobDto> List() =>
        _jobs.Values
            .OrderByDescending(job => job.CreatedAt)
            .Take(50)
            .Select(job => job.Snapshot())
            .ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunAsync(item.Id, item.Request, stoppingToken);
        }
    }

    private async Task RunAsync(Guid id, CacheWarmupRequestDto request, CancellationToken cancellationToken)
    {
        var state = _jobs[id];
        state.Start();
        try
        {
            var combinations = BuildCombinations(request.Locale)
                .Skip(request.Offset)
                .Take(request.MaxCombinations);
            foreach (var combination in combinations)
            {
                try
                {
                    for (var variant = 1; variant <= request.Variants; variant++)
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetRequiredService<ITarotReadingService>();
                        var reading = new TarotReadingDto(
                            $"topic:{combination.Intent}",
                            SpreadIds.Daily1,
                            combination.Locale,
                            [new SelectedCard("GUIDANCE", combination.CardId, combination.Orientation)],
                            request.ReadingMode,
                            request.ModelTier,
                            variant);
                        await service.GenerateAsync(reading, cancellationToken);
                        state.VariantGenerated();
                    }

                    state.CombinationCompleted();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    state.CombinationFailed(exception.Message);
                    logger.LogWarning(exception, "Cache warmup job {JobId} failed one combination", id);
                }
            }

            state.Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.Cancel();
        }
    }

    private IEnumerable<WarmupCombination> BuildCombinations(string? requestedLocale)
    {
        var locales = requestedLocale is null
            ? new[] { LocaleIds.English, LocaleIds.Thai }
            : new[] { requestedLocale.ToLowerInvariant() };
        var intents = TarotIntents.All.Where(intent => intent != TarotIntents.PersonalCustom);
        foreach (var locale in locales)
        foreach (var intent in intents)
        foreach (var card in catalog.AllCards)
        foreach (var orientation in Enum.GetValues<Orientation>())
        {
            yield return new WarmupCombination(locale, intent, card.Id, orientation);
        }
    }

    private int CountCombinations(string? locale) =>
        (locale is null ? 2 : 1) *
        TarotIntents.All.Count(intent => intent != TarotIntents.PersonalCustom) *
        catalog.AllCards.Count *
        Enum.GetValues<Orientation>().Length;

    private sealed record WarmupCombination(string Locale, string Intent, string CardId, Orientation Orientation);

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
                return new CacheWarmupJobDto(
                    id, _status, offset, requested, total, _completed, _variants,
                    _failed, _lastError, CreatedAt, _completedAt);
            }
        }
    }
}
