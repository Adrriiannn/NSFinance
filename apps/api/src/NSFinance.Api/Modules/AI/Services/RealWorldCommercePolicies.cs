using System.Text.RegularExpressions;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class RealWorldProductDomainEligibilityPolicy : IRealWorldProductDomainEligibilityPolicy
{
    private static readonly RealWorldDiscoveryDomain[] ElectronicsPreferredDomains =
    [
        RealWorldDiscoveryDomain.ElectronicsRetail,
        RealWorldDiscoveryDomain.ShoppingGeneral,
        RealWorldDiscoveryDomain.CommerceGeneral
    ];

    private static readonly RealWorldDiscoveryDomain[] ConveniencePreferredDomains =
    [
        RealWorldDiscoveryDomain.ConvenienceStore,
        RealWorldDiscoveryDomain.Grocery,
        RealWorldDiscoveryDomain.PetrolStation,
        RealWorldDiscoveryDomain.ShoppingGeneral,
        RealWorldDiscoveryDomain.CommerceGeneral
    ];

    private static readonly RealWorldDiscoveryDomain[] MixedPreferredDomains =
    [
        RealWorldDiscoveryDomain.ElectronicsRetail,
        RealWorldDiscoveryDomain.ConvenienceStore,
        RealWorldDiscoveryDomain.Grocery,
        RealWorldDiscoveryDomain.PetrolStation,
        RealWorldDiscoveryDomain.ShoppingGeneral,
        RealWorldDiscoveryDomain.CommerceGeneral
    ];

    private static readonly string[] ElectronicsSignals =
    [
        "ps5",
        "playstation",
        "xbox",
        "nintendo",
        "switch",
        "controller",
        "console",
        "laptop",
        "computer",
        "pc",
        "phone",
        "iphone",
        "android",
        "tablet",
        "monitor",
        "headset",
        "electronics"
    ];

    private static readonly string[] ConvenienceSignals =
    [
        "red bull",
        "redbull",
        "energy drink",
        "coke",
        "coca cola",
        "pepsi",
        "water",
        "snack",
        "snacks",
        "sandwich",
        "drink",
        "drinks"
    ];

    public RealWorldCommerceEligibilityResult Evaluate(
        string userQuery,
        RealWorldIntentInterpretation interpretation,
        IReadOnlyList<RealWorldDiscoveryDomain> candidateDomains)
    {
        var normalizedQuery = Normalize(userQuery);
        var isCommerceIntent = interpretation.IntentFamily == RealWorldIntentFamily.CommerceDiscovery
                               || RealWorldDeterministicFallbackBuilder.IsExplicitVendorLookup(normalizedQuery);
        if (!isCommerceIntent)
        {
            return new RealWorldCommerceEligibilityResult(
                IsCommerceVendorRequest: false,
                ProductProfile: "not_commerce",
                ProductHints: [],
                AllowedDomains: [],
                PreferredDomains: [],
                ExcludedDomains: [],
                ReasonCodes: []);
        }

        var reasonCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "real_world_commerce_vendor_intent_detected"
        };
        var productHints = ResolveProductHints(normalizedQuery, interpretation.CandidateConcepts);
        var profile = ResolveProfile(normalizedQuery, productHints);
        reasonCodes.Add($"real_world_commerce_product_detected:{profile}");

        foreach (var hint in productHints.Take(4))
        {
            reasonCodes.Add($"real_world_commerce_product_hint:{hint}");
        }

        var preferredDomains = profile switch
        {
            "electronics_console" => ElectronicsPreferredDomains,
            "convenience_snack" => ConveniencePreferredDomains,
            "mixed_retail" => MixedPreferredDomains,
            _ => MixedPreferredDomains
        };
        var allowedSet = preferredDomains.ToHashSet();
        var excluded = candidateDomains
            .Distinct()
            .Where(domain => !allowedSet.Contains(domain))
            .ToArray();
        foreach (var domain in excluded)
        {
            reasonCodes.Add($"real_world_commerce_domain_excluded:{domain.ToString().ToLowerInvariant()}");
        }

        return new RealWorldCommerceEligibilityResult(
            IsCommerceVendorRequest: true,
            ProductProfile: profile,
            ProductHints: productHints,
            AllowedDomains: allowedSet.ToArray(),
            PreferredDomains: preferredDomains,
            ExcludedDomains: excluded,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static IReadOnlyList<string> ResolveProductHints(
        string normalizedQuery,
        IReadOnlyList<string> candidateConcepts)
    {
        var hints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signal in ElectronicsSignals)
        {
            if (normalizedQuery.Contains(signal, StringComparison.Ordinal))
            {
                hints.Add(signal.Replace(' ', '_'));
            }
        }

        foreach (var signal in ConvenienceSignals)
        {
            if (normalizedQuery.Contains(signal, StringComparison.Ordinal))
            {
                hints.Add(signal.Replace(' ', '_'));
            }
        }

        foreach (var concept in candidateConcepts.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalizedConcept = concept.Trim().ToLowerInvariant();
            if (normalizedConcept.Contains("electronics", StringComparison.Ordinal)
                || normalizedConcept.Contains("console", StringComparison.Ordinal)
                || normalizedConcept.Contains("xbox", StringComparison.Ordinal)
                || normalizedConcept.Contains("playstation", StringComparison.Ordinal))
            {
                hints.Add("electronics");
            }

            if (normalizedConcept.Contains("convenience", StringComparison.Ordinal)
                || normalizedConcept.Contains("grocery", StringComparison.Ordinal)
                || normalizedConcept.Contains("petrol", StringComparison.Ordinal))
            {
                hints.Add("convenience");
            }
        }

        return hints.Take(8).ToArray();
    }

    private static string ResolveProfile(
        string normalizedQuery,
        IReadOnlyList<string> productHints)
    {
        var electronicsMatches = ElectronicsSignals.Count(signal =>
            normalizedQuery.Contains(signal, StringComparison.Ordinal))
            + productHints.Count(hint => hint.Contains("electronics", StringComparison.Ordinal)
                                         || hint.Contains("console", StringComparison.Ordinal)
                                         || hint.Contains("xbox", StringComparison.Ordinal)
                                         || hint.Contains("playstation", StringComparison.Ordinal));
        var convenienceMatches = ConvenienceSignals.Count(signal =>
            normalizedQuery.Contains(signal, StringComparison.Ordinal))
            + productHints.Count(hint => hint.Contains("convenience", StringComparison.Ordinal)
                                         || hint.Contains("grocery", StringComparison.Ordinal)
                                         || hint.Contains("drink", StringComparison.Ordinal)
                                         || hint.Contains("snack", StringComparison.Ordinal));

        if (electronicsMatches > 0 && convenienceMatches > 0)
        {
            return "mixed_retail";
        }

        if (electronicsMatches > 0)
        {
            return "electronics_console";
        }

        if (convenienceMatches > 0)
        {
            return "convenience_snack";
        }

        return "generic_commerce";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}\s'\-]", " ");
        return Regex.Replace(cleaned, "\\s+", " ").Trim();
    }
}
