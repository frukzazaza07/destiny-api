using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class ApiContractTests
{
    [TestMethod]
    [DataRow("http://localhost:3000", true)]
    [DataRow("http://192.168.1.25:3000", true)]
    [DataRow("https://example.com", false)]
    public async Task DevelopmentCorsAllowsOnlySupportedLocalOrigins(string origin, bool expectedAllowed)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await AssertPreflightAsync(client, origin, expectedAllowed);
    }

    [TestMethod]
    public async Task ProductionCorsAllowsOnlyExplicitlyConfiguredOrigins()
    {
        const string configuredOrigin = "https://tarot.example.test";
        await using var factory = new ApiFactory(
            useProductionEnvironment: true,
            allowedOrigins: configuredOrigin);
        using var client = factory.CreateClient();

        await AssertPreflightAsync(client, configuredOrigin, expectedAllowed: true);
        await AssertPreflightAsync(client, "http://localhost:3000", expectedAllowed: false);
    }

    [TestMethod]
    public async Task AllSixPublicRoutesRemainAvailableAndReturnSuccessEnvelopes()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await AssertEnvelopeAsync(await client.GetAsync("/health"), HttpStatusCode.OK, true, "SUCCESS");
        await AssertEnvelopeAsync(await client.GetAsync("/metrics"), HttpStatusCode.OK, true, "SUCCESS");
        await AssertEnvelopeAsync(await client.GetAsync("/api/readings/options"), HttpStatusCode.OK, true, "SUCCESS");

        var shuffle = await AssertEnvelopeAsync(
            await client.PostAsJsonAsync("/api/deck/shuffle", new { spread = "DAILY_1" }),
            HttpStatusCode.OK,
            true,
            "SUCCESS");
        var sessionId = shuffle.GetProperty("data").GetProperty("sessionId").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(sessionId));

        await AssertEnvelopeAsync(
            await client.PostAsJsonAsync(
                $"/api/deck/{sessionId}/resolve",
                new { selectedIndexes = new[] { 0 } }),
            HttpStatusCode.OK,
            true,
            "SUCCESS");

        await AssertEnvelopeAsync(
            await client.PostAsJsonAsync("/api/readings/generate", ValidReadingRequest()),
            HttpStatusCode.OK,
            true,
            "SUCCESS");
    }

    [TestMethod]
    public async Task DevelopmentOpenApiDocumentsAdminTrainingAndWarmupRoutes()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.IsTrue(paths.TryGetProperty("/api/admin/cache/analytics", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/admin/cache/warmups", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/classifier/training-examples", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/classifier/training-examples/{id}/review", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/classifier/training-examples/export", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/auth/csrf", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/auth/login", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/auth/password-reset/confirm", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/admin/users", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/admin/users/{userId}/premium", out _));
        Assert.IsTrue(paths.TryGetProperty("/api/admin/users/{userId}/entitlements", out _));

        using var swagger = await client.GetAsync("/swagger/index.html");
        Assert.AreEqual(HttpStatusCode.OK, swagger.StatusCode);
    }

    [TestMethod]
    public async Task ProductionDoesNotExposeSwagger()
    {
        await using var factory = new ApiFactory(useProductionEnvironment: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminRoutesRequireAdminKeyEnvelope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await AssertEnvelopeAsync(
            await client.GetAsync("/api/admin/cache/analytics"),
            HttpStatusCode.Unauthorized,
            false,
            "UNAUTHORIZED");
        await AssertEnvelopeAsync(
            await client.GetAsync("/api/classifier/training-examples"),
            HttpStatusCode.Unauthorized,
            false,
            "UNAUTHORIZED");
    }

    [TestMethod]
    public async Task ExplicitControllerErrorsUseTheResponseEnvelope()
    {
        await using var developmentFactory = new ApiFactory();
        using var developmentClient = developmentFactory.CreateClient();

        await AssertEnvelopeAsync(
            await developmentClient.PostAsJsonAsync("/api/deck/shuffle", new { spread = "UNKNOWN" }),
            HttpStatusCode.BadRequest,
            false,
            "INVALID_REQUEST");

        await AssertEnvelopeAsync(
            await developmentClient.PostAsJsonAsync(
                "/api/deck/missing-session/resolve",
                new { selectedIndexes = new[] { 0 } }),
            HttpStatusCode.NotFound,
            false,
            "NOT_FOUND");

        await using var productionFactory = new ApiFactory(useProductionEnvironment: true);
        using var productionClient = productionFactory.CreateClient();
        await AssertEnvelopeAsync(
            await productionClient.PostAsJsonAsync("/api/readings/generate", ValidReadingRequest("DEEP")),
            HttpStatusCode.Forbidden,
            false,
            "FORBIDDEN");
    }

    [TestMethod]
    public async Task DataAnnotationsValidationUsesTheGlobalErrorEnvelope()
    {
        await using var factory = new ApiFactory(
            generationException: new Exception("The reading service must not run for an invalid model."));
        using var client = factory.CreateClient();
        var invalidRequest = new
        {
            locale = "x",
            cards = Array.Empty<object>(),
            readingMode = "STANDARD"
        };

        var envelope = await AssertEnvelopeAsync(
            await client.PostAsJsonAsync("/api/readings/generate", invalidRequest),
            HttpStatusCode.BadRequest,
            false,
            "INVALID_REQUEST");

        AssertValidationErrors(envelope, "spread", "locale", "cards");
    }

    [TestMethod]
    public async Task JsonBindingFailuresUseTheGlobalErrorEnvelope()
    {
        await using var factory = new ApiFactory(
            generationException: new Exception("The reading service must not run for an invalid model."));
        using var client = factory.CreateClient();

        const string invalidEnumJson = """
            {
              "question": "Daily guidance",
              "spread": "DAILY_1",
              "locale": "en",
              "cards": [
                {
                  "position": "GUIDANCE",
                  "cardId": "THE_FOOL",
                  "orientation": "UPRIGHT"
                }
              ],
              "readingMode": "NOT_A_MODE"
            }
            """;
        var invalidEnumEnvelope = await AssertEnvelopeAsync(
            await client.PostAsync(
                "/api/readings/generate",
                new StringContent(invalidEnumJson, Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest,
            false,
            "INVALID_REQUEST");
        AssertValidationErrors(invalidEnumEnvelope, "readingMode");

        var malformedEnvelope = await AssertEnvelopeAsync(
            await client.PostAsync(
                "/api/readings/generate",
                new StringContent("{\"spread\":", Encoding.UTF8, "application/json")),
            HttpStatusCode.BadRequest,
            false,
            "INVALID_REQUEST");
        AssertValidationErrors(malformedEnvelope);
    }

    [TestMethod]
    [DataRow("argument", 400, "INVALID_REQUEST", "Known argument failure.")]
    [DataRow("argument-range", 400, "INVALID_REQUEST", "Known range failure.")]
    [DataRow("not-found", 404, "NOT_FOUND", "Known missing resource.")]
    [DataRow("forbidden", 403, "FORBIDDEN", "Known forbidden operation.")]
    [DataRow("invalid-operation", 502, "INTERNAL_ERROR", "Reading generation failed.")]
    [DataRow("unknown", 502, "INTERNAL_ERROR", "Service unavailable.")]
    public async Task GlobalExceptionHandlerMapsExceptionsToConsistentEnvelopes(
        string exceptionKind,
        int expectedStatus,
        string expectedCode,
        string expectedError)
    {
        var exception = CreateException(exceptionKind);
        await using var factory = new ApiFactory(
            generationException: exception,
            useProductionEnvironment: true);
        using var client = factory.CreateClient();

        var envelope = await AssertEnvelopeAsync(
            await client.PostAsJsonAsync("/api/readings/generate", ValidReadingRequest()),
            (HttpStatusCode)expectedStatus,
            false,
            expectedCode);

        var error = envelope.GetProperty("error").GetString();
        Assert.IsNotNull(error);
        if (exceptionKind is "invalid-operation" or "unknown")
        {
            Assert.AreEqual(expectedError, error);
            Assert.IsFalse(error.Contains(exception.Message, StringComparison.Ordinal));
        }
        else
        {
            StringAssert.Contains(error, expectedError);
        }
    }

    private static object ValidReadingRequest(string readingMode = "STANDARD") => new
    {
        question = "Give me daily guidance.",
        spread = "DAILY_1",
        locale = "en",
        cards = new[]
        {
            new
            {
                position = "GUIDANCE",
                cardId = "THE_FOOL",
                orientation = "UPRIGHT"
            }
        },
        readingMode
    };

    private static Exception CreateException(string kind) => kind switch
    {
        "argument" => new ArgumentException("Known argument failure."),
        "argument-range" => new ArgumentOutOfRangeException("index", "Known range failure."),
        "not-found" => new KeyNotFoundException("Known missing resource."),
        "forbidden" => new UnauthorizedAccessException("Known forbidden operation."),
        "invalid-operation" => new InvalidOperationException("Sensitive invalid-operation detail."),
        "unknown" => new Exception("Sensitive unhandled detail."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown test exception kind.")
    };

    private static async Task<JsonElement> AssertEnvelopeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        bool expectedSuccess,
        string expectedCode)
    {
        using (response)
        {
            Assert.AreEqual(expectedStatus, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.AreEqual(JsonValueKind.Object, root.ValueKind);
            CollectionAssert.AreEquivalent(
                new[] { "success", "data", "error", "code" },
                root.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual(expectedSuccess, root.GetProperty("success").GetBoolean());
            Assert.AreEqual(expectedCode, root.GetProperty("code").GetString());

            if (expectedSuccess)
            {
                Assert.AreNotEqual(JsonValueKind.Null, root.GetProperty("data").ValueKind);
                Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            }
            else
            {
                Assert.AreEqual(JsonValueKind.Null, root.GetProperty("data").ValueKind);
                Assert.AreNotEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
            }

            return root.Clone();
        }
    }

    private static async Task AssertPreflightAsync(HttpClient client, string origin, bool expectedAllowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/deck/shuffle");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);
        var hasAllowedOrigin = response.Headers.TryGetValues(
            "Access-Control-Allow-Origin",
            out var allowedOrigins);
        Assert.AreEqual(expectedAllowed, hasAllowedOrigin);
        if (expectedAllowed)
        {
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            CollectionAssert.AreEqual(new[] { origin }, allowedOrigins!.ToArray());
            Assert.IsTrue(
                response.Headers.TryGetValues("Access-Control-Allow-Methods", out var allowedMethods));
            Assert.IsTrue(
                allowedMethods.Any(value => value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("POST", StringComparer.OrdinalIgnoreCase)));
            Assert.IsTrue(
                response.Headers.TryGetValues("Access-Control-Allow-Headers", out var allowedHeaders));
            Assert.IsTrue(
                allowedHeaders.Any(value => value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("content-type", StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static void AssertValidationErrors(JsonElement envelope, params string[] expectedFields)
    {
        var errors = envelope.GetProperty("error");
        Assert.AreEqual(JsonValueKind.Object, errors.ValueKind);
        var properties = errors.EnumerateObject().ToArray();
        Assert.IsTrue(properties.Length > 0);
        var fields = properties.Select(property => property.Name).ToArray();
        foreach (var expectedField in expectedFields)
        {
            CollectionAssert.Contains(fields, expectedField);
        }

        foreach (var property in properties)
        {
            Assert.AreEqual(JsonValueKind.Array, property.Value.ValueKind);
            var messages = property.Value
                .EnumerateArray()
                .Select(message => message.GetString())
                .ToArray();
            Assert.IsTrue(messages.Length > 0);
            Assert.IsTrue(messages.All(message => !string.IsNullOrWhiteSpace(message)));
        }
    }

    private sealed class ApiFactory(
        Exception? generationException = null,
        bool useProductionEnvironment = false,
        string? allowedOrigins = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(useProductionEnvironment ? "Production" : "Development");
            if (allowedOrigins is not null)
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Cors:AllowedOrigins"] = allowedOrigins
                    }));
            }
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITarotReadingService>();
                services.AddSingleton<ITarotReadingService>(
                    new StubReadingService(generationException));
            });
        }
    }

    private sealed class StubReadingService(Exception? generationException) : ITarotReadingService
    {
        public Task<TarotReadingResponse> GenerateAsync(
            TarotReadingDto request,
            CancellationToken cancellationToken)
        {
            if (generationException is not null)
            {
                return Task.FromException<TarotReadingResponse>(generationException);
            }

            return Task.FromResult(
                TestSupport.ValidResponse(request, TestSupport.CareerChangeClassification()));
        }
    }
}
