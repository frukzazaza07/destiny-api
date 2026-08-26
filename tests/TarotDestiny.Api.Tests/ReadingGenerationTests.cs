using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class ReadingGenerationTests
{
    [TestMethod]
    public async Task RepeatedEligibleRequestHitsCacheAndAvoidsLlm()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);

        var first = await service.GenerateAsync(request, CancellationToken.None);
        var second = await service.GenerateAsync(request, CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(first.CacheKey, second.CacheKey);
        Assert.AreEqual(1, llm.CallCount);
    }

    [TestMethod]
    public async Task SharedGenerationDoesNotSendRawQuestionToLlm()
    {
        var llm = new TrackingLlmClient();
        var service = TestSupport.NewReadingService(llm);

        var first = await service.GenerateAsync(
            TestSupport.DestinyRequest("Should I change my job?", readingMode: ReadingMode.DEEP),
            CancellationToken.None);
        var second = await service.GenerateAsync(
            TestSupport.DestinyRequest("Should I leave my company?", readingMode: ReadingMode.DEEP),
            CancellationToken.None);

        Assert.AreEqual(CacheStatus.MISS, first.CacheStatus);
        Assert.AreEqual(CacheStatus.HIT, second.CacheStatus);
        Assert.AreEqual(1, llm.CallCount);
        Assert.IsNull(llm.ReceivedQuestions.Single());
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
}
