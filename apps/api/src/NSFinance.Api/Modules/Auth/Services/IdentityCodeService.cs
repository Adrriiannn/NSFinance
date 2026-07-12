using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public interface IIdentityCodeService
{
    string CreateSixDigitCode();
    string HashChallengeSecret(Guid challengeId, string secret);
    bool VerifyChallengeSecret(Guid challengeId, string secret, string expectedHash);
    string HashDestination(string channel, string destination);
    string HashRecoveryCode(Guid authenticatorId, string code);
    bool VerifyRecoveryCode(Guid authenticatorId, string code, string expectedHash);
}

public sealed class IdentityCodeService : IIdentityCodeService
{
    private readonly byte[] _key;

    public IdentityCodeService(IOptions<IdentitySecurityOptions> options)
    {
        var pepper = options.Value.CodePepper?.Trim() ?? string.Empty;
        if (pepper.Length < 32)
        {
            throw new InvalidOperationException("IdentitySecurity:CodePepper must contain at least 32 characters.");
        }

        _key = Encoding.UTF8.GetBytes(pepper);
    }

    public string CreateSixDigitCode()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    public string HashChallengeSecret(Guid challengeId, string secret)
    {
        return ComputeHash($"challenge:{challengeId:N}:{secret.Trim()}");
    }

    public bool VerifyChallengeSecret(Guid challengeId, string secret, string expectedHash)
    {
        return FixedTimeEquals(HashChallengeSecret(challengeId, secret), expectedHash);
    }

    public string HashDestination(string channel, string destination)
    {
        return ComputeHash($"destination:{channel.Trim().ToLowerInvariant()}:{destination.Trim().ToLowerInvariant()}");
    }

    public string HashRecoveryCode(Guid authenticatorId, string code)
    {
        return ComputeHash($"mfa-recovery:{authenticatorId:N}:{NormalizeRecoveryCode(code)}");
    }

    public bool VerifyRecoveryCode(Guid authenticatorId, string code, string expectedHash)
    {
        return FixedTimeEquals(HashRecoveryCode(authenticatorId, code), expectedHash);
    }

    public static string NormalizeRecoveryCode(string code)
    {
        return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private string ComputeHash(string value)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static bool FixedTimeEquals(string actualHex, string expectedHex)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHex),
                Convert.FromHexString(expectedHex));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
