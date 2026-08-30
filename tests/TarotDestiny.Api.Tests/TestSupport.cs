using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

internal static class TestSupport
{
    public static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });

    public static TarotReadingDto DestinyRequest(
        string? question = "Should I change my job?",
        string locale = "th",
        ReadingMode readingMode = ReadingMode.STANDARD) =>
        new(
            question,
            SpreadIds.Destiny3,
            locale,
            [
                new("PAST", "THE_TOWER", Orientation.UPRIGHT),
                new("PRESENT", "THE_MAGICIAN", Orientation.UPRIGHT),
                new("DIRECTION", "THE_STAR", Orientation.UPRIGHT)
            ],
            readingMode);

    public static ClassificationResult CareerChangeClassification() =>
        new(TarotDomain.CAREER, "CAREER_CHANGE_JOB", 0.95, PersonalizationLevel.LOW);

    public static RuleQuestionClassifier NewClassifier(double threshold = 0.85) =>
        new(
            Options.Create(new ClassifierOptions { MinimumCacheConfidence = threshold }),
            LoggerFactory.CreateLogger<RuleQuestionClassifier>());

    public static TarotReadingResponse ValidResponse(
        TarotReadingDto request,
        ClassificationResult classification)
    {
        var catalog = new TarotCatalog();
        return new TarotReadingResponse(
            "A useful title",
            "A reflective summary.",
            "change and renewal",
            request.Cards.Select(card => new CardReading(
                card.Position,
                card.CardId,
                catalog.Get(card.CardId).Name,
                card.Orientation,
                $"Interpretation for {card.Position}.")).ToArray(),
            ["An opportunity"],
            ["A challenge"],
            ["Practical guidance"],
            "What matters now?",
            "Use this as reflection.",
            CacheStatus.MISS,
            classification,
            null,
            request.ReadingMode,
            request.ReadingMode == ReadingMode.DEEP ? GenerationSource.LLM : GenerationSource.RULE_ENGINE,
            request.ReadingMode == ReadingMode.DEEP ? "test-model" : null);
    }

    public static TarotReadingService NewReadingService(
        ILlmClient llmClient,
        IAnswerCache? cache = null,
        LlmOptions? llmOptions = null,
        IGeneratedAnswerStore? generatedAnswerStore = null,
        TarotCacheOptions? tarotCacheOptions = null,
        IQuestionClassifier? classifier = null)
    {
        classifier ??= NewClassifier();
        var cacheSettings = Options.Create(tarotCacheOptions ?? new TarotCacheOptions());
        var gateSettings = Options.Create(llmOptions ?? new LlmOptions());
        var catalog = new TarotCatalog();

        return new TarotReadingService(
            classifier,
            new CacheKeyBuilder(cacheSettings),
            cache ?? new InMemoryAnswerCache(),
            generatedAnswerStore ?? new NullGeneratedAnswerStore(),
            new RandomAnswerVariantSelector(),
            new InMemoryCacheLock(),
            new RuleInterpretationEngine(catalog),
            new RuleReadingRenderer(),
            llmClient,
            new LlmGate(gateSettings),
            new ReadingResponseValidator(),
            new TarotMetrics(),
            cacheSettings,
            LoggerFactory.CreateLogger<TarotReadingService>());
    }
}

internal sealed class RecordingGeneratedAnswerStore : IGeneratedAnswerStore
{
    private readonly object _sync = new();
    private readonly List<GeneratedAnswerWrite> _writes = [];

    public int CallCount
    {
        get
        {
            lock (_sync)
            {
                return _writes.Count;
            }
        }
    }

    public IReadOnlyList<GeneratedAnswerWrite> Writes
    {
        get
        {
            lock (_sync)
            {
                return [.. _writes];
            }
        }
    }

    public Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CachedAnswerSet?>(null);
    }

    public Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _writes.Add(answer);
        }

        return Task.FromResult(true);
    }

    public Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken) =>
        Task.FromResult(new PersistentCacheAnalytics(0, 0, 0, []));

    public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class ThrowingGeneratedAnswerStore(Exception? exception = null) : IGeneratedAnswerStore
{
    private readonly Exception _exception = exception ?? new InvalidOperationException("PostgreSQL is unavailable.");
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken) =>
        Task.FromResult<CachedAnswerSet?>(null);

    public Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        return Task.FromException<bool>(_exception);
    }

    public Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken) =>
        Task.FromException<PersistentCacheAnalytics>(_exception);

    public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.FromException(_exception);
        yield break;
    }
}

internal sealed class TrackingLlmClient : ILlmClient
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);
    public List<string?> ReceivedQuestions { get; } = [];

    public Task<TarotReadingResponse> GenerateAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        InterpretationPayload payload,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        lock (ReceivedQuestions)
        {
            ReceivedQuestions.Add(request.Question);
        }

        return Task.FromResult(TestSupport.ValidResponse(request, classification));
    }
}
