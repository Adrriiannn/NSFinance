using NSFinance.Api.Modules.ExpenseTracker.Services;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceCategoryClassifier
{
    bool IsProtectedDomain(int? domainCode, string? domainName);

    bool IsDiscretionaryDomain(string? domainName);

    bool IsProtectedRecurringName(string? value);
}

public sealed class FinancialAdviceCategoryClassifier(
    ExpenseTaxonomyService taxonomyService) : IFinancialAdviceCategoryClassifier
{
    private static readonly HashSet<string> ProtectedDomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "health",
        "medical",
        "transport",
        "transit",
        "grocer",
        "grocery",
        "childcare",
        "dependent",
        "tax",
        "debt",
        "loan",
        "housing",
        "rent",
        "mortgage",
        "utility"
    };

    private static readonly HashSet<string> DiscretionaryDomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dining",
        "restaurant",
        "entertainment",
        "hobby",
        "shopping",
        "travel",
        "leisure",
        "subscription",
        "lifestyle"
    };

    public bool IsProtectedDomain(int? domainCode, string? domainName)
    {
        if (domainCode.HasValue)
        {
            var resolvedName = taxonomyService.GetDomainName(domainCode.Value);
            if (!string.IsNullOrWhiteSpace(resolvedName)
                && IsNameMatch(resolvedName, ProtectedDomainKeywords))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(domainName)
               && IsNameMatch(domainName, ProtectedDomainKeywords);
    }

    public bool IsDiscretionaryDomain(string? domainName)
    {
        return !string.IsNullOrWhiteSpace(domainName)
               && IsNameMatch(domainName, DiscretionaryDomainKeywords);
    }

    public bool IsProtectedRecurringName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("rent", StringComparison.OrdinalIgnoreCase)
               || value.Contains("mortgage", StringComparison.OrdinalIgnoreCase)
               || value.Contains("loan", StringComparison.OrdinalIgnoreCase)
               || value.Contains("tax", StringComparison.OrdinalIgnoreCase)
               || value.Contains("insurance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNameMatch(string name, IReadOnlyCollection<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lower = name.Trim().ToLowerInvariant();
        foreach (var keyword in keywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
