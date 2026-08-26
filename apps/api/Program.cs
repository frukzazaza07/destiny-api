using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Middlewares;
using TarotDestiny.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .GroupBy(
                entry => ToCamelCaseModelStateKey(entry.Key),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors)
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The request value is invalid."
                        : error.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var response = new ResponseDto<object, ValidationErrorsDto>(
            null,
            new ValidationErrorsDto(errors),
            ResponseCode.INVALID_REQUEST);
        return new BadRequestObjectResult(response);
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<ClassifierOptions>()
    .Bind(builder.Configuration.GetSection("Classifier"))
    .Validate(options => options.MinimumCacheConfidence is >= 0 and <= 1, "Classifier confidence must be between 0 and 1.")
    .Validate(options => options.DeadlineMilliseconds > 0, "Classifier deadline must be positive.")
    .Validate(options =>
        !options.UseGrpc || Uri.TryCreate(options.GrpcAddress, UriKind.Absolute, out _),
        "Classifier gRPC address must be an absolute URI when gRPC is enabled.")
    .ValidateOnStart();
builder.Services.AddOptions<TarotCacheOptions>()
    .Bind(builder.Configuration.GetSection("TarotCache"))
    .Validate(options => options.AnswerTtlDays > 0, "Tarot cache TTL must be positive.")
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.CacheVersion) &&
        !string.IsNullOrWhiteSpace(options.PromptVersion) &&
        !string.IsNullOrWhiteSpace(options.InterpretationVersion) &&
        !string.IsNullOrWhiteSpace(options.ModelVersion),
        "Tarot cache, content, and Deep model versions are required.")
    .ValidateOnStart();
builder.Services.AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection("LLM"))
    .Validate(options =>
        options.MaxConcurrency > 0 &&
        options.TimeoutSeconds > 0 &&
        options.MaxOutputTokens > 0 &&
        options.RetryCount >= 0,
        "LLM limits must be positive and retry count cannot be negative.")
    .ValidateOnStart();
builder.Services.AddOptions<DeepReadingOptions>()
    .Bind(builder.Configuration.GetSection("DeepReading"))
    .Validate(options =>
        !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.ClaimType) &&
         !string.IsNullOrWhiteSpace(options.ClaimValue)),
        "Deep reading entitlement claim type and value are required when enabled.")
    .ValidateOnStart();

builder.Services.AddCors(options =>
{
    var configuredOrigins = builder.Configuration["Cors:AllowedOrigins"]?
        .Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(origin => origin != "*")
        .ToArray()
        ?? [];

    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                IsAllowedDevelopmentOrigin(origin, configuredOrigins));
            return;
        }

        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins);
            return;
        }

        policy.SetIsOriginAllowed(_ => false);
    });
});

builder.Services.AddHttpClient<ILlmClient, LlmClient>();
var classifierGrpcAddress = builder.Configuration["Classifier:GrpcAddress"] ?? "http://127.0.0.1:50051";
builder.Services.AddGrpcClient<TarotDestiny.Classifier.V1.ClassifierService.ClassifierServiceClient>(options =>
{
    options.Address = new Uri(classifierGrpcAddress);
});
builder.Services.AddSingleton<RuleQuestionClassifier>();
builder.Services.AddSingleton<IRemoteQuestionClassifier, GrpcQuestionClassifier>();
builder.Services.AddSingleton<IQuestionClassifier, ResilientQuestionClassifier>();
builder.Services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IAnswerCache, InMemoryAnswerCache>();
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = Math.Max(1, redisOptions.ConnectRetry);
        return ConnectionMultiplexer.Connect(redisOptions);
    });
    builder.Services.AddSingleton<IAnswerCache, RedisAnswerCache>();
}
builder.Services.AddSingleton<ITarotCatalog, TarotCatalog>();
builder.Services.AddSingleton<IInterpretationEngine, RuleInterpretationEngine>();
builder.Services.AddSingleton<IRuleReadingRenderer, RuleReadingRenderer>();
builder.Services.AddSingleton<IDeckService, DeckService>();
builder.Services.AddSingleton<ILlmGate, LlmGate>();
builder.Services.AddSingleton<IDeepReadingAccessPolicy, DeepReadingAccessPolicy>();
builder.Services.AddSingleton<IReadingResponseValidator, ReadingResponseValidator>();
builder.Services.AddSingleton<TarotMetrics>();
builder.Services.AddScoped<ITarotReadingService, TarotReadingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "Tarot Destiny API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Tarot Destiny API v1");
    });
}
app.UseExceptionHandler();
app.UseCors();
app.MapControllers();

app.Run();

static string ToCamelCaseModelStateKey(string key)
{
    var normalizedKey = key.StartsWith("$.", StringComparison.Ordinal)
        ? key[2..]
        : key;

    if (string.IsNullOrEmpty(normalizedKey) || char.IsLower(normalizedKey[0]))
    {
        return normalizedKey;
    }

    return char.ToLowerInvariant(normalizedKey[0]) + normalizedKey[1..];
}

static bool IsAllowedDevelopmentOrigin(string origin, IReadOnlyCollection<string> configuredOrigins)
{
    if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || uri.Port != 3000
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return false;
    }

    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(uri.Host, out var address)
        && (IPAddress.IsLoopback(address) || IsPrivateAddress(address));
}

static bool IsPrivateAddress(IPAddress address)
{
    if (address.AddressFamily == AddressFamily.InterNetworkV6)
    {
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xfe) == 0xfc;
    }

    if (address.AddressFamily != AddressFamily.InterNetwork)
    {
        return false;
    }

    var octets = address.GetAddressBytes();
    return octets[0] == 10
        || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
        || (octets[0] == 192 && octets[1] == 168);
}

public partial class Program;
