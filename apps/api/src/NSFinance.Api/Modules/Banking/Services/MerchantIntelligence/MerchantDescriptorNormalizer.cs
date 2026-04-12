using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed partial class MerchantDescriptorNormalizer
{
    private const int CacheSizeLimit = 20_000;
    private const int MaxTokenCount = 16;
    private static readonly HashSet<string> TokenStopWords = new(StringComparer.Ordinal)
    {
        "ltd", "limited", "llc", "inc", "co", "company", "corp", "corporation", "plc", "gmbh",
        "sarl", "bv", "ag", "sas", "sa", "spa", "sro", "oy", "pte", "pty", "merchant", "payment",
        "payments", "purchase", "debit", "credit", "card", "transaction", "pos", "ecommerce",
        "online", "store", "shop", "ref", "dd", "sepa", "transfer"
    };

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

        var lowered = RemoveDiacritics(key).ToLowerInvariant();
        lowered = StripProcessorPrefixNoise(lowered);
        var noEmail = EmailPattern().Replace(lowered, " ");
        var noIban = IbanPattern().Replace(noEmail, " ");
        var noLongNumbers = LongDigitPattern().Replace(noIban, " ");
        var noNoise = NonAlphaNumericPattern().Replace(noLongNumbers, " ");
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
            .Select(token => token.Trim())
            .Where(token => token.Length > 1)
            .Where(token => !TokenStopWords.Contains(token))
            .Take(MaxTokenCount)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string StripProcessorPrefixNoise(string value)
    {
        var current = value;
        for (var i = 0; i < 3; i++)
        {
            var stripped = ProcessorPrefixPattern().Replace(current, string.Empty).Trim();
            if (ReferenceEquals(stripped, current) || stripped.Equals(current, StringComparison.Ordinal))
            {
                break;
            }

            current = stripped;
        }

        return current;
    }

    private static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiSpacePattern();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumericPattern();

    [GeneratedRegex(@"^\s*(card\s*purchase|debit\s*card|credit\s*card|card\s*payment|pos|purchase|payment\s*to|payment|dd|sepa|visa|mastercard|mc|txn|transaction|pending)\b[:\-\s]*", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessorPrefixPattern();

    [GeneratedRegex(@"[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b[a-z]{2}\d{2}[a-z0-9]{11,30}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"\d{5,}", RegexOptions.Compiled)]
    private static partial Regex LongDigitPattern();
}
