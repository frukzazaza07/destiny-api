using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class AdvancedCacheAndInferenceTests
{
    [TestMethod]
    public async Task RedisMissReadsPostgresRepopulatesCacheAndIncrementsHits()
    {
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD);
        var classification = TestSupport.CareerChangeClassification();
        var response = TestSupport.ValidResponse(request, classification);
        var cacheOptions = new TarotCacheOptions();
        var hash = new CacheKeyBuilder(Options.Create(cacheOptions)).BuildHash(request, classification);
        var store = new MemoryGeneratedAnswerStore(hash, CachedAnswerSet.Single(response));
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm, generatedAnswerStore: store, tarotCacheOptions: cacheOptions);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.HIT, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(0, llm.CallCount);
        Assert.AreEqual(2, store.HitCount);
        Assert.IsTrue(store.FindCount >= 1);
    }

    [TestMethod]
    public async Task ConcurrentDeepReadingsEachUseFullLlm()
    {
        var llm = new DelayedLlmClient();
        var service = TestSupport.NewReadingService(llm);
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => service.GenerateAsync(request, CancellationToken.None)));

        Assert.AreEqual(12, llm.CallCount);
        Assert.IsTrue(responses.All(response => response.CacheStatus == CacheStatus.SKIPPED));
    }

    [TestMethod]
    public async Task RequestedDeepVariantIsGeneratedEveryTime()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);
        var firstVariant = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);
        var secondVariant = firstVariant with { AnswerVariant = 2 };

        await service.GenerateAsync(firstVariant, CancellationToken.None);
        var generated = await service.GenerateAsync(secondVariant, CancellationToken.None);
        var reused = await service.GenerateAsync(secondVariant, CancellationToken.None);

        Assert.AreEqual(3, llm.CallCount);
        Assert.AreEqual(CacheStatus.SKIPPED, generated.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, reused.CacheStatus);
    }

    [TestMethod]
    public void ModelTiersAndPromptExperimentsPartitionDeepCacheIdentity()
    {
        var llmOptions = Options.Create(new LlmOptions
        {
            DefaultTier = "CORE",
            Tiers =
            [
                new LlmTierOptions { Id = "CORE", Model = "core", CacheModelVersion = "core-v1" },
                new LlmTierOptions { Id = "PREMIUM", Model = "premium", CacheModelVersion = "premium-v1" }
            ],
            PromptExperiment = new PromptExperimentOptions
            {
                Enabled = true,
                Id = "copy-v1",
                Variants =
                [
                    new PromptVariantOptions { Id = "A", Version = "a-v1", Weight = 1 },
                    new PromptVariantOptions { Id = "B", Version = "b-v1", Weight = 1 }
                ]
            }
        });
        var router = new InferenceRouter(llmOptions);
        var builder = new CacheKeyBuilder(Options.Create(new TarotCacheOptions()), router);
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();

        var core = builder.BuildHash(request with { ModelTier = "CORE" }, classification);
        var premium = builder.BuildHash(request with { ModelTier = "PREMIUM" }, classification);
        var assignedOnce = router.Resolve(request, classification).PromptVariant;
        var assignedAgain = router.Resolve(request, classification).PromptVariant;

        Assert.AreNotEqual(core, premium);
        Assert.AreEqual(assignedOnce, assignedAgain);
        Assert.IsFalse(builder.BuildCanonical(request, classification).Contains(request.Question!, StringComparison.Ordinal));
    }

    [TestMethod]
    public void BaseInterpretationCacheReusesQuestionIndependentPayload()
    {
        var inner = new CountingInterpretationEngine();
        using var cache = new CachingInterpretationEngine(
            inner,
            Options.Create(new BaseInterpretationCacheOptions()),
            Options.Create(new TarotCacheOptions()));
        var classification = TestSupport.CareerChangeClassification();

        cache.Build(TestSupport.DestinyRequest("First wording"), classification);
        cache.Build(TestSupport.DestinyRequest("Second wording"), classification);

        Assert.AreEqual(1, inner.BuildCount);
        Assert.AreEqual(1, cache.HitCount);
        Assert.AreEqual(1, cache.MissCount);
    }

    [TestMethod]
    public void ClassifierTrainingModelHasPrivacyAndReviewConstraints()
    {
        var options = new DbContextOptionsBuilder<TarotDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;
        using var context = new TarotDbContext(options);
        var entity = context.Model.FindEntityType(typeof(ClassifierTrainingExampleEntity));

        Assert.IsNotNull(entity);
        Assert.AreEqual("tarot_question_classification_training", entity.GetTableName());
        Assert.IsTrue(entity.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Any(property => property.Name == nameof(ClassifierTrainingExampleEntity.QuestionHash))));
        Assert.IsNotNull(entity.FindProperty(nameof(ClassifierTrainingExampleEntity.ReviewedPersonalization)));
        Assert.IsNotNull(entity.FindProperty(nameof(ClassifierTrainingExampleEntity.ParaphraseGroup)));
        Assert.IsNotNull(entity.FindProperty(nameof(ClassifierTrainingExampleEntity.ReviewerTimeSeconds)));
    }

    [TestMethod]
    public async Task TrainingSubmissionRejectsMissingConsentBeforeDatabaseAccess()
    {
        var dbOptions = new DbContextOptionsBuilder<TarotDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused")
            .Options;
        await using var context = new TarotDbContext(dbOptions);
        var store = new ClassifierTrainingStore(
            context,
            TestSupport.NewClassifier(),
            Options.Create(new ClassifierTrainingOptions()));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SubmitAsync(
            new ClassifierTrainingSubmissionDto("Should I change jobs?", "en", false),
            CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SubmitAsync(
            new ClassifierTrainingSubmissionDto("Email me at person@example.com about my job", "en", true),
            CancellationToken.None));
    }

    [TestMethod]
    public void ExactIntentTopicsSupportOfflineWarmupWithoutRemoteClassification()
    {
        var classifier = TestSupport.NewClassifier();
        var result = classifier.Classify("topic:CAREER_CHANGE_JOB", "en");

        Assert.AreEqual(TarotDomain.CAREER, result.Domain);
        Assert.AreEqual("CAREER_CHANGE_JOB", result.Intent);
        Assert.AreEqual(ClassifierSources.CSharpTopic, result.Source);
    }

    private sealed class MemoryGeneratedAnswerStore(string hash, CachedAnswerSet answers) : IGeneratedAnswerStore
    {
        private int _findCount;
        private int _hitCount;
        public int FindCount => Volatile.Read(ref _findCount);
        public int HitCount => Volatile.Read(ref _hitCount);
        public Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _findCount);
            return Task.FromResult<CachedAnswerSet?>(cacheHash == hash ? answers : null);
        }
        public Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken) { Interlocked.Increment(ref _hitCount); return Task.CompletedTask; }
        public Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken) => Task.FromResult(new PersistentCacheAnalytics(1, 1, HitCount, []));
        public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
    }

    private sealed class DelayedLlmClient : ILlmClient
    {
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);
        public async Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, ClassificationResult classification, InterpretationPayload payload, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(60, cancellationToken);
            return TestSupport.ValidResponse(request, classification);
        }
    }

    private sealed class CountingInterpretationEngine : IInterpretationEngine
    {
        private int _builds;
        public int BuildCount => Volatile.Read(ref _builds);
        public InterpretationPayload Build(TarotReadingDto request, ClassificationResult classification)
        {
            Interlocked.Increment(ref _builds);
            return new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        }
    }
}
