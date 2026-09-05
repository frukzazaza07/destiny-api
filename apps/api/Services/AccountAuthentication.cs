using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public static class AccountAuthentication
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string SessionIdClaim = "tarot:session_id";

    public static ClaimsPrincipal CreatePrincipal(LoginSession login)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, login.Account.UserId!.Value.ToString()),
            new(ClaimTypes.Email, login.Account.Email!),
            new(SessionIdClaim, login.SessionId.ToString()),
            new("tarot:email_verified", login.Account.EmailVerified ? "true" : "false")
        };
        claims.AddRange(login.Account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Email, ClaimTypes.Role));
    }
}

public sealed class DatabaseSessionClaimsTransformation(
    IAccountService accounts,
    IOptions<DeepReadingOptions> deepOptions) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue(AccountAuthentication.SessionIdClaim), out var sessionId))
        {
            return principal;
        }

        var account = await accounts.ValidateSessionAsync(userId, sessionId, CancellationToken.None);
        if (account is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.UserId.ToString()),
            new(ClaimTypes.Email, account.Email),
            new(AccountAuthentication.SessionIdClaim, account.SessionId.ToString()),
            new("tarot:email_verified", account.EmailVerified ? "true" : "false")
        };
        claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (account.PremiumDeepActive)
        {
            claims.Add(new Claim(deepOptions.Value.ClaimType, deepOptions.Value.ClaimValue));
            claims.Add(new Claim("tarot:premium_expires_at", account.PremiumExpiresAt!.Value.ToString("O")));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AccountAuthentication.Scheme, ClaimTypes.Email, ClaimTypes.Role));
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ApiAntiforgeryAttribute : ServiceFilterAttribute
{
    public ApiAntiforgeryAttribute() : base(typeof(ApiAntiforgeryFilter)) { }
}

public sealed class ApiAntiforgeryFilter(Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ResponseDto<object, string>(null, "The CSRF token is missing or invalid.", ResponseCode.FORBIDDEN))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ApiAntiforgeryForCookieUserAttribute : ServiceFilterAttribute
{
    public ApiAntiforgeryForCookieUserAttribute() : base(typeof(ApiAntiforgeryForCookieUserFilter)) { }
}

public sealed class ApiAntiforgeryForCookieUserFilter(
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true ||
            context.HttpContext.Request.Headers.ContainsKey("X-Admin-Key"))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ResponseDto<object, string>(null, "The CSRF token is missing or invalid.", ResponseCode.FORBIDDEN))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}

public sealed class AccountNotificationSender(
    IOptions<AccountEmailOptions> options,
    ILogger<AccountNotificationSender> logger) : IAccountNotificationSender
{
    private readonly AccountEmailOptions _options = options.Value;

    public Task SendEmailVerificationAsync(string email, string token, CancellationToken cancellationToken) =>
        SendAsync(email, "Verify your Tarot Destiny account", "verify-email", token, cancellationToken);

    public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken) =>
        SendAsync(email, "Reset your Tarot Destiny password", "reset-password", token, cancellationToken);

    private async Task SendAsync(string email, string subject, string path, string token, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Account email delivery is disabled; no message was sent.");
            return;
        }

        // The fragment is never sent to the web server or access logs.
        var link = $"{_options.PublicSiteUrl.TrimEnd('/')}/en/{path}#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        using var message = new MailMessage(_options.FromAddress, email, subject,
            $"Open this single-use link before it expires:\n\n{link}\n\nIf you did not request this, ignore this message.");
        using var smtp = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };
        try
        {
            await smtp.SendMailAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not attach the exception or message body: either can contain delivery details or the token URL.
            logger.LogError("Account email delivery failed with {ExceptionType}.", exception.GetType().Name);
        }
    }
}
