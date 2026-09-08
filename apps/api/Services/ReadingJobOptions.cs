namespace TarotDestiny.Api.Services;

public sealed class ReadingJobOptions
{
    public bool Enabled { get; set; }
    public bool AllowCloudForRequestsWithRawQuestion { get; set; }
    public string BrokerUri { get; set; } = "";
    public string WorkerKey { get; set; } = "";
    public string ProviderRef { get; set; } = "cloud-default";
    public string Model { get; set; } = "configured-cloud-model";
    public int DeadlineSeconds { get; set; } = 180;
    public int ReconnectGraceSeconds { get; set; } = 15;
    public int HeartbeatSeconds { get; set; } = 5;
    public int PresenceLeaseSeconds { get; set; } = 10;
    public int ExecutionLeaseSeconds { get; set; } = 15;
    public int MaxConcurrency { get; set; } = 4;
    public int RequestsPerMinute { get; set; } = 20;
    public int TokensPerMinute { get; set; } = 200000;
    public int MaxQueuedJobs { get; set; } = 100;
    public int MaxAttempts { get; set; } = 3;
    public int MaxOutputTokens { get; set; } = 4000;
}
