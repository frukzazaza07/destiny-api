namespace TarotDestiny.Api.Data;

public sealed class GeneratedAnswerEntity
{
    public Guid Id { get; set; }
    public string CacheHash { get; set; } = string.Empty;
    public string CacheVersion { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string ReadingMode { get; set; } = string.Empty;
    public string SpreadId { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string CardsJson { get; set; } = "[]";
    public string PromptVersion { get; set; } = string.Empty;
    public string InterpretationVersion { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public long HitCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public ICollection<GeneratedAnswerVariantEntity> Variants { get; set; } = [];
}
