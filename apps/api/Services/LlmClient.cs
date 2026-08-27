using System.Diagnostics;
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

    public async Task<TarotReadingResponse> GenerateAsync(
        TarotReadingDto request,
        ClassificationResult classification,
        InterpretationPayload payload,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            metrics.RecordLlmLatency(sw.Elapsed);
            metrics.LlmFailed();
            throw new InvalidOperationException("Deep reading LLM endpoint is not configured.");
        }

        for (var attempt = 0; attempt <= _options.RetryCount; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

            try
            {
                var llmRequest = BuildOpenAiCompatibleRequest(request, classification, payload);
                var response = await httpClient.PostAsJsonAsync(_options.Endpoint, llmRequest, JsonOptions, timeoutCts.Token);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var parsed = ParseOpenAiCompatibleResponse(body, classification, payload, _options.Model);
                metrics.RecordLlmLatency(sw.Elapsed);
                logger.LogInformation("Generated LLM reading in {ElapsedMs}ms", sw.ElapsedMilliseconds);
                return parsed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < Math.Max(0, _options.RetryCount))
            {
                logger.LogWarning(ex, "LLM call attempt {Attempt} failed; retrying", attempt + 1);
            }
            catch (Exception ex)
            {
                metrics.LlmFailed();
                metrics.RecordLlmLatency(sw.Elapsed);
                logger.LogError(ex, "LLM generation failed after {AttemptCount} attempt(s)", attempt + 1);
                throw new InvalidOperationException("LLM generation failed after retries.", ex);
            }
        }

        throw new InvalidOperationException("LLM generation failed after retries.");
    }

    private object BuildOpenAiCompatibleRequest(
        TarotReadingDto request,
        ClassificationResult classification,
        InterpretationPayload payload) =>
        new
        {
            model = _options.Model,
            max_tokens = _options.MaxOutputTokens,
            temperature = 0.2,
            seed = 42,
            reasoning_effort = "none",
            response_format = BuildReadingResponseFormat(payload),
            messages = new[]
            {
                new
                {
                    role = "system",
                    // content = $$"""
                    //     You write a detailed, reflective premium Tarot reading in {{LocaleName(payload.Locale)}}.
                    //     {{LanguageInstruction(payload.Locale)}}
                    //     The rule payload is authoritative. Do not change card ids, positions, or order.
                    //     Return one JSON object only, without Markdown or code fences, matching the supplied response schema.
                    //     Include exactly one cards item for every payload card in the same order. Use two or three concise sentences per card. Connect the cards into a coherent narrative, give practical reflection, and avoid guaranteed predictions.
                    //     """
                    content = $$"""
                        You write a detailed, reflective premium Tarot reading in {{LocaleName(payload.Locale)}}.
                        {{LanguageInstruction(payload.Locale)}}
                        The rule payload is authoritative. Do not change card ids, positions, or order.
                        Return one JSON object only, without Markdown or code fences, matching the supplied response schema.
                        Include exactly one cards item for every payload card in the same order. Use two or three concise sentences per card. Connect the cards into a coherent narrative, give practical reflection, and avoid guaranteed predictions.

                        For "reflectionQuestion": write ONE new, original question that invites the user to reflect on their own feelings or actions, based on the cards and their situation. Never copy, paraphrase, or restate the user's original question in this field.

                        For "title", "summary", "mainTheme", "closingMessage": synthesize your own original text based on the cards and the user's question context. Do not copy the user's question verbatim anywhere in the response.

                        For "opportunities", "challenges", "guidance": give 2-3 concrete, actionable items each, grounded in the specific cards drawn.
                        """
                },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(
                        new
                        {
                            question = request.Question,
                            reusableIntent = classification.Intent,
                            classification,
                            payload
                        },
                        JsonOptions)
                }
            }
        };

    private static object BuildReadingResponseFormat(InterpretationPayload payload) => new
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

    private static TarotReadingResponse ParseOpenAiCompatibleResponse(
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
