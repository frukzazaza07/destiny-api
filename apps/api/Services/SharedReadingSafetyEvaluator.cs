using System.Text.Json;
using System.Text.RegularExpressions;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.Services;

public interface ISharedReadingSafetyEvaluator
{
    SharedContentSafetyResult EvaluateRequest(string? question);
    SharedContentSafetyResult EvaluateResponse(string? question, TarotReadingResponse response);
}

public sealed record SharedContentSafetyResult(bool IsSafe, string Reason)
{
    public static readonly SharedContentSafetyResult Safe = new(true, "SAFE");
}

public sealed partial class SharedReadingSafetyEvaluator : ISharedReadingSafetyEvaluator
{
    private static readonly string[] MultiDomainMarkers =
    [
        "love|relationship|partner|แฟน|ความรัก",
        "job|career|work|boss|งาน|บริษัท",
        "money|salary|debt|invest|เงิน|หนี้|ลงทุน",
        "family|parent|child|ครอบครัว|พ่อ|แม่"
    ];

    public SharedContentSafetyResult EvaluateRequest(string? question)
    {
        var text = Normalize(question);
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
        {
            return SharedContentSafetyResult.Safe;
        }

        if (EmailOrPhoneRegex().IsMatch(text)) return Unsafe("CONTACT_DETAIL");
        if (ExactNumberOrDateRegex().IsMatch(text)) return Unsafe("EXACT_NUMBER_OR_DATE");
        if (EmployerOrLocationRegex().IsMatch(text)) return Unsafe("NAMED_ORGANIZATION_OR_LOCATION");
        if (RelationshipHistoryRegex().IsMatch(text)) return Unsafe("UNIQUE_RELATIONSHIP_HISTORY");
        if (LikelyPersonalNameRegex().IsMatch(text)) return Unsafe("LIKELY_PERSONAL_NAME");
        if (text.Length > 180) return Unsafe("LONG_PERSONAL_CONTEXT");

        var domains = MultiDomainMarkers.Count(markers =>
            markers.Split('|').Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        return domains > 1 ? Unsafe("MULTI_DOMAIN") : SharedContentSafetyResult.Safe;
    }

    public SharedContentSafetyResult EvaluateResponse(string? question, TarotReadingResponse response)
    {
        var requestSafety = EvaluateRequest(question);
        if (!requestSafety.IsSafe)
        {
            return requestSafety;
        }

        var normalizedQuestion = Normalize(question);
        if (string.IsNullOrWhiteSpace(normalizedQuestion) || normalizedQuestion.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
        {
            return SharedContentSafetyResult.Safe;
        }

        var responseText = Normalize(JsonSerializer.Serialize(response));
        var distinctiveWords = WordRegex().Matches(normalizedQuestion)
            .Select(match => match.Value)
            .Where(word => word.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index <= distinctiveWords.Length - 4; index++)
        {
            var phrase = string.Join(' ', distinctiveWords.Skip(index).Take(4));
            if (responseText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return Unsafe("QUESTION_TEXT_REPEATED");
            }
        }

        return SharedContentSafetyResult.Safe;
    }

    private static string Normalize(string? value) =>
        WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();

    private static SharedContentSafetyResult Unsafe(string reason) => new(false, reason);

    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b|(?:\+?\d[\d\s().-]{7,}\d)", RegexOptions.IgnoreCase)]
    private static partial Regex EmailOrPhoneRegex();

    [GeneratedRegex(@"(?:\b\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?\b|\b\d+(?:[.,]\d+)?\s*(?:baht|บาท|usd|dollars?|years?|months?|days?|ปี|เดือน|วัน)\b|[$฿]\s*\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExactNumberOrDateRegex();

    [GeneratedRegex(@"\b(?:at|from|for|in)\s+[A-Z][\p{L}\p{M}'-]{2,}(?:\s+[A-Z][\p{L}\p{M}'-]{2,})?\b|(?:บริษัท|โรงเรียน|มหาวิทยาลัย|จังหวัด|อำเภอ)\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex EmployerOrLocationRegex();

    [GeneratedRegex(@"\b(?:my|our)\s+(?:husband|wife|boyfriend|girlfriend|ex|mother|father|brother|sister|child)\b|(?:สามี|ภรรยา|แฟนเก่า|พี่ชาย|น้องชาย|พี่สาว|น้องสาว|ลูกของฉัน)", RegexOptions.IgnoreCase)]
    private static partial Regex RelationshipHistoryRegex();

    [GeneratedRegex(@"\b(?:named|called)\s+[\p{L}\p{M}'-]{2,}\b|(?:ชื่อ|ชื่อว่า)\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LikelyPersonalNameRegex();

    [GeneratedRegex(@"[\p{L}\p{M}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
