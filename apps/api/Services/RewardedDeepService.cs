using System.Collections.Concurrent;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public enum RewardedDeepResultCode
{
    Success,
    Disabled,
    Unavailable,
    NoSession,
    DailyLimit,
    ActiveAttempt,
    InvalidNonce,
    ExpiredNonce,
    ReplayedNonce,
    IdentityMismatch,
    Conflict
}

public sealed record RewardedDeepResult<T>(RewardedDeepResultCode Code, T? Value = default, DateTimeOffset? RetryAt = null)
{
    public bool Succeeded => Code == RewardedDeepResultCode.Success;
    public static RewardedDeepResult<T> Ok(T value) => new(RewardedDeepResultCode.Success, value);
}

public sealed record RewardedDeepSessionResult(RewardedDeepStatusDto Status, string? AnonymousToken);
public sealed record DeepCreditReservation(Guid CreditId, Guid ReservationId);

public interface IRewardedDeepService
{
    Task<RewardedDeepStatusDto> GetStatusAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedDeepSessionResult>> CreateSessionAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedAdAttemptDto>> CreateAttemptAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedDeepStatusDto>> CloseAttemptAsync(ClaimsPrincipal user, string? anonymousToken, CloseRewardedAdAttemptRequestDto request, CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedDeepStatusDto>> GrantAsync(ClaimsPrincipal user, string? anonymousToken, RewardedAdGrantRequestDto request, CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedDeepSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken);
    Task<RewardedDeepResult<RewardedDeepSettingsDto>> UpdateSettingsAsync(UpdateRewardedDeepSettingsRequestDto request, Guid actorUserId, CancellationToken cancellationToken);
    Task<DeepCreditReservation?> ReserveCreditAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken);
    Task FinalizeCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken);
    Task ReleaseCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken);
}

public sealed class RewardedDeepService(
    TarotDbContext database,
    IDataProtectionProvider dataProtection,
    IOptions<RewardedDeepOptions> options,
    TimeProvider timeProvider) : IRewardedDeepService
{
    private const string ProtectorPurpose = "TarotDestiny.RewardedDeep.Attempt.v1";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IdentityLocks = new(StringComparer.Ordinal);
    private readonly RewardedDeepOptions _options = options.Value;
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<RewardedDeepStatusDto> GetStatusAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return DisabledStatus(settings);

        var identity = RewardIdentity.From(user, anonymousToken);
        var session = await FindCurrentSessionAsync(identity, now, cancellationToken);
        var available = await CountAvailableCreditsAsync(identity, session?.Id, now, cancellationToken);
        var latestCompletion = await FindLatestCompletionAsync(identity, cancellationToken);
        return ToStatus(settings!, session, available, latestCompletion is { } completed && completed.AddHours(24) > now
            ? completed.AddHours(24)
            : null);
    }

    public async Task<RewardedDeepResult<RewardedDeepSessionResult>> CreateSessionAsync(
        ClaimsPrincipal user,
        string? anonymousToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return new(settings is null ? RewardedDeepResultCode.Unavailable : RewardedDeepResultCode.Disabled);

        var identity = RewardIdentity.From(user, anonymousToken);
        var key = identity.LockKey;
        var gate = IdentityLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await FindCurrentSessionAsync(identity, now, cancellationToken);
            if (current is not null && current.CompletedAt is null)
            {
                return RewardedDeepResult<RewardedDeepSessionResult>.Ok(
                    new RewardedDeepSessionResult(await BuildStatusAsync(settings!, identity, current, now, cancellationToken), null));
            }

            var latestCompletion = await FindLatestCompletionAsync(identity, cancellationToken);
            if (latestCompletion is { } completed && completed.AddHours(24) > now)
            {
                return new(RewardedDeepResultCode.DailyLimit, RetryAt: completed.AddHours(24));
            }

            string? rawAnonymousToken = null;
            string? anonymousHash = null;
            Guid? userId = identity.UserId;
            if (userId is null)
            {
                rawAnonymousToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                anonymousHash = Hash(rawAnonymousToken);
            }

            var session = new RewardedDeepSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AnonymousTokenHash = anonymousHash,
                RequiredAdCompletions = settings!.RequiredAdCompletions,
                DeepCreditsPerCompletedBundle = settings.DeepCreditsPerCompletedBundle,
                ValidAdCompletions = 0,
                CreatedAt = now,
                ExpiresAt = now.AddHours(_options.SessionHours)
            };
            database.RewardedDeepSessions.Add(session);
            await database.SaveChangesAsync(cancellationToken);
            var createdIdentity = new RewardIdentity(userId, anonymousHash);
            return RewardedDeepResult<RewardedDeepSessionResult>.Ok(
                new RewardedDeepSessionResult(ToStatus(settings, session, 0, null), rawAnonymousToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RewardedDeepResult<RewardedAdAttemptDto>> CreateAttemptAsync(
        ClaimsPrincipal user,
        string? anonymousToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return new(settings is null ? RewardedDeepResultCode.Unavailable : RewardedDeepResultCode.Disabled);
        var identity = RewardIdentity.From(user, anonymousToken);
        var session = await FindCurrentSessionAsync(identity, now, cancellationToken);
        if (session is null || session.CompletedAt is not null) return new(RewardedDeepResultCode.NoSession);

        var activeAttempt = await database.RewardedAdAttempts.AnyAsync(
            item => item.SessionId == session.Id && item.UsedAt == null && item.ExpiresAt > now,
            cancellationToken);
        if (activeAttempt) return new(RewardedDeepResultCode.ActiveAttempt);

        var attemptId = Guid.NewGuid();
        var expiresAt = now.AddMinutes(_options.AttemptMinutes);
        var payload = $"{attemptId:N}.{session.Id:N}.{expiresAt.UtcTicks}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
        var nonce = _protector.Protect(payload);
        database.RewardedAdAttempts.Add(new RewardedAdAttemptEntity
        {
            Id = attemptId,
            SessionId = session.Id,
            NonceHash = Hash(nonce),
            CreatedAt = now,
            ExpiresAt = expiresAt
        });
        await database.SaveChangesAsync(cancellationToken);
        return RewardedDeepResult<RewardedAdAttemptDto>.Ok(new RewardedAdAttemptDto(nonce, expiresAt));
    }

    public async Task<RewardedDeepResult<RewardedDeepStatusDto>> CloseAttemptAsync(
        ClaimsPrincipal user,
        string? anonymousToken,
        CloseRewardedAdAttemptRequestDto request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return new(settings is null ? RewardedDeepResultCode.Unavailable : RewardedDeepResultCode.Disabled);
        if (!TryReadNonce(request.Nonce, out var attemptId, out var protectedSessionId, out _))
            return new(RewardedDeepResultCode.InvalidNonce);
        var identity = RewardIdentity.From(user, anonymousToken);
        var attempt = await database.RewardedAdAttempts.Include(item => item.Session)
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.NonceHash == Hash(request.Nonce), cancellationToken);
        if (attempt is null || attempt.SessionId != protectedSessionId) return new(RewardedDeepResultCode.InvalidNonce);
        if (!identity.Matches(attempt.Session)) return new(RewardedDeepResultCode.IdentityMismatch);
        if (attempt.UsedAt is null)
        {
            attempt.UsedAt = now;
            await database.SaveChangesAsync(cancellationToken);
        }
        return RewardedDeepResult<RewardedDeepStatusDto>.Ok(await BuildStatusAsync(settings!, identity, attempt.Session, now, cancellationToken));
    }

    public async Task<RewardedDeepResult<RewardedDeepStatusDto>> GrantAsync(
        ClaimsPrincipal user,
        string? anonymousToken,
        RewardedAdGrantRequestDto request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return new(settings is null ? RewardedDeepResultCode.Unavailable : RewardedDeepResultCode.Disabled);
        if (!string.Equals(request.EventType, RewardedDeepConstants.GrantedEvent, StringComparison.Ordinal))
            return new(RewardedDeepResultCode.InvalidNonce);

        if (!TryReadNonce(request.Nonce, out var attemptId, out var protectedSessionId, out var protectedExpiry))
            return new(RewardedDeepResultCode.InvalidNonce);
        if (protectedExpiry <= now) return new(RewardedDeepResultCode.ExpiredNonce);

        var identity = RewardIdentity.From(user, anonymousToken);
        var gate = IdentityLocks.GetOrAdd(identity.LockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await BeginSerializableIfRelationalAsync(cancellationToken);
            var attempt = await database.RewardedAdAttempts
                .Include(item => item.Session)
                .SingleOrDefaultAsync(item => item.Id == attemptId && item.NonceHash == Hash(request.Nonce), cancellationToken);
            if (attempt is null || attempt.SessionId != protectedSessionId) return new(RewardedDeepResultCode.InvalidNonce);
            if (!identity.Matches(attempt.Session)) return new(RewardedDeepResultCode.IdentityMismatch);
            if (attempt.UsedAt is not null) return new(RewardedDeepResultCode.ReplayedNonce);
            if (attempt.ExpiresAt <= now || attempt.Session.ExpiresAt <= now) return new(RewardedDeepResultCode.ExpiredNonce);
            if (attempt.Session.CompletedAt is not null) return new(RewardedDeepResultCode.DailyLimit, RetryAt: attempt.Session.CompletedAt.Value.AddHours(24));

            attempt.UsedAt = now;
            attempt.Session.ValidAdCompletions++;
            attempt.Session.Revision++;
            if (attempt.Session.ValidAdCompletions == attempt.Session.RequiredAdCompletions)
            {
                attempt.Session.CompletedAt = now;
                for (var index = 0; index < attempt.Session.DeepCreditsPerCompletedBundle; index++)
                {
                    database.RewardedDeepCredits.Add(new RewardedDeepCreditEntity
                    {
                        Id = Guid.NewGuid(),
                        SessionId = attempt.Session.Id,
                        UserId = attempt.Session.UserId,
                        IssuedAt = now,
                        ExpiresAt = now.AddHours(24)
                    });
                }
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new(RewardedDeepResultCode.Conflict);
            }

            var available = await CountAvailableCreditsAsync(identity, attempt.SessionId, now, cancellationToken);
            return RewardedDeepResult<RewardedDeepStatusDto>.Ok(ToStatus(
                settings!, attempt.Session, available,
                attempt.Session.CompletedAt?.AddHours(24)));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RewardedDeepResult<RewardedDeepSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await LoadValidSettingsAsync(cancellationToken);
        return settings is null
            ? new(RewardedDeepResultCode.Unavailable)
            : RewardedDeepResult<RewardedDeepSettingsDto>.Ok(ToDto(settings));
    }

    public async Task<RewardedDeepResult<RewardedDeepSettingsDto>> UpdateSettingsAsync(
        UpdateRewardedDeepSettingsRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var settings = await database.RewardedDeepSettings.SingleOrDefaultAsync(item => item.Id == RewardedDeepConstants.SettingsId, cancellationToken);
        if (settings is null) return new(RewardedDeepResultCode.Unavailable);
        if (settings.Revision != request.ExpectedRevision) return new(RewardedDeepResultCode.Conflict);
        settings.RequiredAdCompletions = request.RequiredAdCompletions;
        settings.DeepCreditsPerCompletedBundle = request.DeepCreditsPerCompletedBundle;
        settings.UpdatedAt = timeProvider.GetUtcNow();
        settings.UpdatedByUserId = actorUserId;
        settings.Revision++;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return RewardedDeepResult<RewardedDeepSettingsDto>.Ok(ToDto(settings));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(RewardedDeepResultCode.Conflict);
        }
    }

    public async Task<DeepCreditReservation?> ReserveCreditAsync(
        ClaimsPrincipal user,
        string? anonymousToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await LoadValidSettingsAsync(cancellationToken);
        if (!IsEnabled(settings)) return null;
        var identity = RewardIdentity.From(user, anonymousToken);
        if (!identity.HasIdentity) return null;
        var reservationId = Guid.NewGuid();
        var candidateIds = await AvailableCredits(identity, now).Select(item => item.Id).Take(5).ToArrayAsync(cancellationToken);
        foreach (var creditId in candidateIds)
        {
            if (database.Database.IsRelational())
            {
                var updated = await database.RewardedDeepCredits
                    .Where(item => item.Id == creditId && item.ConsumedAt == null && item.ExpiresAt > now &&
                        (item.ReservationId == null || item.ReservationExpiresAt <= now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.ReservationId, reservationId)
                        .SetProperty(item => item.ReservedAt, now)
                        .SetProperty(item => item.ReservationExpiresAt, now.AddMinutes(_options.ReservationMinutes))
                        .SetProperty(item => item.Revision, item => item.Revision + 1), cancellationToken);
                if (updated == 1) return new DeepCreditReservation(creditId, reservationId);
                continue;
            }

            var gate = IdentityLocks.GetOrAdd(identity.LockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var credit = await database.RewardedDeepCredits.SingleOrDefaultAsync(item => item.Id == creditId, cancellationToken);
                if (credit is null || credit.ConsumedAt is not null || credit.ExpiresAt <= now ||
                    (credit.ReservationId is not null && credit.ReservationExpiresAt > now)) continue;
                credit.ReservationId = reservationId;
                credit.ReservedAt = now;
                credit.ReservationExpiresAt = now.AddMinutes(_options.ReservationMinutes);
                credit.Revision++;
                await database.SaveChangesAsync(cancellationToken);
                return new DeepCreditReservation(creditId, reservationId);
            }
            finally { gate.Release(); }
        }
        return null;
    }

    public Task FinalizeCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken) =>
        UpdateReservationAsync(reservation, consume: true, cancellationToken);

    public Task ReleaseCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken) =>
        UpdateReservationAsync(reservation, consume: false, cancellationToken);

    private async Task UpdateReservationAsync(DeepCreditReservation reservation, bool consume, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (database.Database.IsRelational())
        {
            var query = database.RewardedDeepCredits.Where(item =>
                item.Id == reservation.CreditId && item.ReservationId == reservation.ReservationId && item.ConsumedAt == null);
            var changed = consume
                ? await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ConsumedAt, now)
                    .SetProperty(item => item.ReservationId, (Guid?)null)
                    .SetProperty(item => item.ReservedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.ReservationExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.Revision, item => item.Revision + 1), cancellationToken)
                : await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ReservationId, (Guid?)null)
                    .SetProperty(item => item.ReservedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.ReservationExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.Revision, item => item.Revision + 1), cancellationToken);
            if (changed != 1) throw new InvalidOperationException("The DEEP credit reservation is no longer valid.");
            return;
        }

        var credit = await database.RewardedDeepCredits.SingleOrDefaultAsync(item =>
            item.Id == reservation.CreditId && item.ReservationId == reservation.ReservationId && item.ConsumedAt == null,
            cancellationToken) ?? throw new InvalidOperationException("The DEEP credit reservation is no longer valid.");
        if (consume) credit.ConsumedAt = now;
        credit.ReservationId = null;
        credit.ReservedAt = null;
        credit.ReservationExpiresAt = null;
        credit.Revision++;
        await database.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<RewardedDeepCreditEntity> AvailableCredits(RewardIdentity identity, DateTimeOffset now)
    {
        var query = database.RewardedDeepCredits.Where(item => item.ConsumedAt == null && item.ExpiresAt > now &&
            (item.ReservationId == null || item.ReservationExpiresAt <= now));
        return identity.UserId is { } userId
            ? query.Where(item => item.UserId == userId)
            : query.Where(item => item.Session.AnonymousTokenHash == identity.AnonymousTokenHash);
    }

    private Task<int> CountAvailableCreditsAsync(RewardIdentity identity, Guid? sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (identity.UserId is { } userId)
            return database.RewardedDeepCredits.CountAsync(item => item.UserId == userId && item.ConsumedAt == null && item.ExpiresAt > now && (item.ReservationId == null || item.ReservationExpiresAt <= now), cancellationToken);
        if (sessionId is null || identity.AnonymousTokenHash is null) return Task.FromResult(0);
        return database.RewardedDeepCredits.CountAsync(item => item.SessionId == sessionId && item.ConsumedAt == null && item.ExpiresAt > now && (item.ReservationId == null || item.ReservationExpiresAt <= now), cancellationToken);
    }

    private async Task<RewardedDeepStatusDto> BuildStatusAsync(RewardedDeepSettingsEntity settings, RewardIdentity identity, RewardedDeepSessionEntity session, DateTimeOffset now, CancellationToken cancellationToken) =>
        ToStatus(settings, session, await CountAvailableCreditsAsync(identity, session.Id, now, cancellationToken), session.CompletedAt?.AddHours(24));

    private async Task<RewardedDeepSessionEntity?> FindCurrentSessionAsync(RewardIdentity identity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (identity.UserId is { } userId)
            return await database.RewardedDeepSessions.Where(item => item.UserId == userId && item.ExpiresAt > now).OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (identity.AnonymousTokenHash is null) return null;
        return await database.RewardedDeepSessions.Where(item => item.AnonymousTokenHash == identity.AnonymousTokenHash && item.ExpiresAt > now).OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<DateTimeOffset?> FindLatestCompletionAsync(RewardIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.UserId is { } userId)
            return await database.RewardedDeepSessions.Where(item => item.UserId == userId && item.CompletedAt != null).MaxAsync(item => item.CompletedAt, cancellationToken);
        if (identity.AnonymousTokenHash is null) return null;
        return await database.RewardedDeepSessions.Where(item => item.AnonymousTokenHash == identity.AnonymousTokenHash && item.CompletedAt != null).MaxAsync(item => item.CompletedAt, cancellationToken);
    }

    private async Task<RewardedDeepSettingsEntity?> LoadValidSettingsAsync(CancellationToken cancellationToken)
    {
        var item = await database.RewardedDeepSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == RewardedDeepConstants.SettingsId, cancellationToken);
        return item is not null && item.RequiredAdCompletions is >= 1 and <= RewardedDeepConstants.MaximumRequiredAdCompletions &&
            item.DeepCreditsPerCompletedBundle is >= 1 and <= RewardedDeepConstants.MaximumCreditsPerBundle ? item : null;
    }

    private bool IsEnabled(RewardedDeepSettingsEntity? settings) =>
        _options.Enabled && settings is not null && !string.IsNullOrWhiteSpace(_options.AdUnitPath);

    private RewardedDeepStatusDto DisabledStatus(RewardedDeepSettingsEntity? settings) => new(
        false, settings is not null, _options.Provider, null, 0,
        settings?.RequiredAdCompletions ?? 0, settings?.DeepCreditsPerCompletedBundle ?? 0, 0, null, null);

    private RewardedDeepStatusDto ToStatus(RewardedDeepSettingsEntity settings, RewardedDeepSessionEntity? session, int available, DateTimeOffset? nextEligibleAt) => new(
        true, true, _options.Provider, _options.AdUnitPath,
        session?.ValidAdCompletions ?? 0,
        session?.RequiredAdCompletions ?? settings.RequiredAdCompletions,
        session?.DeepCreditsPerCompletedBundle ?? settings.DeepCreditsPerCompletedBundle,
        available, session?.ExpiresAt, nextEligibleAt);

    private static RewardedDeepSettingsDto ToDto(RewardedDeepSettingsEntity item) => new(
        item.RequiredAdCompletions, item.DeepCreditsPerCompletedBundle, item.Revision, item.UpdatedAt, item.UpdatedByUserId);

    private bool TryReadNonce(string nonce, out Guid attemptId, out Guid sessionId, out DateTimeOffset expiry)
    {
        attemptId = default;
        sessionId = default;
        expiry = default;
        try
        {
            var values = _protector.Unprotect(nonce).Split('.');
            return values.Length == 4 && Guid.TryParseExact(values[0], "N", out attemptId) &&
                Guid.TryParseExact(values[1], "N", out sessionId) && long.TryParse(values[2], out var ticks) &&
                (expiry = new DateTimeOffset(ticks, TimeSpan.Zero)) != default;
        }
        catch (CryptographicException) { return false; }
    }

    private Task<IDbContextTransaction?> BeginSerializableIfRelationalAsync(CancellationToken cancellationToken) =>
        database.Database.IsRelational()
            ? database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ContinueWith<IDbContextTransaction?>(task => task.Result, cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record RewardIdentity(Guid? UserId, string? AnonymousTokenHash)
    {
        public bool HasIdentity => UserId is not null || AnonymousTokenHash is not null;
        public string LockKey => UserId is { } userId ? $"u:{userId:N}" : $"a:{AnonymousTokenHash ?? "new"}";
        public bool Matches(RewardedDeepSessionEntity session) => UserId is { } userId
            ? session.UserId == userId
            : AnonymousTokenHash is not null && session.AnonymousTokenHash == AnonymousTokenHash;
        public static RewardIdentity From(ClaimsPrincipal user, string? anonymousToken) =>
            user.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? new(userId, null)
                : new(null, string.IsNullOrWhiteSpace(anonymousToken) ? null : Hash(anonymousToken));
    }
}

public sealed class UnavailableRewardedDeepService(IOptions<RewardedDeepOptions> options) : IRewardedDeepService
{
    private RewardedDeepStatusDto Status => new(false, false, options.Value.Provider, null, 0, 0, 0, 0, null, null);
    public Task<RewardedDeepStatusDto> GetStatusAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken) => Task.FromResult(Status);
    public Task<RewardedDeepResult<RewardedDeepSessionResult>> CreateSessionAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedDeepSessionResult>(RewardedDeepResultCode.Unavailable));
    public Task<RewardedDeepResult<RewardedAdAttemptDto>> CreateAttemptAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedAdAttemptDto>(RewardedDeepResultCode.Unavailable));
    public Task<RewardedDeepResult<RewardedDeepStatusDto>> CloseAttemptAsync(ClaimsPrincipal user, string? anonymousToken, CloseRewardedAdAttemptRequestDto request, CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedDeepStatusDto>(RewardedDeepResultCode.Unavailable));
    public Task<RewardedDeepResult<RewardedDeepStatusDto>> GrantAsync(ClaimsPrincipal user, string? anonymousToken, RewardedAdGrantRequestDto request, CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedDeepStatusDto>(RewardedDeepResultCode.Unavailable));
    public Task<RewardedDeepResult<RewardedDeepSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedDeepSettingsDto>(RewardedDeepResultCode.Unavailable));
    public Task<RewardedDeepResult<RewardedDeepSettingsDto>> UpdateSettingsAsync(UpdateRewardedDeepSettingsRequestDto request, Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(new RewardedDeepResult<RewardedDeepSettingsDto>(RewardedDeepResultCode.Unavailable));
    public Task<DeepCreditReservation?> ReserveCreditAsync(ClaimsPrincipal user, string? anonymousToken, CancellationToken cancellationToken) => Task.FromResult<DeepCreditReservation?>(null);
    public Task FinalizeCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReleaseCreditAsync(DeepCreditReservation reservation, CancellationToken cancellationToken) => Task.CompletedTask;
}
