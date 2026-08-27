using System.ComponentModel.DataAnnotations;
using TarotDestiny.Api.Domain;

namespace TarotDestiny.Api.DTOs;

public sealed record ClassifierTrainingSubmissionDto(
    [param: Required, StringLength(500, MinimumLength = 3)] string Question,
    [param: Required, StringLength(10, MinimumLength = 2)] string Locale,
    bool Consent);

public sealed record ClassifierTrainingSubmissionResultDto(Guid Id, bool Created, string ReviewStatus);

public sealed record ClassifierReviewDto(
    [param: Required] string Status,
    TarotDomain? Domain,
    [param: StringLength(100)] string? Intent,
    [param: Range(0, int.MaxValue)] int ExpectedRevision);

public sealed record ClassifierTrainingExampleDto(
    Guid Id,
    string Question,
    string Locale,
    string PredictedDomain,
    string PredictedIntent,
    double PredictedConfidence,
    string PredictedPersonalization,
    string ClassifierSource,
    string? ClassifierModelVersion,
    string ReviewStatus,
    string? ReviewedDomain,
    string? ReviewedIntent,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

public sealed record ClassifierTrainingPageDto(
    IReadOnlyList<ClassifierTrainingExampleDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record ClassifierTaxonomyItemDto(string Domain, IReadOnlyList<string> Intents);
public sealed record ClassifierTaxonomyDto(IReadOnlyList<ClassifierTaxonomyItemDto> Domains);
public sealed record ReviewedClassifierExampleDto(Guid Id, string Question, string Locale, string Domain, string Intent);
public sealed record ReviewedClassifierExportDto(string SchemaVersion, IReadOnlyList<ReviewedClassifierExampleDto> Examples);
