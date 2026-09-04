using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class LlmClientTests
{
    [TestMethod]
    public async Task ParsesStructuredJsonAndKeepsAuthoritativeCardIdentity()
    {
        string? requestBody = null;
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var content = JsonSerializer.Serialize(new
        {
            title = "A title",
            summary = "A summary",
            mainTheme = "A theme",
            cards = request.Cards.Select(card => new
            {
                position = card.Position,
                cardId = card.CardId,
                interpretation = $"Meaning for {card.Position}"
            }),
            opportunities = new[] { "Opportunity" },
            challenges = new[] { "Challenge" },
            guidance = new[] { "Guidance" },
            reflectionQuestion = "A question?",
            closingMessage = "A closing"
        });
        var handler = new StubHandler(async httpRequest =>
        {
            requestBody = await httpRequest.Content!.ReadAsStringAsync();
            return OpenAiResponse(content);
        });
        var client = NewClient(handler);

        var response = await client.GenerateAsync(request, classification, payload, CancellationToken.None);

        Assert.AreEqual(request.Cards[1].Orientation, response.Cards[1].Orientation);
        Assert.AreEqual(request.Cards[1].CardId, response.Cards[1].CardId);
        Assert.AreEqual(ReadingMode.DEEP, response.ReadingMode);
        Assert.AreEqual(GenerationSource.LLM, response.GenerationSource);
        Assert.AreEqual("test-model", response.GenerationModel);
        Assert.AreEqual("DEEP", response.ModelTier);
        Assert.AreEqual("legacy", response.InferenceWorker);
        Assert.AreEqual("LOCAL_GPU", response.InferenceProvider);
        Assert.AreEqual("CONTROL", response.PromptVariant);
        Assert.IsNotNull(response.QualityScore);
        StringAssert.Contains(requestBody!, "response_format");
        StringAssert.Contains(requestBody!, "json_schema");
        StringAssert.Contains(requestBody!, "reasoning_effort");
        using var sentRequest = JsonDocument.Parse(requestBody!);
        var userContent = sentRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        using var userInput = JsonDocument.Parse(userContent!);
        var properties = userInput.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "question", "cards" }, properties);
        Assert.AreEqual(request.Question, userInput.RootElement.GetProperty("question").GetString());
        var sentCards = userInput.RootElement.GetProperty("cards");
        Assert.AreEqual(request.Cards.Count, sentCards.GetArrayLength());
        Assert.AreEqual(request.Cards[0].CardId, sentCards[0].GetProperty("cardId").GetString());
        var cardProperties = sentCards[0].EnumerateObject().Select(property => property.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "position", "cardId", "orientation" }, cardProperties);
        Assert.IsFalse(sentCards[0].TryGetProperty("meaning", out _));
        Assert.IsFalse(sentCards[0].TryGetProperty("cardName", out _));
        Assert.IsFalse(userInput.RootElement.TryGetProperty("classification", out _));
        Assert.IsFalse(userInput.RootElement.TryGetProperty("payload", out _));
    }

    [TestMethod]
    public async Task RejectsMalformedLlmJson()
    {
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var client = NewClient(new StubHandler(_ => Task.FromResult(OpenAiResponse("not-json"))));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GenerateAsync(request, classification, payload, CancellationToken.None));
    }

    [TestMethod]
    public void RuleRendererUsesRequestedThaiLocale()
    {
        var request = TestSupport.DestinyRequest(locale: "th");
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var response = new RuleReadingRenderer().Render(classification, payload);

        Assert.AreEqual("ภาพรวมของไพ่พร้อมให้คุณพิจารณา", response.Title);
        Assert.AreEqual("เดอะทาวเวอร์", response.Cards[0].CardName);
        StringAssert.Contains(response.ClosingMessage, "ไม่ใช่คำทำนาย");
    }

    [TestMethod]
    public async Task DeepReadingRequiresConfiguredEndpoint()
    {
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var client = new LlmClient(
            new HttpClient(),
            Options.Create(new LlmOptions { Endpoint = null }),
            new TarotMetrics(),
            TestSupport.LoggerFactory.CreateLogger<LlmClient>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GenerateAsync(request, classification, payload, CancellationToken.None));
    }

    [TestMethod]
    public async Task RejectsEnglishOnlyContentForThaiDeepReading()
    {
        var request = TestSupport.DestinyRequest(locale: "th", readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var content = JsonSerializer.Serialize(new
        {
            title = "English title",
            summary = "English summary",
            mainTheme = "English theme",
            cards = request.Cards.Select(card => new
            {
                position = card.Position,
                cardId = card.CardId,
                interpretation = "English interpretation"
            }),
            opportunities = new[] { "English opportunity" },
            challenges = new[] { "English challenge" },
            guidance = new[] { "English guidance" },
            reflectionQuestion = "English question?",
            closingMessage = "English closing"
        });
        var client = NewClient(new StubHandler(_ => Task.FromResult(OpenAiResponse(content))));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GenerateAsync(request, classification, payload, CancellationToken.None));
    }

    [TestMethod]
    public async Task FailsOverAcrossLocalGpuWorkersBeforeCloud()
    {
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP) with { Question = null };
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var called = new List<string>();
        var content = ValidContent(request);
        var handler = new StubHandler(message =>
        {
            called.Add(message.RequestUri!.Host);
            return Task.FromResult(message.RequestUri.Host == "primary.test"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : OpenAiResponse(content));
        });
        var client = NewRoutedClient(handler,
        [
            Worker("primary", "http://primary.test/v1", LlmWorkerProvider.LOCAL_GPU, 0),
            Worker("secondary", "http://secondary.test/v1", LlmWorkerProvider.LOCAL_GPU, 1),
            Worker("cloud", "http://cloud.test/v1", LlmWorkerProvider.CLOUD_GPU, 100)
        ], enableCloud: true);

        var response = await client.GenerateAsync(request, classification, payload, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "primary.test", "secondary.test" }, called);
        Assert.AreEqual("secondary", response.InferenceWorker);
        Assert.AreEqual("LOCAL_GPU", response.InferenceProvider);
    }

    [TestMethod]
    public async Task UsesCloudGpuOnlyAsExplicitFallback()
    {
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP) with { Question = null };
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var called = new List<string>();
        var content = ValidContent(request);
        var handler = new StubHandler(message =>
        {
            called.Add(message.RequestUri!.Host);
            return Task.FromResult(message.RequestUri.Host == "local.test"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : OpenAiResponse(content));
        });
        var client = NewRoutedClient(handler,
        [
            Worker("local", "http://local.test/v1", LlmWorkerProvider.LOCAL_GPU, 0),
            Worker("cloud", "http://cloud.test/v1", LlmWorkerProvider.CLOUD_GPU, 0)
        ], enableCloud: true);

        var response = await client.GenerateAsync(request, classification, payload, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "local.test", "cloud.test" }, called);
        Assert.AreEqual("cloud", response.InferenceWorker);
        Assert.AreEqual("CLOUD_GPU", response.InferenceProvider);
    }

    [TestMethod]
    public async Task BypassesLocalWorkersAndUsesCloudDirectlyWhenExplicitlyEnabled()
    {
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var called = new List<string>();
        var content = ValidContent(request);
        var handler = new StubHandler(message =>
        {
            called.Add(message.RequestUri!.Host);
            return Task.FromResult(OpenAiResponse(content));
        });
        var client = NewRoutedClient(handler,
        [
            Worker("local", "http://local.test/v1", LlmWorkerProvider.LOCAL_GPU, 0),
            Worker("cloud", "http://cloud.test/v1", LlmWorkerProvider.CLOUD_GPU, 100)
        ], enableCloud: true, bypassLocal: true, allowCloudForRawQuestion: true);

        var response = await client.GenerateAsync(request, classification, payload, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "cloud.test" }, called);
        Assert.AreEqual("cloud", response.InferenceWorker);
        Assert.AreEqual("CLOUD_GPU", response.InferenceProvider);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task BypassFailsSafelyWhenCloudIsNotEligible(
        bool enableCloud,
        bool allowCloudForRawQuestion)
    {
        var request = TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP);
        var classification = TestSupport.CareerChangeClassification();
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, classification);
        var called = new List<string>();
        var content = ValidContent(request);
        var handler = new StubHandler(message =>
        {
            called.Add(message.RequestUri!.Host);
            return Task.FromResult(OpenAiResponse(content));
        });
        var client = NewRoutedClient(handler,
        [
            Worker("local", "http://local.test/v1", LlmWorkerProvider.LOCAL_GPU, 0),
            Worker("cloud", "http://cloud.test/v1", LlmWorkerProvider.CLOUD_GPU, 100)
        ], enableCloud, bypassLocal: true, allowCloudForRawQuestion);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.GenerateAsync(request, classification, payload, CancellationToken.None));

        Assert.AreEqual("No healthy LLM worker is currently eligible for this reading.", exception.Message);
        Assert.AreEqual(0, called.Count);
    }

    private static LlmClient NewClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new LlmOptions
            {
                Endpoint = "http://llm.test/v1/chat/completions",
                Model = "test-model",
                RetryCount = 0,
                TimeoutSeconds = 5
            }),
            new TarotMetrics(),
            TestSupport.LoggerFactory.CreateLogger<LlmClient>());

    private static LlmClient NewRoutedClient(
        HttpMessageHandler handler,
        List<LlmWorkerOptions> workers,
        bool enableCloud,
        bool bypassLocal = false,
        bool allowCloudForRawQuestion = false) =>
        new(
            new HttpClient(handler),
            Options.Create(new LlmOptions
            {
                RetryCount = 0,
                TimeoutSeconds = 5,
                EnableCloudFallback = enableCloud,
                BypassLocalWorkers = bypassLocal,
                AllowCloudForRequestsWithRawQuestion = allowCloudForRawQuestion,
                DefaultTier = "CORE",
                Tiers =
                [
                    new LlmTierOptions
                    {
                        Id = "CORE",
                        Model = "test-model",
                        CacheModelVersion = "test-model-v1",
                        Workers = workers
                    }
                ]
            }),
            new TarotMetrics(),
            TestSupport.LoggerFactory.CreateLogger<LlmClient>());

    private static LlmWorkerOptions Worker(
        string id,
        string endpoint,
        LlmWorkerProvider provider,
        int priority) => new()
        {
            Id = id,
            Endpoint = endpoint,
            Model = "test-model",
            Provider = provider,
            Priority = priority,
            MaxConcurrency = 1
        };

    private static string ValidContent(TarotDestiny.Api.DTOs.TarotReadingDto request) =>
        JsonSerializer.Serialize(new
        {
            title = "A grounded direction",
            summary = "A sufficiently detailed reflective summary for this Tarot reading.",
            mainTheme = "Practical change and thoughtful renewal",
            cards = request.Cards.Select(card => new
            {
                position = card.Position,
                cardId = card.CardId,
                interpretation = $"A sufficiently detailed interpretation grounded in {card.CardId} for {card.Position}."
            }),
            opportunities = new[] { "Consider one concrete next step", "Notice useful support" },
            challenges = new[] { "Avoid rushing the decision", "Name the uncertainty" },
            guidance = new[] { "Write down practical options", "Review the tradeoffs" },
            reflectionQuestion = "Which next step best respects your priorities?",
            closingMessage = "Use the cards as a reflective guide while keeping your agency."
        });

    private static HttpResponseMessage OpenAiResponse(string content)
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
