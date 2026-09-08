using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class ReadingJobTests
{
    private TarotDbContext db = null!;
    private ReadingJobService jobs = null!;
    private readonly TestClock clock = new();
    private readonly ClaimsPrincipal user = new(new ClaimsIdentity());
    private const string Cookie = "test-anonymous-session-capability";
    private readonly ReadingJobOptions settings = new() { MaxConcurrency = 1, RequestsPerMinute = 3, AllowCloudForRequestsWithRawQuestion = true };
    private string databaseName = "";

    [TestInitialize]
    public async Task Setup()
    {
        if (Environment.GetEnvironmentVariable("TAROT_JOBS_INTEGRATION") != "1")
            Assert.Inconclusive("Set TAROT_JOBS_INTEGRATION=1 and start the documented isolated PostgreSQL test container.");
        databaseName = "destiny_jobs_test_" + Guid.NewGuid().ToString("N");
        db = new(new DbContextOptionsBuilder<TarotDbContext>().UseNpgsql(
            $"Host=127.0.0.1;Port=55439;Database={databaseName};Username=tarot_test;Password=example-only").Options);
        await db.Database.MigrateAsync();
        var rewardOptions = Options.Create(new RewardedDeepOptions { Enabled = true, AdUnitPath = "/test/reward" });
        var rewards = new RewardedDeepService(db, new EphemeralDataProtectionProvider(), rewardOptions, clock);
        jobs = new(db, rewards, new NoPremium(), new RuleInterpretationEngine(new TarotCatalog()), new ReadingResponseValidator(), Options.Create(settings), clock);
        var session = new RewardedDeepSessionEntity { Id = Guid.NewGuid(), RequiredAdCompletions = 1, DeepCreditsPerCompletedBundle = 1, ValidAdCompletions = 1, AnonymousTokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Cookie))),
            CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddHours(1), CompletedAt = clock.GetUtcNow() };
        db.Add(session);
        db.Add(new RewardedDeepCreditEntity { Id = Guid.NewGuid(), SessionId = session.Id, IssuedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddHours(1) });
        await db.SaveChangesAsync();
    }
    [TestCleanup]
    public async Task Cleanup()
    {
        if (db is not null) {
            Assert.IsTrue(databaseName.StartsWith("destiny_jobs_test_", StringComparison.Ordinal));
            await db.Database.EnsureDeletedAsync(); await db.DisposeAsync();
        }
    }
    private CreateReadingJobDto Input(string? key = null) => new(key ?? Guid.NewGuid().ToString(), TestSupport.DestinyRequest(locale: "en", readingMode: ReadingMode.DEEP));
    private string Owner => ReadingJobService.Owner(user, Cookie)!;
    private async Task<ReadingWorkMessage> Work() => JsonSerializer.Deserialize<ReadingWorkMessage>(
        (await db.Set<ReadingOutboxEntity>().AsNoTracking().Where(x => x.Queue == "tarot.requests.v1").OrderByDescending(x => x.CreatedAt).FirstAsync()).Body, ReadingJobService.Json)!;
    private ReadingResultMessage Result(ReadingWorkMessage work, string? body = null, string? error = null) => new(1, error is null ? "RESULT" : "FAILURE", work.JobId,
        work.AttemptId, work.CorrelationId, work.Deadline, work.ProviderRef, body, error);
    private string ValidBody() {
        var r = TestSupport.ValidResponse(Input().Reading, ReadingJobService.Classification);
        var content = new { r.Title, r.Summary, r.MainTheme, cards = r.Cards.Select(x => new { x.Position, x.CardId, x.Interpretation }), r.Opportunities, r.Challenges, r.Guidance, r.ReflectionQuestion, r.ClosingMessage };
        return JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(content, ReadingJobService.Json) } } } });
    }
    [TestMethod]
    public async Task SubmissionIsIdempotentAndOwnerScoped()
    {
        var input = Input();
        var first = await jobs.Create(input, user, Cookie, default);
        var again = await jobs.Create(input, user, Cookie, default);
        Assert.AreEqual(first.JobId, again.JobId);
        Assert.AreEqual(1, await db.Set<ReadingOutboxEntity>().CountAsync());
        await Assert.ThrowsAsync<ReadingJobException>(() => jobs.Get(first.JobId, "wrong-owner", default));
        await Assert.ThrowsAsync<ReadingJobException>(() => jobs.Create(input with { Reading = input.Reading with { Question = "Changed question" } }, user, Cookie, default));
        Assert.IsNull((await db.RewardedDeepCredits.AsNoTracking().SingleAsync()).ConsumedAt);
    }
    [TestMethod]
    public async Task CompletionConsumesOnceAndSurvivesCancelAndDuplicateDelivery()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        Assert.AreEqual("EXECUTE", (await jobs.Claim(job.JobId, work.AttemptId, false, default)).Decision);
        Assert.IsTrue(await jobs.Result(Result(work, ValidBody()), default));
        Assert.IsFalse(await jobs.Result(Result(work, ValidBody()), default));
        Assert.AreEqual("COMPLETED", (await jobs.Cancel(job.JobId, Owner, default)).State);
        Assert.IsNotNull((await db.RewardedDeepCredits.AsNoTracking().SingleAsync()).ConsumedAt);
        Assert.IsNotNull((await jobs.Get(job.JobId, Owner, default)).Reading);
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CancellationRejectsLateResultsAndReleasesCredit(bool running)
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        if (running) await jobs.Claim(job.JobId, work.AttemptId, false, default);
        await jobs.Cancel(job.JobId, Owner, default);
        Assert.AreEqual("SKIP", (await jobs.Claim(job.JobId, work.AttemptId, false, default)).Decision);
        Assert.IsFalse(await jobs.Result(Result(work, ValidBody()), default));
        var credit = await db.RewardedDeepCredits.AsNoTracking().SingleAsync();
        Assert.IsNull(credit.ConsumedAt); Assert.IsNull(credit.ReservationId);
    }
    [TestMethod]
    [DataRow("invalid")] [DataRow("provider")]
    public async Task FailureReleasesCredit(string reason)
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Claim(job.JobId, work.AttemptId, false, default);
        await jobs.Result(reason == "invalid" ? Result(work, "{}") : Result(work, error: "PROVIDER_FAILED"), default);
        Assert.AreEqual("FAILED", (await jobs.Get(job.JobId, Owner, default)).State);
        Assert.IsNull((await db.RewardedDeepCredits.AsNoTracking().SingleAsync()).ReservationId);
    }
    [TestMethod]
    public async Task InitialSubscriptionFailureAndExpiredReconnectCannotStartProvider()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        clock.Advance(16);
        Assert.AreEqual("CANCELED", (await jobs.Presence(job.JobId, Owner, Guid.NewGuid(), true, default)).State);
        Assert.AreEqual("SKIP", (await jobs.Claim(job.JobId, work.AttemptId, false, default)).Decision);
        Assert.AreEqual(0, await db.Set<ProviderAdmissionEntity>().CountAsync());
    }
    [TestMethod]
    public async Task MultipleSubscribersAndReconnectUseLastPresence()
    {
        var job = await jobs.Create(Input(), user, Cookie, default);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await jobs.Presence(job.JobId, Owner, a, true, default);
        await jobs.Presence(job.JobId, Owner, b, true, default);
        await jobs.Presence(job.JobId, Owner, a, false, default);
        clock.Advance(16);
        Assert.AreEqual("QUEUED", (await jobs.Presence(job.JobId, Owner, b, true, default)).State);
        await jobs.Presence(job.JobId, Owner, b, false, default);
        clock.Advance(14);
        Assert.AreEqual("QUEUED", (await jobs.Presence(job.JobId, Owner, a, true, default)).State);
        clock.Advance(26); await jobs.Expire(default);
        Assert.AreEqual("CANCELED", (await jobs.Get(job.JobId, Owner, default)).State);
    }
    [TestMethod]
    public async Task LostExecutionLeaseFailsClosedAndFencesDuplicateClaims()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Presence(job.JobId, Owner, Guid.NewGuid(), true, default);
        await jobs.Claim(job.JobId, work.AttemptId, false, default);
        Assert.AreEqual("WAIT", (await jobs.Claim(job.JobId, work.AttemptId, false, default)).Decision);
        clock.Advance(16);
        Assert.AreEqual("SKIP", (await jobs.Claim(job.JobId, work.AttemptId, true, default)).Decision);
        Assert.AreEqual("FAILED", (await jobs.Get(job.JobId, Owner, default)).State);
        Assert.IsFalse(await jobs.Result(Result(work, ValidBody()), default));
    }
    [TestMethod]
    public async Task RateLimitRetryHasNewAttemptAndSharedCooldown()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Claim(job.JobId, work.AttemptId, false, default);
        await jobs.Result(Result(work, error: "RATE_LIMITED"), default);
        var current = await db.Set<ReadingJobEntity>().AsNoTracking().SingleAsync();
        Assert.AreNotEqual(work.AttemptId, current.AttemptId);
        Assert.AreEqual("SKIP", (await jobs.Claim(job.JobId, work.AttemptId, false, default)).Decision);
        Assert.AreEqual("WAIT", (await jobs.Claim(job.JobId, current.AttemptId, false, default)).Decision);
    }
    [TestMethod]
    public async Task CloudPrivacyPolicyRejectsBeforeReservingCredit()
    {
        settings.AllowCloudForRequestsWithRawQuestion = false;
        await Assert.ThrowsAsync<ReadingJobException>(() => jobs.Create(Input(), user, Cookie, default));
        Assert.AreEqual(0, await db.Set<ReadingJobEntity>().CountAsync());
        Assert.IsNull((await db.RewardedDeepCredits.AsNoTracking().SingleAsync()).ReservationId);
    }
    [TestMethod]
    public async Task ConcurrentReplicaSubmissionsReserveOnlyOneCredit()
    {
        await using var otherDb = NewContext(); var other = NewService(otherDb);
        var input = Input();
        var both = await Task.WhenAll(jobs.Create(input, user, Cookie, default), other.Create(input, user, Cookie, default));
        Assert.AreEqual(both[0].JobId, both[1].JobId);
        Assert.AreEqual(1, await db.Set<ReadingJobEntity>().CountAsync());
        Assert.AreEqual(1, await db.Set<ReadingOutboxEntity>().CountAsync());
    }
    [TestMethod]
    public async Task CompletionCancellationRaceCommitsOneConsistentTerminalState()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Claim(job.JobId, work.AttemptId, false, default);
        await using var otherDb = NewContext(); var other = NewService(otherDb);
        await Task.WhenAll(jobs.Result(Result(work, ValidBody()), default), other.Cancel(job.JobId, Owner, default));
        var final = await jobs.Get(job.JobId, Owner, default);
        var credit = await db.RewardedDeepCredits.AsNoTracking().SingleAsync();
        Assert.IsTrue(final.State is "COMPLETED" or "CANCELED");
        Assert.AreEqual(final.State == "COMPLETED", credit.ConsumedAt is not null);
        Assert.AreEqual(final.State == "COMPLETED", final.Reading is not null);
        Assert.IsNull(credit.ReservationId);
    }
    [TestMethod]
    public async Task ProviderAdmissionIsSharedAcrossReplicaContexts()
    {
        var first = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Claim(first.JobId, work.AttemptId, false, default);
        await using var otherDb = NewContext(); var other = NewService(otherDb, premium: true);
        var second = await other.Create(Input(), user, "second-test-cookie", default);
        var attempt = (await otherDb.Set<ReadingJobEntity>().AsNoTracking().SingleAsync(x => x.Id == second.JobId)).AttemptId;
        Assert.AreEqual("WAIT", (await other.Claim(second.JobId, attempt, false, default)).Decision);
        await jobs.Cancel(first.JobId, Owner, default);
        Assert.AreEqual("EXECUTE", (await other.Claim(second.JobId, attempt, false, default)).Decision);
        Assert.AreEqual(2, await db.Set<ProviderAdmissionEntity>().CountAsync());
    }
    [TestMethod]
    public async Task DeadlineExpiresDespiteActivePresence()
    {
        settings.DeadlineSeconds = 30; settings.PresenceLeaseSeconds = 60;
        var job = await jobs.Create(Input(), user, Cookie, default);
        await jobs.Presence(job.JobId, Owner, Guid.NewGuid(), true, default);
        clock.Advance(31); await jobs.Expire(default);
        Assert.AreEqual("DEADLINE_EXPIRED", (await jobs.Get(job.JobId, Owner, default)).ErrorCode);
        Assert.IsNull((await db.RewardedDeepCredits.AsNoTracking().SingleAsync()).ReservationId);
    }
    [TestMethod]
    public async Task OutputRejectsExtraFieldsAndDoesNotPersistRawQuestion()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var work = await Work();
        await jobs.Claim(job.JobId, work.AttemptId, false, default);
        var root = System.Text.Json.Nodes.JsonNode.Parse(ValidBody())!;
        var content = System.Text.Json.Nodes.JsonNode.Parse(root["choices"]![0]!["message"]!["content"]!.GetValue<string>())!;
        content["unexpected"] = "not in schema";
        root["choices"]![0]!["message"]!["content"] = content.ToJsonString();
        await jobs.Result(Result(work, root.ToJsonString()), default);
        Assert.AreEqual("INVALID_OUTPUT", (await jobs.Get(job.JobId, Owner, default)).ErrorCode);
        Assert.AreEqual("", (await db.Set<ReadingJobEntity>().AsNoTracking().SingleAsync()).RequestJson);
    }
    [TestMethod]
    public async Task ReadOnlySnapshotsCannotRenewPresenceAndClosedSubscribersCannotHeartbeat()
    {
        var job = await jobs.Create(Input(), user, Cookie, default); var subscriber = Guid.NewGuid();
        await jobs.Presence(job.JobId, Owner, subscriber, true, default);
        clock.Advance(10); await jobs.Get(job.JobId, Owner, default);
        clock.Advance(16); await jobs.Expire(default);
        Assert.AreEqual("CANCELED", (await jobs.Get(job.JobId, Owner, default)).State);
        var next = await jobs.Create(Input(), user, Cookie, default); var nextSubscriber = Guid.NewGuid();
        await jobs.Presence(next.JobId, Owner, nextSubscriber, true, default);
        await jobs.Presence(next.JobId, Owner, nextSubscriber, false, default);
        await Assert.ThrowsAsync<ReadingJobException>(() => jobs.Presence(next.JobId, Owner, nextSubscriber, true, default, requireExisting: true));
    }
    private TarotDbContext NewContext() => new(new DbContextOptionsBuilder<TarotDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
    private ReadingJobService NewService(TarotDbContext context, bool premium = false) => new(context,
        new RewardedDeepService(context, new EphemeralDataProtectionProvider(), Options.Create(new RewardedDeepOptions { Enabled = true, AdUnitPath = "/test/reward" }), clock),
        premium ? new Premium() : new NoPremium(), new RuleInterpretationEngine(new TarotCatalog()), new ReadingResponseValidator(), Options.Create(settings), clock);
    private sealed class Premium : IDeepReadingAccessPolicy { public DeepReadingAccess Evaluate(ClaimsPrincipal user) => new(true, true, null); }
    private sealed class NoPremium : IDeepReadingAccessPolicy { public DeepReadingAccess Evaluate(ClaimsPrincipal user) => new(true, false, null); }
    private sealed class TestClock : TimeProvider {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
}
