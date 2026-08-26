using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface IReadingResponseValidator
{
    void Validate(TarotReadingDto request, TarotReadingResponse response);
}

public sealed class ReadingResponseValidator : IReadingResponseValidator
{
    public void Validate(TarotReadingDto request, TarotReadingResponse response)
    {
        if (request.Cards is null)
        {
            throw new InvalidOperationException("Reading request cards are required for response validation.");
        }

        var missing = new List<string>();
        var responseCards = response.Cards;
        if (string.IsNullOrWhiteSpace(response.Title)) missing.Add(nameof(response.Title));
        if (string.IsNullOrWhiteSpace(response.Summary)) missing.Add(nameof(response.Summary));
        if (string.IsNullOrWhiteSpace(response.MainTheme)) missing.Add(nameof(response.MainTheme));
        if (responseCards is null || responseCards.Count == 0) missing.Add(nameof(response.Cards));
        if (response.Opportunities is null || response.Opportunities.Count == 0) missing.Add(nameof(response.Opportunities));
        if (response.Challenges is null || response.Challenges.Count == 0) missing.Add(nameof(response.Challenges));
        if (response.Guidance is null || response.Guidance.Count == 0) missing.Add(nameof(response.Guidance));
        if (string.IsNullOrWhiteSpace(response.ReflectionQuestion)) missing.Add(nameof(response.ReflectionQuestion));
        if (string.IsNullOrWhiteSpace(response.ClosingMessage)) missing.Add(nameof(response.ClosingMessage));

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Reading response is missing required fields: {string.Join(", ", missing)}.");
        }

        if (responseCards!.Count != request.Cards.Count)
        {
            throw new InvalidOperationException("Reading response card count does not match the requested spread.");
        }

        if (response.ReadingMode != request.ReadingMode)
        {
            throw new InvalidOperationException("Reading response mode does not match the requested mode.");
        }

        if (request.ReadingMode == ReadingMode.STANDARD &&
            (response.GenerationSource != GenerationSource.RULE_ENGINE ||
             !string.IsNullOrWhiteSpace(response.GenerationModel)))
        {
            throw new InvalidOperationException("Standard reading response has invalid generation metadata.");
        }

        if (request.ReadingMode == ReadingMode.DEEP &&
            (response.GenerationSource != GenerationSource.LLM ||
             string.IsNullOrWhiteSpace(response.GenerationModel)))
        {
            throw new InvalidOperationException("Deep reading response has invalid generation metadata.");
        }

        for (var index = 0; index < request.Cards.Count; index++)
        {
            var expected = request.Cards[index];
            var actual = responseCards[index];
            if (!string.Equals(expected.Position, actual.Position, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expected.CardId, actual.CardId, StringComparison.OrdinalIgnoreCase) ||
                expected.Orientation != actual.Orientation ||
                string.IsNullOrWhiteSpace(actual.CardName) ||
                string.IsNullOrWhiteSpace(actual.Interpretation))
            {
                throw new InvalidOperationException($"Reading response card {index + 1} does not match the requested card.");
            }
        }
    }
}
