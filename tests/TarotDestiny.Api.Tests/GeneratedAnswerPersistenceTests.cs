using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class GeneratedAnswerPersistenceTests
{
    [TestMethod]
    public void PostgresModelUsesJsonbAndUniqueCacheHash()
    {
        var options = new DbContextOptionsBuilder<TarotDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var dbContext = new TarotDbContext(options);
        var entity = dbContext.Model.FindEntityType(typeof(GeneratedAnswerEntity));

        Assert.IsNotNull(entity);
        Assert.AreEqual("tarot_generated_answer", entity.GetTableName());
        Assert.AreEqual("jsonb", entity.FindProperty(nameof(GeneratedAnswerEntity.CardsJson))?.GetColumnType());
        var variant = dbContext.Model.FindEntityType(typeof(GeneratedAnswerVariantEntity));
        Assert.IsNotNull(variant);
        Assert.AreEqual("jsonb", variant.FindProperty(nameof(GeneratedAnswerVariantEntity.ResponseJson))?.GetColumnType());
        Assert.AreEqual(
            "timestamp with time zone",
            entity.FindProperty(nameof(GeneratedAnswerEntity.CreatedAt))?.GetColumnType());
        Assert.AreEqual(
            0L,
            entity.FindProperty(nameof(GeneratedAnswerEntity.HitCount))?.GetDefaultValue());

        var cacheHashIndex = entity.GetIndexes().Single(index =>
            index.Properties.Count == 1 &&
            index.Properties[0].Name == nameof(GeneratedAnswerEntity.CacheHash));
        Assert.IsTrue(cacheHashIndex.IsUnique);
        Assert.AreEqual("ux_tarot_generated_answer_cache_hash", cacheHashIndex.GetDatabaseName());
    }

    [TestMethod]
    public async Task EligibleCacheMissPersistsGeneratedAnswerOnce()
    {
        var store = new RecordingGeneratedAnswerStore();
        var service = TestSupport.NewReadingService(
            new TrackingLlmClient(),
            generatedAnswerStore: store);

        var response = await service.GenerateAsync(
            TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD),
            CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, response.CacheStatus);
        Assert.AreEqual(1, store.CallCount);
    }

    [TestMethod]
    public async Task RedisHitDoesNotPersistGeneratedAnswerAgain()
    {
        var store = new RecordingGeneratedAnswerStore();
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm, generatedAnswerStore: store);
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(1, store.CallCount);
        Assert.AreEqual(0, llm.CallCount);
    }

    [TestMethod]
    public async Task PersonalizedReadingSkipsGeneratedAnswerPersistence()
    {
        const string question = "I have worked with my brother for 8 years and we are considering selling our company because he wants to move overseas. What should I do?";
        var store = new RecordingGeneratedAnswerStore();
        var service = TestSupport.NewReadingService(
            new TrackingLlmClient(),
            generatedAnswerStore: store);

        var response = await service.GenerateAsync(
            TestSupport.DestinyRequest(question, readingMode: ReadingMode.DEEP),
            CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, response.CacheStatus);
        Assert.AreEqual(0, store.CallCount);
    }

    [TestMethod]
    public async Task PersistenceFailureStillPopulatesRedisAndReturnsGeneratedReading()
    {
        var store = new ThrowingGeneratedAnswerStore();
        var cache = new InMemoryAnswerCache();
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            cache,
            generatedAnswerStore: store);
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(first.CacheKey, second.CacheKey);
        Assert.AreEqual(1, store.CallCount);
        Assert.AreEqual(0, llm.CallCount);
    }

    [TestMethod]
    public async Task DirectDeepReadingDoesNotPersistAndKeepsRawQuestionForLlm()
    {
        const string rawQuestion = "Should I leave this specific company?";
        var request = TestSupport.DestinyRequest(rawQuestion, readingMode: ReadingMode.DEEP);
        var store = new RecordingGeneratedAnswerStore();
        var llm = new TrackingLlmClient();
        var options = new TarotCacheOptions
        {
            CacheVersion = "cache-test-v7",
            PromptVersion = "prompt-test-v4",
            InterpretationVersion = "interpretation-test-v2",
            ModelVersion = "deep-model-test-v9",
            AnswerTtlDays = 30
        };
        var service = TestSupport.NewReadingService(
            llm,
            generatedAnswerStore: store,
            tarotCacheOptions: options);

        var response = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, response.CacheStatus);
        Assert.IsNull(response.CacheKey);
        Assert.AreEqual(0, store.CallCount);
        Assert.AreEqual(rawQuestion, llm.ReceivedQuestions.Single());
        Assert.AreEqual(ClassifierSources.DeepDirect, response.Classification.Source);
    }
}
