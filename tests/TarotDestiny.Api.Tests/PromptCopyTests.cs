using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class PromptCopyTests
{
    private const string User = "user:11111111-1111-1111-1111-111111111111";
    private readonly PromptCopyOptions options = new() { Enabled = true, CallbackSigningKey = "test-only-signing-key-at-least-32-bytes" };
    private readonly EphemeralDataProtectionProvider protection = new();
    private TarotDbContext db = null!;
    private PromptCopyService service = null!;

    [TestInitialize]
    public void Init()
    {
        db = new(new DbContextOptionsBuilder<TarotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        service = NewService();
    }
    [TestCleanup] public void Cleanup() => db.Dispose();
    private PromptCopyService NewService() => new(db, protection, Options.Create(options), TimeProvider.System);

    private async Task<Guid> Reading(string owner = User, bool completed = true)
    {
        var request = TestSupport.DestinyRequest();
        var snapshot = ReadingPromptBuilder.Build(request, new RuleInterpretationEngine(new TarotCatalog())
            .Build(request, TestSupport.CareerChangeClassification()), new("admin", "reflective", "v7", "You are the gentle village Tarot adviser. Never guarantee outcomes."));
        var id = Guid.NewGuid(); service.Snapshot(id, owner, snapshot, completed);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return id;
    }
    private PromptCopyCallbackDto Proof(Guid attempt, string? eventId = null) => new() {
        AttemptId = attempt, EventId = eventId ?? Guid.NewGuid().ToString(), Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
    private string Sign(PromptCopyCallbackDto proof) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(options.CallbackSigningKey),
        Encoding.UTF8.GetBytes($"prompt-copy-v1\n{proof.AttemptId:D}\n{proof.EventId}\n{proof.Timestamp}")));
    private Task<PromptCopyStatusDto> Confirm(PromptCopyCallbackDto proof) => service.Confirm(proof, Sign(proof), default);

    [TestMethod]
    [DataRow(2)] [DataRow(3)]
    public async Task ExactlyNVerifiedAdsUnlockAndSnapshotRequirementSurvivesRefresh(int count)
    {
        options.RequiredAds = count;
        var id = await Reading();
        var start = await service.Start(id, User, null, default);
        Assert.AreEqual(count, start.RequiredAds);
        options.RequiredAds = 9;
        for (var n = 1; n <= count; n++)
        {
            Assert.AreEqual(403, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Export(id, User, default))).Status);
            var attempt = await service.Attempt(id, User, default);
            var proof = Proof(attempt.AttemptId);
            var result = await Confirm(proof);
            Assert.AreEqual(n, result.CompletedAds); Assert.AreEqual(n == count, result.Unlocked);
            var replay = await Confirm(proof);
            Assert.AreEqual(n, replay.CompletedAds);
            service = NewService(); db.ChangeTracker.Clear();
        }
        var exported = await service.Export(id, User, default);
        Assert.AreEqual(exported, await service.Export(id, User, default));
        StringAssert.Contains(exported.Prompt, "gentle village Tarot adviser");
        Assert.AreEqual(0, await db.RewardedDeepCredits.CountAsync());
        var another = await Reading();
        Assert.IsFalse((await service.Start(another, User, null, default)).Unlocked);
    }

    [TestMethod]
    public async Task OwnershipLoginAndSingleClaimAreEnforced()
    {
        var id = await Reading("session:browser-A");
        Assert.AreEqual(401, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Start(id, "session:browser-A", "session:browser-A", default))).Status);
        Assert.AreEqual(404, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Start(id, User, "session:browser-B", default))).Status);
        await service.Start(id, User, "session:browser-A", default);
        Assert.AreEqual(404, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Start(id, "user:another", "session:browser-A", default))).Status);
        var pending = await Reading(completed: false);
        Assert.AreEqual(404, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Start(pending, User, null, default))).Status);
    }

    [TestMethod]
    public async Task BrowserEventsInvalidProofExpiredClosedAndReusedProviderEventsCannotCount()
    {
        var id = await Reading(); await service.Start(id, User, null, default);
        var attempt = await service.Attempt(id, User, default);
        var proof = Proof(attempt.AttemptId);
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Confirm(proof, "rewardedSlotGranted", default));
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Confirm(proof, new string('0', 64), default));
        var stale = proof with { Timestamp = proof.Timestamp - 301 };
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => Confirm(stale));
        await service.Close(id, attempt.AttemptId, User, default);
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => Confirm(proof));
        Assert.AreEqual(0, (await service.Status(id, User, null, default)).CompletedAds);
        var next = await service.Attempt(id, User, default);
        var nextProof = Proof(next.AttemptId); await Confirm(nextProof);
        var third = await service.Attempt(id, User, default);
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => Confirm(Proof(third.AttemptId, nextProof.EventId)));
        db.ChangeTracker.Clear();
        (await db.Set<PromptAdAttemptEntity>().SingleAsync(x => x.Id == third.AttemptId)).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<ReadingJobException>(() => Confirm(Proof(third.AttemptId)));
        Assert.AreEqual(1, (await service.Status(id, User, null, default)).CompletedAds);
    }

    [TestMethod]
    public async Task DisabledProviderDoesNotStartAdsOrReleasePrompt()
    {
        options.Enabled = false;
        var id = await Reading();
        Assert.IsFalse((await service.Status(id, User, null, default)).Available);
        Assert.AreEqual(503, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Start(id, User, null, default))).Status);
        Assert.AreEqual(403, (await Assert.ThrowsExactlyAsync<ReadingJobException>(() => service.Export(id, User, default))).Status);
    }

    [TestMethod]
    [DataRow("en")] [DataRow("th")]
    public void ExportPreservesRealMessagesAndOnlyAdaptsResponseInstructions(string locale)
    {
        var request = TestSupport.DestinyRequest(locale: locale) with { Cards = [
            new("PAST", "THE_TOWER", TarotDestiny.Api.Domain.Orientation.REVERSED),
            new("PRESENT", "THE_MAGICIAN", TarotDestiny.Api.Domain.Orientation.UPRIGHT),
            new("DIRECTION", "THE_STAR", TarotDestiny.Api.Domain.Orientation.REVERSED)] };
        var variant = new PromptVariantSelection("admin", "wise", "42",
            "You are an empathetic Tarot adviser. Return JSON only. Preserve the reader's autonomy.\nOutput schema:\n{\"type\":\"object\",\"properties\":{\"summary\":{\"type\":\"string\"}}}\nNever guarantee outcomes. Answer in JSON and respect the reader's choices. Do not include text outside the JSON object. Only JSON.");
        if (locale == "th") variant = variant with { AdditionalSystemInstruction = variant.AdditionalSystemInstruction + "\nตอบเป็น JSON เท่านั้น\nเคารพการตัดสินใจของผู้ถาม" };
        var payload = new RuleInterpretationEngine(new TarotCatalog()).Build(request, TestSupport.CareerChangeClassification());
        var snapshot = ReadingPromptBuilder.Build(request, payload, variant);
        var exported = snapshot.Export();
        var original = snapshot.Messages[0].Content;
        StringAssert.Contains(original, "Return exactly one valid JSON object.");
        StringAssert.Contains(exported, "=== SYSTEM ===\nYou are a professional Tarot");
        StringAssert.Contains(exported, "You are an empathetic Tarot adviser.");
        StringAssert.Contains(exported, "Preserve the reader's autonomy.");
        StringAssert.Contains(exported, "Never guarantee outcomes.");
        StringAssert.Contains(exported, "respect the reader's choices.");
        StringAssert.Contains(exported, "plain-text Tarot response in " + (locale == "th" ? "Thai" : "English"));
        var exportedSystem = exported.Split("=== USER ===")[0];
        Assert.IsFalse(exportedSystem.Contains("JSON", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(exportedSystem.Contains("schema", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(snapshot.Messages[1].Content, exported.Split("=== USER ===\n")[1]);
        var safety = original[original.IndexOf("SAFETY AND TONE")..original.IndexOf("Before returning")];
        StringAssert.Contains(exported, safety);
        using var user = JsonDocument.Parse(snapshot.Messages[1].Content);
        Assert.AreEqual(request.Question, user.RootElement.GetProperty("question").GetString());
        Assert.AreEqual("REVERSED", user.RootElement.GetProperty("cards")[0].GetProperty("orientation").GetString());
        Assert.AreEqual(payload.Guidance, user.RootElement.GetProperty("interpretationContext").GetProperty("guidance").GetString());
    }
}
