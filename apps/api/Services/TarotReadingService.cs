using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface ITarotReadingService
{
    Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken);
}

public sealed class TarotReadingService(
    IQuestionClassifier classifier,
    ICacheKeyBuilder cacheKeyBuilder,
    IAnswerCache cache,
    IGeneratedAnswerStore generatedAnswerStore,
    IAnswerVariantSelector variantSelector,
    ICacheLock cacheLock,
    IInterpretationEngine interpretationEngine,
    IRuleReadingRenderer ruleRenderer,
    ILlmClient llmClient,
    ILlmGate llmGate,
    IReadingResponseValidator validator,
    TarotMetrics metrics,
    IOptions<TarotCacheOptions> cacheOptions,
    ILogger<TarotReadingService> logger) : ITarotReadingService
{
    private readonly TarotCacheOptions _cacheOptions = cacheOptions.Value;
    private static readonly ClassificationResult DeepDirectClassification = new(
        TarotDomain.GENERAL,
        "UNCLASSIFIED",
        0,
        PersonalizationLevel.HIGH,
        ClassifierSources.DeepDirect,
        DecisionMethod: ClassifierDecisionMethods.ClassifierBypassed);

    public async Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken)
    {
        metrics.ReadingRequested();

        if (request.ReadingMode == ReadingMode.DEEP)
        {
            metrics.CacheSkipped();
            logger.LogInformation(
                "Generating direct deep reading without classifier or shared cache; raw question included");
            var direct = await GenerateResponseAsync(
                request,
                DeepDirectClassification,
                cancellationToken);
            return direct with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }

        var classification = await classifier.ClassifyAsync(
            request.Question,
            request.Locale,
            cancellationToken);
        var canUseSharedCache = classifier.CanUseSharedCache(classification);
        var cacheHash = canUseSharedCache ? cacheKeyBuilder.BuildHash(request, classification) : null;
        var cacheKey = canUseSharedCache ? cacheKeyBuilder.BuildRedisKey(request, classification) : null;

        if (canUseSharedCache)
        {
            metrics.ClassifierAccepted();
            logger.LogInformation("Classifier accepted {Domain}/{Intent} for shared cache", classification.Domain, classification.Intent);
        }
        else
        {
            metrics.ClassifierRejected();
            logger.LogInformation("Classifier rejected {Domain}/{Intent} for shared cache", classification.Domain, classification.Intent);
        }

        if (!canUseSharedCache)
        {
            metrics.CacheSkipped();
            logger.LogInformation("Skipping shared cache for {Domain}/{Intent}", classification.Domain, classification.Intent);
            var uncached = await GenerateResponseAsync(request, classification, cancellationToken);
            return uncached with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }

        var cached = await FindCachedAsync(cacheKey!, cacheHash!, cancellationToken);
        if (HasRequestedVariant(cached, request.AnswerVariant))
        {
            return await ReturnHitAsync(cached!, cacheHash!, cacheKey!, request, classification, cancellationToken);
        }

        await using var cacheLease = await cacheLock.TryAcquireAsync(cacheHash!, cancellationToken);
        cached = await FindCachedAsync(cacheKey!, cacheHash!, cancellationToken);
        if (HasRequestedVariant(cached, request.AnswerVariant))
        {
            return await ReturnHitAsync(cached!, cacheHash!, cacheKey!, request, classification, cancellationToken);
        }

        metrics.CacheMiss();
        logger.LogInformation("Tarot answer cache MISS for {CacheKey}", cacheKey);
        var generated = await GenerateResponseAsync(request, classification, cancellationToken);
        var toCache = generated with { CacheStatus = CacheStatus.MISS, CacheKey = cacheKey };
        await PersistGeneratedAnswerAsync(
            cacheHash!,
            request,
            classification,
            toCache,
            cancellationToken);
        CachedAnswerSet answers;
        try
        {
            answers = await generatedAnswerStore.FindAsync(cacheHash!, cancellationToken)
                ?? MergeVariant(cached, request.AnswerVariant, toCache);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not reload generated variants for {CacheHash}", cacheHash);
            answers = MergeVariant(cached, request.AnswerVariant, toCache);
        }
        await cache.SetAsync(
            cacheKey!,
            answers,
            TimeSpan.FromDays(_cacheOptions.AnswerTtlDays),
            cancellationToken);
        return toCache;
    }

    private async Task<CachedAnswerSet?> FindCachedAsync(
        string cacheKey,
        string cacheHash,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var persisted = await generatedAnswerStore.FindAsync(cacheHash, cancellationToken);
            if (persisted is null)
            {
                return null;
            }

            await cache.SetAsync(
                cacheKey,
                persisted,
                TimeSpan.FromDays(_cacheOptions.AnswerTtlDays),
                cancellationToken);
            logger.LogInformation("Repopulated Redis from PostgreSQL for {CacheKey}", cacheKey);
            return persisted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "PostgreSQL lookup failed for {CacheHash}; continuing without persistent cache",
                cacheHash);
            return null;
        }
    }

    private async Task<TarotReadingResponse> ReturnHitAsync(
        CachedAnswerSet cached,
        string cacheHash,
        string cacheKey,
        TarotReadingDto request,
        ClassificationResult classification,
        CancellationToken cancellationToken)
    {
        metrics.CacheHit();
        if (request.ReadingMode == ReadingMode.DEEP)
        {
            metrics.LlmAvoided();
        }

        try
        {
            await generatedAnswerStore.IncrementHitCountAsync(cacheHash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to increment persistent hit count for {CacheHash}", cacheHash);
        }

        var selected = request.AnswerVariant > 1
            ? cached.Variants.Single(variant => variant.VariantNumber == request.AnswerVariant).Response
            : variantSelector.Select(cached).Response;
        logger.LogInformation("Tarot answer cache HIT for {CacheKey}", cacheKey);
        return selected with
        {
            CacheStatus = CacheStatus.HIT,
            CacheKey = cacheKey,
            Classification = classification
        };
    }

    private async Task PersistGeneratedAnswerAsync(
        string cacheHash,
        TarotReadingDto request,
        ClassificationResult classification,
        TarotReadingResponse response,
        CancellationToken cancellationToken)
    {
        var answer = new GeneratedAnswerWrite(
            cacheHash,
            classification.Domain,
            classification.Intent,
            request.ReadingMode,
            SpreadIds.Normalize(request.Spread),
            request.Locale.ToLowerInvariant(),
            request.Cards
                .Select(card => new SelectedCard(
                    card.Position.ToUpperInvariant(),
                    card.CardId.ToUpperInvariant(),
                    card.Orientation))
                .ToArray(),
            _cacheOptions.CacheVersion,
            _cacheOptions.PromptVersion,
            _cacheOptions.InterpretationVersion,
            request.ReadingMode == ReadingMode.DEEP ? _cacheOptions.ModelVersion : null,
            response);

        try
        {
            await generatedAnswerStore.SaveVariantAsync(answer, request.AnswerVariant, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "PostgreSQL write failed for generated Tarot answer {CacheHash}; continuing with Redis",
                cacheHash);
        }
    }

    private static bool HasRequestedVariant(CachedAnswerSet? answers, int requestedVariant) =>
        answers is not null && (requestedVariant <= 1 ||
            answers.Variants.Any(variant => variant.VariantNumber == requestedVariant));

    private static CachedAnswerSet MergeVariant(
        CachedAnswerSet? existing,
        int variantNumber,
        TarotReadingResponse response)
    {
        var variants = (existing?.Variants ?? [])
            .Where(variant => variant.VariantNumber != variantNumber)
            .Append(new CachedAnswerVariant(variantNumber, response))
            .OrderBy(variant => variant.VariantNumber)
            .ToArray();
        return new CachedAnswerSet(variants);
    }

    private async Task<TarotReadingResponse> GenerateResponseAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        CancellationToken cancellationToken)
    {
        var payload = interpretationEngine.Build(request, classification);
        if (request.ReadingMode == ReadingMode.STANDARD)
        {
            logger.LogInformation(
                "Rendering standard reading with rules for {Domain}/{Intent}",
                classification.Domain,
                classification.Intent);
            return ruleRenderer.Render(classification, payload);
        }

        metrics.LlmCalled();
        logger.LogInformation(
            "Calling LLM directly for {Domain}/{Intent}; raw question included",
            classification.Domain,
            classification.Intent);
        var result = await llmGate.RunAsync(
            token => llmClient.GenerateAsync(request, classification, payload, token),
            cancellationToken);
        validator.Validate(request, result);
        return result;
    }
}
