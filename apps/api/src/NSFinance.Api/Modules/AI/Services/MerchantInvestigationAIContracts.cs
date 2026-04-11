using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public enum MerchantInvestigationRecommendation
{
    AcceptCandidate = 0,
    AcceptCautiously = 1,
    Unresolved = 2,
    InsufficientEvidence = 3,
    ConflictingCandidates = 4
}

public sealed record MerchantInvestigationStructuredResponse(
    MerchantInvestigationSummary Summary,
    IReadOnlyList<MerchantInvestigationStructuredCandidate> Candidates,
    IReadOnlyList<MerchantInvestigationAliasSuggestion> AliasSuggestions,
    IReadOnlyList<MerchantInvestigationStructuredEvidence> Evidence);

public sealed record MerchantInvestigationSummary(
    double OverallConfidence,
    double AmbiguityLevel,
    MerchantInvestigationRecommendation Recommendation,
    string Summary);

public sealed record MerchantInvestigationStructuredCandidate(
    string CanonicalName,
    string DisplayName,
    string? LikelyOfficialWebsite,
    MerchantType MerchantType,
    MerchantUsageType MerchantUsageType,
    string BusinessSummary,
    bool SupportsSubscriptions,
    bool SupportsRecurringPayments,
    bool SupportsOneTimePurchases,
    bool SupportsMarketplacePayments,
    bool SupportsInAppPurchases,
    IReadOnlyList<string> LikelyCategoryFamilies,
    double DescriptorMatchStrength,
    double EntityMatchStrength,
    bool MixedUseRisk,
    double Confidence,
    string WhyItMayMatch,
    string WhyItMayBeWrong,
    string PrimaryCountryCode,
    bool HasContradictions,
    IReadOnlyList<string> AliasCandidates);

public sealed record MerchantInvestigationAliasSuggestion(
    string AliasText,
    string AliasType,
    double Confidence,
    bool IsPreferred);

public sealed record MerchantInvestigationStructuredEvidence(
    MerchantEvidenceType EvidenceType,
    string EvidenceSummary,
    double Confidence,
    string? SourceReference,
    double Relevance);
