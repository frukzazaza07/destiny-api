using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public interface ILlmClient
{
    Task<TarotReadingResponse> GenerateAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        InterpretationPayload payload,
        CancellationToken cancellationToken);
}

public interface ILlmGate
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

public interface IRuleReadingRenderer
{
    TarotReadingResponse Render(
        ClassificationResult classification,
        InterpretationPayload payload);
}

public sealed class LlmGate(IOptions<LlmOptions> options) : ILlmGate
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(1, options.Value.MaxConcurrency));

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

public sealed class RuleReadingRenderer : IRuleReadingRenderer
{
    public TarotReadingResponse Render(
        ClassificationResult classification,
        InterpretationPayload payload)
    {
        var thai = string.Equals(payload.Locale, "th", StringComparison.OrdinalIgnoreCase);
        var cardReadings = payload.Cards.Select(card =>
            new CardReading(
                card.Position,
                card.CardId,
                card.CardName,
                card.Orientation,
                $"{card.CoreMeaning} {card.PositionMeaning} {card.DomainMeaning}"))
            .ToArray();

        return new TarotReadingResponse(
            thai ? "ภาพรวมของไพ่พร้อมให้คุณพิจารณา" : "A Pattern Is Ready To Be Read",
            payload.OverallNarrative,
            payload.MainTheme,
            cardReadings,
            [payload.Opportunity],
            [payload.Challenge],
            [payload.Guidance],
            payload.ReflectionQuestion,
            thai
                ? "ใช้ไพ่เป็นกระจกสำหรับการทบทวน ไม่ใช่คำทำนายที่ตายตัว"
                : "Let this reading be a mirror for reflection, not a fixed prediction.",
            CacheStatus.MISS,
            classification,
            null,
            ReadingMode.STANDARD,
            GenerationSource.RULE_ENGINE,
            null);
    }
}

public sealed class LlmClient(
    HttpClient httpClient,
    IOptions<LlmOptions> options,
    TarotMetrics metrics,
    ILogger<LlmClient> logger) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly LlmOptions _options = options.Value;
    private readonly InferenceRouter _router = new(options);
    private readonly ReadingQualityScorer _qualityScorer = new();
    private readonly InferenceWorkerPool _workerPool = new(
        options.Value.CircuitBreakerFailureThreshold,
        TimeSpan.FromSeconds(options.Value.CircuitBreakerCooldownSeconds));

    public async Task<TarotReadingResponse> GenerateAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        InterpretationPayload payload,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var plan = _router.Resolve(request, classification);
        if (plan.Workers.Count == 0)
        {
            metrics.RecordLlmLatency(sw.Elapsed);
            metrics.LlmFailed();
            throw new InvalidOperationException("Deep reading LLM endpoint is not configured.");
        }

        var allowCloudForRequest = request.Question is null || _options.AllowCloudForRequestsWithRawQuestion;
        var workers = _workerPool.OrderCandidates(
            plan.TierId,
            plan.Workers,
            _options.BypassLocalWorkers,
            _options.EnableCloudFallback,
            allowCloudForRequest);
        if (workers.Count == 0)
        {
            metrics.RecordLlmLatency(sw.Elapsed);
            metrics.LlmFailed();
            throw new InvalidOperationException(
                "No healthy LLM worker is currently eligible for this reading.");
        }

        var failures = new List<Exception>();
        foreach (var worker in workers)
        {
            using var lease = await _workerPool.AcquireAsync(plan.TierId, worker, cancellationToken);
            Exception? workerFailure = null;
            for (var attempt = 0; attempt <= Math.Max(0, _options.RetryCount); attempt++)
            {
                try
                {
                    var body = await SendToWorkerAsync(
                        worker,
                        BuildOpenAiCompatibleRequest(request, payload, plan, worker),
                        cancellationToken);
                    var parsed = ParseOpenAiCompatibleResponse(
                        body,
                        classification,
                        payload,
                        worker.Model);
                    var quality = _qualityScorer.Score(request, payload, parsed);
                    if (!quality.Meets(_options.MinimumQualityScore))
                    {
                        throw new LlmResponseQualityException(quality);
                    }

                    _workerPool.MarkSuccess(plan.TierId, worker);
                    metrics.PromptVariantSucceeded(
                        plan.PromptVariant.ExperimentId,
                        plan.PromptVariant.VariantId,
                        quality.Score);
                    metrics.RecordLlmLatency(sw.Elapsed);
                    logger.LogInformation(
                        "Generated LLM reading in {ElapsedMs}ms with tier {Tier}, worker {Worker}, provider {Provider}, prompt variant {PromptVariant}, quality {QualityScore}",
                        sw.ElapsedMilliseconds,
                        plan.TierId,
                        worker.Id,
                        worker.Provider,
                        plan.PromptVariant.VariantId,
                        quality.Score);
                    return parsed with
                    {
                        ModelTier = plan.TierId,
                        InferenceWorker = worker.Id,
                        InferenceProvider = worker.Provider.ToString(),
                        PromptVariant = plan.PromptVariant.VariantId,
                        QualityScore = quality.Score,
                        PromptSnapshot = ReadingPromptBuilder.Build(request, payload, plan.PromptVariant)
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    workerFailure = exception;
                    var canRetryWorker = attempt < Math.Max(0, _options.RetryCount)
                        && IsTransient(exception);
                    if (!canRetryWorker)
                    {
                        break;
                    }

                    logger.LogWarning(
                        exception,
                        "LLM worker {Worker} attempt {Attempt} failed transiently; retrying",
                        worker.Id,
                        attempt + 1);
                }
            }

            workerFailure ??= new InvalidOperationException("LLM worker failed without an error.");
            failures.Add(workerFailure);
            _workerPool.MarkFailure(plan.TierId, worker);
            logger.LogWarning(
                workerFailure,
                "LLM worker {Worker} in tier {Tier} failed; trying the next eligible worker",
                worker.Id,
                plan.TierId);
        }

        metrics.LlmFailed();
        metrics.PromptVariantFailed(
            plan.PromptVariant.ExperimentId,
            plan.PromptVariant.VariantId);
        metrics.RecordLlmLatency(sw.Elapsed);
        var aggregate = new AggregateException(failures);
        logger.LogError(
            aggregate,
            "LLM generation failed across {WorkerCount} eligible worker(s)",
            workers.Count);
        throw new InvalidOperationException("LLM generation failed after retries and failover.", aggregate);
    }

    private async Task<string> SendToWorkerAsync(
        LlmWorkerDefinition worker,
        dynamic llmRequest,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(worker.TimeoutSeconds));
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, worker.Endpoint)
        {
            Content = JsonContent.Create(llmRequest, options: JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(worker.ApiKey))
        {
            var apiKey = string.Equals(worker.ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase)
                && !worker.ApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? $"Bearer {worker.ApiKey}"
                    : worker.ApiKey;
            requestMessage.Headers.TryAddWithoutValidation(worker.ApiKeyHeader, apiKey);
        }

        try
        {
            using var response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmWorkerHttpException(response.StatusCode);
            }

            return await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"LLM worker '{worker.Id}' exceeded its configured timeout.",
                exception);
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is TimeoutException or HttpRequestException { StatusCode: null })
        {
            return true;
        }

        return exception is LlmWorkerHttpException httpException &&
            (httpException.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
             (int)httpException.StatusCode >= 500);
    }

    private object BuildOpenAiCompatibleRequest(
        TarotReadingDto request,
        InterpretationPayload payload,
        InferencePlan plan,
        LlmWorkerDefinition worker) =>
        new
        {
            model = worker.Model,
            max_tokens = _options.MaxOutputTokens,
            temperature = 0.3,
            seed = 41 + Math.Clamp(request.AnswerVariant, 1, 10),
            reasoning_effort = "none",
            response_format = BuildReadingResponseFormat(payload),
            messages = ReadingPromptBuilder.Build(request, payload, plan.PromptVariant).Messages
        };

    internal static string BuildSystemPrompt(InterpretationPayload payload, PromptVariantSelection promptVariant) =>
        ReadingPromptBuilder.BuildSystemPrompt(payload, promptVariant);

    internal static object BuildReadingResponseFormat(InterpretationPayload payload) => new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "tarot_deep_reading",
            strict = true,
            schema = new
            {
                type = "object",
                properties = new
                {
                    title = StringSchema(180),
                    summary = StringSchema(1200),
                    mainTheme = StringSchema(500),
                    cards = new
                    {
                        type = "array",
                        minItems = payload.Cards.Count,
                        maxItems = payload.Cards.Count,
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                position = new
                                {
                                    type = "string",
                                    @enum = payload.Cards.Select(card => card.Position).ToArray()
                                },
                                cardId = new
                                {
                                    type = "string",
                                    @enum = payload.Cards.Select(card => card.CardId).ToArray()
                                },
                                interpretation = StringSchema(1400)
                            },
                            required = new[] { "position", "cardId", "interpretation" },
                            additionalProperties = false
                        }
                    },
                    opportunities = StringArraySchema(3),
                    challenges = StringArraySchema(3),
                    guidance = StringArraySchema(3),
                    reflectionQuestion = StringSchema(600),
                    closingMessage = StringSchema(600)
                },
                required = new[]
                {
                    "title", "summary", "mainTheme", "cards", "opportunities",
                    "challenges", "guidance", "reflectionQuestion", "closingMessage"
                },
                additionalProperties = false
            }
        }
    };

    private static object StringArraySchema(int maxItems) => new
    {
        type = "array",
        minItems = 1,
        maxItems,
        items = StringSchema(700)
    };

    private static object StringSchema(int maxLength) => new
    {
        type = "string",
        minLength = 1,
        maxLength
    };

    internal static TarotReadingResponse ParseOpenAiCompatibleResponse(
        string body,
        ClassificationResult classification,
        InterpretationPayload payload,
        string model)
    {
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM response did not include message content.");
        }

        var parsed = JsonSerializer.Deserialize<LlmReadingContent>(content, JsonOptions)
            ?? throw new InvalidOperationException("LLM content was not a reading response.");

        ValidateOutputLanguage(parsed, payload.Locale);

        if (parsed.Cards is null || parsed.Cards.Count != payload.Cards.Count)
        {
            throw new InvalidOperationException("LLM content did not return the requested number of cards.");
        }

        var cards = payload.Cards.Select((seed, index) =>
        {
            var generated = parsed.Cards[index];
            if (!string.Equals(seed.Position, generated.Position, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(seed.CardId, generated.CardId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(generated.Interpretation))
            {
                throw new InvalidOperationException($"LLM content changed or omitted card {index + 1}.");
            }

            return new CardReading(
                seed.Position,
                seed.CardId,
                seed.CardName,
                seed.Orientation,
                generated.Interpretation);
        }).ToArray();

        return new TarotReadingResponse(
            parsed.Title,
            parsed.Summary,
            parsed.MainTheme,
            cards,
            parsed.Opportunities ?? [],
            parsed.Challenges ?? [],
            parsed.Guidance ?? [],
            parsed.ReflectionQuestion,
            parsed.ClosingMessage,
            CacheStatus.MISS,
            classification,
            null,
            ReadingMode.DEEP,
            GenerationSource.LLM,
            model);
    }

    private static string LocaleName(string locale) =>
        string.Equals(locale, "th", StringComparison.OrdinalIgnoreCase) ? "Thai" : "English";

    private static string LanguageInstruction(string locale) =>
        string.Equals(locale, "th", StringComparison.OrdinalIgnoreCase)
            ? "เขียนเนื้อหาทุกช่องเป็นภาษาไทยตามธรรมชาติเท่านั้น ยกเว้น cardId และ position ที่ต้องคงค่าตามข้อมูล ห้ามอธิบายเกี่ยวกับโมเดลหรือกระบวนการสร้างคำตอบ"
            : "Write every prose field in natural English. Do not discuss the model or the answer-generation process.";

    private static void ValidateOutputLanguage(LlmReadingContent content, string locale)
    {
        if (!string.Equals(locale, "th", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var prose = string.Join(
            " ",
            new[]
            {
                content.Title,
                content.Summary,
                content.MainTheme,
                content.ReflectionQuestion,
                content.ClosingMessage
            }
            .Concat(content.Cards?.Select(card => card.Interpretation) ?? [])
            .Concat(content.Opportunities ?? [])
            .Concat(content.Challenges ?? [])
            .Concat(content.Guidance ?? []));
        var thaiCharacterCount = prose.Count(character => character is >= '\u0E00' and <= '\u0E7F');
        if (thaiCharacterCount < 20)
        {
            throw new InvalidOperationException("LLM content did not use the requested Thai locale.");
        }
    }

    private sealed record LlmReadingContent(
        string Title,
        string Summary,
        string MainTheme,
        IReadOnlyList<LlmCardContent> Cards,
        IReadOnlyList<string> Opportunities,
        IReadOnlyList<string> Challenges,
        IReadOnlyList<string> Guidance,
        string ReflectionQuestion,
        string ClosingMessage);

    private sealed record LlmCardContent(string Position, string CardId, string Interpretation);
}

internal sealed class LlmWorkerHttpException(HttpStatusCode statusCode)
    : HttpRequestException(
        $"LLM worker returned HTTP {(int)statusCode}.",
        null,
        statusCode)
{
    public new HttpStatusCode StatusCode { get; } = statusCode;
}
