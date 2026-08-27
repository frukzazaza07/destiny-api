using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.Services;

public sealed class RedisAnswerCache(
    IConnectionMultiplexer connection,
    ILogger<RedisAnswerCache> logger) : IAnswerCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CachedAnswerSet?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await connection.GetDatabase().StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var json = value.ToString();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("variants", out _))
            {
                var answers = JsonSerializer.Deserialize<CachedAnswerSet>(json, JsonOptions);
                return answers is { Variants.Count: > 0 } ? answers : null;
            }

            // Values written before variant support contain a response directly.
            var legacy = JsonSerializer.Deserialize<TarotReadingResponse>(json, JsonOptions);
            return legacy is null ? null : CachedAnswerSet.Single(legacy);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis read failed for {CacheKey}; continuing without cache", key);
            return null;
        }
    }

    public async Task SetAsync(string key, CachedAnswerSet answers, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(answers, JsonOptions);
            await connection.GetDatabase().StringSetAsync(key, payload, ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis write failed for {CacheKey}; generated reading will still be returned", key);
        }
    }
}
