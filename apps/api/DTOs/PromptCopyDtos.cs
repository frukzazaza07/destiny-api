using System.ComponentModel.DataAnnotations;

namespace TarotDestiny.Api.DTOs;

public sealed record PromptCopyStatusDto(Guid ReadingId, int CompletedAds, int RequiredAds, bool Unlocked, bool Available);
public sealed record PromptCopyTextDto(string Prompt);
public sealed record PromptCopyAttemptDto(Guid AttemptId, DateTimeOffset ExpiresAt);
public sealed record PromptCopyCallbackDto
{
    [Required] public Guid? AttemptId { get; init; }
    [Required, StringLength(200, MinimumLength = 1)] public string EventId { get; init; } = "";
    [Required] public long? Timestamp { get; init; }
}
