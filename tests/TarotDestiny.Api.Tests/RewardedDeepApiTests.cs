using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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
public sealed class RewardedDeepApiTests
{
    [TestMethod]
    public async Task SeededDefaultsRequireThreeDistinctGrantsAndCreditIsSingleUse()
    {
        await using var factory = new RewardApiFactory();
        using var client = factory.CreateSecureClient();
        var initial = await ReadDataAsync<JsonElement>(await client.GetAsync("/api/rewards/deep/status"));
        Assert.IsTrue(initial.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(3, initial.GetProperty("requiredAdCompletions").GetInt32());
        Assert.AreEqual(1, initial.GetProperty("deepCreditsPerCompletedBundle").GetInt32());

        var csrf = await GetCsrfAsync(client);
        using var sessionRequest = Request(HttpMethod.Post, "/api/rewards/deep/sessions", null, csrf);
        using var sessionResponse = await client.SendAsync(sessionRequest);
        Assert.AreEqual(HttpStatusCode.OK, sessionResponse.StatusCode);
        var cookie = sessionResponse.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-Tarot.Reward=", StringComparison.Ordinal));
        StringAssert.Contains(cookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "httponly", StringComparison.OrdinalIgnoreCase);

        for (var completed = 1; completed <= 3; completed++)
        {
            var nonce = await CreateAttemptAsync(client, csrf);
            using var grant = Request(HttpMethod.Post, "/api/rewards/deep/grants", new { nonce, eventType = "rewardedSlotGranted" }, csrf);
            using var grantResponse = await client.SendAsync(grant);
            var status = await ReadDataAsync<JsonElement>(grantResponse);
            Assert.AreEqual(completed, status.GetProperty("validAdCompletions").GetInt32());
            Assert.AreEqual(completed == 3 ? 1 : 0, status.GetProperty("availableDeepCredits").GetInt32());

            if (completed < 3)
            {
                using var denied = await client.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }
        }

        using var first = await client.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        using var second = await client.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [TestMethod]
    public async Task CloseAndReplayNeverDuplicateProgress()
    {
        await using var factory = new RewardApiFactory();
        using var client = factory.CreateSecureClient();
        var csrf = await GetCsrfAsync(client);
        await StartSessionAsync(client, csrf);

        var closedNonce = await CreateAttemptAsync(client, csrf);
        using var close = Request(HttpMethod.Post, "/api/rewards/deep/attempts/close", new { nonce = closedNonce }, csrf);
        var closed = await ReadDataAsync<JsonElement>(await client.SendAsync(close));
        Assert.AreEqual(0, closed.GetProperty("validAdCompletions").GetInt32());

        var nonce = await CreateAttemptAsync(client, csrf);
        using var grant = Request(HttpMethod.Post, "/api/rewards/deep/grants", new { nonce, eventType = "rewardedSlotGranted" }, csrf);
        using var grantedResponse = await client.SendAsync(grant);
        Assert.AreEqual(HttpStatusCode.OK, grantedResponse.StatusCode);

        using var replay = Request(HttpMethod.Post, "/api/rewards/deep/grants", new { nonce, eventType = "rewardedSlotGranted" }, csrf);
        using var replayed = await client.SendAsync(replay);
        Assert.AreEqual(HttpStatusCode.Conflict, replayed.StatusCode);
        var status = await ReadDataAsync<JsonElement>(await client.GetAsync("/api/rewards/deep/status"));
        Assert.AreEqual(1, status.GetProperty("validAdCompletions").GetInt32());
    }

    [TestMethod]
    public async Task AdminUpdateDoesNotChangeInProgressSessionSnapshot()
    {
        await using var factory = new RewardApiFactory();
        await factory.BootstrapAdminAsync();
        using var reader = factory.CreateSecureClient();
        var readerCsrf = await GetCsrfAsync(reader);
        await StartSessionAsync(reader, readerCsrf);

        using var admin = factory.CreateSecureClient();
        await LoginAsync(admin);
        var settings = await ReadDataAsync<JsonElement>(await admin.GetAsync("/api/admin/rewarded-deep/settings"));
        var adminCsrf = await GetCsrfAsync(admin);
        using var update = Request(HttpMethod.Put, "/api/admin/rewarded-deep/settings", new
        {
            requiredAdCompletions = 2,
            deepCreditsPerCompletedBundle = 2,
            expectedRevision = settings.GetProperty("revision").GetInt32()
        }, adminCsrf);
        using var updateResponse = await admin.SendAsync(update);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode, await updateResponse.Content.ReadAsStringAsync());

        var status = await ReadDataAsync<JsonElement>(await reader.GetAsync("/api/rewards/deep/status"));
        Assert.AreEqual(3, status.GetProperty("requiredAdCompletions").GetInt32());
        Assert.AreEqual(1, status.GetProperty("deepCreditsPerCompletedBundle").GetInt32());

        using var newReader = factory.CreateSecureClient();
        var newCsrf = await GetCsrfAsync(newReader);
        var newStatus = await StartSessionAsync(newReader, newCsrf);
        Assert.AreEqual(2, newStatus.GetProperty("requiredAdCompletions").GetInt32());
        Assert.AreEqual(2, newStatus.GetProperty("deepCreditsPerCompletedBundle").GetInt32());
    }

    [TestMethod]
    public async Task PremiumIsFirstPriorityAndDoesNotConsumeRewardCredit()
    {
        await using var factory = new RewardApiFactory();
        var adminId = await factory.BootstrapAdminAsync();
        using var admin = factory.CreateSecureClient();
        await LoginAsync(admin);
        var csrf = await GetCsrfAsync(admin);
        await StartSessionAsync(admin, csrf);
        await CompleteBundleAsync(admin, csrf);
        await factory.GrantPremiumAsync(adminId);

        using var reading = await admin.PostAsJsonAsync("/api/readings/generate", ValidDeepReading());
        Assert.AreEqual(HttpStatusCode.OK, reading.StatusCode, await reading.Content.ReadAsStringAsync());
        Assert.AreEqual(1, await factory.AvailableCreditCountAsync(adminId));
    }

    [TestMethod]
    public async Task ConcurrentDeepRequestsConsumeOneCreditAtMostOnce()
    {
        await using var factory = new RewardApiFactory(delayReading: true);
        using var client = factory.CreateSecureClient();
        var csrf = await GetCsrfAsync(client);
        await StartSessionAsync(client, csrf);
        await CompleteBundleAsync(client, csrf);

        var requests = new[]
        {
            client.PostAsJsonAsync("/api/readings/generate", ValidDeepReading()),
            client.PostAsJsonAsync("/api/readings/generate", ValidDeepReading())
        };
        var responses = await Task.WhenAll(requests);
        CollectionAssert.AreEquivalent(
            new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden },
            responses.Select(response => response.StatusCode).ToArray());
        foreach (var response in responses) response.Dispose();
    }

    private static object ValidDeepReading() => new
    {
        question = "Give me guidance.", spread = "DAILY_1", locale = "en", readingMode = "DEEP",
        cards = new[] { new { position = "GUIDANCE", cardId = "THE_FOOL", orientation = "UPRIGHT" } }
    };

    private static async Task<JsonElement> StartSessionAsync(HttpClient client, string csrf)
    {
        using var request = Request(HttpMethod.Post, "/api/rewards/deep/sessions", null, csrf);
        return await ReadDataAsync<JsonElement>(await client.SendAsync(request));
    }

    private static async Task CompleteBundleAsync(HttpClient client, string csrf)
    {
        for (var index = 0; index < 3; index++)
        {
            var nonce = await CreateAttemptAsync(client, csrf);
            using var grant = Request(HttpMethod.Post, "/api/rewards/deep/grants", new { nonce, eventType = "rewardedSlotGranted" }, csrf);
            using var response = await client.SendAsync(grant);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task<string> CreateAttemptAsync(HttpClient client, string csrf)
    {
        using var request = Request(HttpMethod.Post, "/api/rewards/deep/attempts", null, csrf);
        var data = await ReadDataAsync<JsonElement>(await client.SendAsync(request));
        return data.GetProperty("nonce").GetString()!;
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var csrf = await GetCsrfAsync(client);
        using var request = Request(HttpMethod.Post, "/api/auth/login", new { email = "admin@example.test", password = "Strong-admin-123!" }, csrf);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var data = await ReadDataAsync<JsonElement>(await client.GetAsync("/api/auth/csrf"));
        return data.GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body, string csrf)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return request;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Deserialize<T>()!;
    }

    private sealed class RewardApiFactory(bool delayReading = false) : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeepReading:AllowUnentitledInDevelopment"] = "false",
                ["RewardedDeep:Enabled"] = "true",
                ["RewardedDeep:AdUnitPath"] = "/1234567/tarot-reward-test"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountService>();
                services.RemoveAll<IRewardedDeepService>();
                services.AddDbContext<TarotDbContext>(options => options.UseInMemoryDatabase(_databaseName));
                services.AddScoped<IAccountService, AccountService>();
                services.AddScoped<IRewardedDeepService, RewardedDeepService>();
                services.RemoveAll<ITarotReadingService>();
                services.AddSingleton<ITarotReadingService>(new StubReadingService(delayReading));
            });
        }

        public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

        public async Task<Guid> BootstrapAdminAsync()
        {
            _ = Services;
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TarotDbContext>();
            await database.Database.EnsureCreatedAsync();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            var result = await accounts.BootstrapAdminAsync("admin@example.test", "Strong-admin-123!", default);
            Assert.IsTrue(result.Succeeded);
            return result.Account!.UserId!.Value;
        }

        public async Task GrantPremiumAsync(Guid userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            var result = await accounts.GrantPremiumAsync(userId, new PremiumGrantRequestDto { Days = 30 }, userId, default);
            Assert.IsTrue(result.Succeeded);
        }

        public async Task<int> AvailableCreditCountAsync(Guid userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TarotDbContext>();
            var now = DateTimeOffset.UtcNow;
            return await database.RewardedDeepCredits.CountAsync(item => item.UserId == userId && item.ConsumedAt == null && item.ExpiresAt > now);
        }
    }

    private sealed class StubReadingService(bool delay) : ITarotReadingService
    {
        public async Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, CancellationToken cancellationToken)
        {
            if (delay) await Task.Delay(100, cancellationToken);
            return TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification());
        }
    }
}
