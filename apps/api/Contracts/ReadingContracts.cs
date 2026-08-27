using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Contracts;

public sealed record ClassificationResult(
    TarotDomain Domain,
    string Intent,
    double Confidence,
    PersonalizationLevel Personalization,
    string Source = ClassifierSources.CSharpRule,
    string? ModelVersion = null,
    string DecisionMethod = ClassifierDecisionMethods.CSharpRule,
    double? SemanticSimilarity = null,
    string? EmbeddingModelVersion = null);

public static class ClassifierSources
{
    public const string CSharpRule = "CSHARP_RULE";
    public const string CSharpTopic = "CSHARP_TOPIC";
    public const string CSharpRuleFallback = "CSHARP_RULE_FALLBACK";
    public const string PythonGrpc = "PYTHON_GRPC";
}

public static class ClassifierDecisionMethods
{
    public const string CSharpRule = "CSHARP_RULE";
    public const string TfidfLogisticRegression = "TFIDF_LOGREG";
    public const string HybridAgreement = "HYBRID_AGREEMENT";
    public const string SemanticNeighbor = "SEMANTIC_NEIGHBOR";
    public const string SemanticConflict = "SEMANTIC_CONFLICT";

    public static readonly string[] RemoteMethods =
    [
        TfidfLogisticRegression,
        HybridAgreement,
        SemanticNeighbor,
        SemanticConflict
    ];
}

public sealed record TarotReadingResponse(
    string Title,
    string Summary,
    string MainTheme,
    IReadOnlyList<CardReading> Cards,
    IReadOnlyList<string> Opportunities,
    IReadOnlyList<string> Challenges,
    IReadOnlyList<string> Guidance,
    string ReflectionQuestion,
    string ClosingMessage,
    CacheStatus CacheStatus,
    ClassificationResult Classification,
    string? CacheKey,
    ReadingMode ReadingMode = ReadingMode.STANDARD,
    GenerationSource GenerationSource = GenerationSource.RULE_ENGINE,
    string? GenerationModel = null,
    string? ModelTier = null,
    string? InferenceWorker = null,
    string? InferenceProvider = null,
    string? PromptVariant = null,
    double? QualityScore = null);

public sealed record CardReading(
    string Position,
    string CardId,
    string CardName,
    Orientation Orientation,
    string Interpretation);

public sealed record ShuffleDeckResponse(string SessionId, string Spread, int CardCount, int SelectCount);

public sealed record ResolveDeckResponse(string SessionId, string Spread, IReadOnlyList<SelectedCard> Cards);
