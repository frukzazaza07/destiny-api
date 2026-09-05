using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Authorize(Roles = AccountRoles.Admin)]
[Route("api/admin/rewarded-deep/settings")]
[ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
public sealed class RewardedDeepAdminController(IRewardedDeepService rewards) : MasterController
{
    [HttpGet]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepSettingsDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await rewards.GetSettingsAsync(cancellationToken);
        return result.Succeeded
            ? SuccessResponse(result.Value)
            : ErrorResponse("Rewarded DEEP settings are unavailable.", ResponseCode.SERVICE_UNAVAILABLE);
    }

    [HttpPut]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<RewardedDeepSettingsDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update([FromBody] UpdateRewardedDeepSettingsRequestDto request, CancellationToken cancellationToken)
    {
        var actorId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await rewards.UpdateSettingsAsync(request, actorId, cancellationToken);
        if (result.Succeeded) return SuccessResponse(result.Value);
        return result.Code == RewardedDeepResultCode.Conflict
            ? ErrorResponse("Rewarded DEEP settings changed. Reload and try again.", ResponseCode.CONFLICT)
            : ErrorResponse("Rewarded DEEP settings are unavailable.", ResponseCode.SERVICE_UNAVAILABLE);
    }
}
