using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/rewards/deep")]
public sealed class RewardedDeepController(IRewardedDeepService rewards) : MasterController
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepStatusDto, object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        SuccessResponse(await rewards.GetStatusAsync(User, RewardedDeepCookie.Read(Request), cancellationToken));

    [HttpPost("sessions")]
    [ApiAntiforgery]
    [EnableRateLimiting("reward-session")]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        var result = await rewards.CreateSessionAsync(User, RewardedDeepCookie.Read(Request), cancellationToken);
        if (result.Succeeded)
        {
            if (result.Value!.AnonymousToken is { } token) RewardedDeepCookie.Write(Response, token);
            return SuccessResponse(result.Value.Status);
        }
        return Map(result.Code, result.RetryAt);
    }

    [HttpPost("attempts")]
    [ApiAntiforgery]
    [EnableRateLimiting("reward-attempt")]
    [ProducesResponseType(typeof(ResponseDto<RewardedAdAttemptDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAttempt(CancellationToken cancellationToken)
    {
        var result = await rewards.CreateAttemptAsync(User, RewardedDeepCookie.Read(Request), cancellationToken);
        return result.Succeeded ? SuccessResponse(result.Value) : Map(result.Code, result.RetryAt);
    }

    [HttpPost("attempts/close")]
    [ApiAntiforgery]
    [EnableRateLimiting("reward-attempt")]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CloseAttempt([FromBody] CloseRewardedAdAttemptRequestDto request, CancellationToken cancellationToken)
    {
        var result = await rewards.CloseAttemptAsync(User, RewardedDeepCookie.Read(Request), request, cancellationToken);
        return result.Succeeded ? SuccessResponse(result.Value) : Map(result.Code, result.RetryAt);
    }

    [HttpPost("grants")]
    [ApiAntiforgery]
    [EnableRateLimiting("reward-grant")]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Grant([FromBody] RewardedAdGrantRequestDto request, CancellationToken cancellationToken)
    {
        var result = await rewards.GrantAsync(User, RewardedDeepCookie.Read(Request), request, cancellationToken);
        return result.Succeeded ? SuccessResponse(result.Value) : Map(result.Code, result.RetryAt);
    }

    private IActionResult Map(RewardedDeepResultCode code, DateTimeOffset? retryAt) => code switch
    {
        RewardedDeepResultCode.Disabled => ErrorResponse("Rewarded DEEP access is not enabled.", ResponseCode.FORBIDDEN),
        RewardedDeepResultCode.Unavailable => ErrorResponse("Rewarded DEEP access is unavailable.", ResponseCode.SERVICE_UNAVAILABLE),
        RewardedDeepResultCode.NoSession => ErrorResponse("Start a reward session before requesting an ad.", ResponseCode.CONFLICT),
        RewardedDeepResultCode.DailyLimit => ErrorResponse($"The daily reward limit has been reached. Next eligible time: {retryAt:O}.", ResponseCode.CONFLICT),
        RewardedDeepResultCode.ActiveAttempt => ErrorResponse("Finish or close the current rewarded-ad attempt first.", ResponseCode.CONFLICT),
        RewardedDeepResultCode.ExpiredNonce => ErrorResponse("The rewarded-ad attempt expired. Start a new attempt.", ResponseCode.CONFLICT),
        RewardedDeepResultCode.ReplayedNonce => ErrorResponse("This rewarded-ad completion was already processed.", ResponseCode.CONFLICT),
        RewardedDeepResultCode.IdentityMismatch => ErrorResponse("The rewarded-ad attempt does not belong to this reward session.", ResponseCode.FORBIDDEN),
        RewardedDeepResultCode.Conflict => ErrorResponse("Reward progress changed. Refresh and try again.", ResponseCode.CONFLICT),
        _ => ErrorResponse("The rewarded-ad completion proof is invalid.", ResponseCode.INVALID_REQUEST)
    };
}

internal static class RewardedDeepCookie
{
    private const string Name = "__Host-Tarot.Reward";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token) => response.Cookies.Append(Name, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(24),
        IsEssential = true
    });
}
