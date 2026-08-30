using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface ICacheKeyBuilder
{
    string BuildCanonical(TarotReadingDto request, ClassificationResult classification);
    string BuildCanonical(TarotReadingDto request, ClassificationResult classification, ClassificationResult routingClassification);
    string BuildHash(TarotReadingDto request, ClassificationResult classification);
    string BuildHash(TarotReadingDto request, ClassificationResult classification, ClassificationResult routingClassification);
    string BuildRedisKey(TarotReadingDto request, ClassificationResult classification);
    string BuildRedisKey(TarotReadingDto request, ClassificationResult classification, ClassificationResult routingClassification);
}

public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    private readonly TarotCacheOptions _options;
    private readonly IInferenceRouter? _inferenceRouter;

    public CacheKeyBuilder(IOptions<TarotCacheOptions> options)
        : this(options, null)
    {
    }

    public CacheKeyBuilder(
        IOptions<TarotCacheOptions> options,
        IInferenceRouter? inferenceRouter)
    {
        _options = options.Value;
        _inferenceRouter = inferenceRouter;
    }

    public string BuildCanonical(TarotReadingDto request, ClassificationResult classification)
        => BuildCanonical(request, classification, classification);

    public string BuildCanonical(
        TarotReadingDto request,
        ClassificationResult classification,
        ClassificationResult routingClassification)
    {
        var parts = new List<string>
        {
            _options.CacheVersion,
            _options.TaxonomyVersion,
            classification.Domain.ToString(),
            classification.Intent,
            request.ReadingMode.ToString(),
            request.Spread.ToUpperInvariant(),
            request.Locale.ToUpperInvariant()
        };

        parts.AddRange(request.Cards.Select(card =>
            $"{card.Position.ToUpperInvariant()}:{card.CardId.ToUpperInvariant()}:{card.Orientation}"));

        parts.Add(_options.PromptVersion);
        parts.Add(_options.InterpretationVersion);

        if (request.ReadingMode == ReadingMode.DEEP &&
            _inferenceRouter is not null)
        {
            var plan = _inferenceRouter.Resolve(request, routingClassification);
            parts.Add(plan.TierId);
            parts.Add(plan.CacheModelVersion);
            if (plan.PromptVariant.CacheDiscriminator is not null)
            {
                parts.Add(plan.PromptVariant.CacheDiscriminator);
            }
        }
        else if (request.ReadingMode == ReadingMode.DEEP &&
                 !string.IsNullOrWhiteSpace(_options.ModelVersion))
        {
            parts.Add(_options.ModelVersion);
        }

        return string.Join("|", parts);
    }

    public string BuildHash(TarotReadingDto request, ClassificationResult classification)
    {
        var canonical = BuildCanonical(request, classification);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public string BuildHash(
        TarotReadingDto request,
        ClassificationResult classification,
        ClassificationResult routingClassification)
    {
        var canonical = BuildCanonical(request, classification, routingClassification);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public string BuildRedisKey(TarotReadingDto request, ClassificationResult classification) =>
        $"tarot:answer:{BuildHash(request, classification)}";

    public string BuildRedisKey(
        TarotReadingDto request,
        ClassificationResult classification,
        ClassificationResult routingClassification) =>
        $"tarot:answer:{BuildHash(request, classification, routingClassification)}";
}
