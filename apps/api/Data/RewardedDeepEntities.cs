namespace TarotDestiny.Api.Data;

public static class RewardedDeepConstants
{
    public const int SettingsId = 1;
    public const int MaximumRequiredAdCompletions = 10;
    public const int MaximumCreditsPerBundle = 5;
    public const string GrantedEvent = "rewardedSlotGranted";
}

public sealed class RewardedDeepSettingsEntity
{
    public int Id { get; set; } = RewardedDeepConstants.SettingsId;
    public int RequiredAdCompletions { get; set; } = 3;
    public int DeepCreditsPerCompletedBundle { get; set; } = 1;
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class RewardedDeepSessionEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? AnonymousTokenHash { get; set; }
    public int RequiredAdCompletions { get; set; }
    public int DeepCreditsPerCompletedBundle { get; set; }
    public int ValidAdCompletions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Revision { get; set; }
    public UserAccountEntity? User { get; set; }
    public ICollection<RewardedAdAttemptEntity> Attempts { get; set; } = [];
    public ICollection<RewardedDeepCreditEntity> Credits { get; set; } = [];
}

public sealed class RewardedAdAttemptEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string NonceHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public RewardedDeepSessionEntity Session { get; set; } = null!;
}

public sealed class RewardedDeepCreditEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ReservationId { get; set; }
    public DateTimeOffset? ReservedAt { get; set; }
    public DateTimeOffset? ReservationExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int Revision { get; set; }
    public RewardedDeepSessionEntity Session { get; set; } = null!;
    public UserAccountEntity? User { get; set; }
}
