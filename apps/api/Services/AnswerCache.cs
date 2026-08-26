using System.Collections.Concurrent;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.Services;

public interface IAnswerCache
{
    Task<TarotReadingResponse?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, TarotReadingResponse response, TimeSpan ttl, CancellationToken cancellationToken);
}

public sealed class InMemoryAnswerCache : IAnswerCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public Task<TarotReadingResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult<TarotReadingResponse?>(null);
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<TarotReadingResponse?>(null);
        }

        return Task.FromResult<TarotReadingResponse?>(entry.Response);
    }

    public Task SetAsync(string key, TarotReadingResponse response, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _entries[key] = new CacheEntry(response, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(TarotReadingResponse Response, DateTimeOffset ExpiresAt);
}
