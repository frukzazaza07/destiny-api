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
[Route("api/admin/users")]
[ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
public sealed class UserAdminController(IAccountService accounts) : MasterController
{
    [HttpGet]
    [ProducesResponseType(typeof(ResponseDto<AdminUserPageDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? query = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Map(await accounts.ListUsersAsync(query, page, pageSize, cancellationToken));

    [HttpPost]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<AdminUserDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.CreateUserAsync(request, ActorId(), cancellationToken));

    [HttpPut("{userId:guid}/status")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<AdminUserDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStatus(Guid userId, [FromBody] AdminAccountStatusRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.SetUserEnabledAsync(userId, request.Enabled, request.ExpectedRevision, ActorId(), cancellationToken));

    [HttpPost("{userId:guid}/premium")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<AdminUserDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantPremium(Guid userId, [FromBody] PremiumGrantRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.GrantPremiumAsync(userId, request, ActorId(), cancellationToken));

    [HttpDelete("{userId:guid}/premium")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<AdminUserDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokePremium(Guid userId, [FromBody] PremiumRevokeRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.RevokePremiumAsync(userId, request, ActorId(), cancellationToken));

    [HttpGet("{userId:guid}/entitlements")]
    [ProducesResponseType(typeof(ResponseDto<EntitlementHistoryDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> History(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Map(await accounts.GetEntitlementHistoryAsync(userId, page, pageSize, cancellationToken));

    private Guid ActorId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult Map<T>(AccountResult<T> result)
    {
        if (result.Succeeded) return SuccessResponse(result.Value);
        return result.Code switch
        {
            AccountResultCode.DuplicateEmail => ErrorResponse("An account with this email already exists.", ResponseCode.CONFLICT),
            AccountResultCode.NotFound => ErrorResponse("Account or active entitlement not found.", ResponseCode.NOT_FOUND),
            AccountResultCode.Conflict => ErrorResponse("The account or entitlement changed. Reload and try again.", ResponseCode.CONFLICT),
            _ => ErrorResponse("Account service is unavailable.", ResponseCode.SERVICE_UNAVAILABLE)
        };
    }
}
