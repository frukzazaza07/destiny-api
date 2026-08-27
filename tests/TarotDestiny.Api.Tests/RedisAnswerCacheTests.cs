using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class RedisAnswerCacheTests
{
    [TestMethod]
    [Timeout(5000)]
    public async Task DisconnectedRedisBehavesAsCacheMissAndDoesNotBlockGenerationPath()
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectRetry = 0,
            ConnectTimeout = 100,
            SyncTimeout = 100,
            AsyncTimeout = 100
        };
        options.EndPoints.Add("127.0.0.1", 6398);

        using var connection = ConnectionMultiplexer.Connect(options);
        var cache = new RedisAnswerCache(
            connection,
            TestSupport.LoggerFactory.CreateLogger<RedisAnswerCache>());

        var result = await cache.GetAsync("tarot:answer:unavailable-test", CancellationToken.None);
        await cache.SetAsync(
            "tarot:answer:unavailable-test",
            CachedAnswerSet.Single(TestSupport.ValidResponse(
                TestSupport.DestinyRequest(),
                TestSupport.CareerChangeClassification())),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        Assert.IsNull(result);
    }
}
