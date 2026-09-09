using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/readings/{id:guid}/prompt")]
[Authorize]
[ProducesResponseType(typeof(ResponseDto<object, string>), 400)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 401)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 403)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 404)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 409)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 429)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 503)]
public sealed class PromptCopyController : MasterController
{
    private PromptCopyService Service => HttpContext.RequestServices.GetService<PromptCopyService>()
        ?? throw new ReadingJobException(503, "PROMPT_REWARDS_UNAVAILABLE");
    private string Owner => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? $"user:{id}" : throw new ReadingJobException(401, "LOGIN_REQUIRED");
    private string? RewardAnonymous => ReadingJobService.Owner(new ClaimsPrincipal(), RewardedDeepCookie.Read(Request));

    [HttpGet("status")]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyStatusDto, object>), 200)]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return SuccessResponse(await Service.Status(id, Owner, PromptReadingCookie.Owner(Request), ct, RewardAnonymous));
    }

    [HttpPost("sessions"), ApiAntiforgery, EnableRateLimiting("reward-session")]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyStatusDto, object>), 200)]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct) =>
        SuccessResponse(await Service.Start(id, Owner, PromptReadingCookie.Owner(Request), ct, RewardAnonymous));

    [HttpPost("attempts"), ApiAntiforgery, EnableRateLimiting("reward-attempt")]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyAttemptDto, object>), 200)]
    public async Task<IActionResult> Attempt(Guid id, CancellationToken ct) =>
        SuccessResponse(await Service.Attempt(id, Owner, ct));

    [HttpPost("attempts/{attemptId:guid}/close"), ApiAntiforgery, EnableRateLimiting("reward-attempt")]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyStatusDto, object>), 200)]
    public async Task<IActionResult> Close(Guid id, Guid attemptId, CancellationToken ct) =>
        SuccessResponse(await Service.Close(id, attemptId, Owner, ct));

    [HttpPost("copy"), ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyTextDto, object>), 200)]
    public async Task<IActionResult> Copy(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return SuccessResponse(await Service.Export(id, Owner, ct));
    }
}

[ApiController]
[Route("api/rewards/prompt/provider-completions")]
public sealed class PromptProviderCallbackController : MasterController
{
    [HttpPost]
    [ProducesResponseType(typeof(ResponseDto<PromptCopyStatusDto, object>), 200)]
    [ProducesResponseType(typeof(ResponseDto<object, ValidationErrorsDto>), 400)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), 403)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), 404)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), 409)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), 503)]
    public async Task<IActionResult> Complete([FromBody] PromptCopyCallbackDto input,
        [FromHeader(Name = "X-Prompt-Signature")] string? signature, CancellationToken ct)
    {
        var service = HttpContext.RequestServices.GetService<PromptCopyService>()
            ?? throw new ReadingJobException(503, "PROMPT_REWARDS_UNAVAILABLE");
        return SuccessResponse(await service.Confirm(input, signature ?? "", ct));
    }
}

internal static class PromptReadingCookie
{
    private const string Name = "__Host-Tarot.PromptReading";
    public static string? Owner(HttpRequest request) => request.Cookies[Name] is { } cookie
        ? ReadingJobService.Owner(new ClaimsPrincipal(), cookie) : null;
    public static string EnsureOwner(HttpContext context)
    {
        var authenticated = ReadingJobService.Owner(context.User, null);
        if (authenticated is not null) return authenticated;
        if (Owner(context.Request) is { } existing) return existing;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(Name, token, new CookieOptions {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true,
            MaxAge = TimeSpan.FromDays(30)
        });
        return ReadingJobService.Owner(new ClaimsPrincipal(), token)!;
    }
}
