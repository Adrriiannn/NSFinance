using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed partial class MerchantDescriptorNormalizer
{
    private const int CacheSizeLimit = 20_000;
    private const int MaxTokenCount = 16;
    private readonly ConcurrentDictionary<string, string> _normalizedCache = new(StringComparer.Ordinal);

    public string Normalize(string? descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return string.Empty;
        }

        var key = descriptor.Trim();
        if (_normalizedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var lowered = key.ToLowerInvariant();
        var noEmail = EmailPattern().Replace(lowered, " ");
        var noIban = IbanPattern().Replace(noEmail, " ");
        var noNoise = NonAlphaNumericPattern().Replace(noIban, " ");
        var collapsed = MultiSpacePattern().Replace(noNoise, " ").Trim();

        var normalized = collapsed.Length > 320
            ? collapsed[..320]
            : collapsed;

        if (_normalizedCache.Count >= CacheSizeLimit)
        {
            _normalizedCache.Clear();
        }

        _normalizedCache.TryAdd(key, normalized);
        return normalized;
    }

    public string SanitizeForStorage(string? descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return string.Empty;
        }

        var trimmed = descriptor.Trim();
        var sanitized = EmailPattern().Replace(trimmed, "[redacted-email]");
        sanitized = IbanPattern().Replace(sanitized, "[redacted-iban]");
        sanitized = LongDigitPattern().Replace(sanitized, "[redacted-number]");
        sanitized = MultiSpacePattern().Replace(sanitized, " ").Trim();

        return sanitized.Length > 512
            ? sanitized[..512]
            : sanitized;
    }

    public IReadOnlySet<string> Tokenize(string normalizedDescriptor)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescriptor))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return normalizedDescriptor
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .Take(MaxTokenCount)
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiSpacePattern();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumericPattern();

    [GeneratedRegex(@"[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b[a-z]{2}\d{2}[a-z0-9]{11,30}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"\d{5,}", RegexOptions.Compiled)]
    private static partial Regex LongDigitPattern();
}
