using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using TarotDestiny.Api.Contracts;

namespace TarotDestiny.Api.DTOs;

public sealed record CreateReadingJobDto
{
    public CreateReadingJobDto(string idempotencyKey, TarotReadingDto reading) { IdempotencyKey = idempotencyKey; Reading = reading; }
    [Required, StringLength(100, MinimumLength = 16)] public string IdempotencyKey { get; init; }
    [Required] public TarotReadingDto Reading { get; init; }
}
public sealed record ReadingJobDto(Guid JobId, string State, long EventId, DateTimeOffset Deadline,
    TarotReadingResponse? Reading, string? ErrorCode);
public sealed record ReadingPresenceDto(Guid SubscriberId, int HeartbeatSeconds);
public sealed record ReadingHeartbeatDto([Required] Guid SubscriberId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (SubscriberId == Guid.Empty) yield return new ValidationResult("A subscriber ID is required.", [nameof(SubscriberId)]);
    }
}
public sealed record WorkerAttemptDto([Required] Guid AttemptId) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (AttemptId == Guid.Empty) yield return new ValidationResult("An attempt ID is required.", [nameof(AttemptId)]);
    }
}
public sealed record WorkerClaimDto(string Decision, DateTimeOffset? LeaseUntil = null,
    string? ProviderRef = null, JsonElement? Request = null, string? Model = null);
public sealed record ReadingJobMetricsDto(int Queued, int Running, double OldestQueuedSeconds,
    int RequestsLastMinute, int ReservedTokensLastMinute, int PendingOutbox, int Failed, int Canceled);
// v1 messages contain identifiers only. Prompts are fetched after an authenticated, fenced claim.
public sealed record ReadingWorkMessage(int Version, string Kind, Guid JobId, Guid AttemptId,
    Guid CorrelationId, DateTimeOffset Deadline, string ProviderRef);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingResultMessage(int Version, string Kind, Guid JobId, Guid AttemptId,
    Guid CorrelationId, DateTimeOffset Deadline, string ProviderRef, string? Body, string? ErrorCode);
