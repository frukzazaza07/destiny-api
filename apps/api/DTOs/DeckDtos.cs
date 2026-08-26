using System.ComponentModel.DataAnnotations;

namespace TarotDestiny.Api.DTOs;

public sealed class ShuffleDeckDto
{
    [RegularExpression(
        "^(?i:DAILY_1|DESTINY_3)$",
        ErrorMessage = "spread must be DAILY_1 or DESTINY_3.")]
    public string? Spread { get; init; }
}

public sealed class ResolveDeckDto
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one selected index is required.")]
    public IReadOnlyList<int>? SelectedIndexes { get; init; }
}
