using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed class PromptCopyOptions
{
    public int RequiredAds { get; set; } = 2;
    public bool Enabled { get; set; }
    public string CallbackSigningKey { get; set; } = "";
}

public sealed class PromptCopyService(TarotDbContext db, IDataProtectionProvider protection,
    IOptions<PromptCopyOptions> options, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("TarotDestiny.PromptSnapshot.v1");
    private readonly PromptCopyOptions settings = options.Value;
    private static readonly SemaphoreSlim Gate = new(1);
    public bool Available => settings.Enabled && Encoding.UTF8.GetByteCount(settings.CallbackSigningKey) >= 32;

    public PromptReadingEntity Snapshot(Guid id, string owner, ReadingPromptSnapshot snapshot, bool completed)
    {
        var entity = new PromptReadingEntity { Id = id, Owner = owner, Completed = completed,
            CreatedAt = clock.GetUtcNow(), ProtectedSnapshot = protector.Protect(JsonSerializer.Serialize(snapshot, ReadingJobService.Json)) };
        db.Add(entity);
        return entity;
    }

    public ReadingPromptSnapshot ReadSnapshot(PromptReadingEntity entity) =>
        JsonSerializer.Deserialize<ReadingPromptSnapshot>(protector.Unprotect(entity.ProtectedSnapshot), ReadingJobService.Json)!;

    public async Task<PromptCopyStatusDto> Status(Guid id, string user, string? anonymous, CancellationToken ct, string? rewardAnonymous = null)
    {
        var entity = await Owned(id, user, anonymous, ct, rewardAnonymous);
        return ToStatus(entity);
    }

    public Task<PromptCopyStatusDto> Start(Guid id, string user, string? anonymous, CancellationToken ct, string? rewardAnonymous = null) => Mutate(async () => {
        var entity = await Owned(id, user, anonymous, ct, rewardAnonymous);
        if (entity.RequiredAds is null)
        {
            if (!Available) throw new ReadingJobException(503, "PROMPT_REWARDS_UNAVAILABLE");
            entity.RequiredAds = settings.RequiredAds;
        }
        entity.Owner = user; // Claim a reading from this browser once, after authentication.
        entity.Revision++;
        return ToStatus(entity);
    }, ct);

    public Task<PromptCopyAttemptDto> Attempt(Guid id, string user, CancellationToken ct) => Mutate(async () => {
        var entity = await Owned(id, user, null, ct);
        if (!Available) throw new ReadingJobException(503, "PROMPT_REWARDS_UNAVAILABLE");
        if (entity.RequiredAds is null || ToStatus(entity).Unlocked) throw new ReadingJobException(409, "PROMPT_SESSION_REQUIRED");
        var now = clock.GetUtcNow();
        var active = await db.Set<PromptAdAttemptEntity>().Where(x => x.ReadingId == id && !x.Closed && x.ProviderEventId == null).ToListAsync(ct);
        var previous = active.FirstOrDefault(x => x.ExpiresAt > now);
        if (previous is not null) throw new ReadingJobException(409, "AD_CONFIRMATION_PENDING");
        var attempt = new PromptAdAttemptEntity { Id = Guid.NewGuid(), ReadingId = id, ExpiresAt = now.AddMinutes(10) };
        db.Add(attempt);
        return new PromptCopyAttemptDto(attempt.Id, attempt.ExpiresAt);
    }, ct);

    public Task<PromptCopyStatusDto> Close(Guid id, Guid attemptId, string user, CancellationToken ct) => Mutate(async () => {
        var entity = await Owned(id, user, null, ct);
        var attempt = await db.Set<PromptAdAttemptEntity>().SingleOrDefaultAsync(x => x.Id == attemptId && x.ReadingId == id, ct)
            ?? throw new ReadingJobException(404, "ATTEMPT_NOT_FOUND");
        attempt.Closed = true;
        return ToStatus(entity);
    }, ct);

    public async Task<PromptCopyTextDto> Export(Guid id, string user, CancellationToken ct)
    {
        var entity = await Owned(id, user, null, ct);
        if (!ToStatus(entity).Unlocked) throw new ReadingJobException(403, "PROMPT_LOCKED");
        return new(ReadSnapshot(entity).Export());
    }

    // Only a trusted provider backend may sign these callbacks. Never sign browser grant events.
    public Task<PromptCopyStatusDto> Confirm(PromptCopyCallbackDto input, string signature, CancellationToken ct)
    {
        if (!Available || !Verify(input, signature)) throw new ReadingJobException(403, "INVALID_PROVIDER_PROOF");
        return Mutate(async () => {
            var attempt = await db.Set<PromptAdAttemptEntity>().SingleOrDefaultAsync(x => x.Id == input.AttemptId, ct)
                ?? throw new ReadingJobException(404, "ATTEMPT_NOT_FOUND");
            var entity = await db.Set<PromptReadingEntity>().SingleAsync(x => x.Id == attempt.ReadingId, ct);
            if (attempt.ProviderEventId == input.EventId) return ToStatus(entity);
            if (attempt.Closed || attempt.ProviderEventId is not null || attempt.ExpiresAt <= clock.GetUtcNow() ||
                entity.RequiredAds is null || !entity.Completed || !entity.Owner.StartsWith("user:") ||
                await db.Set<PromptAdAttemptEntity>().AnyAsync(x => x.ProviderEventId == input.EventId, ct))
                throw new ReadingJobException(409, "INELIGIBLE_COMPLETION");
            attempt.ProviderEventId = input.EventId;
            entity.CompletedAds = Math.Min(entity.RequiredAds.Value, entity.CompletedAds + 1);
            entity.Revision++;
            return ToStatus(entity);
        }, ct);
    }

    private bool Verify(PromptCopyCallbackDto input, string signature)
    {
        if (input.AttemptId is null || input.Timestamp is null || string.IsNullOrWhiteSpace(input.EventId) ||
            input.EventId.Contains('\n') || Math.Abs((decimal)clock.GetUtcNow().ToUnixTimeSeconds() - input.Timestamp.Value) > 300) return false;
        var body = $"prompt-copy-v1\n{input.AttemptId.Value:D}\n{input.EventId}\n{input.Timestamp.Value}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.CallbackSigningKey), Encoding.UTF8.GetBytes(body));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }

    private async Task<PromptReadingEntity> Owned(Guid id, string user, string? anonymous, CancellationToken ct, string? rewardAnonymous = null)
    {
        if (!user.StartsWith("user:")) throw new ReadingJobException(401, "LOGIN_REQUIRED");
        return await db.Set<PromptReadingEntity>().SingleOrDefaultAsync(x => x.Id == id && x.Completed &&
            (x.Owner == user || (anonymous != null && x.Owner == anonymous) ||
                (rewardAnonymous != null && x.Owner == rewardAnonymous)), ct)
            ?? throw new ReadingJobException(404, "READING_NOT_FOUND");
    }

    private PromptCopyStatusDto ToStatus(PromptReadingEntity entity) => new(entity.Id, entity.CompletedAds,
        entity.RequiredAds ?? settings.RequiredAds, entity.RequiredAds is { } required && entity.CompletedAds >= required, Available);

    private async Task<T> Mutate<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(827194303)", ct);
            db.ChangeTracker.Clear();
            var result = await action();
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        finally { Gate.Release(); }
    }
}
