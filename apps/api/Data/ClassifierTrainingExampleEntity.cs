namespace TarotDestiny.Api.Data;

public sealed class ClassifierTrainingExampleEntity
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string QuestionHash { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string PredictedDomain { get; set; } = string.Empty;
    public string PredictedIntent { get; set; } = string.Empty;
    public double PredictedConfidence { get; set; }
    public string PredictedPersonalization { get; set; } = string.Empty;
    public string ClassifierSource { get; set; } = string.Empty;
    public string? ClassifierModelVersion { get; set; }
    public string ReviewStatus { get; set; } = "PENDING";
    public string? ReviewedDomain { get; set; }
    public string? ReviewedIntent { get; set; }
    public string? ReviewedPersonalization { get; set; }
    public string? ParaphraseGroup { get; set; }
    public int? ReviewerTimeSeconds { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
