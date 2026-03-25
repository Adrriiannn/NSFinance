using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public enum PwnedPasswordCheckStatus
{
    Safe,
    Compromised,
    Unavailable
}

public sealed class PwnedPasswordService(
    HttpClient httpClient,
    IOptions<PasswordPolicyOptions> options,
    ILogger<PwnedPasswordService> logger)
{
    private readonly PasswordPolicyOptions _options = options.Value;

    public async Task<PwnedPasswordCheckStatus> CheckAsync(string password, CancellationToken cancellationToken)
    {
        if (!_options.BreachCheckEnabled)
        {
            return PwnedPasswordCheckStatus.Safe;
        }

        try
        {
            var sha1Hash = ComputeSha1Hex(password);
            var prefix = sha1Hash[..5];
            var suffix = sha1Hash[5..];

            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            request.Headers.TryAddWithoutValidation("Add-Padding", "true");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "HIBP password check failed with status code {StatusCode}.",
                    (int)response.StatusCode);
                return PwnedPasswordCheckStatus.Unavailable;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var lines = body.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return PwnedPasswordCheckStatus.Compromised;
                }
            }

            return PwnedPasswordCheckStatus.Safe;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HIBP password check is unavailable.");
            return PwnedPasswordCheckStatus.Unavailable;
        }
    }

    private static string ComputeSha1Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
