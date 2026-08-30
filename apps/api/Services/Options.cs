namespace TarotDestiny.Api.Services;

public sealed class ClassifierOptions
{
    public double MinimumCacheConfidence { get; set; } = 0.85;
    public double MinimumSharedCacheConfidence { get; set; } = 0.90;
    public bool UseGrpc { get; set; } = true;
    public string GrpcAddress { get; set; } = "http://127.0.0.1:50051";
    public int DeadlineMilliseconds { get; set; } = 500;
}

public sealed class TarotCacheOptions
{
    public string CacheVersion { get; set; } = "v3";
    public string TaxonomyVersion { get; set; } = "TAXONOMY_V1";
    public string PromptVersion { get; set; } = "PROMPT_V1";
    public string InterpretationVersion { get; set; } = "INTERPRETATION_V1";
    public string? ModelVersion { get; set; }
    public int AnswerTtlDays { get; set; } = 30;
}

public sealed class DeepSharedCacheOptions
{
    public bool Enabled { get; set; }
    public bool ReadEnabled { get; set; }
    public bool WriteEnabled { get; set; }
    public List<string> ApprovedIntents { get; set; } = [];
}

public sealed class StartupCacheWarmupOptions
{
    public bool Enabled { get; set; }
    public int DelaySeconds { get; set; } = 15;
    public bool RehydrateRedisFromPostgres { get; set; } = true;
    public List<string> Locales { get; set; } = ["en", "th"];
    public List<string> ReadingModes { get; set; } = ["STANDARD"];
    public List<string> ApprovedIntents { get; set; } = [];
    public List<string> Spreads { get; set; } = ["DAILY_1"];
    public int Variants { get; set; } = 1;
    public int MaxCombinationsPerStartup { get; set; } = 500;
    public int MaxConcurrency { get; set; } = 1;
    public int MaxDeepGenerationsPerStartup { get; set; }
    public int RetryCount { get; set; } = 2;
    public int RetryDelaySeconds { get; set; } = 10;
    public Dictionary<string, Dictionary<string, string>> CanonicalDeepQuestions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LlmOptions
{
    public string? Endpoint { get; set; }
    public string Model { get; set; } = "qwen3:8b";
    public int MaxConcurrency { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxOutputTokens { get; set; } = 1000;
    public int RetryCount { get; set; } = 1;
    public string DefaultTier { get; set; } = "DEEP";
    public List<LlmTierOptions> Tiers { get; set; } = [];
    public bool EnableCloudFallback { get; set; }
    public bool AllowCloudForRequestsWithRawQuestion { get; set; }
    public int CircuitBreakerFailureThreshold { get; set; } = 2;
    public int CircuitBreakerCooldownSeconds { get; set; } = 30;
    public double MinimumQualityScore { get; set; }
    public PromptExperimentOptions PromptExperiment { get; set; } = new();
}

public sealed class LlmTierOptions
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CacheModelVersion { get; set; } = string.Empty;
    public List<LlmWorkerOptions> Workers { get; set; } = [];
}

public sealed class LlmWorkerOptions
{
    public string Id { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public LlmWorkerProvider Provider { get; set; } = LlmWorkerProvider.LOCAL_GPU;
    public int Priority { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int? TimeoutSeconds { get; set; }
    public string? ApiKey { get; set; }
    public string ApiKeyHeader { get; set; } = "Authorization";
    public bool Enabled { get; set; } = true;
}

public enum LlmWorkerProvider
{
    LOCAL_GPU,
    CLOUD_GPU
}

public sealed class PromptExperimentOptions
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = string.Empty;
    public List<PromptVariantOptions> Variants { get; set; } = [];
}

public sealed class PromptVariantOptions
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int Weight { get; set; } = 1;
    public string AdditionalSystemInstruction { get; set; } = string.Empty;
}

public sealed class BaseInterpretationCacheOptions
{
    public bool Enabled { get; set; } = true;
    public int MaximumEntries { get; set; } = 4096;
    public int TtlMinutes { get; set; } = 120;
}

public sealed class ClassifierTrainingOptions
{
    public bool Enabled { get; set; } = true;
    public string ConsentVersion { get; set; } = "classifier-training-consent-v1";
}

public sealed class AdminOptions
{
    public string? Key { get; set; }
}

public sealed class DeepReadingOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowUnentitledInDevelopment { get; set; } = true;
    public string ClaimType { get; set; } = "tarot:deep_reading";
    public string ClaimValue { get; set; } = "true";
    public string? UpgradeUrl { get; set; }
}
