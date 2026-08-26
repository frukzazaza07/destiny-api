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
    string BuildRedisKey(TarotReadingDto request, ClassificationResult classification);
}

public sealed class CacheKeyBuilder(IOptions<TarotCacheOptions> options) : ICacheKeyBuilder
{
    private readonly TarotCacheOptions _options = options.Value;

    public string BuildCanonical(TarotReadingDto request, ClassificationResult classification)
    {
        var parts = new List<string>
        {
            _options.CacheVersion,
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
            !string.IsNullOrWhiteSpace(_options.ModelVersion))
        {
            parts.Add(_options.ModelVersion);
        }

        return string.Join("|", parts);
    }

    public string BuildRedisKey(TarotReadingDto request, ClassificationResult classification)
    {
        var canonical = BuildCanonical(request, classification);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return $"tarot:answer:{hash}";
    }
}
