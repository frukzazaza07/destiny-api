using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.DTOs;

public sealed record HealthStatusDto(string Status, string Service);

public sealed record ReadingOptionsDto(
    IReadOnlyList<ReadingMode> Modes,
    DeepReadingAccess DeepReading);

public sealed record ApiErrorDto(string Message, string? UpgradeUrl = null);

public sealed class ValidationErrorsDto(
    IDictionary<string, string[]> errors) : Dictionary<string, string[]>(errors);
