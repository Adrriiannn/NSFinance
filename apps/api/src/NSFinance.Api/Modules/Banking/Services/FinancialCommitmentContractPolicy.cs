namespace NSFinance.Api.Modules.Banking.Services;

internal static class FinancialCommitmentContractPolicy
{
    internal static string ResolveLabel(string? primary, string? fallback, string defaultLabel)
    {
        return NormalizeOptional(primary)
            ?? NormalizeOptional(fallback)
            ?? defaultLabel;
    }

    internal static string? NormalizeCadence(string? value)
    {
        var normalized = NormalizeToken(value);
        return normalized.Length == 0 ? null : normalized;
    }

    internal static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            '_',
            value.Trim()
                .ToLowerInvariant()
                .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string? NormalizeCurrency(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    internal static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static DateTime? NormalizeUtc(DateTime? value)
    {
        return value.HasValue ? EnsureUtc(value.Value) : null;
    }

    internal static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
