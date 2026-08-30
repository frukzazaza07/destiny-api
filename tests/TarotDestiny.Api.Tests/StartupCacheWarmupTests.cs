using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class StartupCacheWarmupTests
{
    [TestMethod]
    public async Task StartupWarmupWaitsForReadyThenCreatesVisibleBoundedJob()
    {
        var reading = new RecordingReadingService();
        var store = new WarmupStore([]);
        await using var provider = BuildProvider(reading, store);
        var lifetime = new TestApplicationLifetime();
        var cacheLock = new RecordingCacheLock(acquire: true);
        var service = NewService(provider, lifetime, cacheLock, new InMemoryAnswerCache(),
            new StartupCacheWarmupOptions
            {
                Enabled = true,
                DelaySeconds = 0,
                RehydrateRedisFromPostgres = false,
                Locales = ["en"],
                ReadingModes = ["STANDARD"],
                ApprovedIntents = ["CAREER_CHANGE_JOB"],
                Spreads = [SpreadIds.Daily1],
                MaxCombinationsPerStartup = 1,
                MaxConcurrency = 1,
                RetryCount = 0
            });

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.AreEqual(0, cacheLock.CallCount);
        Assert.AreEqual(0, reading.CallCount);

        lifetime.SignalStarted();
        await WaitUntilAsync(() => service.List().Any(job => job.Status == "COMPLETED"));

        var job = service.List().Single();
        Assert.AreEqual(1, cacheLock.CallCount);
        Assert.AreEqual(1, job.RequestedCombinations);
        Assert.AreEqual(1, job.CompletedCombinations);
        Assert.AreEqual(1, reading.CallCount);
        await service.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task StartupWarmupRehydratesCurrentEntriesWithoutGeneration()
    {
        var request = TestSupport.DestinyRequest();
        var answer = CachedAnswerSet.Single(TestSupport.ValidResponse(
            request, TestSupport.CareerChangeClassification()));
        var store = new WarmupStore([new PersistedAnswerCacheEntry(
            new string('a', 64), answer, "CAREER_CHANGE_JOB", "en", "DEEP", SpreadIds.Daily1)]);
        var reading = new RecordingReadingService();
        await using var provider = BuildProvider(reading, store);
        var lifetime = new TestApplicationLifetime();
        var cache = new InMemoryAnswerCache();
        var service = NewService(provider, lifetime, new RecordingCacheLock(true), cache,
            new StartupCacheWarmupOptions
            {
                Enabled = true,
                DelaySeconds = 0,
                RehydrateRedisFromPostgres = true,
                Locales = ["en"],
                ReadingModes = ["DEEP"],
                ApprovedIntents = ["CAREER_CHANGE_JOB"],
                Spreads = [SpreadIds.Daily1],
                MaxCombinationsPerStartup = 1
            });

        await service.StartAsync(CancellationToken.None);
        lifetime.SignalStarted();
        await WaitUntilAsync(async () => await cache.GetAsync($"tarot:answer:{new string('a', 64)}", CancellationToken.None) is not null);

        Assert.AreEqual(0, reading.CallCount);
        Assert.AreEqual(1, store.LoadCount);
        await service.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(RecordingReadingService reading, WarmupStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITarotReadingService>(_ => reading);
        services.AddScoped<IGeneratedAnswerStore>(_ => store);
        return services.BuildServiceProvider();
    }

    private static CacheWarmupService NewService(
        IServiceProvider provider,
        TestApplicationLifetime lifetime,
        ICacheLock cacheLock,
        IAnswerCache answerCache,
        StartupCacheWarmupOptions startup) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TarotCatalog(),
            cacheLock,
            answerCache,
            lifetime,
            Options.Create(startup),
            Options.Create(new DeepSharedCacheOptions()),
            Options.Create(new TarotCacheOptions()),
            new TarotMetrics(),
            NullLogger<CacheWarmupService>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
        Assert.IsTrue(condition());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!await condition() && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
        Assert.IsTrue(await condition());
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void StopApplication() => _stopping.Cancel();
        public void SignalStarted() => _started.Cancel();
    }

    private sealed class RecordingCacheLock(bool acquire) : ICacheLock
    {
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);
        public Task<IAsyncDisposable?> TryAcquireAsync(string cacheHash, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IAsyncDisposable?>(acquire ? new Lease() : null);
        }
        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReadingService : ITarotReadingService
    {
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);
        public Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var classification = TestSupport.CareerChangeClassification();
            return Task.FromResult(TestSupport.ValidResponse(request, classification));
        }
    }

    private sealed class WarmupStore(IReadOnlyList<PersistedAnswerCacheEntry> entries) : IGeneratedAnswerStore
    {
        private int _loads;
        public int LoadCount => Volatile.Read(ref _loads);
        public Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken) => Task.FromResult<CachedAnswerSet?>(null);
        public Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken) => Task.FromResult(new PersistentCacheAnalytics(0, 0, 0, []));
        public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<PersistedAnswerCacheEntry>> LoadCurrentEntriesAsync(
            string cacheVersion, string promptVersion, string interpretationVersion,
            int maximumEntries, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            return Task.FromResult(entries);
        }
    }
}
