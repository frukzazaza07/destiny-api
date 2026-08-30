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
    ISharedReadingSafetyEvaluator safetyEvaluator,
    TarotMetrics metrics,
    IOptions<TarotCacheOptions> cacheOptions,
    IOptions<DeepSharedCacheOptions> deepSharedCacheOptions,
    ILogger<TarotReadingService> logger) : ITarotReadingService
{
    private readonly TarotCacheOptions _cacheOptions = cacheOptions.Value;
    private readonly DeepSharedCacheOptions _deepCacheOptions = deepSharedCacheOptions.Value;
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

        var isDeep = request.ReadingMode == ReadingMode.DEEP;
        if (isDeep && !_deepCacheOptions.Enabled)
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

        metrics.CachePreflightRequested(request.ReadingMode);
        ClassificationResult cacheClassification;
        try
        {
            cacheClassification = await classifier.ClassifyAsync(
                request.Question,
                request.Locale,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (isDeep)
        {
            metrics.ClassifierRejected();
            metrics.CachePreflightRejected(request.ReadingMode, "CLASSIFIER_FAILURE");
            metrics.CacheSkipped();
            logger.LogWarning(exception, "DEEP cache preflight failed; continuing through direct generation");
            var direct = await GenerateResponseAsync(request, DeepDirectClassification, cancellationToken);
            return direct with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }
        metrics.ClassifierObserved(cacheClassification);
        var generationClassification = isDeep ? DeepDirectClassification : cacheClassification;
        var classificationEligible = classifier.CanUseSharedCache(cacheClassification) &&
            (!isDeep || string.Equals(
                cacheClassification.Source,
                ClassifierSources.PythonGrpc,
                StringComparison.Ordinal));
        var approvedIntent = !isDeep || _deepCacheOptions.ApprovedIntents.Contains(
            cacheClassification.Intent,
            StringComparer.OrdinalIgnoreCase);
        var requestSafety = isDeep
            ? safetyEvaluator.EvaluateRequest(request.Question)
            : SharedContentSafetyResult.Safe;
        var canUseSharedCache = classificationEligible && approvedIntent && requestSafety.IsSafe;
        var cacheHash = canUseSharedCache
            ? cacheKeyBuilder.BuildHash(request, cacheClassification, generationClassification)
            : null;
        var cacheKey = canUseSharedCache
            ? cacheKeyBuilder.BuildRedisKey(request, cacheClassification, generationClassification)
            : null;

        if (canUseSharedCache)
        {
            metrics.ClassifierAccepted();
            metrics.CachePreflightEligible(request.ReadingMode);
            logger.LogInformation("Classifier accepted {Domain}/{Intent} for shared cache", cacheClassification.Domain, cacheClassification.Intent);
        }
        else
        {
            metrics.ClassifierRejected();
            metrics.CachePreflightRejected(
                request.ReadingMode,
                GetPreflightRejectionReason(cacheClassification, classificationEligible, approvedIntent, requestSafety));
            logger.LogInformation("Classifier rejected {Domain}/{Intent} for shared cache", cacheClassification.Domain, cacheClassification.Intent);
        }

        if (!canUseSharedCache)
        {
            metrics.CacheSkipped();
            logger.LogInformation("Skipping shared cache for {Domain}/{Intent}", cacheClassification.Domain, cacheClassification.Intent);
            var uncached = await GenerateResponseAsync(request, generationClassification, cancellationToken);
            return uncached with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }

        var mayRead = !isDeep || _deepCacheOptions.ReadEnabled;
        var mayWrite = !isDeep || _deepCacheOptions.WriteEnabled;
        var cached = mayRead
            ? await FindCachedAsync(cacheKey!, cacheHash!, cancellationToken)
            : null;
        if (mayRead && HasRequestedVariant(cached, request.AnswerVariant))
        {
            return await ReturnHitAsync(cached!, cacheHash!, cacheKey!, request, generationClassification, cancellationToken);
        }

        IAsyncDisposable? cacheLease;
        try
        {
            cacheLease = await cacheLock.TryAcquireAsync(cacheHash!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (isDeep)
        {
            logger.LogWarning(exception, "DEEP cache lock failed; continuing through direct generation");
            metrics.CacheSkipped();
            var direct = await GenerateResponseAsync(request, DeepDirectClassification, cancellationToken);
            return direct with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }
        await using var cacheLeaseScope = cacheLease;
        cached = mayRead
            ? await FindCachedAsync(cacheKey!, cacheHash!, cancellationToken)
            : null;
        if (mayRead && HasRequestedVariant(cached, request.AnswerVariant))
        {
            return await ReturnHitAsync(cached!, cacheHash!, cacheKey!, request, generationClassification, cancellationToken);
        }

        metrics.CacheMiss(request.ReadingMode);
        logger.LogInformation("Tarot answer cache MISS for {CacheKey}", cacheKey);
        var generated = await GenerateResponseAsync(request, generationClassification, cancellationToken);

        if (!mayWrite)
        {
            metrics.CacheSkipped();
            return generated with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
        }

        if (isDeep)
        {
            var responseSafety = safetyEvaluator.EvaluateResponse(request.Question, generated);
            if (!responseSafety.IsSafe)
            {
                metrics.CachePreflightRejected(request.ReadingMode, $"CONTENT_{responseSafety.Reason}");
                metrics.CacheSkipped();
                logger.LogInformation("Skipping shared DEEP persistence because content safety rejected {Reason}", responseSafety.Reason);
                return generated with { CacheStatus = CacheStatus.SKIPPED, CacheKey = null };
            }
        }

        var toCache = generated with { CacheStatus = CacheStatus.MISS, CacheKey = cacheKey };
        await PersistGeneratedAnswerAsync(
            cacheHash!,
            request,
            cacheClassification,
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
        var runtimeStored = await SetRuntimeCacheAsync(cacheKey!, answers, cancellationToken);
        metrics.CacheStored(request.ReadingMode, runtimeStored);
        return toCache;
    }

    private static string GetPreflightRejectionReason(
        ClassificationResult classification,
        bool classificationEligible,
        bool approvedIntent,
        SharedContentSafetyResult requestSafety)
    {
        if (classification.Personalization == PersonalizationLevel.HIGH) return "PERSONALIZATION";
        if (string.Equals(classification.Intent, TarotIntents.PersonalCustom, StringComparison.OrdinalIgnoreCase)) return "INTENT";
        if (!string.Equals(classification.Source, ClassifierSources.PythonGrpc, StringComparison.Ordinal) &&
            !classificationEligible) return "CLASSIFIER_SOURCE";
        if (!classificationEligible) return "CONFIDENCE";
        if (!approvedIntent) return "INTENT_NOT_APPROVED";
        return requestSafety.IsSafe ? "UNKNOWN" : $"CONTENT_{requestSafety.Reason}";
    }

    private async Task<CachedAnswerSet?> FindCachedAsync(
        string cacheKey,
        string cacheHash,
        CancellationToken cancellationToken)
    {
        CachedAnswerSet? cached;
        try
        {
            cached = await cache.GetAsync(cacheKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Runtime cache lookup failed for {CacheKey}; continuing", cacheKey);
            cached = null;
        }
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

            await SetRuntimeCacheAsync(cacheKey, persisted, cancellationToken);
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

    private async Task<bool> SetRuntimeCacheAsync(
        string cacheKey,
        CachedAnswerSet answers,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(
                cacheKey,
                answers,
                TimeSpan.FromDays(_cacheOptions.AnswerTtlDays),
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Runtime cache write failed for {CacheKey}; continuing", cacheKey);
            return false;
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
        metrics.CacheHit(request.ReadingMode);
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
