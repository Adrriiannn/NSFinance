using System.Security.Cryptography;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 180_000;
    private const string AlgorithmId = "pbkdf2-sha256";
    private static readonly string DummyHash = CreateHash("NSFinance constant-work login verification");

    public string HashPassword(string password)
    {
        return CreateHash(password);
    }

    public void PerformDummyVerification(string password)
    {
        _ = VerifyPassword(password, DummyHash);
    }

    private static string CreateHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return $"{AlgorithmId}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !parts[0].Equals(AlgorithmId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    public bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return true;
        }

        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !parts[0].Equals(AlgorithmId, StringComparison.Ordinal))
        {
            return true;
        }

        return !int.TryParse(parts[1], out var iterations) || iterations < Iterations;
    }
}
