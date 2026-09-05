using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TarotDestiny.Api.Data;
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
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("TarotDestiny");
var dataProtectionKeysPath = builder.Configuration["Account:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Tarot.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddAuthentication(AccountAuthentication.Scheme)
    .AddCookie(AccountAuthentication.Scheme, options =>
    {
        options.Cookie.Name = "__Host-Tarot.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.SlidingExpiration = false;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => WriteAuthenticationErrorAsync(context.Response, ResponseCode.UNAUTHORIZED, "Authentication is required."),
            OnRedirectToAccessDenied = context => WriteAuthenticationErrorAsync(context.Response, ResponseCode.FORBIDDEN, "Administrator access is required.")
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ResponseDto<object, string>(null, "Too many account requests. Try again later.", ResponseCode.TOO_MANY_REQUESTS),
            cancellationToken);
    };
    options.AddPolicy("login", context => CreateRateLimitPartition(context, 10, TimeSpan.FromMinutes(1)));
    options.AddPolicy("account-create", context => CreateRateLimitPartition(context, 3, TimeSpan.FromHours(1)));
    options.AddPolicy("account-recovery", context => CreateRateLimitPartition(context, 5, TimeSpan.FromMinutes(15)));
    options.AddPolicy("reward-session", context => CreateRateLimitPartition(context, 10, TimeSpan.FromHours(1)));
    options.AddPolicy("reward-attempt", context => CreateRateLimitPartition(context, 20, TimeSpan.FromHours(1)));
    options.AddPolicy("reward-grant", context => CreateRateLimitPartition(context, 20, TimeSpan.FromHours(1)));
});

builder.Services.AddOptions<ClassifierOptions>()
    .Bind(builder.Configuration.GetSection("Classifier"))
    .Validate(options => options.MinimumCacheConfidence is >= 0 and <= 1, "Classifier confidence must be between 0 and 1.")
    .Validate(options => options.MinimumSharedCacheConfidence is >= 0 and <= 1, "Shared-cache confidence must be between 0 and 1.")
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
        !string.IsNullOrWhiteSpace(options.TaxonomyVersion) &&
        !string.IsNullOrWhiteSpace(options.PromptVersion) &&
        !string.IsNullOrWhiteSpace(options.InterpretationVersion) &&
        !string.IsNullOrWhiteSpace(options.ModelVersion),
        "Tarot cache, content, and Deep model versions are required.")
    .ValidateOnStart();
builder.Services.AddOptions<DeepSharedCacheOptions>()
    .Bind(builder.Configuration.GetSection("DeepSharedCache"))
    .Validate(options => options.ApprovedIntents.All(intent => TarotIntents.All.Contains(intent, StringComparer.OrdinalIgnoreCase)),
        "Deep shared-cache approved intents must use the fixed taxonomy.")
    .ValidateOnStart();
builder.Services.AddOptions<StartupCacheWarmupOptions>()
    .Bind(builder.Configuration.GetSection("StartupCacheWarmup"))
    .Validate(options =>
        options.DelaySeconds >= 0 && options.Variants > 0 &&
        options.MaxCombinationsPerStartup > 0 && options.MaxConcurrency > 0 &&
        options.MaxDeepGenerationsPerStartup >= 0 && options.RetryCount >= 0 &&
        options.RetryDelaySeconds >= 0,
        "Startup cache warmup limits are invalid.")
    .Validate(options => options.Locales.All(LocaleIds.IsSupported), "Startup warmup locales must be en or th.")
    .Validate(options => options.ApprovedIntents.All(intent => TarotIntents.All.Contains(intent, StringComparer.OrdinalIgnoreCase)),
        "Startup warmup approved intents must use the fixed taxonomy.")
    .Validate(options => options.Spreads.All(spread => SpreadIds.TryNormalize(spread, out _)),
        "Startup warmup spreads are invalid.")
    .Validate(options => options.ReadingModes.All(mode => Enum.TryParse<ReadingMode>(mode, true, out _)),
        "Startup warmup reading modes are invalid.")
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
builder.Services.AddOptions<RewardedDeepOptions>()
    .Bind(builder.Configuration.GetSection("RewardedDeep"))
    .Validate(options => options.SessionHours is > 0 and <= 24 &&
        options.AttemptMinutes is > 0 and <= 15 &&
        options.ReservationMinutes is > 0 and <= 15,
        "Rewarded DEEP expiry limits are invalid.")
    .Validate(options => !options.Enabled ||
        (string.Equals(options.Provider, "GOOGLE_AD_MANAGER", StringComparison.Ordinal) &&
         options.AdUnitPath.StartsWith("/", StringComparison.Ordinal) &&
         options.AdUnitPath.Length <= 200),
        "Rewarded DEEP requires a reviewed Google Ad Manager ad-unit path when enabled.")
    .ValidateOnStart();
builder.Services.AddOptions<BaseInterpretationCacheOptions>()
    .Bind(builder.Configuration.GetSection("BaseInterpretationCache"))
    .Validate(options => options.MaximumEntries > 0 && options.TtlMinutes > 0,
        "Base interpretation cache limits must be positive.")
    .ValidateOnStart();
builder.Services.AddOptions<ClassifierTrainingOptions>()
    .Bind(builder.Configuration.GetSection("ClassifierTraining"))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConsentVersion),
        "Classifier training consent version is required when collection is enabled.")
    .ValidateOnStart();
builder.Services.AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection("Admin"));
builder.Services.AddOptions<AccountOptions>()
    .Bind(builder.Configuration.GetSection("Account"))
    .Validate(options => options.SessionHours is > 0 and <= 168 &&
        options.VerificationTokenMinutes is > 0 and <= 1440 &&
        options.PasswordResetTokenMinutes is > 0 and <= 1440 &&
        options.MaxFailedAccessAttempts is > 0 and <= 20 &&
        options.LockoutMinutes is > 0 and <= 1440,
        "Account security limits are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<AccountEmailOptions>()
    .Bind(builder.Configuration.GetSection("AccountEmail"))
    .Validate(options => !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.Host) && options.Port > 0 &&
         System.Net.Mail.MailAddress.TryCreate(options.FromAddress, out _) &&
         IsSecurePublicSiteUrl(options.PublicSiteUrl) &&
         (string.IsNullOrWhiteSpace(options.Username) == string.IsNullOrWhiteSpace(options.Password))),
        "Account email settings are required when delivery is enabled.")
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
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();

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
builder.Services.AddSingleton<IInferenceRouter, InferenceRouter>();
builder.Services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IAnswerCache, InMemoryAnswerCache>();
    builder.Services.AddSingleton<ICacheLock, InMemoryCacheLock>();
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
    builder.Services.AddSingleton<ICacheLock, RedisCacheLock>();
}
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["Postgres:ConnectionString"];
if (string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddSingleton<IGeneratedAnswerStore, NullGeneratedAnswerStore>();
    builder.Services.AddSingleton<IClassifierTrainingStore, UnavailableClassifierTrainingStore>();
    builder.Services.AddSingleton<IAccountService, UnavailableAccountService>();
    builder.Services.AddSingleton<IRewardedDeepService, UnavailableRewardedDeepService>();
}
else
{
    builder.Services.AddDbContext<TarotDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
    builder.Services.AddScoped<IGeneratedAnswerStore, PostgresGeneratedAnswerStore>();
    builder.Services.AddScoped<IClassifierTrainingStore, ClassifierTrainingStore>();
    builder.Services.AddScoped<IAccountService, AccountService>();
    builder.Services.AddScoped<IRewardedDeepService, RewardedDeepService>();
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<UserAccountEntity>, PasswordHasher<UserAccountEntity>>();
builder.Services.AddSingleton<IAccountNotificationSender, AccountNotificationSender>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, DatabaseSessionClaimsTransformation>();
builder.Services.AddScoped<ApiAntiforgeryFilter>();
builder.Services.AddScoped<ApiAntiforgeryForCookieUserFilter>();
builder.Services.AddSingleton<IAdminAccessPolicy, AdminAccessPolicy>();
builder.Services.AddSingleton<ITarotCatalog, TarotCatalog>();
builder.Services.AddSingleton<RuleInterpretationEngine>();
builder.Services.AddSingleton<IInterpretationEngine>(services =>
    new CachingInterpretationEngine(
        services.GetRequiredService<RuleInterpretationEngine>(),
        services.GetRequiredService<IOptions<BaseInterpretationCacheOptions>>(),
        services.GetRequiredService<IOptions<TarotCacheOptions>>()));
builder.Services.AddSingleton<IRuleReadingRenderer, RuleReadingRenderer>();
builder.Services.AddSingleton<IAnswerVariantSelector, RandomAnswerVariantSelector>();
builder.Services.AddSingleton<IDeckService, DeckService>();
builder.Services.AddSingleton<ILlmGate, LlmGate>();
builder.Services.AddSingleton<IDeepReadingAccessPolicy, DeepReadingAccessPolicy>();
builder.Services.AddSingleton<IReadingResponseValidator, ReadingResponseValidator>();
builder.Services.AddSingleton<ISharedReadingSafetyEvaluator, SharedReadingSafetyEvaluator>();
builder.Services.AddSingleton<TarotMetrics>();
builder.Services.AddSingleton<CacheWarmupService>();
builder.Services.AddSingleton<ICacheWarmupService>(services => services.GetRequiredService<CacheWarmupService>());
builder.Services.AddHostedService(services => services.GetRequiredService<CacheWarmupService>());
builder.Services.AddScoped<ITarotReadingService, TarotReadingService>();

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        throw new InvalidOperationException("PostgreSQL must be configured to run database migrations.");
    }

    await using var migrationScope = app.Services.CreateAsyncScope();
    var database = migrationScope.ServiceProvider.GetRequiredService<TarotDbContext>();
    await database.Database.MigrateAsync();
    return;
}

if (args.Contains("--bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        throw new InvalidOperationException("PostgreSQL must be configured to bootstrap an administrator.");
    }

    var bootstrapOptions = app.Services.GetRequiredService<IOptions<AccountOptions>>().Value;
    if (string.IsNullOrWhiteSpace(bootstrapOptions.BootstrapAdminEmail) ||
        string.IsNullOrWhiteSpace(bootstrapOptions.BootstrapAdminPassword) ||
        PasswordRules.Validate(bootstrapOptions.BootstrapAdminPassword).Any())
    {
        throw new InvalidOperationException("Set a valid Account__BootstrapAdminEmail and Account__BootstrapAdminPassword for this one-time command.");
    }

    await using var bootstrapScope = app.Services.CreateAsyncScope();
    var accounts = bootstrapScope.ServiceProvider.GetRequiredService<IAccountService>();
    var result = await accounts.BootstrapAdminAsync(
        bootstrapOptions.BootstrapAdminEmail,
        bootstrapOptions.BootstrapAdminPassword,
        CancellationToken.None);
    if (!result.Succeeded)
    {
        throw new InvalidOperationException("Administrator bootstrap was refused because an administrator already exists or the account could not be created.");
    }
    app.Logger.LogInformation("The first administrator account was created successfully.");
    return;
}

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
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
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

static RateLimitPartition<string> CreateRateLimitPartition(HttpContext context, int permitLimit, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });

static Task WriteAuthenticationErrorAsync(HttpResponse response, ResponseCode code, string message)
{
    response.StatusCode = (int)code;
    response.ContentType = "application/json";
    return response.WriteAsJsonAsync(new ResponseDto<object, string>(null, message, code));
}

static bool IsSecurePublicSiteUrl(string value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    (uri.Scheme == Uri.UriSchemeHttps ||
     (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))));

public partial class Program;
