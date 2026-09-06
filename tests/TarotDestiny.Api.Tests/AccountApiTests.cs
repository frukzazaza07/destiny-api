using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class AccountApiTests
{
    [TestMethod]
    public async Task PublicRegistrationIsDisabledByDefaultPolicy()
    {
        await using var factory = new AccountApiFactory(publicRegistration: false);
        using var client = factory.CreateSecureClient();
        var csrf = await GetCsrfAsync(client);
        using var request = JsonRequest(HttpMethod.Post, "/api/auth/register", new { email = "new@example.test", password = "Strong-user-123!" }, csrf);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task LoginRequiresCsrfAndIssuesHardenedSessionCookie()
    {
        await using var factory = new AccountApiFactory();
        await factory.BootstrapAdminAsync();
        using var client = factory.CreateSecureClient();

        using var rejected = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = "Strong-admin-123!" });
        Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        var token = (await ReadDataAsync<JsonElement>(csrfResponse)).GetProperty("token").GetString()!;
        var csrfCookie = csrfResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-Tarot.Csrf=", StringComparison.Ordinal));
        AssertHardenedCookie(csrfCookie);
        using var request = JsonRequest(HttpMethod.Post, "/api/auth/login", new { email = "admin@example.test", password = "Strong-admin-123!" }, token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-Tarot.Auth=", StringComparison.Ordinal));
        AssertHardenedCookie(cookie);

        using var me = await client.GetAsync("/api/auth/me");
        var account = (await ReadDataAsync<JsonElement>(me));
        Assert.IsTrue(account.GetProperty("authenticated").GetBoolean());
        CollectionAssert.Contains(account.GetProperty("roles").EnumerateArray().Select(item => item.GetString()).ToArray(), "ADMIN");
    }

    [TestMethod]
    public async Task AdminCanGrantPremiumAndOnlyEntitledUserCanGenerateDeepReading()
    {
        await using var factory = new AccountApiFactory();
        await factory.BootstrapAdminAsync();
        using var admin = factory.CreateSecureClient();
        await LoginAsync(admin, "admin@example.test", "Strong-admin-123!");

        var adminCsrf = await GetCsrfAsync(admin);
        using var createRequest = JsonRequest(HttpMethod.Post, "/api/admin/users", new
        {
            email = "reader@example.test", password = "Strong-reader-123!", emailVerified = true, isAdmin = false
        }, adminCsrf);
        using var createdResponse = await admin.SendAsync(createRequest);
        var created = await ReadDataAsync<JsonElement>(createdResponse);
        var userId = created.GetProperty("id").GetGuid();

        adminCsrf = await GetCsrfAsync(admin);
        using var grantRequest = JsonRequest(HttpMethod.Post, $"/api/admin/users/{userId}/premium", new { days = 30 }, adminCsrf);
        using var grantResponse = await admin.SendAsync(grantRequest);
        Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode, await grantResponse.Content.ReadAsStringAsync());
        var granted = await ReadDataAsync<JsonElement>(grantResponse);
        var premiumRevision = granted.GetProperty("premiumRevision").GetInt32();

        using var reader = factory.CreateSecureClient();
        await LoginAsync(reader, "reader@example.test", "Strong-reader-123!");
        using var options = await reader.GetAsync("/api/readings/options");
        var optionsData = await ReadDataAsync<JsonElement>(options);
        Assert.IsTrue(optionsData.GetProperty("deepReading").GetProperty("entitled").GetBoolean());
        Assert.AreNotEqual(JsonValueKind.Null, optionsData.GetProperty("deepReading").GetProperty("premiumExpiresAt").ValueKind);

        using var deep = await reader.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.OK, deep.StatusCode, await deep.Content.ReadAsStringAsync());
        using var ordinaryAdmin = await reader.GetAsync("/api/admin/users");
        Assert.AreEqual(HttpStatusCode.Forbidden, ordinaryAdmin.StatusCode);

        adminCsrf = await GetCsrfAsync(admin);
        using var revokeRequest = JsonRequest(HttpMethod.Delete, $"/api/admin/users/{userId}/premium", new { expectedRevision = premiumRevision }, adminCsrf);
        using var revoked = await admin.SendAsync(revokeRequest);
        Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());
        using var revokedDeep = await reader.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.Forbidden, revokedDeep.StatusCode);

        using var anonymous = factory.CreateSecureClient();
        using var denied = await anonymous.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using var adminDenied = await anonymous.GetAsync("/api/admin/users");
        Assert.AreEqual(HttpStatusCode.Unauthorized, adminDenied.StatusCode);
    }

    [TestMethod]
    public async Task LoginRateLimitAndLogoutSessionRevocationAreEnforced()
    {
        await using var factory = new AccountApiFactory();
        await factory.BootstrapAdminAsync();
        using var client = factory.CreateSecureClient();
        await LoginAsync(client, "admin@example.test", "Strong-admin-123!");

        using var missingCsrfLogout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, missingCsrfLogout.StatusCode);
        var stillAuthenticated = await ReadDataAsync<JsonElement>(await client.GetAsync("/api/auth/me"));
        Assert.IsTrue(stillAuthenticated.GetProperty("authenticated").GetBoolean());

        var csrf = await GetCsrfAsync(client);
        using var logout = JsonRequest(HttpMethod.Post, "/api/auth/logout", new { }, csrf);
        using var logoutResponse = await client.SendAsync(logout);
        Assert.AreEqual(HttpStatusCode.OK, logoutResponse.StatusCode);
        var me = await ReadDataAsync<JsonElement>(await client.GetAsync("/api/auth/me"));
        Assert.IsFalse(me.GetProperty("authenticated").GetBoolean());

        using var attacker = factory.CreateSecureClient();
        csrf = await GetCsrfAsync(attacker);
        HttpStatusCode lastStatus = 0;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var request = JsonRequest(HttpMethod.Post, "/api/auth/login", new { email = "nobody@example.test", password = "wrong" }, csrf);
            using var response = await attacker.SendAsync(request);
            lastStatus = response.StatusCode;
        }
        Assert.AreEqual(HttpStatusCode.TooManyRequests, lastStatus);
    }

    [TestMethod]
    public async Task VerificationAndPasswordResetEndpointsRejectReplayAndResetRevokesSession()
    {
        await using var factory = new AccountApiFactory(publicRegistration: true);
        using var client = factory.CreateSecureClient();
        var csrf = await GetCsrfAsync(client);

        using var register = JsonRequest(HttpMethod.Post, "/api/auth/register", new
        {
            email = "recovery@example.test", password = "Strong-user-123!"
        }, csrf);
        using var registered = await client.SendAsync(register);
        Assert.AreEqual(HttpStatusCode.OK, registered.StatusCode, await registered.Content.ReadAsStringAsync());

        var verificationToken = factory.Notifications.VerificationToken!;
        using var confirmVerification = JsonRequest(HttpMethod.Post, "/api/auth/email-verification/confirm", new
        {
            email = "recovery@example.test", token = verificationToken
        }, csrf);
        using var verified = await client.SendAsync(confirmVerification);
        Assert.AreEqual(HttpStatusCode.OK, verified.StatusCode, await verified.Content.ReadAsStringAsync());

        using var replayVerification = JsonRequest(HttpMethod.Post, "/api/auth/email-verification/confirm", new
        {
            email = "recovery@example.test", token = verificationToken
        }, csrf);
        using var verificationReplay = await client.SendAsync(replayVerification);
        Assert.AreEqual(HttpStatusCode.BadRequest, verificationReplay.StatusCode);

        await LoginAsync(client, "recovery@example.test", "Strong-user-123!");
        csrf = await GetCsrfAsync(client);
        using var requestReset = JsonRequest(HttpMethod.Post, "/api/auth/password-reset/request", new
        {
            email = "recovery@example.test"
        }, csrf);
        using var resetRequested = await client.SendAsync(requestReset);
        Assert.AreEqual(HttpStatusCode.OK, resetRequested.StatusCode, await resetRequested.Content.ReadAsStringAsync());

        var resetToken = factory.Notifications.ResetToken!;
        using var confirmReset = JsonRequest(HttpMethod.Post, "/api/auth/password-reset/confirm", new
        {
            email = "recovery@example.test", token = resetToken,
            newPassword = "New-strong-456!", confirmPassword = "New-strong-456!"
        }, csrf);
        using var reset = await client.SendAsync(confirmReset);
        Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode, await reset.Content.ReadAsStringAsync());

        using var me = await client.GetAsync("/api/auth/me");
        Assert.IsFalse((await ReadDataAsync<JsonElement>(me)).GetProperty("authenticated").GetBoolean());

        csrf = await GetCsrfAsync(client);
        using var replayReset = JsonRequest(HttpMethod.Post, "/api/auth/password-reset/confirm", new
        {
            email = "recovery@example.test", token = resetToken,
            newPassword = "Another-strong-789!", confirmPassword = "Another-strong-789!"
        }, csrf);
        using var resetReplay = await client.SendAsync(replayReset);
        Assert.AreEqual(HttpStatusCode.BadRequest, resetReplay.StatusCode);

        csrf = await GetCsrfAsync(client);
        using var oldPassword = JsonRequest(HttpMethod.Post, "/api/auth/login", new
        {
            email = "recovery@example.test", password = "Strong-user-123!"
        }, csrf);
        using var oldPasswordResponse = await client.SendAsync(oldPassword);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);
        await LoginAsync(client, "recovery@example.test", "New-strong-456!");
    }

    [TestMethod]
    public async Task RegistrationAndRecoveryEndpointsEnforceTheirRateLimits()
    {
        await using var factory = new AccountApiFactory(publicRegistration: true);
        using var client = factory.CreateSecureClient();
        var csrf = await GetCsrfAsync(client);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = JsonRequest(HttpMethod.Post, "/api/auth/register", new
            {
                email = $"rate-{attempt}@example.test", password = "Strong-user-123!"
            }, csrf);
            using var response = await client.SendAsync(request);
            Assert.AreEqual(attempt < 3 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var request = JsonRequest(HttpMethod.Post, "/api/auth/password-reset/request", new
            {
                email = "unknown@example.test"
            }, csrf);
            using var response = await client.SendAsync(request);
            Assert.AreEqual(attempt < 5 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private static object ValidDeepReading() => new
    {
        question = "Give me guidance.", spread = "DAILY_1", locale = "en", readingMode = "DEEP",
        cards = new[] { new { position = "GUIDANCE", cardId = "THE_FOOL", orientation = "UPRIGHT" } }
    };

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var token = await GetCsrfAsync(client);
        using var request = JsonRequest(HttpMethod.Post, "/api/auth/login", new { email, password }, token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        var data = await ReadDataAsync<JsonElement>(response);
        return data.GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return request;
    }

    private static void AssertHardenedCookie(string cookie)
    {
        StringAssert.Contains(cookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "httponly", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "samesite=lax", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "path=/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Deserialize<T>()!;
    }

    private sealed class AccountApiFactory(bool publicRegistration = true) : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();
        public RecordingNotifications Notifications { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeepReading:AllowUnentitledInDevelopment"] = "false",
                ["Account:PublicRegistrationEnabled"] = publicRegistration.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountService>();
                services.AddDbContext<TarotDbContext>(options => options.UseInMemoryDatabase(_databaseName));
                services.AddScoped<IAccountService, AccountService>();
                services.RemoveAll<IAccountNotificationSender>();
                services.AddSingleton<IAccountNotificationSender>(Notifications);
                services.RemoveAll<ITarotReadingService>();
                services.AddSingleton<ITarotReadingService>(new StubReadingService());
            });
        }

        public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

        public async Task BootstrapAdminAsync()
        {
            _ = Services;
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TarotDbContext>();
            await database.Database.EnsureCreatedAsync();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            var result = await accounts.BootstrapAdminAsync("admin@example.test", "Strong-admin-123!", default);
            Assert.IsTrue(result.Succeeded);
        }
    }

    private sealed class RecordingNotifications : IAccountNotificationSender
    {
        public string? VerificationToken { get; private set; }
        public string? ResetToken { get; private set; }

        public Task SendEmailVerificationAsync(string email, string token, CancellationToken cancellationToken)
        {
            VerificationToken = token;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken)
        {
            ResetToken = token;
            return Task.CompletedTask;
        }
    }

    private sealed class StubReadingService : ITarotReadingService
    {
        public Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken) =>
            Task.FromResult(TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification()));
    }
}
