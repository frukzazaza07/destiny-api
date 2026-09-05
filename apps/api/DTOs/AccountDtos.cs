using System.ComponentModel.DataAnnotations;

namespace TarotDestiny.Api.DTOs;

public sealed record CsrfTokenDto(string Token);
public sealed record AccountMessageDto(string Message);

public sealed record RegisterRequestDto : IValidatableObject
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        PasswordRules.Validate(Password).Select(message => new ValidationResult(message, [nameof(Password)]));
}

public sealed record LoginRequestDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed record EmailRequestDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;
}

public sealed record TokenConfirmationRequestDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(512, MinimumLength = 32)]
    public string Token { get; init; } = string.Empty;
}

public sealed record PasswordResetConfirmationRequestDto : IValidatableObject
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(512, MinimumLength = 32)]
    public string Token { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        PasswordRules.Validate(NewPassword).Select(message => new ValidationResult(message, [nameof(NewPassword)]));
}

public sealed record DeleteAccountRequestDto
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public sealed record AccountStatusDto(
    bool Authenticated,
    Guid? UserId,
    string? Email,
    bool EmailVerified,
    IReadOnlyList<string> Roles,
    bool PremiumDeepActive,
    DateTimeOffset? PremiumExpiresAt,
    bool RegistrationEnabled = false,
    bool ServiceAvailable = true);

public sealed record AdminCreateUserRequestDto : IValidatableObject
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    public bool EmailVerified { get; init; } = true;
    public bool IsAdmin { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        PasswordRules.Validate(Password).Select(message => new ValidationResult(message, [nameof(Password)]));
}

public sealed record AdminAccountStatusRequestDto
{
    public bool Enabled { get; init; }
    [Range(0, int.MaxValue)]
    public int ExpectedRevision { get; init; }
}

public sealed record PremiumGrantRequestDto
{
    [Range(1, 3650)]
    public int Days { get; init; }

    [Range(0, int.MaxValue)]
    public int? ExpectedRevision { get; init; }
}

public sealed record PremiumRevokeRequestDto
{
    [Range(0, int.MaxValue)]
    public int ExpectedRevision { get; init; }
}

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    bool EmailVerified,
    bool Enabled,
    IReadOnlyList<string> Roles,
    bool PremiumDeepActive,
    DateTimeOffset? PremiumExpiresAt,
    int? PremiumRevision,
    int Revision,
    DateTimeOffset CreatedAt);

public sealed record AdminUserPageDto(IReadOnlyList<AdminUserDto> Items, int Total, int Page, int PageSize);

public sealed record EntitlementAuditDto(
    Guid Id,
    Guid EntitlementId,
    string Action,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    Guid ActorUserId,
    DateTimeOffset CreatedAt);

public sealed record EntitlementHistoryDto(IReadOnlyList<EntitlementAuditDto> Items, int Total, int Page, int PageSize);

internal static class PasswordRules
{
    public static IEnumerable<string> Validate(string password)
    {
        if (!password.Any(char.IsUpper)) yield return "Password must contain an uppercase letter.";
        if (!password.Any(char.IsLower)) yield return "Password must contain a lowercase letter.";
        if (!password.Any(char.IsDigit)) yield return "Password must contain a number.";
        if (!password.Any(character => !char.IsLetterOrDigit(character))) yield return "Password must contain a symbol.";
    }
}
