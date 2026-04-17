namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IDomainTriggerPolicyService
{
    DomainTriggerPolicyEvaluation Evaluate(
        IReadOnlyCollection<int> domainCandidates,
        string normalizedDescriptor);
}

public sealed record DomainTriggerPolicyEvaluation(
    DomainTriggerMode TriggerMode,
    IReadOnlyList<int> DomainCandidates,
    bool UsedInferredCandidates);

public sealed class DomainTriggerPolicyService : IDomainTriggerPolicyService
{
    private static readonly HashSet<int> D0Domains =
    [
        900, 910, 920, 170, 180, 270
    ];

    private static readonly HashSet<int> D1Domains =
    [
        140, 150, 280
    ];

    private static readonly HashSet<int> D2Domains =
    [
        100, 110, 130, 160, 190, 210, 220, 250
    ];

    private static readonly HashSet<int> D3Domains =
    [
        200, 230, 240, 260, 290, 300, 310
    ];

    private static readonly string[] D0Keywords =
    [
        "transfer",
        "internal transfer",
        "bank transfer",
        "salary",
        "payroll",
        "wages",
        "benefit",
        "refund",
        "reversal",
        "chargeback",
        "savings pocket",
        "round up",
        "round-up",
        "vault"
    ];

    private static readonly string[] D1Keywords =
    [
        "utility",
        "utilities",
        "electric",
        "electricity",
        "gas",
        "water",
        "internet",
        "broadband",
        "insurance",
        "policy",
        "subscription",
        "membership",
        "tax",
        "debt",
        "loan"
    ];

    private static readonly string[] D3Keywords =
    [
        "gift",
        "donation",
        "charity",
        "church",
        "religious",
        "business",
        "invoice",
        "office"
    ];

    public DomainTriggerPolicyEvaluation Evaluate(
        IReadOnlyCollection<int> domainCandidates,
        string normalizedDescriptor)
    {
        var explicitCandidates = domainCandidates
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        var usedInference = explicitCandidates.Length == 0;
        var effectiveCandidates = usedInference
            ? InferDomainCandidates(normalizedDescriptor)
            : explicitCandidates;

        var mode = ResolveMode(effectiveCandidates);
        return new DomainTriggerPolicyEvaluation(
            TriggerMode: mode,
            DomainCandidates: effectiveCandidates,
            UsedInferredCandidates: usedInference);
    }

    private static IReadOnlyList<int> InferDomainCandidates(string normalizedDescriptor)
    {
        if (ContainsAny(normalizedDescriptor, D0Keywords))
        {
            return [920];
        }

        if (ContainsAny(normalizedDescriptor, D3Keywords))
        {
            return [240];
        }

        if (ContainsAny(normalizedDescriptor, D1Keywords))
        {
            return [140];
        }

        // Conservative default for unresolved consumer spend descriptors.
        return [130];
    }

    private static DomainTriggerMode ResolveMode(IReadOnlyCollection<int> domainCandidates)
    {
        if (domainCandidates.Any(x => D0Domains.Contains(x)))
        {
            return DomainTriggerMode.D0;
        }

        if (domainCandidates.Any(x => D3Domains.Contains(x)))
        {
            return DomainTriggerMode.D3;
        }

        if (domainCandidates.Any(x => D2Domains.Contains(x)))
        {
            return DomainTriggerMode.D2;
        }

        if (domainCandidates.Any(x => D1Domains.Contains(x)))
        {
            return DomainTriggerMode.D1;
        }

        return DomainTriggerMode.D1;
    }

    private static bool ContainsAny(string value, IReadOnlyCollection<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

