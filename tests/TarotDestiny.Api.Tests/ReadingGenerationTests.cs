using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class ReadingGenerationTests
{
    [TestMethod]
    public async Task DeepReadingAlwaysCallsLlmWithRawQuestionAndSkipsCache()
    {
        var llm = new TrackingLlmClient();
        var classifier = new ThrowingQuestionClassifier();
        var service = TestSupport.NewReadingService(llm, classifier: classifier);
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, first.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, second.CacheStatus);
        Assert.IsNull(first.CacheKey);
        Assert.IsNull(second.CacheKey);
        Assert.AreEqual(2, llm.CallCount);
        CollectionAssert.AreEqual(new[] { request.Question, request.Question }, llm.ReceivedQuestions);
        Assert.AreEqual(ClassifierSources.DeepDirect, first.Classification.Source);
        Assert.AreEqual(ClassifierDecisionMethods.ClassifierBypassed, first.Classification.DecisionMethod);
    }

    [TestMethod]
    public async Task DeepGenerationSendsEachRawQuestionToLlm()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);

        var first = await service.GenerateAsync(
            TestSupport.DestinyRequest("Should I change my job?", readingMode: ReadingMode.DEEP),
            CancellationToken.None);
        var second = await service.GenerateAsync(
            TestSupport.DestinyRequest("Should I leave my company?", readingMode: ReadingMode.DEEP),
            CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, first.CacheStatus);
        Assert.AreEqual(CacheStatus.SKIPPED, second.CacheStatus);
        Assert.AreEqual(2, llm.CallCount);
        CollectionAssert.AreEqual(
            new[] { "Should I change my job?", "Should I leave my company?" },
            llm.ReceivedQuestions);
    }

    [TestMethod]
    public async Task PersonalizedGenerationKeepsRawQuestionAndSkipsCache()
    {
        const string question = "I have worked with my brother for 8 years and we are considering selling our company because he wants to move overseas. What should I do?";
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);

        var response = await service.GenerateAsync(
            TestSupport.DestinyRequest(question, readingMode: ReadingMode.DEEP),
            CancellationToken.None);

        Assert.AreEqual(CacheStatus.SKIPPED, response.CacheStatus);
        Assert.AreEqual(question, llm.ReceivedQuestions.Single());
    }

    [TestMethod]
    public async Task StandardReadingUsesRulesAndNeverCallsLlm()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);

        var response = await service.GenerateAsync(
            TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD),
            CancellationToken.None);

        Assert.AreEqual(0, llm.CallCount);
        Assert.AreEqual(ReadingMode.STANDARD, response.ReadingMode);
        Assert.AreEqual(GenerationSource.RULE_ENGINE, response.GenerationSource);
        Assert.IsNull(response.GenerationModel);
    }

    [TestMethod]
    public async Task LlmGateHonorsMaximumConcurrency()
    {
        var gate = new LlmGate(Microsoft.Extensions.Options.Options.Create(
            new LlmOptions { MaxConcurrency = 2 }));
        var active = 0;
        var maximum = 0;
        var sync = new object();

        var work = Enumerable.Range(0, 8).Select(_ => gate.RunAsync(async token =>
        {
            lock (sync)
            {
                active++;
                maximum = Math.Max(maximum, active);
            }

            await Task.Delay(30, token);

            lock (sync)
            {
                active--;
            }

            return true;
        }, CancellationToken.None));

        await Task.WhenAll(work);
        Assert.AreEqual(2, maximum);
    }

    private sealed class ThrowingQuestionClassifier : IQuestionClassifier
    {
        public Task<ClassificationResult> ClassifyAsync(
            string? question,
            string locale,
            CancellationToken cancellationToken) =>
            throw new AssertFailedException("DEEP readings must bypass the classifier.");

        public bool CanUseSharedCache(ClassificationResult classification) =>
            throw new AssertFailedException("DEEP readings must bypass classifier cache decisions.");
    }
}
