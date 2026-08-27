using System.ComponentModel.DataAnnotations;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;

namespace TarotDestiny.Api.DTOs;

public sealed record SelectedCard
{
    public SelectedCard(string position, string cardId, Orientation orientation)
    {
        Position = position;
        CardId = cardId;
        Orientation = orientation;
    }

    [Required]
    public string Position { get; init; }

    [Required]
    public string CardId { get; init; }

    [EnumDataType(typeof(Orientation))]
    public Orientation Orientation { get; init; }
}

public sealed record TarotReadingDto : IValidatableObject
{
    public TarotReadingDto(
        string? question,
        string spread,
        string locale,
        IReadOnlyList<SelectedCard> cards,
        ReadingMode readingMode = ReadingMode.STANDARD,
        string? modelTier = null,
        int answerVariant = 1)
    {
        Question = question;
        Spread = spread;
        Locale = locale;
        Cards = cards;
        ReadingMode = readingMode;
        ModelTier = modelTier;
        AnswerVariant = answerVariant;
    }

    public string? Question { get; init; }

    [Required]
    public string Spread { get; init; }

    [Required]
    [StringLength(10, MinimumLength = 2)]
    public string Locale { get; init; }

    [Required]
    [MinLength(1)]
    public IReadOnlyList<SelectedCard> Cards { get; init; }

    [EnumDataType(typeof(ReadingMode))]
    public ReadingMode ReadingMode { get; init; }

    [StringLength(50)]
    public string? ModelTier { get; init; }

    [Range(1, 10)]
    public int AnswerVariant { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        RequestValidator.Validate(this)
            .Select(error => new ValidationResult(error));
}
