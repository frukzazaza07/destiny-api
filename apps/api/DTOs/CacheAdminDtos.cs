using System.ComponentModel.DataAnnotations;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.DTOs;

public sealed record CacheAnalyticsDto(
    TarotMetricSnapshot Runtime,
    PersistentCacheAnalytics Persistent,
    double EligibleHitRate,
    long BaseInterpretationHits,
    long BaseInterpretationMisses);

public sealed record CacheWarmupRequestDto(
    ReadingMode ReadingMode = ReadingMode.STANDARD,
    [param: Range(1, 10)] int Variants = 1,
    [param: Range(1, 10000)] int MaxCombinations = 100,
    [param: Range(0, int.MaxValue)] int Offset = 0,
    string? Locale = null,
    [param: StringLength(50)] string? ModelTier = null);

public sealed record CacheWarmupJobDto(
    Guid Id,
    string Status,
    int Offset,
    int RequestedCombinations,
    int TotalCombinations,
    int CompletedCombinations,
    int GeneratedVariants,
    int FailedCombinations,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
