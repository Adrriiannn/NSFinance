using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed record MerchantCreateRequest(
    string CanonicalName,
    string DisplayName,
    MerchantStatus MerchantStatus,
    MerchantType MerchantType,
    MerchantUsageType MerchantUsageType,
    string PrimaryCountryCode,
    string? OfficialWebsite,
    string? DescriptionSummary,
    Guid? ParentMerchantId);

public sealed record MerchantUpdateRequest(
    Guid MerchantId,
    string? DisplayName,
    MerchantStatus? MerchantStatus,
    MerchantType? MerchantType,
    MerchantUsageType? MerchantUsageType,
    string? PrimaryCountryCode,
    string? OfficialWebsite,
    string? DescriptionSummary,
    Guid? ParentMerchantId);

public sealed record MerchantAliasCreateRequest(
    Guid MerchantId,
    string AliasText,
    MerchantAliasType AliasType,
    double Confidence,
    bool IsExactMatchPreferred,
    string Source,
    bool IsActive,
    MerchantAliasTrustLevel TrustLevel = MerchantAliasTrustLevel.Observed,
    string? LifecycleReason = null);

public sealed record MerchantEvidenceCreateRequest(
    Guid MerchantId,
    MerchantEvidenceType EvidenceType,
    string EvidenceSummary,
    double Confidence,
    string? SourceReference);

public sealed record MerchantCategoryHintCreateRequest(
    Guid MerchantId,
    int DomainId,
    int CategoryId,
    int? SubcategoryId,
    double Confidence,
    MerchantHintStrength HintStrength,
    string Source,
    bool IsActive);

public sealed record MerchantBehaviorProfileUpsertRequest(
    Guid MerchantId,
    bool SupportsSubscriptions,
    bool SupportsRecurringPayments,
    bool SupportsOneTimePurchases,
    bool SupportsMarketplacePayments,
    bool SupportsInAppPurchases,
    bool AnnualRenewalsCommon,
    bool RefundsCommon,
    bool MixedUseRisk,
    double PaymentBehaviorConfidence,
    string BehaviorSummary);

public sealed record MerchantIntelligencePackage(
    Merchant Merchant,
    IReadOnlyList<MerchantAlias> Aliases,
    MerchantBehaviorProfile? BehaviorProfile,
    IReadOnlyList<MerchantCategoryHint> CategoryHints,
    IReadOnlyList<MerchantEvidence> Evidence);

public sealed record MerchantResolutionResult(
    Guid? MerchantId,
    double ResolutionConfidence,
    MerchantResolutionType ResolutionType,
    string? MatchedAlias,
    bool IsResolved,
    Guid? UnresolvedMerchantId,
    string NormalizedDescriptor,
    MerchantAcceptanceDecisionType? AcceptanceDecisionType,
    IReadOnlyList<string> ReasonCodes,
    MerchantResolutionFinalState FinalState = MerchantResolutionFinalState.Unresolved,
    DomainTriggerMode? TriggerMode = null,
    AITriggerSkipReason? AIGateSkipReason = null,
    bool AIGateDecision = false,
    string? ModelUsed = null);

public sealed record MerchantAcceptanceDecision(
    MerchantAcceptanceDecisionType DecisionType,
    double Confidence,
    MerchantInvestigationCandidate? SelectedCandidate,
    IReadOnlyList<string> ReasonCodes);

public sealed record MerchantInvestigationRequest(
    string RawDescriptor,
    string NormalizedDescriptor,
    string TriggerSource);

public sealed record MerchantInvestigationResult(
    bool Succeeded,
    bool InsufficientEvidence,
    IReadOnlyList<MerchantInvestigationCandidate> Candidates,
    IReadOnlyList<MerchantInvestigationEvidence> Evidence,
    string? FailureReason,
    MerchantInvestigationRecommendation Recommendation = MerchantInvestigationRecommendation.Unresolved,
    double OverallConfidence = 0d,
    double AmbiguityLevel = 1d,
    IReadOnlyList<MerchantInvestigationAliasSuggestion>? AliasSuggestions = null,
    IReadOnlyList<string>? InvestigationReasonCodes = null,
    bool ParserRejected = false);

public sealed record MerchantInvestigationCandidate(
    Guid? ExistingMerchantId,
    string CanonicalName,
    string DisplayName,
    MerchantType MerchantType,
    MerchantUsageType MerchantUsageType,
    string PrimaryCountryCode,
    double Confidence,
    double AmbiguityScore,
    bool MixedUseRisk,
    bool HasContradictions,
    string? OfficialWebsite,
    string? DescriptionSummary,
    IReadOnlyList<string> AliasCandidates,
    string WhyItMayMatch = "",
    string WhyItMayBeWrong = "",
    double DescriptorMatchStrength = 0d,
    double EntityMatchStrength = 0d,
    IReadOnlyList<MerchantInvestigationAliasSuggestion>? AliasSuggestions = null,
    IReadOnlyList<MerchantInvestigationEvidence>? EvidenceItems = null,
    bool DomainNameMismatchRisk = false,
    bool WeakSourceRisk = false,
    bool SuspiciousIdentityRisk = false);

public sealed record MerchantInvestigationEvidence(
    MerchantEvidenceType EvidenceType,
    string EvidenceSummary,
    double Confidence,
    string? SourceReference,
    string? SourceClass = null,
    double Relevance = 0d,
    MerchantSourceTrustLevel SourceTrustLevel = MerchantSourceTrustLevel.Unknown);

public sealed record MerchantInvestigationAliasSuggestion(
    string AliasText,
    string AliasType,
    double Confidence,
    string? Notes,
    bool IsPreferred = false);
