using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public enum AccountResultCode
{
    Success,
    InvalidCredentials,
    LockedOut,
    RegistrationDisabled,
    DuplicateEmail,
    NotFound,
    Conflict,
    InvalidToken,
    Unavailable
}

public sealed record AccountResult<T>(AccountResultCode Code, T? Value = default)
{
    public bool Succeeded => Code == AccountResultCode.Success;
}

public sealed record LoginSession(AccountStatusDto Account, Guid SessionId, DateTimeOffset ExpiresAt);

public sealed record AuthenticatedAccount(
    Guid UserId,
    Guid SessionId,
    string Email,
    bool EmailVerified,
    IReadOnlyList<string> Roles,
    DateTimeOffset? PremiumExpiresAt)
{
    public bool PremiumDeepActive => PremiumExpiresAt > DateTimeOffset.UtcNow;
}

public interface IAccountNotificationSender
{
    Task SendEmailVerificationAsync(string email, string token, CancellationToken cancellationToken);
    Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken);
}

public interface IAccountService
{
    bool Available { get; }
    bool PublicRegistrationEnabled { get; }
    Task<AccountResult<AccountStatusDto>> RegisterAsync(string email, string password, CancellationToken cancellationToken);
    Task<AccountResult<LoginSession>> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<AuthenticatedAccount?> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken);
    Task<AccountResult<AccountStatusDto>> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);
    Task<AccountResult<AccountStatusDto>> ResetPasswordAsync(string email, string token, string password, CancellationToken cancellationToken);
    Task<AccountResult<bool>> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserDto>> CreateUserAsync(AdminCreateUserRequestDto request, Guid actorUserId, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserPageDto>> ListUsersAsync(string? query, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserDto>> SetUserEnabledAsync(Guid userId, bool enabled, int expectedRevision, Guid actorUserId, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserDto>> GrantPremiumAsync(Guid userId, PremiumGrantRequestDto request, Guid actorUserId, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserDto>> RevokePremiumAsync(Guid userId, PremiumRevokeRequestDto request, Guid actorUserId, CancellationToken cancellationToken);
    Task<AccountResult<EntitlementHistoryDto>> GetEntitlementHistoryAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<AccountResult<AdminUserDto>> BootstrapAdminAsync(string email, string password, CancellationToken cancellationToken);
}

public sealed class AccountService(
    TarotDbContext database,
    IPasswordHasher<UserAccountEntity> passwordHasher,
    IAccountNotificationSender notifications,
    IOptions<AccountOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountService> logger) : IAccountService
{
    private readonly AccountOptions _options = options.Value;

    public bool Available => _options.Enabled;
    public bool PublicRegistrationEnabled => _options.Enabled && _options.PublicRegistrationEnabled;

    public async Task<AccountResult<AccountStatusDto>> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (!PublicRegistrationEnabled)
        {
            return new(AccountResultCode.RegistrationDisabled);
        }

        var created = await CreateUserCoreAsync(email, password, emailVerified: false, isAdmin: false, cancellationToken);
        if (!created.Succeeded || created.Value is null)
        {
            return new(created.Code);
        }

        await IssueAndSendTokenAsync(created.Value.Id, created.Value.Email, AccountTokenPurposes.EmailVerification, cancellationToken);
        return new(AccountResultCode.Success, ToAccountStatus(created.Value));
    }

    public async Task<AccountResult<LoginSession>> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new(AccountResultCode.Unavailable);
        }

        var normalizedEmail = NormalizeEmail(email);
        var user = await database.Users
            .Include(item => item.Roles).ThenInclude(item => item.Role)
            .Include(item => item.Entitlements)
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (user is null || user.DisabledAt is not null)
        {
            _ = passwordHasher.HashPassword(new UserAccountEntity(), password);
            return new(AccountResultCode.InvalidCredentials);
        }

        if (user.LockoutEnd > now)
        {
            return new(AccountResultCode.LockedOut);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= _options.MaxFailedAccessAttempts)
            {
                user.LockoutEnd = now.AddMinutes(_options.LockoutMinutes);
                user.AccessFailedCount = 0;
            }
            user.Revision++;
            user.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
            return new(AccountResultCode.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.Revision++;
        user.UpdatedAt = now;

        var session = new UserSessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SecurityStamp = user.SecurityStamp,
            CreatedAt = now,
            ExpiresAt = now.AddHours(_options.SessionHours)
        };
        database.UserSessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);

        var account = ToAccountStatus(ToAuthenticatedAccount(user, session.Id, now));
        return new(AccountResultCode.Success, new LoginSession(account, session.Id, session.ExpiresAt));
    }

    public async Task<AuthenticatedAccount?> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var session = await database.UserSessions
            .Include(item => item.User).ThenInclude(item => item.Roles).ThenInclude(item => item.Role)
            .Include(item => item.User).ThenInclude(item => item.Entitlements)
            .SingleOrDefaultAsync(item => item.Id == sessionId && item.UserId == userId, cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now ||
            session.User.DisabledAt is not null || session.SecurityStamp != session.User.SecurityStamp)
        {
            return null;
        }

        if (session.LastSeenAt is null || session.LastSeenAt < now.AddMinutes(-5))
        {
            session.LastSeenAt = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        return ToAuthenticatedAccount(session.User, session.Id, now);
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await database.UserSessions.FindAsync([sessionId], cancellationToken);
        if (session is not null && session.RevokedAt is null)
        {
            session.RevokedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(email, cancellationToken);
        if (user is not null && user.DisabledAt is null && user.EmailVerifiedAt is null)
        {
            await IssueAndSendTokenAsync(user.Id, user.Email, AccountTokenPurposes.EmailVerification, cancellationToken);
        }
    }

    public async Task<AccountResult<AccountStatusDto>> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken)
    {
        var result = await ConsumeTokenAsync(email, token, AccountTokenPurposes.EmailVerification, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return new(result.Code);
        }

        var now = timeProvider.GetUtcNow();
        result.Value.EmailVerifiedAt ??= now;
        result.Value.SecurityStamp = NewSecurityStamp();
        result.Value.UpdatedAt = now;
        result.Value.Revision++;
        await RevokeSessionsAsync(result.Value.Id, now, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(AccountResultCode.InvalidToken);
        }
        return new(AccountResultCode.Success, ToAccountStatus(result.Value));
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(email, cancellationToken);
        if (user is not null && user.DisabledAt is null)
        {
            await IssueAndSendTokenAsync(user.Id, user.Email, AccountTokenPurposes.PasswordReset, cancellationToken);
        }
    }

    public async Task<AccountResult<AccountStatusDto>> ResetPasswordAsync(string email, string token, string password, CancellationToken cancellationToken)
    {
        var result = await ConsumeTokenAsync(email, token, AccountTokenPurposes.PasswordReset, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return new(result.Code);
        }

        var now = timeProvider.GetUtcNow();
        result.Value.PasswordHash = passwordHasher.HashPassword(result.Value, password);
        result.Value.SecurityStamp = NewSecurityStamp();
        result.Value.AccessFailedCount = 0;
        result.Value.LockoutEnd = null;
        result.Value.UpdatedAt = now;
        result.Value.Revision++;
        await RevokeSessionsAsync(result.Value.Id, now, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(AccountResultCode.InvalidToken);
        }
        return new(AccountResultCode.Success, ToAccountStatus(result.Value));
    }

    public async Task<AccountResult<bool>> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await database.Users.FindAsync([userId], cancellationToken);
        if (user is null || user.DisabledAt is not null ||
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return new(AccountResultCode.InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow();
        user.DisabledAt = now;
        user.SecurityStamp = NewSecurityStamp();
        user.UpdatedAt = now;
        user.Revision++;
        await RevokeSessionsAsync(user.Id, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return new(AccountResultCode.Success, true);
    }

    public async Task<AccountResult<AdminUserDto>> CreateUserAsync(AdminCreateUserRequestDto request, Guid actorUserId, CancellationToken cancellationToken) =>
        await CreateUserCoreAsync(request.Email, request.Password, request.EmailVerified, request.IsAdmin, cancellationToken);

    public async Task<AccountResult<AdminUserPageDto>> ListUsersAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var users = database.Users.AsNoTracking().Include(item => item.Roles).ThenInclude(item => item.Role).Include(item => item.Entitlements).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = NormalizeEmail(query);
            users = users.Where(item => item.NormalizedEmail.Contains(normalized));
        }

        var total = await users.CountAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var items = await users.OrderBy(item => item.Email)
            .Skip((safePage - 1) * safePageSize).Take(safePageSize).ToListAsync(cancellationToken);
        return new(AccountResultCode.Success, new AdminUserPageDto(items.Select(item => ToAdminUser(item, now)).ToArray(), total, safePage, safePageSize));
    }

    public async Task<AccountResult<AdminUserDto>> SetUserEnabledAsync(Guid userId, bool enabled, int expectedRevision, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await LoadAdminUserAsync(userId, cancellationToken);
        if (user is null) return new(AccountResultCode.NotFound);
        if (user.Revision != expectedRevision) return new(AccountResultCode.Conflict);
        if (user.Id == actorUserId && !enabled) return new(AccountResultCode.Conflict);

        var now = timeProvider.GetUtcNow();
        user.DisabledAt = enabled ? null : now;
        user.SecurityStamp = NewSecurityStamp();
        user.UpdatedAt = now;
        user.Revision++;
        await RevokeSessionsAsync(user.Id, now, cancellationToken);
        return await SaveAdminUserAsync(user, now, cancellationToken);
    }

    public async Task<AccountResult<AdminUserDto>> GrantPremiumAsync(Guid userId, PremiumGrantRequestDto request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await LoadAdminUserAsync(userId, cancellationToken);
        if (user is null) return new(AccountResultCode.NotFound);
        var now = timeProvider.GetUtcNow();
        var entitlement = user.Entitlements.SingleOrDefault(item => item.Type == EntitlementTypes.PremiumDeep);
        if (entitlement is not null && request.ExpectedRevision is not null && entitlement.Revision != request.ExpectedRevision)
        {
            return new(AccountResultCode.Conflict);
        }

        var action = entitlement is null || entitlement.RevokedAt is not null || entitlement.ExpiresAt <= now ? "GRANTED" : "EXTENDED";
        if (entitlement is null)
        {
            entitlement = new UserEntitlementEntity
            {
                Id = Guid.NewGuid(), UserId = user.Id, Type = EntitlementTypes.PremiumDeep,
                StartsAt = now, ExpiresAt = now.AddDays(request.Days), GrantedByUserId = actorUserId,
                CreatedAt = now, UpdatedAt = now
            };
            database.UserEntitlements.Add(entitlement);
            user.Entitlements.Add(entitlement);
        }
        else
        {
            entitlement.StartsAt = entitlement.ExpiresAt > now && entitlement.RevokedAt is null ? entitlement.StartsAt : now;
            entitlement.ExpiresAt = (entitlement.ExpiresAt > now && entitlement.RevokedAt is null ? entitlement.ExpiresAt : now).AddDays(request.Days);
            entitlement.RevokedAt = null;
            entitlement.GrantedByUserId = actorUserId;
            entitlement.UpdatedAt = now;
            entitlement.Revision++;
        }

        AddEntitlementAudit(user.Id, entitlement, action, actorUserId, now);
        await RevokeSessionsAsync(user.Id, now, cancellationToken);
        return await SaveAdminUserAsync(user, now, cancellationToken);
    }

    public async Task<AccountResult<AdminUserDto>> RevokePremiumAsync(Guid userId, PremiumRevokeRequestDto request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await LoadAdminUserAsync(userId, cancellationToken);
        if (user is null) return new(AccountResultCode.NotFound);
        var now = timeProvider.GetUtcNow();
        var entitlement = user.Entitlements.Where(item => item.Type == EntitlementTypes.PremiumDeep && item.RevokedAt is null && item.ExpiresAt > now)
            .OrderByDescending(item => item.ExpiresAt).FirstOrDefault();
        if (entitlement is null) return new(AccountResultCode.NotFound);
        if (entitlement.Revision != request.ExpectedRevision) return new(AccountResultCode.Conflict);

        entitlement.RevokedAt = now;
        entitlement.UpdatedAt = now;
        entitlement.Revision++;
        AddEntitlementAudit(user.Id, entitlement, "REVOKED", actorUserId, now);
        await RevokeSessionsAsync(user.Id, now, cancellationToken);
        return await SaveAdminUserAsync(user, now, cancellationToken);
    }

    public async Task<AccountResult<EntitlementHistoryDto>> GetEntitlementHistoryAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await database.Users.AnyAsync(item => item.Id == userId, cancellationToken)) return new(AccountResultCode.NotFound);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var query = database.UserEntitlementAudits.AsNoTracking().Where(item => item.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAt).Skip((safePage - 1) * safePageSize).Take(safePageSize)
            .Select(item => new EntitlementAuditDto(item.Id, item.EntitlementId, item.Action, item.StartsAt, item.ExpiresAt, item.ActorUserId, item.CreatedAt))
            .ToArrayAsync(cancellationToken);
        return new(AccountResultCode.Success, new EntitlementHistoryDto(items, total, safePage, safePageSize));
    }

    public async Task<AccountResult<AdminUserDto>> BootstrapAdminAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (await database.UserRoles.AnyAsync(item => item.RoleId == AccountDataConfiguration.AdminRoleId, cancellationToken))
        {
            return new(AccountResultCode.Conflict);
        }

        return await CreateUserCoreAsync(email, password, emailVerified: true, isAdmin: true, cancellationToken);
    }

    private async Task<AccountResult<AdminUserDto>> CreateUserCoreAsync(string email, string password, bool emailVerified, bool isAdmin, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (await database.Users.AnyAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return new(AccountResultCode.DuplicateEmail);
        }

        var now = timeProvider.GetUtcNow();
        var user = new UserAccountEntity
        {
            Id = Guid.NewGuid(), Email = email.Trim(), NormalizedEmail = normalizedEmail,
            SecurityStamp = NewSecurityStamp(), EmailVerifiedAt = emailVerified ? now : null,
            CreatedAt = now, UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        database.Users.Add(user);
        database.UserRoles.Add(new UserAccountRoleEntity { UserId = user.Id, RoleId = AccountDataConfiguration.UserRoleId });
        if (isAdmin) database.UserRoles.Add(new UserAccountRoleEntity { UserId = user.Id, RoleId = AccountDataConfiguration.AdminRoleId });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning("Account creation was rejected by a database constraint ({ExceptionType}).", exception.GetType().Name);
            return new(AccountResultCode.DuplicateEmail);
        }

        return new(AccountResultCode.Success, new AdminUserDto(user.Id, user.Email, emailVerified, true,
            isAdmin ? [AccountRoles.User, AccountRoles.Admin] : [AccountRoles.User], false, null, null, user.Revision, user.CreatedAt));
    }

    private async Task<UserAccountEntity?> FindUserAsync(string email, CancellationToken cancellationToken) =>
        await database.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == NormalizeEmail(email), cancellationToken);

    private async Task IssueAndSendTokenAsync(Guid userId, string email, string purpose, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var oldTokens = await database.AccountTokens.Where(item => item.UserId == userId && item.Purpose == purpose && item.ConsumedAt == null).ToListAsync(cancellationToken);
        foreach (var oldToken in oldTokens) oldToken.ConsumedAt = now;
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        database.AccountTokens.Add(new UserAccountTokenEntity
        {
            Id = Guid.NewGuid(), UserId = userId, Purpose = purpose, TokenHash = HashToken(rawToken), CreatedAt = now,
            ExpiresAt = now.AddMinutes(purpose == AccountTokenPurposes.EmailVerification ? _options.VerificationTokenMinutes : _options.PasswordResetTokenMinutes)
        });
        await database.SaveChangesAsync(cancellationToken);
        if (purpose == AccountTokenPurposes.EmailVerification)
            await notifications.SendEmailVerificationAsync(email, rawToken, cancellationToken);
        else
            await notifications.SendPasswordResetAsync(email, rawToken, cancellationToken);
    }

    private async Task<AccountResult<UserAccountEntity>> ConsumeTokenAsync(string email, string rawToken, string purpose, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(email, cancellationToken);
        if (user is null || user.DisabledAt is not null) return new(AccountResultCode.InvalidToken);
        var now = timeProvider.GetUtcNow();
        var hash = HashToken(rawToken);
        var token = await database.AccountTokens.SingleOrDefaultAsync(item => item.UserId == user.Id && item.Purpose == purpose && item.TokenHash == hash, cancellationToken);
        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now) return new(AccountResultCode.InvalidToken);
        token.ConsumedAt = now;
        return new(AccountResultCode.Success, user);
    }

    private async Task<UserAccountEntity?> LoadAdminUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await database.Users.Include(item => item.Roles).ThenInclude(item => item.Role).Include(item => item.Entitlements)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

    private async Task<AccountResult<AdminUserDto>> SaveAdminUserAsync(UserAccountEntity user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new(AccountResultCode.Success, ToAdminUser(user, now));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(AccountResultCode.Conflict);
        }
        catch (DbUpdateException)
        {
            return new(AccountResultCode.Conflict);
        }
    }

    private async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await database.UserSessions.Where(item => item.UserId == userId && item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.RevokedAt = now;
    }

    private void AddEntitlementAudit(Guid userId, UserEntitlementEntity entitlement, string action, Guid actorUserId, DateTimeOffset now) =>
        database.UserEntitlementAudits.Add(new UserEntitlementAuditEntity
        {
            Id = Guid.NewGuid(), UserId = userId, EntitlementId = entitlement.Id, Action = action,
            StartsAt = entitlement.StartsAt, ExpiresAt = entitlement.ExpiresAt, ActorUserId = actorUserId, CreatedAt = now
        });

    private static AuthenticatedAccount ToAuthenticatedAccount(UserAccountEntity user, Guid sessionId, DateTimeOffset now) =>
        new(user.Id, sessionId, user.Email, user.EmailVerifiedAt is not null,
            user.Roles.Select(item => item.Role.Name).Order().ToArray(),
            user.EmailVerifiedAt is null ? null : user.Entitlements
                .Where(item => item.Type == EntitlementTypes.PremiumDeep && item.RevokedAt is null && item.StartsAt <= now && item.ExpiresAt > now)
                .Max(item => (DateTimeOffset?)item.ExpiresAt));

    private static AccountStatusDto ToAccountStatus(AuthenticatedAccount account) =>
        new(true, account.UserId, account.Email, account.EmailVerified, account.Roles, account.PremiumDeepActive, account.PremiumExpiresAt);

    private static AccountStatusDto ToAccountStatus(AdminUserDto account) =>
        new(false, account.Id, account.Email, account.EmailVerified, account.Roles, account.PremiumDeepActive, account.PremiumExpiresAt);

    private static AccountStatusDto ToAccountStatus(UserAccountEntity user) =>
        new(false, user.Id, user.Email, user.EmailVerifiedAt is not null, [], false, null);

    private static AdminUserDto ToAdminUser(UserAccountEntity user, DateTimeOffset now)
    {
        var premium = user.Entitlements.Where(item => item.Type == EntitlementTypes.PremiumDeep && item.RevokedAt is null && item.StartsAt <= now && item.ExpiresAt > now)
            .OrderByDescending(item => item.ExpiresAt).FirstOrDefault();
        return new(user.Id, user.Email, user.EmailVerifiedAt is not null, user.DisabledAt is null,
            user.Roles.Select(item => item.Role.Name).Order().ToArray(), premium is not null, premium?.ExpiresAt, premium?.Revision, user.Revision, user.CreatedAt);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed class UnavailableAccountService : IAccountService
{
    public bool Available => false;
    public bool PublicRegistrationEnabled => false;
    private static AccountResult<T> Unavailable<T>() => new(AccountResultCode.Unavailable);
    public Task<AccountResult<AccountStatusDto>> RegisterAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AccountStatusDto>());
    public Task<AccountResult<LoginSession>> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(Unavailable<LoginSession>());
    public Task<AuthenticatedAccount?> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedAccount?>(null);
    public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RequestEmailVerificationAsync(string email, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AccountResult<AccountStatusDto>> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AccountStatusDto>());
    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AccountResult<AccountStatusDto>> ResetPasswordAsync(string email, string token, string password, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AccountStatusDto>());
    public Task<AccountResult<bool>> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken) => Task.FromResult(Unavailable<bool>());
    public Task<AccountResult<AdminUserDto>> CreateUserAsync(AdminCreateUserRequestDto request, Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserDto>());
    public Task<AccountResult<AdminUserPageDto>> ListUsersAsync(string? query, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserPageDto>());
    public Task<AccountResult<AdminUserDto>> SetUserEnabledAsync(Guid userId, bool enabled, int expectedRevision, Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserDto>());
    public Task<AccountResult<AdminUserDto>> GrantPremiumAsync(Guid userId, PremiumGrantRequestDto request, Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserDto>());
    public Task<AccountResult<AdminUserDto>> RevokePremiumAsync(Guid userId, PremiumRevokeRequestDto request, Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserDto>());
    public Task<AccountResult<EntitlementHistoryDto>> GetEntitlementHistoryAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(Unavailable<EntitlementHistoryDto>());
    public Task<AccountResult<AdminUserDto>> BootstrapAdminAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(Unavailable<AdminUserDto>());
}
