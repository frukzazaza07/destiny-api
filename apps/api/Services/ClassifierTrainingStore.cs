using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface IClassifierTrainingStore
{
    Task<ClassifierTrainingSubmissionResultDto> SubmitAsync(ClassifierTrainingSubmissionDto request, CancellationToken cancellationToken);
    Task<ClassifierTrainingPageDto> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<ClassifierTrainingExampleDto?> ReviewAsync(Guid id, ClassifierReviewDto review, CancellationToken cancellationToken);
    Task<ReviewedClassifierExportDto> ExportApprovedAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableClassifierTrainingStore : IClassifierTrainingStore
{
    private static InvalidOperationException Error() =>
        new("PostgreSQL is required for classifier training collection and review.");

    public Task<ClassifierTrainingSubmissionResultDto> SubmitAsync(ClassifierTrainingSubmissionDto request, CancellationToken cancellationToken) =>
        Task.FromException<ClassifierTrainingSubmissionResultDto>(Error());
    public Task<ClassifierTrainingPageDto> ListAsync(string? status, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromException<ClassifierTrainingPageDto>(Error());
    public Task<ClassifierTrainingExampleDto?> ReviewAsync(Guid id, ClassifierReviewDto review, CancellationToken cancellationToken) =>
        Task.FromException<ClassifierTrainingExampleDto?>(Error());
    public Task<ReviewedClassifierExportDto> ExportApprovedAsync(CancellationToken cancellationToken) =>
        Task.FromException<ReviewedClassifierExportDto>(Error());
}

public sealed partial class ClassifierTrainingStore(
    TarotDbContext dbContext,
    IQuestionClassifier classifier,
    IOptions<ClassifierTrainingOptions> options) : IClassifierTrainingStore
{
    public const string ExportSchemaVersion = "tarot-classifier-reviewed-v1";
    private readonly ClassifierTrainingOptions _options = options.Value;

    public async Task<ClassifierTrainingSubmissionResultDto> SubmitAsync(
        ClassifierTrainingSubmissionDto request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Classifier training collection is disabled.");
        }

        if (!request.Consent)
        {
            throw new ArgumentException("Explicit classifier-training consent is required.");
        }

        var locale = request.Locale.Trim().ToLowerInvariant();
        if (!LocaleIds.IsSupported(locale))
        {
            throw new ArgumentException("Only en and th classifier training examples are supported.");
        }

        var question = NormalizeQuestion(request.Question);
        if (question.Length is < 3 or > 500 || question.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The question is not eligible for classifier training.");
        }

        if (EmailRegex().IsMatch(question) || UrlRegex().IsMatch(question) || PhoneRegex().IsMatch(question))
        {
            throw new ArgumentException("Questions containing contact or identifying information cannot be stored.");
        }

        var classification = await classifier.ClassifyAsync(question, locale, cancellationToken);
        if (classification.Personalization == PersonalizationLevel.HIGH)
        {
            throw new ArgumentException("Highly personalized questions cannot be stored for classifier training.");
        }

        var questionHash = BuildQuestionHash(question, locale);
        var existing = await dbContext.ClassifierTrainingExamples
            .AsNoTracking()
            .SingleOrDefaultAsync(example => example.QuestionHash == questionHash, cancellationToken);
        if (existing is not null)
        {
            return new ClassifierTrainingSubmissionResultDto(existing.Id, false, existing.ReviewStatus);
        }

        var id = Guid.NewGuid();
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO tarot_question_classification_training (
                id, question, question_hash, locale, predicted_domain, predicted_intent,
                predicted_confidence, predicted_personalization, classifier_source,
                classifier_model_version, review_status, consent_version, revision, created_at)
            VALUES (
                {id}, {question}, {questionHash}, {locale}, {classification.Domain.ToString()},
                {classification.Intent}, {classification.Confidence}, {classification.Personalization.ToString()},
                {classification.Source}, {classification.ModelVersion}, 'PENDING',
                {_options.ConsentVersion}, 0, {DateTimeOffset.UtcNow})
            ON CONFLICT (question_hash) DO NOTHING;
            """,
            cancellationToken);
        if (inserted == 1)
        {
            return new ClassifierTrainingSubmissionResultDto(id, true, "PENDING");
        }

        existing = await dbContext.ClassifierTrainingExamples
            .AsNoTracking()
            .SingleAsync(example => example.QuestionHash == questionHash, cancellationToken);
        return new ClassifierTrainingSubmissionResultDto(existing.Id, false, existing.ReviewStatus);
    }

    public async Task<ClassifierTrainingPageDto> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.ClassifierTrainingExamples.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = NormalizeStatus(status);
            query = query.Where(example => example.ReviewStatus == normalizedStatus);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(example => example.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(example => ToDto(example))
            .ToArrayAsync(cancellationToken);
        return new ClassifierTrainingPageDto(items, page, pageSize, total);
    }

    public async Task<ClassifierTrainingExampleDto?> ReviewAsync(
        Guid id,
        ClassifierReviewDto review,
        CancellationToken cancellationToken)
    {
        var status = NormalizeStatus(review.Status);
        string? domain = null;
        string? intent = null;
        if (status == "APPROVED")
        {
            domain = review.Domain?.ToString()
                ?? throw new ArgumentException("Approved examples require a reviewed domain.");
            intent = review.Intent?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(intent) || intent == TarotIntents.PersonalCustom ||
                !TarotIntents.All.Contains(intent, StringComparer.Ordinal))
            {
                throw new ArgumentException("Approved examples require a reusable taxonomy intent.");
            }

            if (!intent.StartsWith($"{domain}_", StringComparison.Ordinal) &&
                !(domain == nameof(TarotDomain.PERSONAL_GROWTH) && intent.StartsWith("PERSONAL_GROWTH_", StringComparison.Ordinal)))
            {
                throw new ArgumentException("The reviewed intent does not belong to the reviewed domain.");
            }
        }

        var updated = await dbContext.ClassifierTrainingExamples
            .Where(example => example.Id == id && example.Revision == review.ExpectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(example => example.ReviewStatus, status)
                .SetProperty(example => example.ReviewedDomain, domain)
                .SetProperty(example => example.ReviewedIntent, intent)
                .SetProperty(example => example.ReviewedAt, DateTimeOffset.UtcNow)
                .SetProperty(example => example.Revision, example => example.Revision + 1),
                cancellationToken);
        if (updated == 0)
        {
            var exists = await dbContext.ClassifierTrainingExamples.AnyAsync(example => example.Id == id, cancellationToken);
            if (!exists)
            {
                return null;
            }

            throw new ArgumentException("The training example changed; reload it before reviewing.");
        }

        return await dbContext.ClassifierTrainingExamples
            .AsNoTracking()
            .Where(example => example.Id == id)
            .Select(example => ToDto(example))
            .SingleAsync(cancellationToken);
    }

    public async Task<ReviewedClassifierExportDto> ExportApprovedAsync(CancellationToken cancellationToken)
    {
        var examples = await dbContext.ClassifierTrainingExamples
            .AsNoTracking()
            .Where(example => example.ReviewStatus == "APPROVED")
            .OrderBy(example => example.Locale)
            .ThenBy(example => example.ReviewedIntent)
            .ThenBy(example => example.QuestionHash)
            .Select(example => new ReviewedClassifierExampleDto(
                example.Id,
                example.Question,
                example.Locale,
                example.ReviewedDomain!,
                example.ReviewedIntent!))
            .ToArrayAsync(cancellationToken);
        return new ReviewedClassifierExportDto(ExportSchemaVersion, examples);
    }

    private static ClassifierTrainingExampleDto ToDto(ClassifierTrainingExampleEntity example) => new(
        example.Id, example.Question, example.Locale, example.PredictedDomain,
        example.PredictedIntent, example.PredictedConfidence, example.PredictedPersonalization,
        example.ClassifierSource, example.ClassifierModelVersion, example.ReviewStatus,
        example.ReviewedDomain, example.ReviewedIntent, example.Revision,
        example.CreatedAt, example.ReviewedAt);

    private static string NormalizeQuestion(string question) =>
        WhitespaceRegex().Replace(question.Normalize(NormalizationForm.FormKC).Trim(), " ");

    private static string BuildQuestionHash(string question, string locale)
    {
        var value = $"training-v1|{locale}|{question.ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = status.Trim().ToUpperInvariant();
        return normalized is "PENDING" or "APPROVED" or "REJECTED"
            ? normalized
            : throw new ArgumentException("Review status must be PENDING, APPROVED, or REJECTED.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\d\s().-]{7,}\d)(?!\d)")]
    private static partial Regex PhoneRegex();
}
