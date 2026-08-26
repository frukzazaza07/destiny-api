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

    public async Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken)
    {
        metrics.ReadingRequested();
        var classification = await classifier.ClassifyAsync(
            request.Question,
            request.Locale,
            cancellationToken);
        var canUseSharedCache = classifier.CanUseSharedCache(classification);
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
            var uncached = await GenerateResponseAsync(request, classification, includeRawQuestion: true, cancellationToken);
            return uncached with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }

        var cached = await cache.GetAsync(cacheKey!, cancellationToken);
        if (cached is not null)
        {
            metrics.CacheHit();
            if (request.ReadingMode == ReadingMode.DEEP)
            {
                metrics.LlmAvoided();
                logger.LogInformation("LLM call avoided for {CacheKey}", cacheKey);
            }
            logger.LogInformation("Tarot answer cache HIT for {CacheKey}", cacheKey);
            return cached with
            {
                CacheStatus = CacheStatus.HIT,
                CacheKey = cacheKey,
                Classification = classification
            };
        }

        metrics.CacheMiss();
        logger.LogInformation("Tarot answer cache MISS for {CacheKey}", cacheKey);
        var generated = await GenerateResponseAsync(request, classification, includeRawQuestion: false, cancellationToken);
        var toCache = generated with { CacheStatus = CacheStatus.MISS, CacheKey = cacheKey };
        await cache.SetAsync(cacheKey!, toCache, TimeSpan.FromDays(_cacheOptions.AnswerTtlDays), cancellationToken);
        return toCache;
    }

    private async Task<TarotReadingResponse> GenerateResponseAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        bool includeRawQuestion,
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

        var generationRequest = includeRawQuestion ? request : request with { Question = null };
        metrics.LlmCalled();
        logger.LogInformation(
            "Calling LLM for {Domain}/{Intent}; raw question included: {IncludeRawQuestion}",
            classification.Domain,
            classification.Intent,
            includeRawQuestion);
        var result = await llmGate.RunAsync(
            token => llmClient.GenerateAsync(generationRequest, classification, payload, token),
            cancellationToken);
        validator.Validate(request, result);
        return result;
    }
}
