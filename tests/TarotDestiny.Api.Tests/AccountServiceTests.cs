using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class AccountServiceTests
{
    [TestMethod]
    public async Task VerificationAndResetTokensAreSingleUseAndResetRevokesSessions()
    {
        await using var fixture = await AccountFixture.CreateAsync(publicRegistration: true);
        var registered = await fixture.Service.RegisterAsync("reader@example.test", "Strong-pass-123!", default);
        Assert.IsTrue(registered.Succeeded);
        Assert.IsNotNull(fixture.Notifications.VerificationToken);

        var verified = await fixture.Service.ConfirmEmailAsync("reader@example.test", fixture.Notifications.VerificationToken, default);
        Assert.IsTrue(verified.Succeeded);
        var replay = await fixture.Service.ConfirmEmailAsync("reader@example.test", fixture.Notifications.VerificationToken, default);
        Assert.AreEqual(AccountResultCode.InvalidToken, replay.Code);

        var login = await fixture.Service.LoginAsync("reader@example.test", "Strong-pass-123!", default);
        Assert.IsTrue(login.Succeeded);
        await fixture.Service.RequestPasswordResetAsync("reader@example.test", default);
        var resetToken = fixture.Notifications.ResetToken!;
        var reset = await fixture.Service.ResetPasswordAsync("reader@example.test", resetToken, "New-strong-456!", default);
        Assert.IsTrue(reset.Succeeded);
        Assert.AreEqual(AccountResultCode.InvalidToken,
            (await fixture.Service.ResetPasswordAsync("reader@example.test", resetToken, "Another-strong-789!", default)).Code);

        Assert.IsNull(await fixture.Service.ValidateSessionAsync(login.Value!.Account.UserId!.Value, login.Value.SessionId, default));
        Assert.AreEqual(AccountResultCode.InvalidCredentials, (await fixture.Service.LoginAsync("reader@example.test", "Strong-pass-123!", default)).Code);
        Assert.IsTrue((await fixture.Service.LoginAsync("reader@example.test", "New-strong-456!", default)).Succeeded);
    }

    [TestMethod]
    public async Task RegistrationStoresAnAdaptiveHashInsteadOfThePassword()
    {
        const string password = "Strong-pass-123!";
        await using var fixture = await AccountFixture.CreateAsync(publicRegistration: true);

        var registered = await fixture.Service.RegisterAsync("hash@example.test", password, default);

        Assert.IsTrue(registered.Succeeded);
        var user = await fixture.FindUserAsync("hash@example.test");
        Assert.IsNotNull(user);
        Assert.AreNotEqual(password, user.PasswordHash);
        Assert.AreEqual(
            PasswordVerificationResult.Success,
            new PasswordHasher<UserAccountEntity>().VerifyHashedPassword(user, user.PasswordHash, password));
    }

    [TestMethod]
    public async Task PremiumGrantIsAuditedAndInvalidatesExistingSession()
    {
        await using var fixture = await AccountFixture.CreateAsync();
        var admin = await fixture.Service.BootstrapAdminAsync("admin@example.test", "Strong-admin-123!", default);
        var user = await fixture.Service.CreateUserAsync(new AdminCreateUserRequestDto
        {
            Email = "premium@example.test", Password = "Strong-user-123!", EmailVerified = true
        }, admin.Value!.Id, default);
        var login = await fixture.Service.LoginAsync("premium@example.test", "Strong-user-123!", default);

        var granted = await fixture.Service.GrantPremiumAsync(user.Value!.Id, new PremiumGrantRequestDto { Days = 30 }, admin.Value.Id, default);
        Assert.IsTrue(granted.Value!.PremiumDeepActive);
        Assert.IsNull(await fixture.Service.ValidateSessionAsync(user.Value.Id, login.Value!.SessionId, default));

        var refreshed = await fixture.Service.LoginAsync("premium@example.test", "Strong-user-123!", default);
        var validated = await fixture.Service.ValidateSessionAsync(user.Value.Id, refreshed.Value!.SessionId, default);
        Assert.IsTrue(validated!.PremiumDeepActive);
        var history = await fixture.Service.GetEntitlementHistoryAsync(user.Value.Id, 1, 25, default);
        Assert.AreEqual("GRANTED", history.Value!.Items.Single().Action);
    }

    [TestMethod]
    public async Task FailedPasswordsTriggerConfiguredLockout()
    {
        await using var fixture = await AccountFixture.CreateAsync();
        var admin = await fixture.Service.BootstrapAdminAsync("admin@example.test", "Strong-admin-123!", default);
        await fixture.Service.CreateUserAsync(new AdminCreateUserRequestDto
        {
            Email = "locked@example.test", Password = "Strong-user-123!", EmailVerified = true
        }, admin.Value!.Id, default);

        for (var attempt = 0; attempt < 5; attempt++)
            await fixture.Service.LoginAsync("locked@example.test", "wrong", default);

        Assert.AreEqual(AccountResultCode.LockedOut,
            (await fixture.Service.LoginAsync("locked@example.test", "Strong-user-123!", default)).Code);
    }

    [TestMethod]
    public async Task ExpiredVerificationAndPasswordResetTokensAreRejected()
    {
        await using var fixture = await AccountFixture.CreateAsync(publicRegistration: true);
        await fixture.Service.RegisterAsync("expires@example.test", "Strong-pass-123!", default);
        var verificationToken = fixture.Notifications.VerificationToken!;
        await fixture.Service.RequestPasswordResetAsync("expires@example.test", default);
        var resetToken = fixture.Notifications.ResetToken!;
        fixture.Clock.Advance(TimeSpan.FromMinutes(61));

        var verification = await fixture.Service.ConfirmEmailAsync("expires@example.test", verificationToken, default);
        var reset = await fixture.Service.ResetPasswordAsync("expires@example.test", resetToken, "New-strong-456!", default);

        Assert.AreEqual(AccountResultCode.InvalidToken, verification.Code);
        Assert.AreEqual(AccountResultCode.InvalidToken, reset.Code);
    }

    [TestMethod]
    public async Task EntitlementRevisionRejectsAStaleConcurrentWrite()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TarotDbContext>().UseInMemoryDatabase(databaseName).Options;
        Guid entitlementId;
        await using (var seed = new TarotDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var user = new UserAccountEntity { Id = Guid.NewGuid(), Email = "user@example.test", NormalizedEmail = "USER@EXAMPLE.TEST", PasswordHash = "hash", SecurityStamp = "stamp", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            seed.Users.Add(user);
            entitlementId = Guid.NewGuid();
            seed.UserEntitlements.Add(new UserEntitlementEntity { Id = entitlementId, UserId = user.Id, Type = EntitlementTypes.PremiumDeep, StartsAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), GrantedByUserId = user.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using var first = new TarotDbContext(options);
        await using var second = new TarotDbContext(options);
        var firstCopy = await first.UserEntitlements.SingleAsync(item => item.Id == entitlementId);
        var staleCopy = await second.UserEntitlements.SingleAsync(item => item.Id == entitlementId);
        firstCopy.Revision++; firstCopy.ExpiresAt = firstCopy.ExpiresAt.AddDays(1);
        staleCopy.Revision++; staleCopy.ExpiresAt = staleCopy.ExpiresAt.AddDays(2);
        await first.SaveChangesAsync();

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private sealed class AccountFixture : IAsyncDisposable
    {
        private readonly TarotDbContext _database;
        public AccountService Service { get; }
        public RecordingNotifications Notifications { get; }
        public TestTimeProvider Clock { get; }

        private AccountFixture(TarotDbContext database, AccountService service, RecordingNotifications notifications, TestTimeProvider clock)
        {
            _database = database; Service = service; Notifications = notifications; Clock = clock;
        }

        public static async Task<AccountFixture> CreateAsync(bool publicRegistration = false)
        {
            var options = new DbContextOptionsBuilder<TarotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var database = new TarotDbContext(options);
            await database.Database.EnsureCreatedAsync();
            var notifications = new RecordingNotifications();
            var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
            var service = new AccountService(database, new PasswordHasher<UserAccountEntity>(), notifications,
                Options.Create(new AccountOptions { PublicRegistrationEnabled = publicRegistration }), clock,
                TestSupport.LoggerFactory.CreateLogger<AccountService>());
            return new(database, service, notifications, clock);
        }

        public Task<UserAccountEntity?> FindUserAsync(string email) =>
            _database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email);

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }

    private sealed class RecordingNotifications : IAccountNotificationSender
    {
        public string? VerificationToken { get; private set; }
        public string? ResetToken { get; private set; }
        public Task SendEmailVerificationAsync(string email, string token, CancellationToken cancellationToken) { VerificationToken = token; return Task.CompletedTask; }
        public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken) { ResetToken = token; return Task.CompletedTask; }
    }

    public sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
