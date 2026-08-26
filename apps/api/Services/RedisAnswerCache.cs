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

    public async Task<TarotReadingResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await connection.GetDatabase().StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<TarotReadingResponse>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis read failed for {CacheKey}; continuing without cache", key);
            return null;
        }
    }

    public async Task SetAsync(string key, TarotReadingResponse response, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(response, JsonOptions);
            await connection.GetDatabase().StringSetAsync(key, payload, ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis write failed for {CacheKey}; generated reading will still be returned", key);
        }
    }
}
