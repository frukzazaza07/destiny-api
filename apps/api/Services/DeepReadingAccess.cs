using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace TarotDestiny.Api.Services;

public interface IDeepReadingAccessPolicy
{
    DeepReadingAccess Evaluate(ClaimsPrincipal user);
}

public sealed record DeepReadingAccess(
    bool Enabled,
    bool Entitled,
    string? UpgradeUrl,
    bool Authenticated = false,
    DateTimeOffset? PremiumExpiresAt = null,
    int AvailableAdEarnedCredits = 0);

public sealed class DeepReadingAccessPolicy(
    IOptions<DeepReadingOptions> options,
    IHostEnvironment environment) : IDeepReadingAccessPolicy
{
    private readonly DeepReadingOptions _options = options.Value;

    public DeepReadingAccess Evaluate(ClaimsPrincipal user)
    {
        if (!_options.Enabled)
        {
            return new DeepReadingAccess(false, false, _options.UpgradeUrl, user.Identity?.IsAuthenticated == true);
        }

        var developmentAccess = environment.IsDevelopment() &&
            _options.AllowUnentitledInDevelopment;
        var claimAccess = user.Identity?.IsAuthenticated == true &&
            user.HasClaim(_options.ClaimType, _options.ClaimValue);

        DateTimeOffset? premiumExpiresAt = DateTimeOffset.TryParse(
            user.FindFirstValue("tarot:premium_expires_at"),
            out var parsedExpiry)
            ? parsedExpiry
            : null;

        return new DeepReadingAccess(
            true,
            developmentAccess || claimAccess,
            _options.UpgradeUrl,
            user.Identity?.IsAuthenticated == true,
            premiumExpiresAt);
    }
}
