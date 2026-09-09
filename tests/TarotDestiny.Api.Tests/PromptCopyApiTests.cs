using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class PromptCopyApiTests
{
    [TestMethod]
    public async Task StandardReadingSurvivesLoginButDirectRequestsCannotBypassRewards()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var readingResponse = await client.PostAsJsonAsync("/api/readings/generate", TestSupport.DestinyRequest());
        Assert.AreEqual(HttpStatusCode.OK, readingResponse.StatusCode);
        var reading = await Data(readingResponse);
        var id = reading.GetProperty("promptReadingId").GetGuid();
        Assert.IsFalse(reading.TryGetProperty("promptSnapshot", out _));
        foreach (var method in new[] { "status", "sessions", "attempts", "copy" })
        {
            using var response = method == "status" ? await client.GetAsync($"/api/readings/{id}/prompt/{method}")
                : await client.PostAsJsonAsync($"/api/readings/{id}/prompt/{method}", new { });
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.IsFalse(error.GetProperty("success").GetBoolean());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            Assert.IsTrue((await accounts.BootstrapAdminAsync("copy@example.test", "Strong-copy-123!", default)).Succeeded);
        }
        var csrf = await Data(await client.GetAsync("/api/auth/csrf"));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = "copy@example.test", password = "Strong-copy-123!" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = await Data(await client.GetAsync("/api/auth/csrf"));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var status = await Data(await client.GetAsync($"/api/readings/{id}/prompt/status"));
        Assert.AreEqual(2, status.GetProperty("requiredAds").GetInt32());
        Assert.IsFalse(status.GetProperty("unlocked").GetBoolean());
        // Provider setup is intentionally disabled; login alone never releases the snapshot.
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/api/readings/{id}/prompt/sessions", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/readings/{id}/prompt/copy", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/rewards/prompt/provider-completions", new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/rewards/prompt/provider-completions", new {
            attemptId = Guid.NewGuid(), eventId = "browser-grant", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        })).StatusCode);
        using var other = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await other.GetAsync($"/api/readings/{id}/prompt/status")).StatusCode);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(envelope.GetProperty("success").GetBoolean());
        return envelope.GetProperty("data");
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string name = Guid.NewGuid().ToString();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => {
                services.AddDbContext<TarotDbContext>(options => options.UseInMemoryDatabase(name));
                services.RemoveAll<IAccountService>(); services.AddScoped<IAccountService, AccountService>();
                services.AddScoped<PromptCopyService>();
            });
        }
    }
}
