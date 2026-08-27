using System.Collections.Concurrent;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.Services;

public sealed record CachedAnswerVariant(
    int VariantNumber,
    TarotReadingResponse Response);

public sealed record CachedAnswerSet(
    IReadOnlyList<CachedAnswerVariant> Variants)
{
    public static CachedAnswerSet Single(TarotReadingResponse response) =>
        new([new CachedAnswerVariant(1, response)]);
}

public interface IAnswerCache
{
    Task<CachedAnswerSet?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, CachedAnswerSet answers, TimeSpan ttl, CancellationToken cancellationToken);
}

public sealed class InMemoryAnswerCache : IAnswerCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public Task<CachedAnswerSet?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult<CachedAnswerSet?>(null);
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<CachedAnswerSet?>(null);
        }

        return Task.FromResult<CachedAnswerSet?>(entry.Answers);
    }

    public Task SetAsync(string key, CachedAnswerSet answers, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[key] = new CacheEntry(answers, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(CachedAnswerSet Answers, DateTimeOffset ExpiresAt);
}
