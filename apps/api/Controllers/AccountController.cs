using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AccountController(IAccountService accounts, IAntiforgery antiforgery) : MasterController
{
    [HttpGet("csrf")]
    [ProducesResponseType(typeof(ResponseDto<CsrfTokenDto, object>), StatusCodes.Status200OK)]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return SuccessResponse(new CsrfTokenDto(tokens.RequestToken!));
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(ResponseDto<AccountStatusDto, object>), StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return SuccessResponse(new AccountStatusDto(false, null, null, false, [], false, null,
                accounts.PublicRegistrationEnabled, accounts.Available));
        }

        DateTimeOffset? premiumExpiry = DateTimeOffset.TryParse(User.FindFirstValue("tarot:premium_expires_at"), out var parsed) ? parsed : null;
        return SuccessResponse(new AccountStatusDto(
            User.HasClaim("tarot:email_verified", "true"),
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            User.FindFirstValue(ClaimTypes.Email),
            true,
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
            premiumExpiry > DateTimeOffset.UtcNow,
            premiumExpiry,
            accounts.PublicRegistrationEnabled,
            accounts.Available));
    }

    [HttpPost("register")]
    [ApiAntiforgery]
    [EnableRateLimiting("account-create")]
    [ProducesResponseType(typeof(ResponseDto<AccountStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.RegisterAsync(request.Email, request.Password, cancellationToken), "Registration is disabled.");

    [HttpPost("login")]
    [ApiAntiforgery]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ResponseDto<AccountStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var result = await accounts.LoginAsync(request.Email, request.Password, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return result.Code == AccountResultCode.Unavailable
                ? ErrorResponse("Account service is unavailable.", ResponseCode.SERVICE_UNAVAILABLE)
                : ErrorResponse("Invalid email or password.", ResponseCode.UNAUTHORIZED);
        }

        if (Guid.TryParse(User.FindFirstValue(AccountAuthentication.SessionIdClaim), out var previousSessionId))
        {
            await accounts.RevokeSessionAsync(previousSessionId, cancellationToken);
        }

        await HttpContext.SignInAsync(
            AccountAuthentication.Scheme,
            AccountAuthentication.CreatePrincipal(result.Value),
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = result.Value.ExpiresAt
            });
        return SuccessResponse(result.Value.Account);
    }

    [Authorize]
    [HttpPost("logout")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<bool, object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(User.FindFirstValue(AccountAuthentication.SessionIdClaim), out var sessionId))
        {
            await accounts.RevokeSessionAsync(sessionId, cancellationToken);
        }
        await HttpContext.SignOutAsync(AccountAuthentication.Scheme);
        return SuccessResponse(true);
    }

    [HttpPost("email-verification/request")]
    [ApiAntiforgery]
    [EnableRateLimiting("account-recovery")]
    [ProducesResponseType(typeof(ResponseDto<AccountMessageDto, object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestVerification([FromBody] EmailRequestDto request, CancellationToken cancellationToken)
    {
        await accounts.RequestEmailVerificationAsync(request.Email, cancellationToken);
        return SuccessResponse(new AccountMessageDto("If the account is eligible, a verification message will be sent."));
    }

    [HttpPost("email-verification/confirm")]
    [ApiAntiforgery]
    [EnableRateLimiting("account-recovery")]
    [ProducesResponseType(typeof(ResponseDto<AccountStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmVerification([FromBody] TokenConfirmationRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.ConfirmEmailAsync(request.Email, request.Token, cancellationToken), "The verification token is invalid or expired.");

    [HttpPost("password-reset/request")]
    [ApiAntiforgery]
    [EnableRateLimiting("account-recovery")]
    [ProducesResponseType(typeof(ResponseDto<AccountMessageDto, object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestPasswordReset([FromBody] EmailRequestDto request, CancellationToken cancellationToken)
    {
        await accounts.RequestPasswordResetAsync(request.Email, cancellationToken);
        return SuccessResponse(new AccountMessageDto("If the account is eligible, a password-reset message will be sent."));
    }

    [HttpPost("password-reset/confirm")]
    [ApiAntiforgery]
    [EnableRateLimiting("account-recovery")]
    [ProducesResponseType(typeof(ResponseDto<AccountStatusDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] PasswordResetConfirmationRequestDto request, CancellationToken cancellationToken) =>
        Map(await accounts.ResetPasswordAsync(request.Email, request.Token, request.NewPassword, cancellationToken), "The password-reset token is invalid or expired.");

    [Authorize]
    [HttpDelete("account")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<bool, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequestDto request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await accounts.DeleteAccountAsync(userId, request.Password, cancellationToken);
        if (!result.Succeeded) return Map(result, "The password is invalid.");
        await HttpContext.SignOutAsync(AccountAuthentication.Scheme);
        return SuccessResponse(true);
    }

    private IActionResult Map<T>(AccountResult<T> result, string invalidMessage)
    {
        if (result.Succeeded) return SuccessResponse(result.Value);
        return result.Code switch
        {
            AccountResultCode.RegistrationDisabled => ErrorResponse(invalidMessage, ResponseCode.FORBIDDEN),
            AccountResultCode.DuplicateEmail => ErrorResponse("An account with this email already exists.", ResponseCode.CONFLICT),
            AccountResultCode.InvalidToken or AccountResultCode.InvalidCredentials => ErrorResponse(invalidMessage, ResponseCode.INVALID_REQUEST),
            AccountResultCode.Conflict => ErrorResponse("The account changed. Reload and try again.", ResponseCode.CONFLICT),
            AccountResultCode.NotFound => ErrorResponse("Account not found.", ResponseCode.NOT_FOUND),
            _ => ErrorResponse("Account service is unavailable.", ResponseCode.SERVICE_UNAVAILABLE)
        };
    }
}
