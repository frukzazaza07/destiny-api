using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Contracts;

public static class RequestValidator
{
    public static IEnumerable<string> Validate(TarotReadingDto request)
    {
        if (!Enum.IsDefined(request.ReadingMode))
        {
            yield return "readingMode must be STANDARD or DEEP.";
        }

        var hasKnownSpread = SpreadIds.TryNormalize(request.Spread, out var normalizedSpread);
        if (string.IsNullOrWhiteSpace(request.Spread))
        {
            yield return "spread is required.";
        }
        else if (!hasKnownSpread)
        {
            yield return $"spread must be {SpreadIds.Daily1} or {SpreadIds.Destiny3}.";
        }

        if (string.IsNullOrWhiteSpace(request.Locale))
        {
            yield return "locale is required.";
        }
        else if (!LocaleIds.IsSupported(request.Locale))
        {
            yield return $"locale must be {LocaleIds.English} or {LocaleIds.Thai}.";
        }

        var cards = request.Cards;
        if (cards is null)
        {
            yield return "cards is required.";
            yield break;
        }

        if (cards.Count == 0)
        {
            yield return "at least one card is required.";
        }

        var expectedPositions = hasKnownSpread
            ? ReadingPositions.ForSpread(normalizedSpread)
            : null;
        if (expectedPositions is not null && cards.Count != expectedPositions.Count)
        {
            yield return $"{normalizedSpread} requires {expectedPositions.Count} selected card(s).";
        }

        var cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasDuplicateCard = false;

        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (card is null)
            {
                yield return $"card {i + 1} is required.";
                continue;
            }

            if (expectedPositions is not null
                && i < expectedPositions.Count
                && !string.Equals(card.Position, expectedPositions[i], StringComparison.OrdinalIgnoreCase))
            {
                yield return $"card {i + 1} must use position {expectedPositions[i]}.";
            }

            if (string.IsNullOrWhiteSpace(card.CardId))
            {
                yield return $"card {i + 1} cardId is required.";
            }
            else
            {
                if (!TarotCardIds.IsKnown(card.CardId))
                {
                    yield return $"card {i + 1} has unknown cardId '{card.CardId}'.";
                }

                if (!cardIds.Add(card.CardId))
                {
                    hasDuplicateCard = true;
                }
            }

            if (!Enum.IsDefined(card.Orientation))
            {
                yield return $"card {i + 1} has an invalid orientation.";
            }
        }

        if (hasDuplicateCard)
        {
            yield return "cards must not repeat in one spread.";
        }
    }
}
