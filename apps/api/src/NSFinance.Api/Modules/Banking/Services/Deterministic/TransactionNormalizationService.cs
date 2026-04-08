using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class TransactionNormalizationService
{
    private const int DeterministicSourceSignatureMaxLength = 160;
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NoisePunctuation = new(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled);

    private static readonly HashSet<string> TransferKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "transfer",
        "xfer",
        "faster"
    };

    private static readonly HashSet<string> SavingsKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "savings",
        "vault",
        "pocket",
        "roundup",
        "spare"
    };

    private static readonly HashSet<string> WeakSavingsSupportKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pot",
        "round",
        "cash",
        "fund",
        "flexible"
    };

    private static readonly string[] StrongSavingsPhrases =
    [
        "flexible cash",
        "savings pot",
        "spare change",
        "round up",
        "round-up",
        "vault",
        "pocket"
    ];

    private static readonly HashSet<string> ExternalPayeeRiskKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "paypal",
        "venmo",
        "cashapp",
        "zelle",
        "invoice",
        "rent",
        "gift",
        "friend",
        "family",
        "split"
    };

    public string NormalizeDescription(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lowered = input.Trim().ToLowerInvariant();
        var withoutNoise = NoisePunctuation.Replace(lowered, " ");
        var collapsed = MultiWhitespace.Replace(withoutNoise, " ").Trim();
        return collapsed;
    }

    public HashSet<string> Tokenize(string? input)
    {
        var normalized = NormalizeDescription(input);
        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool HasTransferKeyword(string normalizedDescription, IReadOnlySet<string> tokens)
    {
        if (normalizedDescription.Contains("internal transfer", StringComparison.Ordinal)
            || normalizedDescription.Contains("bank transfer", StringComparison.Ordinal))
        {
            return true;
        }

        return tokens.Any(token => TransferKeywords.Contains(token));
    }

    public bool HasSavingsKeyword(string normalizedDescription, IReadOnlySet<string> tokens)
    {
        if (StrongSavingsPhrases.Any(phrase => normalizedDescription.Contains(phrase, StringComparison.Ordinal)))
        {
            return true;
        }

        return tokens.Any(token => SavingsKeywords.Contains(token));
    }

    public bool HasWeakSavingsSupportKeyword(IReadOnlySet<string> tokens)
    {
        return tokens.Any(token => WeakSavingsSupportKeywords.Contains(token));
    }

    public bool HasStrongSavingsKeyword(string normalizedDescription)
    {
        return StrongSavingsPhrases.Any(phrase => normalizedDescription.Contains(phrase, StringComparison.Ordinal));
    }

    public string? ExtractAccountHint(string normalizedDescription)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return null;
        }

        var digits = new string(normalizedDescription.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            return null;
        }

        return digits[^4..];
    }

    public double ComputeReferenceEntropy(string normalizedDescription)
    {
        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return 0d;
        }

        var alphanumeric = normalizedDescription.Where(char.IsLetterOrDigit).ToArray();
        if (alphanumeric.Length == 0)
        {
            return 0d;
        }

        var uniqueCount = alphanumeric
            .Distinct()
            .Count();

        return Math.Round(uniqueCount / (double)alphanumeric.Length, 4, MidpointRounding.AwayFromZero);
    }

    public bool LooksLikeExternalCounterparty(string normalizedDescription, IReadOnlySet<string> tokens)
    {
        if (tokens.Any(token => ExternalPayeeRiskKeywords.Contains(token)))
        {
            return true;
        }

        if (normalizedDescription.StartsWith("to ", StringComparison.Ordinal)
            && !HasStrongSavingsKeyword(normalizedDescription)
            && !normalizedDescription.Contains("internal transfer", StringComparison.Ordinal)
            && !normalizedDescription.Contains("bank transfer", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public string BuildSourceSignature(
        decimal amount,
        string currency,
        DateTime bookedAtUtc,
        string normalizedDescription,
        Guid? linkedTransactionId)
    {
        var linkedId = linkedTransactionId?.ToString("N") ?? "none";
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var bookedAt = bookedAtUtc.ToUniversalTime().ToString("O");

        var builder = new StringBuilder(200);
        builder.Append(amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(normalizedCurrency);
        builder.Append('|');
        builder.Append(bookedAt);
        builder.Append('|');
        builder.Append(normalizedDescription);
        builder.Append('|');
        builder.Append(linkedId);

        var signature = builder.ToString();
        if (signature.Length <= DeterministicSourceSignatureMaxLength)
        {
            return signature;
        }

        // Keep signatures deterministic but bounded so a single long narrative
        // cannot make the whole enrichment batch fail on DB length limits.
        var normalizedDescriptionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDescription)))
            .ToLowerInvariant();
        var boundedSignature =
            $"{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}|{normalizedCurrency}|{bookedAt}|sha256:{normalizedDescriptionHash}|{linkedId}";

        return boundedSignature.Length <= DeterministicSourceSignatureMaxLength
            ? boundedSignature
            : boundedSignature[..DeterministicSourceSignatureMaxLength];
    }
}
