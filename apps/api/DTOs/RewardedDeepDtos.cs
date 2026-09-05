using System.ComponentModel.DataAnnotations;

namespace TarotDestiny.Api.DTOs;

public sealed record RewardedDeepStatusDto(
    bool Enabled,
    bool ServiceAvailable,
    string Provider,
    string? AdUnitPath,
    int ValidAdCompletions,
    int RequiredAdCompletions,
    int DeepCreditsPerCompletedBundle,
    int AvailableDeepCredits,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NextEligibleAt);

public sealed record RewardedAdAttemptDto(string Nonce, DateTimeOffset ExpiresAt);

public sealed record RewardedAdGrantRequestDto
{
    [Required, StringLength(4096, MinimumLength = 32)]
    public string Nonce { get; init; } = string.Empty;

    [Required, RegularExpression("^rewardedSlotGranted$")]
    public string EventType { get; init; } = string.Empty;
}

public sealed record CloseRewardedAdAttemptRequestDto
{
    [Required, StringLength(4096, MinimumLength = 32)]
    public string Nonce { get; init; } = string.Empty;
}

public sealed record RewardedDeepSettingsDto(
    int RequiredAdCompletions,
    int DeepCreditsPerCompletedBundle,
    int Revision,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

public sealed record UpdateRewardedDeepSettingsRequestDto
{
    [Range(1, 10)]
    public int RequiredAdCompletions { get; init; }

    [Range(1, 5)]
    public int DeepCreditsPerCompletedBundle { get; init; }

    [Range(0, int.MaxValue)]
    public int ExpectedRevision { get; init; }
}
