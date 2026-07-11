using System.Security.Cryptography;
using System.Text;

namespace NSFinance.Api.Modules.Auth.Services;

public class TokenSecretService
{
    public virtual string CreateToken(int bytes = 48)
    {
        var random = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(random)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", string.Empty);
    }

    public virtual string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
