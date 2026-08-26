namespace TarotDestiny.Api.Domain;

public enum TarotDomain
{
    GENERAL,
    LOVE,
    CAREER,
    MONEY,
    FAMILY,
    PERSONAL_GROWTH
}

public enum PersonalizationLevel
{
    LOW,
    MEDIUM,
    HIGH
}

public enum Orientation
{
    UPRIGHT,
    REVERSED
}

public enum CacheStatus
{
    HIT,
    MISS,
    SKIPPED
}

public enum ReadingMode
{
    STANDARD,
    DEEP
}

public enum GenerationSource
{
    RULE_ENGINE,
    LLM
}

public static class SpreadIds
{
    public const string Daily1 = "DAILY_1";
    public const string Destiny3 = "DESTINY_3";

    public static bool TryNormalize(string? spread, out string normalizedSpread)
    {
        if (string.Equals(spread, Daily1, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSpread = Daily1;
            return true;
        }

        if (string.Equals(spread, Destiny3, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSpread = Destiny3;
            return true;
        }

        normalizedSpread = string.Empty;
        return false;
    }

    public static string Normalize(string? spread) =>
        TryNormalize(spread, out var normalizedSpread)
            ? normalizedSpread
            : throw new ArgumentException($"Unknown tarot spread '{spread}'.", nameof(spread));
}

public static class ReadingPositions
{
    public static readonly string[] Daily1 = ["GUIDANCE"];
    public static readonly string[] Destiny3 = ["PAST", "PRESENT", "DIRECTION"];

    public static IReadOnlyList<string> ForSpread(string spread) =>
        SpreadIds.Normalize(spread) switch
        {
            SpreadIds.Daily1 => Daily1,
            SpreadIds.Destiny3 => Destiny3,
            _ => throw new InvalidOperationException("Spread normalization returned an unsupported value.")
        };
}

public static class LocaleIds
{
    public const string English = "en";
    public const string Thai = "th";

    public static bool IsSupported(string? locale) =>
        string.Equals(locale, English, StringComparison.OrdinalIgnoreCase)
        || string.Equals(locale, Thai, StringComparison.OrdinalIgnoreCase);
}

public static class TarotCardIds
{
    private static readonly HashSet<string> KnownIds = BuildKnownIds();

    public static bool IsKnown(string? cardId) =>
        !string.IsNullOrWhiteSpace(cardId) && KnownIds.Contains(cardId);

    private static HashSet<string> BuildKnownIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "THE_FOOL",
            "THE_MAGICIAN",
            "THE_HIGH_PRIESTESS",
            "THE_EMPRESS",
            "THE_EMPEROR",
            "THE_HIEROPHANT",
            "THE_LOVERS",
            "THE_CHARIOT",
            "STRENGTH",
            "THE_HERMIT",
            "WHEEL_OF_FORTUNE",
            "JUSTICE",
            "THE_HANGED_MAN",
            "DEATH",
            "TEMPERANCE",
            "THE_DEVIL",
            "THE_TOWER",
            "THE_STAR",
            "THE_MOON",
            "THE_SUN",
            "JUDGEMENT",
            "THE_WORLD"
        };

        string[] ranks =
        [
            "ACE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN",
            "EIGHT", "NINE", "TEN", "PAGE", "KNIGHT", "QUEEN", "KING"
        ];
        string[] suits = ["WANDS", "CUPS", "SWORDS", "PENTACLES"];

        foreach (var suit in suits)
        {
            foreach (var rank in ranks)
            {
                ids.Add($"{rank}_OF_{suit}");
            }
        }

        return ids;
    }
}
