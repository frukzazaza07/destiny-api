namespace TarotDestiny.Api.Services;

public sealed class ClassifierOptions
{
    public double MinimumCacheConfidence { get; set; } = 0.85;
    public bool UseGrpc { get; set; } = true;
    public string GrpcAddress { get; set; } = "http://127.0.0.1:50051";
    public int DeadlineMilliseconds { get; set; } = 500;
}

public sealed class TarotCacheOptions
{
    public string CacheVersion { get; set; } = "v2";
    public string PromptVersion { get; set; } = "PROMPT_V1";
    public string InterpretationVersion { get; set; } = "INTERPRETATION_V1";
    public string? ModelVersion { get; set; }
    public int AnswerTtlDays { get; set; } = 30;
}

public sealed class LlmOptions
{
    public string? Endpoint { get; set; }
    public string Model { get; set; } = "qwen3:8b";
    public int MaxConcurrency { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxOutputTokens { get; set; } = 1000;
    public int RetryCount { get; set; } = 1;
}

public sealed class DeepReadingOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowUnentitledInDevelopment { get; set; } = true;
    public string ClaimType { get; set; } = "tarot:deep_reading";
    public string ClaimValue { get; set; } = "true";
    public string? UpgradeUrl { get; set; }
}
