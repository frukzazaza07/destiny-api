using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed class CachingInterpretationEngine : IInterpretationEngine, IDisposable
{
    private readonly IInterpretationEngine _inner;
    private readonly BaseInterpretationCacheOptions _options;
    private readonly string _interpretationVersion;
    private readonly MemoryCache _cache;
    private long _hitCount;
    private long _missCount;

    public CachingInterpretationEngine(
        IInterpretationEngine inner,
        IOptions<BaseInterpretationCacheOptions> options,
        IOptions<TarotCacheOptions> cacheOptions)
    {
        _inner = inner;
        _options = options.Value;
        _interpretationVersion = cacheOptions.Value.InterpretationVersion;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = Math.Max(1, _options.MaximumEntries)
        });
    }

    public long HitCount => Interlocked.Read(ref _hitCount);
    public long MissCount => Interlocked.Read(ref _missCount);

    public InterpretationPayload Build(
        TarotReadingDto request,
        ClassificationResult classification)
    {
        if (!_options.Enabled)
        {
            return _inner.Build(request, classification);
        }

        var key = BuildKey(request, classification, _interpretationVersion);
        if (_cache.TryGetValue<InterpretationPayload>(key, out var cached) && cached is not null)
        {
            Interlocked.Increment(ref _hitCount);
            return cached;
        }

        Interlocked.Increment(ref _missCount);
        var generated = _inner.Build(request, classification);
        _cache.Set(
            key,
            generated,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _options.TtlMinutes)),
                Size = 1
            });
        return generated;
    }

    public static string BuildKey(
        TarotReadingDto request,
        ClassificationResult classification,
        string interpretationVersion)
    {
        var parts = new List<string>
        {
            interpretationVersion,
            classification.Domain.ToString(),
            classification.Intent.ToUpperInvariant(),
            request.Spread.ToUpperInvariant(),
            request.Locale.ToUpperInvariant()
        };
        parts.AddRange(request.Cards.Select(card =>
            $"{card.Position.ToUpperInvariant()}:{card.CardId.ToUpperInvariant()}:{card.Orientation}"));
        var canonical = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    public void Dispose() => _cache.Dispose();
}
