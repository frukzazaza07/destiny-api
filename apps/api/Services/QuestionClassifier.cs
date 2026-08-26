using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Classifier.V1;

namespace TarotDestiny.Api.Services;

public interface IQuestionClassifier
{
    Task<ClassificationResult> ClassifyAsync(string? question, string locale, CancellationToken cancellationToken);
    bool CanUseSharedCache(ClassificationResult classification);
}

public interface IRemoteQuestionClassifier
{
    Task<ClassificationResult> ClassifyAsync(string question, string locale, CancellationToken cancellationToken);
}

public sealed class RuleQuestionClassifier(IOptions<ClassifierOptions> options, ILogger<RuleQuestionClassifier> logger)
    : IQuestionClassifier
{
    private readonly ClassifierOptions _options = options.Value;

    public ClassificationResult Classify(string? question, string locale)
    {
        var normalized = Normalize(question);
        var personalization = DetectPersonalization(normalized);

        var explicitTopic = ClassifyExplicitTopic(normalized);
        var result = explicitTopic ?? normalized switch
        {
            "" => new ClassificationResult(TarotDomain.GENERAL, "GENERAL_DAILY", 0.9, PersonalizationLevel.LOW),
            var q when HasAny(q, "job", "career", "work", "company", "boss", "promotion", "business", "ลาออก", "งาน", "บริษัท") =>
                ClassifyCareer(q, personalization),
            var q when HasAny(q, "love", "relationship", "partner", "single", "breakup", "marry", "แฟน", "รัก", "เลิก") =>
                ClassifyLove(q, personalization),
            var q when HasAny(q, "money", "income", "invest", "debt", "salary", "buy", "เงิน", "หนี้", "ลงทุน") =>
                ClassifyMoney(q, personalization),
            var q when HasAny(q, "family", "parent", "brother", "sister", "child", "ครอบครัว", "พ่อ", "แม่") =>
                new ClassificationResult(TarotDomain.FAMILY, HasAny(q, "fight", "conflict", "argue", "ทะเลาะ") ? "FAMILY_CONFLICT" : "FAMILY_GENERAL", 0.88, personalization),
            var q when HasAny(q, "grow", "healing", "purpose", "self", "spiritual", "เติบโต", "รักษาใจ") =>
                new ClassificationResult(TarotDomain.PERSONAL_GROWTH, HasAny(q, "heal", "healing", "รักษา") ? "PERSONAL_GROWTH_HEALING" : "PERSONAL_GROWTH_GENERAL", 0.88, personalization),
            var q when HasAny(q, "decide", "choice", "choose", "decision", "ควร", "เลือก") =>
                new ClassificationResult(TarotDomain.GENERAL, "GENERAL_DECISION", 0.82, personalization),
            _ => new ClassificationResult(TarotDomain.GENERAL, "GENERAL_DIRECTION", 0.76, PersonalizationLevel.MEDIUM)
        };

        if (result.Confidence < _options.MinimumCacheConfidence)
        {
            result = result with { Intent = TarotIntents.PersonalCustom };
        }

        logger.LogInformation(
            "Question classified as {Domain}/{Intent} confidence {Confidence} personalization {Personalization}",
            result.Domain,
            result.Intent,
            result.Confidence,
            result.Personalization);

        return result;
    }

    public Task<ClassificationResult> ClassifyAsync(
        string? question,
        string locale,
        CancellationToken cancellationToken) =>
        Task.FromResult(Classify(question, locale));

    public static bool IsExplicitTopic(string? question) =>
        Normalize(question).StartsWith("topic:", StringComparison.OrdinalIgnoreCase);

    private static ClassificationResult? ClassifyExplicitTopic(string question)
    {
        if (!question.StartsWith("topic:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Regex.Match(question, @"^topic:\s*([a-z_]+)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() switch
        {
            "general" => new(TarotDomain.GENERAL, "GENERAL_DIRECTION", 0.98, PersonalizationLevel.LOW),
            "love" => new(TarotDomain.LOVE, "LOVE_GENERAL", 0.98, PersonalizationLevel.LOW),
            "career" => new(TarotDomain.CAREER, "CAREER_GENERAL", 0.98, PersonalizationLevel.LOW),
            "money" => new(TarotDomain.MONEY, "MONEY_GENERAL", 0.98, PersonalizationLevel.LOW),
            "family" => new(TarotDomain.FAMILY, "FAMILY_GENERAL", 0.98, PersonalizationLevel.LOW),
            "personal_growth" => new(TarotDomain.PERSONAL_GROWTH, "PERSONAL_GROWTH_GENERAL", 0.98, PersonalizationLevel.LOW),
            _ => null
        } : null;
    }

    public bool CanUseSharedCache(ClassificationResult classification) =>
        classification.Confidence >= _options.MinimumCacheConfidence &&
        classification.Personalization != PersonalizationLevel.HIGH &&
        !string.Equals(classification.Intent, TarotIntents.PersonalCustom, StringComparison.OrdinalIgnoreCase);

    private static ClassificationResult ClassifyCareer(string q, PersonalizationLevel personalization)
    {
        if (HasAny(q, "change job", "change my job", "change current job", "leave", "quit", "ลาออก", "เปลี่ยนงาน")) return new(TarotDomain.CAREER, "CAREER_CHANGE_JOB", 0.95, personalization);
        if (HasAny(q, "new job", "สมัคร", "หางาน")) return new(TarotDomain.CAREER, "CAREER_NEW_JOB", 0.93, personalization);
        if (HasAny(q, "promotion", "เลื่อนตำแหน่ง")) return new(TarotDomain.CAREER, "CAREER_PROMOTION", 0.92, personalization);
        if (HasAny(q, "business", "startup", "company", "ธุรกิจ")) return new(TarotDomain.CAREER, "CAREER_BUSINESS", 0.9, personalization);
        if (HasAny(q, "conflict", "boss", "coworker", "ทะเลาะ")) return new(TarotDomain.CAREER, "CAREER_CONFLICT", 0.9, personalization);
        if (HasAny(q, "decide", "choice", "ควร", "เลือก")) return new(TarotDomain.CAREER, "CAREER_DECISION", 0.9, personalization);
        return new(TarotDomain.CAREER, "CAREER_GENERAL", 0.88, personalization);
    }

    private static ClassificationResult ClassifyLove(string q, PersonalizationLevel personalization)
    {
        if (HasAny(q, "single", "โสด")) return new(TarotDomain.LOVE, "LOVE_SINGLE", 0.93, personalization);
        if (HasAny(q, "breakup", "เลิก")) return new(TarotDomain.LOVE, "LOVE_BREAKUP", 0.92, personalization);
        if (HasAny(q, "reconcile", "กลับมา", "คืนดี")) return new(TarotDomain.LOVE, "LOVE_RECONCILIATION", 0.92, personalization);
        if (HasAny(q, "new person", "new love", "คนใหม่")) return new(TarotDomain.LOVE, "LOVE_NEW_PERSON", 0.91, personalization);
        if (HasAny(q, "marry", "commit", "แต่งงาน")) return new(TarotDomain.LOVE, "LOVE_COMMITMENT", 0.91, personalization);
        if (HasAny(q, "decide", "choice", "ควร", "เลือก")) return new(TarotDomain.LOVE, "LOVE_DECISION", 0.9, personalization);
        return new(TarotDomain.LOVE, "LOVE_RELATIONSHIP", 0.88, personalization);
    }

    private static ClassificationResult ClassifyMoney(string q, PersonalizationLevel personalization)
    {
        if (HasAny(q, "income", "salary", "รายได้", "เงินเดือน")) return new(TarotDomain.MONEY, "MONEY_INCOME", 0.92, personalization);
        if (HasAny(q, "invest", "stock", "ลงทุน", "หุ้น")) return new(TarotDomain.MONEY, "MONEY_INVESTMENT", 0.92, personalization);
        if (HasAny(q, "business", "ธุรกิจ")) return new(TarotDomain.MONEY, "MONEY_BUSINESS", 0.9, personalization);
        if (HasAny(q, "debt", "loan", "หนี้", "กู้")) return new(TarotDomain.MONEY, "MONEY_DEBT", 0.92, personalization);
        if (HasAny(q, "buy", "purchase", "ซื้อ")) return new(TarotDomain.MONEY, "MONEY_PURCHASE", 0.9, personalization);
        if (HasAny(q, "decide", "choice", "ควร", "เลือก")) return new(TarotDomain.MONEY, "MONEY_DECISION", 0.9, personalization);
        return new(TarotDomain.MONEY, "MONEY_GENERAL", 0.88, personalization);
    }

    private static PersonalizationLevel DetectPersonalization(string q)
    {
        if (q.Length > 180 ||
            Regex.IsMatch(q, @"\b\d{2,}\b") ||
            HasAny(q, "my brother", "my sister", "my wife", "my husband", "8 years", "overseas"))
        {
            return PersonalizationLevel.HIGH;
        }

        if (q.Length > 90 || q.Count(ch => ch == ',') >= 2)
        {
            return PersonalizationLevel.MEDIUM;
        }

        return PersonalizationLevel.LOW;
    }

    private static bool HasAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
}

public sealed class GrpcQuestionClassifier(
    ClassifierService.ClassifierServiceClient client,
    IOptions<ClassifierOptions> options,
    ILogger<GrpcQuestionClassifier> logger) : IRemoteQuestionClassifier
{
    private readonly ClassifierOptions _options = options.Value;

    public async Task<ClassificationResult> ClassifyAsync(
        string question,
        string locale,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_options.DeadlineMilliseconds);
        var response = await client.ClassifyAsync(
            new ClassifyRequest { Question = question, Locale = locale },
            deadline: deadline,
            cancellationToken: cancellationToken);

        if (!Enum.TryParse<TarotDomain>(response.Domain, ignoreCase: true, out var domain) ||
            !Enum.IsDefined(domain))
        {
            throw new InvalidOperationException($"Classifier returned unknown domain '{response.Domain}'.");
        }

        if (!TarotIntents.All.Contains(response.Intent, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Classifier returned unknown intent '{response.Intent}'.");
        }

        if (!IntentMatchesDomain(response.Intent, domain))
        {
            throw new InvalidOperationException(
                $"Classifier returned intent '{response.Intent}' for incompatible domain '{domain}'.");
        }

        if (!Enum.TryParse<PersonalizationLevel>(response.Personalization, ignoreCase: true, out var personalization) ||
            !Enum.IsDefined(personalization))
        {
            throw new InvalidOperationException(
                $"Classifier returned unknown personalization '{response.Personalization}'.");
        }

        if (!double.IsFinite(response.Confidence) || response.Confidence is < 0 or > 1)
        {
            throw new InvalidOperationException("Classifier confidence must be between 0 and 1.");
        }

        if (!string.Equals(response.Source, ClassifierSources.PythonGrpc, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(response.ModelVersion))
        {
            throw new InvalidOperationException("Classifier source and model version are required.");
        }
        logger.LogInformation(
            "Python classifier returned {Domain}/{Intent} confidence {Confidence} model {ModelVersion}",
            domain,
            response.Intent,
            response.Confidence,
            response.ModelVersion);

        return new ClassificationResult(
            domain,
            response.Intent,
            response.Confidence,
            personalization,
            ClassifierSources.PythonGrpc,
            response.ModelVersion);
    }

    private static bool IntentMatchesDomain(string intent, TarotDomain domain)
    {
        if (string.Equals(intent, TarotIntents.PersonalCustom, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedPrefix = domain == TarotDomain.PERSONAL_GROWTH
            ? "PERSONAL_GROWTH_"
            : $"{domain}_";
        return intent.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ResilientQuestionClassifier(
    RuleQuestionClassifier rules,
    IRemoteQuestionClassifier remote,
    IOptions<ClassifierOptions> options,
    TarotMetrics metrics,
    ILogger<ResilientQuestionClassifier> logger) : IQuestionClassifier
{
    private readonly ClassifierOptions _options = options.Value;

    public async Task<ClassificationResult> ClassifyAsync(
        string? question,
        string locale,
        CancellationToken cancellationToken)
    {
        var local = rules.Classify(question, locale);
        if (!_options.UseGrpc || string.IsNullOrWhiteSpace(question) || RuleQuestionClassifier.IsExplicitTopic(question))
        {
            var source = RuleQuestionClassifier.IsExplicitTopic(question)
                ? ClassifierSources.CSharpTopic
                : ClassifierSources.CSharpRule;
            return local with { Source = source };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var remoteResult = await remote.ClassifyAsync(question, locale, cancellationToken);
            metrics.ClassifierRemoteSucceeded();

            var guardedPersonalization = MoreConservative(
                remoteResult.Personalization,
                local.Personalization);
            var guarded = remoteResult with { Personalization = guardedPersonalization };
            if (guarded.Confidence < _options.MinimumCacheConfidence)
            {
                guarded = guarded with { Intent = TarotIntents.PersonalCustom };
            }

            return guarded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            metrics.ClassifierFallbackUsed();
            logger.LogWarning(
                ex,
                "Python classifier unavailable or invalid; using C# rule fallback for locale {Locale}",
                locale);
            return local with { Source = ClassifierSources.CSharpRuleFallback, ModelVersion = null };
        }
        finally
        {
            stopwatch.Stop();
            metrics.RecordClassifierLatency(stopwatch.Elapsed);
        }
    }

    public bool CanUseSharedCache(ClassificationResult classification) =>
        classification.Confidence >= _options.MinimumCacheConfidence &&
        classification.Personalization != PersonalizationLevel.HIGH &&
        !string.Equals(classification.Intent, TarotIntents.PersonalCustom, StringComparison.OrdinalIgnoreCase);

    private static PersonalizationLevel MoreConservative(
        PersonalizationLevel remote,
        PersonalizationLevel local) =>
        (PersonalizationLevel)Math.Max((int)remote, (int)local);
}
