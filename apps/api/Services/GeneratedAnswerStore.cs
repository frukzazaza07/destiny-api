using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed record GeneratedAnswerWrite(
    string CacheHash,
    TarotDomain Domain,
    string Intent,
    ReadingMode ReadingMode,
    string SpreadId,
    string Locale,
    IReadOnlyList<SelectedCard> Cards,
    string CacheVersion,
    string PromptVersion,
    string InterpretationVersion,
    string? ModelVersion,
    TarotReadingResponse Response);

public sealed record GeneratedAnswerSummary(
    string CacheHash,
    string Domain,
    string Intent,
    string ReadingMode,
    string SpreadId,
    string Locale,
    long HitCount,
    int VariantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record PersistentCacheAnalytics(
    long AnswerCount,
    long VariantCount,
    long TotalHits,
    IReadOnlyList<GeneratedAnswerSummary> MostUsed);

public interface IGeneratedAnswerStore
{
    Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken);
    Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken);
    Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken);
    Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken);
    IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(CancellationToken cancellationToken);
}

public sealed class NullGeneratedAnswerStore : IGeneratedAnswerStore
{
    public Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CachedAnswerSet?>(null);
    }

    public Task<bool> SaveVariantAsync(GeneratedAnswerWrite answer, int variantNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PersistentCacheAnalytics(0, 0, 0, []));
    }

    public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}

public sealed class PostgresGeneratedAnswerStore(
    TarotDbContext dbContext,
    ILogger<PostgresGeneratedAnswerStore> logger) : IGeneratedAnswerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CachedAnswerSet?> FindAsync(string cacheHash, CancellationToken cancellationToken)
    {
        var entity = await dbContext.GeneratedAnswers
            .AsNoTracking()
            .Include(answer => answer.Variants)
            .SingleOrDefaultAsync(answer => answer.CacheHash == cacheHash, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var variants = entity.Variants
            .OrderBy(variant => variant.VariantNumber)
            .Select(variant => new CachedAnswerVariant(
                variant.VariantNumber,
                JsonSerializer.Deserialize<TarotReadingResponse>(variant.ResponseJson, JsonOptions)
                    ?? throw new InvalidOperationException($"Stored answer variant {variant.Id} is invalid.")))
            .ToArray();
        return variants.Length == 0 ? null : new CachedAnswerSet(variants);
    }

    public async Task<bool> SaveVariantAsync(
        GeneratedAnswerWrite answer,
        int variantNumber,
        CancellationToken cancellationToken)
    {
        if (variantNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variantNumber));
        }

        var cardsJson = JsonSerializer.Serialize(answer.Cards, JsonOptions);
        var responseJson = JsonSerializer.Serialize(answer.Response, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO tarot_generated_answer (
                id, cache_hash, cache_version, domain, intent, reading_mode,
                spread_id, locale, cards, prompt_version, interpretation_version,
                model_version, hit_count, created_at)
            VALUES (
                {Guid.NewGuid()}, {answer.CacheHash}, {answer.CacheVersion}, {answer.Domain.ToString()},
                {answer.Intent}, {answer.ReadingMode.ToString()}, {answer.SpreadId}, {answer.Locale},
                CAST({cardsJson} AS jsonb), {answer.PromptVersion}, {answer.InterpretationVersion},
                {answer.ModelVersion}, 0, {now})
            ON CONFLICT (cache_hash) DO NOTHING;
            """,
            cancellationToken);

        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO tarot_generated_answer_variant (
                id, generated_answer_id, variant_number, response, created_at)
            SELECT {Guid.NewGuid()}, id, {variantNumber}, CAST({responseJson} AS jsonb), {now}
            FROM tarot_generated_answer
            WHERE cache_hash = {answer.CacheHash}
            ON CONFLICT (generated_answer_id, variant_number) DO NOTHING;
            """,
            cancellationToken);

        if (inserted == 1)
        {
            logger.LogInformation(
                "Persisted generated Tarot answer {CacheHash} variant {VariantNumber}",
                answer.CacheHash,
                variantNumber);
        }

        return inserted == 1;
    }

    public async Task IncrementHitCountAsync(string cacheHash, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tarot_generated_answer
            SET hit_count = hit_count + 1, updated_at = {DateTimeOffset.UtcNow}
            WHERE cache_hash = {cacheHash};
            """,
            cancellationToken);
    }

    public async Task<PersistentCacheAnalytics> GetAnalyticsAsync(int top, CancellationToken cancellationToken)
    {
        top = Math.Clamp(top, 1, 100);
        var aggregate = await dbContext.GeneratedAnswers
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Answers = group.LongCount(),
                Hits = group.Sum(answer => answer.HitCount)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var variantCount = await dbContext.GeneratedAnswerVariants.LongCountAsync(cancellationToken);
        var mostUsed = await dbContext.GeneratedAnswers
            .AsNoTracking()
            .OrderByDescending(answer => answer.HitCount)
            .ThenByDescending(answer => answer.CreatedAt)
            .Take(top)
            .Select(answer => new GeneratedAnswerSummary(
                answer.CacheHash,
                answer.Domain,
                answer.Intent,
                answer.ReadingMode,
                answer.SpreadId,
                answer.Locale,
                answer.HitCount,
                answer.Variants.Count,
                answer.CreatedAt,
                answer.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        return new PersistentCacheAnalytics(
            aggregate?.Answers ?? 0,
            variantCount,
            aggregate?.Hits ?? 0,
            mostUsed);
    }

    public async IAsyncEnumerable<GeneratedAnswerSummary> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var answer in dbContext.GeneratedAnswers
            .AsNoTracking()
            .OrderBy(answer => answer.CreatedAt)
            .Select(answer => new GeneratedAnswerSummary(
                answer.CacheHash,
                answer.Domain,
                answer.Intent,
                answer.ReadingMode,
                answer.SpreadId,
                answer.Locale,
                answer.HitCount,
                answer.Variants.Count,
                answer.CreatedAt,
                answer.UpdatedAt))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return answer;
        }
    }

}
