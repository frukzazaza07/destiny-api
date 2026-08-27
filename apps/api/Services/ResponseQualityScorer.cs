using System.Text.RegularExpressions;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed record ResponseQualityAssessment(
    double Score,
    IReadOnlyList<string> Signals)
{
    public bool Meets(double minimumScore) => Score >= Math.Clamp(minimumScore, 0, 1);
}

public interface IReadingQualityScorer
{
    ResponseQualityAssessment Score(
        TarotReadingDto request,
        InterpretationPayload payload,
        TarotReadingResponse response);
}

public sealed partial class ReadingQualityScorer : IReadingQualityScorer
{
    private const double CompletenessWeight = 0.20;
    private const double LocaleWeight = 0.15;
    private const double CardGroundingWeight = 0.20;
    private const double DepthWeight = 0.15;
    private const double ActionabilityWeight = 0.15;
    private const double OriginalityWeight = 0.075;
    private const double SafetyWeight = 0.075;

    private static readonly string[] AbsolutistPhrases =
    [
        "will definitely",
        "is guaranteed",
        "certain to happen",
        "cannot fail",
        "fate has decided",
        "อย่างแน่นอนร้อยเปอร์เซ็นต์",
        "รับประกันว่าจะ",
        "โชคชะตากำหนดแล้ว"
    ];

    public ResponseQualityAssessment Score(
        TarotReadingDto request,
        InterpretationPayload payload,
        TarotReadingResponse response)
    {
        var signals = new List<string>();
        var completeness = ScoreCompleteness(response, signals);
        var locale = ScoreLocale(payload.Locale, response, signals);
        var grounding = ScoreCardGrounding(payload, response, signals);
        var depth = ScoreDepth(response, signals);
        var actionability = ScoreActionability(response, signals);
        var originality = ScoreOriginality(request.Question, response, signals);
        var safety = ScoreSafety(response, signals);

        var score =
            (completeness * CompletenessWeight) +
            (locale * LocaleWeight) +
            (grounding * CardGroundingWeight) +
            (depth * DepthWeight) +
            (actionability * ActionabilityWeight) +
            (originality * OriginalityWeight) +
            (safety * SafetyWeight);

        return new ResponseQualityAssessment(Math.Round(Math.Clamp(score, 0, 1), 4), signals);
    }

    private static double ScoreCompleteness(
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        bool[] checks =
        [
            HasText(response.Title),
            HasText(response.Summary),
            HasText(response.MainTheme),
            response.Cards is { Count: > 0 },
            response.Opportunities is { Count: > 0 },
            response.Challenges is { Count: > 0 },
            response.Guidance is { Count: > 0 },
            HasText(response.ReflectionQuestion),
            HasText(response.ClosingMessage)
        ];
        var score = checks.Count(check => check) / (double)checks.Length;
        if (score < 1)
        {
            signals.Add("missing_required_content");
        }

        return score;
    }

    private static double ScoreLocale(
        string locale,
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        if (!string.Equals(locale, "th", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var prose = JoinProse(response);
        var letterCount = prose.Count(char.IsLetter);
        var thaiCount = prose.Count(character => character is >= '\u0E00' and <= '\u0E7F');
        var score = letterCount == 0
            ? 0
            : Math.Clamp((double)thaiCount / Math.Max(20, letterCount / 2d), 0, 1);
        if (thaiCount < 20 || score < 0.8)
        {
            signals.Add("locale_mismatch");
        }

        return score;
    }

    private static double ScoreCardGrounding(
        InterpretationPayload payload,
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        if (payload.Cards.Count == 0 || response.Cards is null)
        {
            signals.Add("cards_not_grounded");
            return 0;
        }

        var score = 0d;
        for (var index = 0; index < payload.Cards.Count; index++)
        {
            if (index >= response.Cards.Count)
            {
                continue;
            }

            var seed = payload.Cards[index];
            var card = response.Cards[index];
            var identityMatches =
                string.Equals(seed.Position, card.Position, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(seed.CardId, card.CardId, StringComparison.OrdinalIgnoreCase) &&
                seed.Orientation == card.Orientation;
            if (!identityMatches)
            {
                continue;
            }

            score += 0.7;
            var interpretation = Normalize(card.Interpretation);
            var groundingTerms = seed.MeaningKeywords
                .Concat(seed.Keywords)
                .SelectMany(Tokenize)
                .Where(term => term.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            if (groundingTerms.Any(term => interpretation.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.3;
            }
        }

        score /= payload.Cards.Count;
        if (score < 0.7)
        {
            signals.Add("cards_not_grounded");
        }
        else if (score < 0.95)
        {
            signals.Add("weak_card_grounding");
        }

        return score;
    }

    private static double ScoreDepth(
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        var checks = new List<bool>
        {
            response.Title?.Trim().Length >= 4,
            response.Summary?.Trim().Length >= 30,
            response.MainTheme?.Trim().Length >= 15,
            response.ReflectionQuestion?.Trim().Length >= 12,
            response.ClosingMessage?.Trim().Length >= 15
        };
        checks.AddRange(response.Cards?.Select(card => card.Interpretation?.Trim().Length >= 30) ?? []);
        var score = checks.Count(check => check) / (double)Math.Max(1, checks.Count);
        if (score < 0.75)
        {
            signals.Add("insufficient_depth");
        }

        return score;
    }

    private static double ScoreActionability(
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        IReadOnlyList<IReadOnlyList<string>> groups =
        [
            response.Opportunities ?? [],
            response.Challenges ?? [],
            response.Guidance ?? []
        ];
        var score = groups.Average(group =>
        {
            var countScore = group.Count is >= 2 and <= 3 ? 1 : group.Count > 0 ? 0.5 : 0;
            var detailScore = group.Count == 0
                ? 0
                : group.Count(item => item?.Trim().Length >= 12) / (double)group.Count;
            return (countScore + detailScore) / 2;
        });
        if (score < 0.75)
        {
            signals.Add("weak_actionability");
        }

        return score;
    }

    private static double ScoreOriginality(
        string? question,
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        var sections = EnumerateProse(response)
            .Select(Normalize)
            .Where(section => section.Length > 0)
            .ToArray();
        var uniqueRatio = sections.Length == 0
            ? 0
            : sections.Distinct(StringComparer.OrdinalIgnoreCase).Count() / (double)sections.Length;

        var normalizedQuestion = Normalize(question);
        var echoesQuestion = normalizedQuestion.Length >= 12 && sections.Any(section =>
            section.Equals(normalizedQuestion, StringComparison.OrdinalIgnoreCase) ||
            section.Contains(normalizedQuestion, StringComparison.OrdinalIgnoreCase));
        if (echoesQuestion)
        {
            signals.Add("question_echo");
        }

        if (uniqueRatio < 0.9)
        {
            signals.Add("repetitive_content");
        }

        return Math.Clamp(uniqueRatio - (echoesQuestion ? 0.5 : 0), 0, 1);
    }

    private static double ScoreSafety(
        TarotReadingResponse response,
        ICollection<string> signals)
    {
        var prose = JoinProse(response);
        var hasAbsolutistClaim = AbsolutistPhrases.Any(phrase =>
            prose.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        if (hasAbsolutistClaim)
        {
            signals.Add("absolute_prediction_language");
            return 0;
        }

        return 1;
    }

    private static IEnumerable<string> EnumerateProse(TarotReadingResponse response) =>
        new[]
        {
            response.Title,
            response.Summary,
            response.MainTheme,
            response.ReflectionQuestion,
            response.ClosingMessage
        }
        .Concat(response.Cards?.Select(card => card.Interpretation) ?? [])
        .Concat(response.Opportunities ?? [])
        .Concat(response.Challenges ?? [])
        .Concat(response.Guidance ?? []);

    private static string JoinProse(TarotReadingResponse response) =>
        string.Join(" ", EnumerateProse(response));

    private static IEnumerable<string> Tokenize(string? value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Normalize(string? value) =>
        WhitespaceRegex().Replace(NonLetterOrNumberRegex().Replace(value ?? string.Empty, " "), " ")
            .Trim()
            .ToLowerInvariant();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonLetterOrNumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed class LlmResponseQualityException(
    ResponseQualityAssessment assessment) : InvalidOperationException(
        $"LLM response quality score {assessment.Score:F4} was below the configured threshold.")
{
    public ResponseQualityAssessment Assessment { get; } = assessment;
}
