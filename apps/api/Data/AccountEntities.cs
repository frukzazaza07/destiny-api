namespace TarotDestiny.Api.Data;

public static class AccountRoles
{
    public const string User = "USER";
    public const string Admin = "ADMIN";
}

public static class EntitlementTypes
{
    public const string PremiumDeep = "PREMIUM_DEEP";
}

public static class AccountTokenPurposes
{
    public const string EmailVerification = "EMAIL_VERIFICATION";
    public const string PasswordReset = "PASSWORD_RESET";
}

public sealed class UserAccountEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<UserAccountRoleEntity> Roles { get; set; } = [];
    public ICollection<UserSessionEntity> Sessions { get; set; } = [];
    public ICollection<UserEntitlementEntity> Entitlements { get; set; } = [];
}

public sealed class AccountRoleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<UserAccountRoleEntity> Users { get; set; } = [];
}

public sealed class UserAccountRoleEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public UserAccountEntity User { get; set; } = null!;
    public AccountRoleEntity Role { get; set; } = null!;
}

public sealed class UserSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public UserAccountEntity User { get; set; } = null!;
}

public sealed class UserAccountTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public UserAccountEntity User { get; set; } = null!;
}

public sealed class UserEntitlementEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = EntitlementTypes.PremiumDeep;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Revision { get; set; }
    public UserAccountEntity User { get; set; } = null!;
    public UserAccountEntity GrantedByUser { get; set; } = null!;
}

public sealed class UserEntitlementAuditEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EntitlementId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
