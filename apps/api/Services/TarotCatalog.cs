using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;

namespace TarotDestiny.Api.Services;

public sealed record LocalizedOrientationMeaning(
    string Summary,
    IReadOnlyList<string> Keywords);

public sealed record LocalizedCardMeaning(
    string Name,
    LocalizedOrientationMeaning Upright,
    LocalizedOrientationMeaning Reversed)
{
    internal static LocalizedCardMeaning Empty { get; } = new(
        string.Empty,
        new LocalizedOrientationMeaning(string.Empty, []),
        new LocalizedOrientationMeaning(string.Empty, []));
}

public sealed record TarotCardMeaning(
    string Id,
    string Name,
    string Arcana,
    string Element,
    IReadOnlyList<string> UprightKeywords,
    IReadOnlyList<string> ReversedKeywords)
{
    public LocalizedCardMeaning English { get; init; } = LocalizedCardMeaning.Empty;
    public LocalizedCardMeaning Thai { get; init; } = LocalizedCardMeaning.Empty;

    public LocalizedCardMeaning ForLocale(string? locale) =>
        locale?.StartsWith("th", StringComparison.OrdinalIgnoreCase) == true ? Thai : English;

    public LocalizedOrientationMeaning ForOrientation(string? locale, Orientation orientation)
    {
        var localized = ForLocale(locale);
        return orientation == Orientation.UPRIGHT ? localized.Upright : localized.Reversed;
    }
}

public interface ITarotCatalog
{
    IReadOnlyList<TarotCardMeaning> AllCards { get; }
    TarotCardMeaning Get(string cardId);
}

public sealed class TarotCatalog : ITarotCatalog
{
    private readonly Dictionary<string, TarotCardMeaning> _cards;

    public TarotCatalog()
    {
        _cards = BuildCards().ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);

        if (_cards.Count != 78)
        {
            throw new InvalidOperationException($"Tarot master data must contain 78 unique cards; found {_cards.Count}.");
        }

        AllCards = _cards.Values.OrderBy(card => card.Id).ToArray();
    }

    public IReadOnlyList<TarotCardMeaning> AllCards { get; }

    public TarotCardMeaning Get(string cardId) =>
        _cards.TryGetValue(cardId, out var card)
            ? card
            : throw new InvalidOperationException($"Unknown tarot card id '{cardId}'.");

    private static IEnumerable<TarotCardMeaning> BuildCards()
    {
        foreach (var definition in TarotMeaningData.Cards)
        {
            var englishUprightKeywords = SplitKeywords(definition.EnglishUprightKeywords);
            var englishReversedKeywords = SplitKeywords(definition.EnglishReversedKeywords);

            yield return new TarotCardMeaning(
                definition.Id,
                definition.EnglishName,
                definition.Arcana,
                definition.Element,
                englishUprightKeywords,
                englishReversedKeywords)
            {
                English = new LocalizedCardMeaning(
                    definition.EnglishName,
                    new LocalizedOrientationMeaning(definition.EnglishUpright, englishUprightKeywords),
                    new LocalizedOrientationMeaning(definition.EnglishReversed, englishReversedKeywords)),
                Thai = new LocalizedCardMeaning(
                    definition.ThaiName,
                    new LocalizedOrientationMeaning(definition.ThaiUpright, SplitKeywords(definition.ThaiUprightKeywords)),
                    new LocalizedOrientationMeaning(definition.ThaiReversed, SplitKeywords(definition.ThaiReversedKeywords)))
            };
        }
    }

    private static IReadOnlyList<string> SplitKeywords(string keywords) =>
        keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
