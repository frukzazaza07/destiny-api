using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class RequestValidationTests
{
    [TestMethod]
    public void RejectsUnknownSpreadLocaleAndCard()
    {
        var request = TestSupport.DestinyRequest() with
        {
            Spread = "UNKNOWN",
            Locale = "fr",
            Cards = [new("PAST", "NOT_A_CARD", Orientation.UPRIGHT)]
        };

        var errors = RequestValidator.Validate(request).ToArray();

        Assert.IsTrue(errors.Any(error => error.Contains("spread", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("locale", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(error => error.Contains("unknown cardId", StringComparison.OrdinalIgnoreCase)));
    }
}

[TestClass]
public sealed class ReadingResponseValidatorTests
{
    [TestMethod]
    public void RejectsCardThatDoesNotMatchRequest()
    {
        var request = TestSupport.DestinyRequest();
        var response = TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification());
        var changed = response with
        {
            Cards =
            [
                response.Cards[0] with { Orientation = Orientation.REVERSED },
                response.Cards[1],
                response.Cards[2]
            ]
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ReadingResponseValidator().Validate(request, changed));
    }

    [TestMethod]
    public void RejectsMissingStructuredSections()
    {
        var request = TestSupport.DestinyRequest();
        var response = TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification()) with
        {
            Guidance = []
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ReadingResponseValidator().Validate(request, response));
    }

    [TestMethod]
    public void RejectsLlmMetadataForStandardReading()
    {
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD);
        var response = TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification()) with
        {
            GenerationSource = GenerationSource.LLM,
            GenerationModel = "qwen3:8b"
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ReadingResponseValidator().Validate(request, response));
    }

    [TestMethod]
    public void RejectsRuleEngineMetadataForDeepReading()
    {
        var request = TestSupport.DestinyRequest(readingMode: ReadingMode.DEEP);
        var response = TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification()) with
        {
            GenerationSource = GenerationSource.RULE_ENGINE,
            GenerationModel = null
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ReadingResponseValidator().Validate(request, response));
    }
}

[TestClass]
public sealed class DeckServiceTests
{
    [TestMethod]
    public void UsesSeventyEightPhysicalCards()
    {
        var session = new DeckService(new TarotCatalog()).CreateSession(SpreadIds.Destiny3);

        Assert.AreEqual(78, session.CardCount);
        Assert.AreEqual(3, session.SelectCount);
    }

    [TestMethod]
    public void ResolveIsIdempotentForSameSelection()
    {
        var service = new DeckService(new TarotCatalog());
        var session = service.CreateSession(SpreadIds.Destiny3);

        var first = service.Resolve(session.SessionId, [0, 1, 2]);
        var second = service.Resolve(session.SessionId, [0, 1, 2]);

        CollectionAssert.AreEqual(first!.Cards.ToArray(), second!.Cards.ToArray());
    }

    [TestMethod]
    public void RejectsDuplicateAndOutOfRangeSelections()
    {
        var service = new DeckService(new TarotCatalog());
        var session = service.CreateSession(SpreadIds.Destiny3);

        Assert.ThrowsExactly<ArgumentException>(() => service.Resolve(session.SessionId, [0, 0, 1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => service.Resolve(session.SessionId, [0, 1, 78]));
    }
}

[TestClass]
public sealed class TarotCatalogTests
{
    [TestMethod]
    public void AllSeventyEightCardsHaveBilingualOrientationMeanings()
    {
        var catalog = new TarotCatalog();

        Assert.AreEqual(78, catalog.AllCards.Count);
        foreach (var card in catalog.AllCards)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.English.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.English.Upright.Summary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.English.Reversed.Summary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.Thai.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.Thai.Upright.Summary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.Thai.Reversed.Summary));
        }
    }
}
