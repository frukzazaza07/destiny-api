using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/admin/cache")]
public sealed class CacheAdminController(
    IGeneratedAnswerStore store,
    TarotMetrics metrics,
    IInterpretationEngine interpretationEngine,
    ICacheWarmupService warmup,
    IAdminAccessPolicy adminAccess) : MasterController
{
    [HttpGet("analytics")]
    [ProducesResponseType(typeof(ResponseDto<CacheAnalyticsDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Analytics([FromQuery] int top = 20, CancellationToken cancellationToken = default)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        var runtime = metrics.Snapshot();
        var eligible = runtime.CacheHits + runtime.CacheMisses;
        var persistent = await store.GetAnalyticsAsync(top, cancellationToken);
        var caching = interpretationEngine as CachingInterpretationEngine;
        return SuccessResponse(new CacheAnalyticsDto(
            runtime,
            persistent,
            eligible == 0 ? 0 : Math.Round((double)runtime.CacheHits / eligible, 4),
            caching?.HitCount ?? 0,
            caching?.MissCount ?? 0));
    }

    [HttpPost("warmups")]
    [ApiAntiforgeryForCookieUser]
    [ProducesResponseType(typeof(ResponseDto<CacheWarmupJobDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public IActionResult StartWarmup([FromBody] CacheWarmupRequestDto request)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        return SuccessResponse(warmup.Enqueue(request));
    }

    [HttpGet("warmups")]
    [ProducesResponseType(typeof(ResponseDto<IReadOnlyList<CacheWarmupJobDto>, object>), StatusCodes.Status200OK)]
    public IActionResult ListWarmups()
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        return SuccessResponse(warmup.List());
    }

    [HttpGet("warmups/{id:guid}")]
    [ProducesResponseType(typeof(ResponseDto<CacheWarmupJobDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status404NotFound)]
    public IActionResult GetWarmup(Guid id)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        var job = warmup.Get(id);
        return job is null
            ? ErrorResponse("Cache warmup job was not found.", ResponseCode.NOT_FOUND)
            : SuccessResponse(job);
    }
}
