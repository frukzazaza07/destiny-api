using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TarotDestiny.Api.Services;

public interface IAdminAccessPolicy
{
    bool IsAllowed(HttpRequest request);
}

public sealed class AdminAccessPolicy(IOptions<AdminOptions> options) : IAdminAccessPolicy
{
    private readonly byte[]? _expectedHash = string.IsNullOrWhiteSpace(options.Value.Key)
        ? null
        : SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));

    public bool IsAllowed(HttpRequest request)
    {
        if (_expectedHash is null || !request.Headers.TryGetValue("X-Admin-Key", out var supplied))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        return CryptographicOperations.FixedTimeEquals(_expectedHash, suppliedHash);
    }
}
