using NSFinance.Api.Persistence.Entities;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public static class MerchantInvestigationContract
{
    public const string RecommendationAcceptCandidate = "accept_candidate";
    public const string RecommendationAcceptCautiously = "accept_cautiously";
    public const string RecommendationUnresolved = "unresolved";
    public const string RecommendationInsufficientEvidence = "insufficient_evidence";
    public const string RecommendationConflictingCandidates = "conflicting_candidates";

    public static readonly IReadOnlySet<string> AllowedRecommendations = new HashSet<string>(StringComparer.Ordinal)
    {
        RecommendationAcceptCandidate,
        RecommendationAcceptCautiously,
        RecommendationUnresolved,
        RecommendationInsufficientEvidence,
        RecommendationConflictingCandidates
    };

    public const int MaxCandidateCount = 8;
    public const int MaxEvidenceCount = 16;
    public const int MaxAliasSuggestionCount = 24;
}

public sealed record MerchantInvestigationStructuredResponse(
    double OverallConfidence,
    double AmbiguityLevel,
    string Recommendation,
    string Summary,
    IReadOnlyList<MerchantInvestigationStructuredCandidate> Candidates,
    IReadOnlyList<MerchantInvestigationAliasSuggestionPayload> AliasSuggestions,
    IReadOnlyList<MerchantInvestigationStructuredEvidence> Evidence);

public sealed record MerchantInvestigationStructuredCandidate(
    string CanonicalName,
    string? DisplayName,
    string? LikelyOfficialWebsite,
    string? ParentBrand,
    MerchantType MerchantType,
    MerchantUsageType MerchantUsageType,
    string? BusinessSummary,
    bool? SupportsSubscriptions,
    bool? SupportsRecurringPayments,
    bool? SupportsOneTimePurchases,
    bool? SupportsMarketplacePayments,
    bool? SupportsInAppPurchases,
    IReadOnlyList<string>? LikelyCategoryFamilies,
    double Confidence,
    double DescriptorMatchStrength,
    double EntityMatchStrength,
    bool MixedUseRisk,
    bool HasContradictions,
    string WhyItMayMatch,
    string WhyItMayBeWrong,
    string? PrimaryCountryCode,
    IReadOnlyList<string>? AliasCandidates,
    IReadOnlyList<MerchantInvestigationAliasSuggestionPayload>? AliasSuggestions,
    IReadOnlyList<MerchantInvestigationStructuredEvidence>? EvidenceItems,
    bool? DomainNameMismatchRisk = null,
    bool? WeakSourceRisk = null,
    bool? SuspiciousIdentityRisk = null);

public sealed record MerchantInvestigationAliasSuggestionPayload(
    string AliasText,
    string AliasType,
    double Confidence,
    string? Notes,
    bool IsPreferred = false);

public sealed record MerchantInvestigationStructuredEvidence(
    MerchantEvidenceType EvidenceType,
    string SourceClass,
    string Summary,
    double Confidence,
    double Relevance,
    string? SourceReference,
    MerchantSourceTrustLevel? SourceTrustLevel = null);

public sealed record MerchantInvestigationParseResult(
    bool ParsedSuccessfully,
    bool SemanticallyValid,
    bool IsLowTrustValid,
    MerchantInvestigationStructuredResponse? Structured,
    NSFinance.Api.Modules.Banking.Services.MerchantIntelligence.MerchantInvestigationResult InvestigationResult,
    IReadOnlyList<string> ReasonCodes,
    string? FailureReason,
    string? FailureCode);
