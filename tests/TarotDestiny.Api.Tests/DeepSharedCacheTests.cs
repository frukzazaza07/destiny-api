using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class DeepSharedCacheTests
{
    [TestMethod]
    public async Task EligibleSafeDeepMissPersistsAndNextRequestAvoidsLlm()
    {
        var llm = new TrackingLlmClient();
        var classifier = new FixedClassifier(0.95, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB");
        var service = TestSupport.NewReadingService(
            llm,
            classifier: classifier,
            deepSharedCacheOptions: EnabledOptions());
        var firstRequest = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);
        var secondRequest = TestSupport.DestinyRequest("Would a new role suit me?", "en", ReadingMode.DEEP);

        var first = await service.GenerateAsync(firstRequest, CancellationToken.None);
        var second = await service.GenerateAsync(secondRequest, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(1, llm.CallCount);
        Assert.AreEqual(first.CacheKey, second.CacheKey);
        CollectionAssert.AreEqual(new[] { firstRequest.Question }, llm.ReceivedQuestions);
        Assert.AreEqual(ClassifierSources.DeepDirect, first.Classification.Source);
        Assert.AreEqual(ClassifierSources.DeepDirect, second.Classification.Source);
    }

    [TestMethod]
    public async Task UnsafeDeepQuestionIsReturnedButNeverStored()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FixedClassifier(0.99, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB"),
            deepSharedCacheOptions: EnabledOptions());
        const string question = "I worked at Acme for 8 years and earn $120000. Should I leave?";
        var request = TestSupport.DestinyRequest(question, "en", ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, first.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, second.CacheStatus);
        Assert.AreEqual(2, llm.CallCount);
        CollectionAssert.AreEqual(new[] { question, question }, llm.ReceivedQuestions);
    }

    [TestMethod]
    public async Task WriteOnlyRolloutBuildsCandidateWithoutServingHit()
    {
        var llm = new TrackingLlmClient();
        var options = EnabledOptions();
        options.ReadEnabled = false;
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FixedClassifier(0.95, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB"),
            deepSharedCacheOptions: options);
        var request = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.MISS, second.CacheStatus);
        Assert.AreEqual(2, llm.CallCount);
    }

    [TestMethod]
    public async Task ConcurrentEligibleDeepMissesGenerateOneSharedAnswer()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FixedClassifier(0.95, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB"),
            deepSharedCacheOptions: EnabledOptions());
        var request = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => service.GenerateAsync(request, CancellationToken.None)));

        Assert.AreEqual(1, llm.CallCount);
        Assert.AreEqual(1, responses.Count(response => response.CacheStatus == CacheStatus.MISS));
        Assert.AreEqual(11, responses.Count(response => response.CacheStatus == CacheStatus.HIT));
    }

    [TestMethod]
    public async Task ClassifierFailureFallsThroughToUnchangedDeepGeneration()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FailingClassifier(),
            deepSharedCacheOptions: EnabledOptions());
        var request = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);

        var response = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, response.CacheStatus);
        Assert.AreEqual(1, llm.CallCount);
        Assert.AreEqual(request.Question, llm.ReceivedQuestions.Single());
        Assert.AreEqual(ClassifierSources.DeepDirect, response.Classification.Source);
    }

    [TestMethod]
    public async Task QuestionSpecificDeepResponseIsReturnedButNotStored()
    {
        var llm = new EchoingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FixedClassifier(0.98, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB"),
            deepSharedCacheOptions: EnabledOptions());
        const string question = "Could a completely different professional direction bring lasting fulfillment?";
        var request = TestSupport.DestinyRequest(question, "en", ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, first.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, second.CacheStatus);
        Assert.AreEqual(2, llm.CallCount);
        Assert.AreEqual(question, first.Summary);
    }

    [TestMethod]
    public async Task RuntimeCacheFailureDoesNotPreventDeepReading()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            cache: new ThrowingAnswerCache(),
            classifier: new FixedClassifier(0.98, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB"),
            deepSharedCacheOptions: EnabledOptions());
        var request = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);

        var response = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, response.CacheStatus);
        Assert.AreEqual(1, llm.CallCount);
    }

    [TestMethod]
    public async Task RuleFallbackClassificationNeverEnablesDeepSharedCache()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(
            llm,
            classifier: new FixedClassifier(
                0.99, PersonalizationLevel.LOW, "CAREER_CHANGE_JOB",
                ClassifierSources.CSharpRuleFallback),
            deepSharedCacheOptions: EnabledOptions());
        var request = TestSupport.DestinyRequest("Should I change my job?", "en", ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, first.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, second.CacheStatus);
        Assert.AreEqual(2, llm.CallCount);
    }

    private static DeepSharedCacheOptions EnabledOptions() => new()
    {
        Enabled = true,
        ReadEnabled = true,
        WriteEnabled = true,
        ApprovedIntents = ["CAREER_CHANGE_JOB"]
    };

    private sealed class FixedClassifier(
        double confidence,
        PersonalizationLevel personalization,
        string intent,
        string source = ClassifierSources.PythonGrpc) : IQuestionClassifier
    {
        public Task<ClassificationResult> ClassifyAsync(
            string? question,
            string locale,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ClassificationResult(
                TarotDomain.CAREER,
                intent,
                confidence,
                personalization,
                source,
                "test-model"));

        public bool CanUseSharedCache(ClassificationResult classification) =>
            classification.Confidence > 0.90 &&
            classification.Personalization != PersonalizationLevel.HIGH &&
            !string.Equals(classification.Intent, TarotIntents.PersonalCustom, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailingClassifier : IQuestionClassifier
    {
        public Task<ClassificationResult> ClassifyAsync(string? question, string locale, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("classifier unavailable");

        public bool CanUseSharedCache(ClassificationResult classification) => false;
    }

    private sealed class EchoingLlmClient : ILlmClient
    {
        private int _calls;
        public int CallCount => Volatile.Read(ref _calls);
        public Task<TarotReadingResponse> GenerateAsync(
            TarotReadingDto request,
            ClassificationResult classification,
            InterpretationPayload payload,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(TestSupport.ValidResponse(request, classification) with
            {
                Summary = request.Question!
            });
        }
    }

    private sealed class ThrowingAnswerCache : IAnswerCache
    {
        public Task<CachedAnswerSet?> GetAsync(string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cache unavailable");

        public Task SetAsync(string key, CachedAnswerSet answers, TimeSpan ttl, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cache unavailable");
    }
}
