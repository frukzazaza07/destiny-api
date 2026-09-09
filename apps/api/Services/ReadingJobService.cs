using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed class ReadingJobException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
}

public sealed class ReadingJobService(TarotDbContext db, IRewardedDeepService rewards,
    IDeepReadingAccessPolicy access, IInterpretationEngine engine, IReadingResponseValidator validator,
    IOptions<ReadingJobOptions> options, TimeProvider clock,
    PromptCopyService? promptCopy = null, IOptions<LlmOptions>? llmOptions = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    public static readonly ClassificationResult Classification = new(TarotDomain.GENERAL, "UNCLASSIFIED", 0,
        PersonalizationLevel.HIGH, ClassifierSources.DeepDirect, DecisionMethod: ClassifierDecisionMethods.ClassifierBypassed);
    private readonly ReadingJobOptions settings = options.Value;
    public static bool Terminal(string state) => state is "COMPLETED" or "FAILED" or "CANCELED";
    public static string? Owner(ClaimsPrincipal user, string? cookie) =>
        user.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? $"user:{id}" : string.IsNullOrWhiteSpace(cookie) ? null : $"session:{Hash(cookie)}";
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // A transaction-scoped PostgreSQL lock serializes lifecycle and shared admission decisions across replicas.
    // External calls never run under this lock. Keeping one account quota is deliberately conservative.
    private async Task<T> Mutate<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(827194302)", ct);
        db.ChangeTracker.Clear();
        var result = await work();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task<ReadingJobDto> Create(CreateReadingJobDto input, ClaimsPrincipal user, string? cookie, CancellationToken ct) => Mutate(async () =>
    {
        var owner = Owner(user, cookie) ?? throw new ReadingJobException(403, "DEEP_ACCESS_REQUIRED");
        if (input.Reading.ReadingMode != ReadingMode.DEEP) throw new ReadingJobException(400, "DEEP_REQUIRED");
        if (input.Reading.Question is not null && !settings.AllowCloudForRequestsWithRawQuestion)
            throw new ReadingJobException(403, "CLOUD_QUESTION_POLICY_DISABLED");
        if (input.Reading.ModelTier is not null && input.Reading.ModelTier != "CLOUD")
            throw new ReadingJobException(400, "MODEL_TIER_UNAVAILABLE");
        var requestJson = JsonSerializer.Serialize(input.Reading, Json);
        var hash = Hash(requestJson);
        var existing = await db.Set<ReadingJobEntity>().SingleOrDefaultAsync(x => x.Owner == owner && x.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) throw new ReadingJobException(409, "IDEMPOTENCY_CONFLICT");
            return ToDto(existing);
        }
        // Millisecond precision is portable across PostgreSQL timestamps and JavaScript dates.
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        await ExpireCore(now, ct);
        if (await db.Set<ReadingJobEntity>().CountAsync(x => x.State == "QUEUED" || x.State == "RUNNING", ct) >= settings.MaxQueuedJobs ||
            await db.Set<ReadingJobEntity>().AnyAsync(x => x.Owner == owner && (x.State == "QUEUED" || x.State == "RUNNING"), ct))
            throw new ReadingJobException(429, "QUEUE_FULL");
        DeepCreditReservation? reservation = null;
        if (!access.Evaluate(user).Entitled)
            reservation = await rewards.ReserveCreditAsync(user, cookie, ct) ?? throw new ReadingJobException(403, "DEEP_ACCESS_REQUIRED");
        var job = new ReadingJobEntity {
            Id = Guid.NewGuid(), Owner = owner, IdempotencyKey = input.IdempotencyKey, RequestHash = hash,
            RequestJson = requestJson, ProviderRef = settings.ProviderRef, AttemptId = Guid.NewGuid(),
            CreatedAt = now, UpdatedAt = now, Deadline = now.AddSeconds(settings.DeadlineSeconds),
            PresenceUntil = now.AddSeconds(settings.ReconnectGraceSeconds),
            CreditId = reservation?.CreditId, ReservationId = reservation?.ReservationId
        };
        if (reservation is not null)
        {
            var credit = await db.RewardedDeepCredits.SingleAsync(x => x.Id == reservation.CreditId, ct);
            // A durable job reservation cannot be reclaimed by the legacy timeout while recovery is pending.
            credit.ReservationExpiresAt = DateTimeOffset.MaxValue;
        }
        db.Add(job);
        if (promptCopy is not null)
        {
            var variant = llmOptions is null ? PromptVariantSelection.Control :
                new InferenceRouter(llmOptions).Resolve(input.Reading with { ModelTier = null }, Classification).PromptVariant;
            promptCopy.Snapshot(job.Id, owner, ReadingPromptBuilder.Build(input.Reading,
                engine.Build(input.Reading, Classification), variant), false);
        }
        Enqueue(job, "REQUEST", "tarot.requests.v1");
        return ToDto(job);
    }, ct);

    public async Task<ReadingJobDto> Get(Guid id, string? owner, CancellationToken ct)
    {
        var job = await db.Set<ReadingJobEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Owner == owner, ct)
            ?? throw new ReadingJobException(404, "JOB_NOT_FOUND");
        return ToDto(job);
    }

    public Task<ReadingJobDto> Cancel(Guid id, string? owner, CancellationToken ct) => Mutate(async () => {
        var job = await Owned(id, owner, ct);
        if (!Terminal(job.State)) await Finish(job, "CANCELED", "USER_CANCELED", ct);
        return ToDto(job);
    }, ct);

    public Task<ReadingJobDto> Presence(Guid id, string? owner, Guid subscriber, bool connected, CancellationToken ct, bool requireExisting = false) => Mutate(async () => {
        var job = await Owned(id, owner, ct);
        await ExpireJob(job, clock.GetUtcNow(), ct);
        if (!Terminal(job.State))
        {
            var now = clock.GetUtcNow();
            var lease = await db.Set<ReadingPresenceEntity>().SingleOrDefaultAsync(x => x.Id == subscriber && x.JobId == id, ct);
            if (requireExisting && lease is null) throw new ReadingJobException(404, "SUBSCRIBER_NOT_FOUND");
            if (connected)
            {
                if (lease is null) { lease = new() { Id = subscriber, JobId = id }; db.Add(lease); }
                lease.ExpiresAt = now.AddSeconds(settings.PresenceLeaseSeconds);
            }
            else if (lease is not null) db.Remove(lease);
            var others = await db.Set<ReadingPresenceEntity>().Where(x => x.JobId == id && x.Id != subscriber)
                .Select(x => (DateTimeOffset?)x.ExpiresAt).MaxAsync(ct);
            var last = connected ? now.AddSeconds(settings.PresenceLeaseSeconds) : now;
            if (others > last) last = others.Value;
            job.PresenceUntil = last.AddSeconds(settings.ReconnectGraceSeconds);
        }
        return ToDto(job);
    }, ct);

    public Task<WorkerClaimDto> Claim(Guid id, Guid attempt, bool renew, CancellationToken ct) => Mutate(async () => {
        var job = await db.Set<ReadingJobEntity>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (job is null) return new WorkerClaimDto("SKIP");
        var now = clock.GetUtcNow();
        await ExpireJob(job, now, ct);
        if (Terminal(job.State) || job.AttemptId != attempt) return new WorkerClaimDto("SKIP");
        if (renew)
        {
            if (job.State != "RUNNING" || job.LeaseUntil <= now) return new WorkerClaimDto("SKIP");
            job.LeaseUntil = now.AddSeconds(settings.ExecutionLeaseSeconds);
            return new WorkerClaimDto("RENEWED", job.LeaseUntil);
        }
        if (job.State != "QUEUED") return new WorkerClaimDto("WAIT");
        if (await db.Set<ReadingJobEntity>().AnyAsync(x => x.ErrorCode == "RATE_LIMITED" && x.UpdatedAt > now.AddSeconds(-30), ct))
            return new WorkerClaimDto("WAIT");
        var request = JsonSerializer.Deserialize<TarotReadingDto>(job.RequestJson, Json)!;
        if (request.Question is not null && !settings.AllowCloudForRequestsWithRawQuestion)
        { await Finish(job, "FAILED", "CLOUD_QUESTION_POLICY_DISABLED", ct); return new WorkerClaimDto("SKIP"); }
        var payload = engine.Build(request, Classification);
        var savedPrompt = await db.Set<PromptReadingEntity>().SingleOrDefaultAsync(x => x.Id == job.Id, ct);
        var snapshot = savedPrompt is not null && promptCopy is not null ? promptCopy.ReadSnapshot(savedPrompt)
            : ReadingPromptBuilder.Build(request, payload, PromptVariantSelection.Control);
        var prompt = JsonSerializer.SerializeToElement(new {
            max_tokens = settings.MaxOutputTokens, temperature = 0.3,
            response_format = LlmClient.BuildReadingResponseFormat(payload),
            messages = snapshot.Messages
        }, Json);
        // UTF-8 bytes upper-bound input tokens, so this conservative reservation cannot overspend the budget.
        var tokens = Encoding.UTF8.GetByteCount(prompt.GetRawText()) + settings.MaxOutputTokens;
        var recent = await db.Set<ProviderAdmissionEntity>().Where(x => x.CreatedAt > now.AddMinutes(-1)).ToListAsync(ct);
        if (tokens > settings.TokensPerMinute) { await Finish(job, "FAILED", "TOKEN_BUDGET_EXCEEDED", ct); return new WorkerClaimDto("SKIP"); }
        if (recent.Count >= settings.RequestsPerMinute || recent.Sum(x => x.Tokens) + tokens > settings.TokensPerMinute ||
            await db.Set<ReadingJobEntity>().CountAsync(x => x.State == "RUNNING" && x.LeaseUntil > now, ct) >= settings.MaxConcurrency)
            return new WorkerClaimDto("WAIT");
        var attempts = await db.Set<ProviderAdmissionEntity>().CountAsync(x => x.JobId == id, ct);
        if (attempts >= settings.MaxAttempts) { await Finish(job, "FAILED", "ATTEMPTS_EXHAUSTED", ct); return new WorkerClaimDto("SKIP"); }
        db.Add(new ProviderAdmissionEntity { Id = Guid.NewGuid(), JobId = id, CreatedAt = now, Tokens = tokens });
        job.State = "RUNNING"; job.Revision++; job.UpdatedAt = now;
        job.LeaseUntil = now.AddSeconds(settings.ExecutionLeaseSeconds);
        return new WorkerClaimDto("EXECUTE", job.LeaseUntil, job.ProviderRef, prompt, settings.Model);
    }, ct);

    public Task<bool> Result(ReadingResultMessage result, CancellationToken ct) => Mutate(async () => {
        if (result.Version != 1 || result.Kind is not ("RESULT" or "FAILURE") || result.JobId != result.CorrelationId)
            throw new ReadingJobException(400, "INVALID_MESSAGE");
        if (result.Kind == "RESULT" ? result.Body is null || result.ErrorCode is not null : result.Body is not null || string.IsNullOrWhiteSpace(result.ErrorCode))
            throw new ReadingJobException(400, "INVALID_MESSAGE");
        var job = await db.Set<ReadingJobEntity>().SingleOrDefaultAsync(x => x.Id == result.JobId, ct);
        if (job is null) return false;
        await ExpireJob(job, clock.GetUtcNow(), ct);
        if (Terminal(job.State) || job.State != "RUNNING" || job.AttemptId != result.AttemptId ||
            job.ProviderRef != result.ProviderRef || job.Deadline != result.Deadline) return false;
        if (result.Kind == "FAILURE")
        {
            if (result.ErrorCode == "RATE_LIMITED" && await db.Set<ProviderAdmissionEntity>().CountAsync(x => x.JobId == job.Id, ct) < settings.MaxAttempts)
            {
                job.State = "QUEUED"; job.ErrorCode = "RATE_LIMITED"; job.UpdatedAt = clock.GetUtcNow();
                job.LeaseUntil = null; job.AttemptId = Guid.NewGuid(); job.Revision++;
                Enqueue(job, "REQUEST", "tarot.requests.v1");
            }
            else await Finish(job, "FAILED", "PROVIDER_FAILED", ct);
            return true;
        }
        try
        {
            if (result.Body is null || result.Body.Length > 262144) throw new InvalidOperationException();
            var request = JsonSerializer.Deserialize<TarotReadingDto>(job.RequestJson, Json)!;
            var payload = engine.Build(request, Classification);
            QueuedOutputSchema.Validate(result.Body, payload);
            var response = LlmClient.ParseOpenAiCompatibleResponse(result.Body, Classification, payload, settings.Model);
            validator.Validate(request, response);
            var savedPrompt = await db.Set<PromptReadingEntity>().SingleOrDefaultAsync(x => x.Id == job.Id, ct);
            if (savedPrompt is not null) savedPrompt.Completed = true;
            job.ResultJson = JsonSerializer.Serialize(response with { CacheStatus = CacheStatus.SKIPPED,
                PromptReadingId = savedPrompt?.Id }, Json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
        { await Finish(job, "FAILED", "INVALID_OUTPUT", ct); return true; }
        await Finish(job, "COMPLETED", null, ct);
        return true;
    }, ct);

    public Task<bool> Expire(CancellationToken ct) => Mutate(async () => { await ExpireCore(clock.GetUtcNow(), ct); return true; }, ct);
    private async Task ExpireCore(DateTimeOffset now, CancellationToken ct)
    {
        var jobs = await db.Set<ReadingJobEntity>().Where(x => x.State == "QUEUED" || x.State == "RUNNING").ToListAsync(ct);
        foreach (var job in jobs) await ExpireJob(job, now, ct);
    }
    private async Task ExpireJob(ReadingJobEntity job, DateTimeOffset now, CancellationToken ct)
    {
        if (Terminal(job.State)) return;
        if (job.PresenceUntil <= now) { await Finish(job, "CANCELED", "PRESENCE_EXPIRED", ct); return; }
        if (job.Deadline <= now) { await Finish(job, "FAILED", "DEADLINE_EXPIRED", ct); return; }
        if (job.State == "RUNNING" && job.LeaseUntil <= now)
        {
            // Unknown provider outcome: fail closed rather than create a second charge after a worker crash.
            await Finish(job, "FAILED", "EXECUTION_LEASE_LOST", ct);
        }
    }
    private async Task Finish(ReadingJobEntity job, string state, string? error, CancellationToken ct)
    {
        if (Terminal(job.State)) return;
        if (job.CreditId is { } creditId && job.ReservationId is { } reservationId)
        {
            var credit = await db.RewardedDeepCredits.SingleAsync(x => x.Id == creditId && x.ReservationId == reservationId && x.ConsumedAt == null, ct);
            if (state == "COMPLETED") credit.ConsumedAt = clock.GetUtcNow();
            credit.ReservationId = null; credit.ReservedAt = null; credit.ReservationExpiresAt = null; credit.Revision++;
        }
        job.State = state; job.ErrorCode = error; job.UpdatedAt = clock.GetUtcNow(); job.Revision++;
        job.RequestJson = ""; // Raw questions are needed only during execution; retain the hash for idempotency.
        if (state != "COMPLETED") Enqueue(job, "CANCEL", "tarot.cancellations.v1");
    }
    private void Enqueue(ReadingJobEntity job, string kind, string queue) => db.Add(new ReadingOutboxEntity {
        Id = Guid.NewGuid(), Queue = queue, CreatedAt = clock.GetUtcNow(),
        Body = JsonSerializer.Serialize(new ReadingWorkMessage(1, kind, job.Id, job.AttemptId, job.Id, job.Deadline, job.ProviderRef), Json)
    });
    private async Task<ReadingJobEntity> Owned(Guid id, string? owner, CancellationToken ct) =>
        await db.Set<ReadingJobEntity>().SingleOrDefaultAsync(x => x.Id == id && x.Owner == owner, ct)
        ?? throw new ReadingJobException(404, "JOB_NOT_FOUND");
    private static ReadingJobDto ToDto(ReadingJobEntity job) => new(job.Id, job.State, job.Revision, job.Deadline,
        job.ResultJson is null ? null : JsonSerializer.Deserialize<TarotReadingResponse>(job.ResultJson, Json), job.ErrorCode);
}
