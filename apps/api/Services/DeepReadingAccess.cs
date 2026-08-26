using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace TarotDestiny.Api.Services;

public interface IDeepReadingAccessPolicy
{
    DeepReadingAccess Evaluate(ClaimsPrincipal user);
}

public sealed record DeepReadingAccess(bool Enabled, bool Entitled, string? UpgradeUrl);

public sealed class DeepReadingAccessPolicy(
    IOptions<DeepReadingOptions> options,
    IHostEnvironment environment) : IDeepReadingAccessPolicy
{
    private readonly DeepReadingOptions _options = options.Value;

    public DeepReadingAccess Evaluate(ClaimsPrincipal user)
    {
        if (!_options.Enabled)
        {
            return new DeepReadingAccess(false, false, _options.UpgradeUrl);
        }

        var developmentAccess = environment.IsDevelopment() &&
            _options.AllowUnentitledInDevelopment;
        var claimAccess = user.Identity?.IsAuthenticated == true &&
            user.HasClaim(_options.ClaimType, _options.ClaimValue);

        return new DeepReadingAccess(true, developmentAccess || claimAccess, _options.UpgradeUrl);
    }
}
