using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace TarotDestiny.Api.DTOs;

public sealed record ThaiAstrologyReadingDto : IValidatableObject
{
    [Required, RegularExpression("^THAI_ASTROLOGY$")]
    public string ReadingType { get; init; } = "THAI_ASTROLOGY";
    /// <summary>Required Gregorian date, YYYY-MM-DD. Future dates are rejected.</summary>
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$")]
    public string BirthDate { get; init; } = "";
    /// <summary>Optional local 24-hour HH:mm; null means unknown. No timezone is assumed.</summary>
    [StringLength(5)] public string? BirthTime { get; init; }
    /// <summary>Optional customer location text, up to 200 characters; no geocoding is performed.</summary>
    [StringLength(200)] public string? BirthPlace { get; init; }
    [Required, StringLength(2000)] public string Question { get; init; } = "";
    /// <summary>th or en. When omitted, the question's main Thai/Latin script resolves the language; otherwise th (application default).</summary>
    [RegularExpression("^(th|en)$")] public string? Locale { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Locale is not null && Locale is not ("th" or "en"))
            yield return new("Locale must be th or en when supplied.", [nameof(Locale)]);
        var clock = context.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        if (!DateOnly.TryParseExact(BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            yield return new("A real, non-future Gregorian birthdate is required.", [nameof(BirthDate)]);
        if (!string.IsNullOrWhiteSpace(BirthTime) && !TimeOnly.TryParseExact(BirthTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            yield return new("Birth time must use HH:mm (24-hour local time).", [nameof(BirthTime)]);
    }

    public ThaiAstrologyReadingDto Normalize() => this with {
        Question = Question.Trim(), BirthPlace = string.IsNullOrWhiteSpace(BirthPlace) ? null : BirthPlace.Trim(),
        BirthTime = string.IsNullOrWhiteSpace(BirthTime) ? null : BirthTime.Trim(),
        Locale = Locale ?? (Question.Count(c => char.IsAsciiLetter(c)) > Question.Count(c => c is >= '\u0e01' and <= '\u0e5b') ? "en" : "th")
    };
}

public sealed record CreateThaiAstrologyJobDto
{
    [Required, StringLength(100, MinimumLength = 16)] public string IdempotencyKey { get; init; } = "";
    [Required] public ThaiAstrologyReadingDto Reading { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ThaiAstrologyOutput(string Overview, string Analysis, string DirectAnswer,
    string? Timing, string TimingExplanation, string Advice, string DataLimitations);
public sealed record ThaiAstrologyResponse(string ReadingType, string Locale, string BirthDate,
    string? BirthTime, string? BirthPlace, string PromptVersion, string SchemaVersion,
    string CalculationVersion, ThaiAstrologyOutput Sections);
