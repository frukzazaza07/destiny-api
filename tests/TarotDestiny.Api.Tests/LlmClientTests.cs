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
        StringAssert.Contains(requestBody!, "response_format");
        StringAssert.Contains(requestBody!, "json_schema");
        StringAssert.Contains(requestBody!, "reasoning_effort");
        using var sentRequest = JsonDocument.Parse(requestBody!);
        var userContent = sentRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        StringAssert.Contains(userContent!, "\"CAREER\"");
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
