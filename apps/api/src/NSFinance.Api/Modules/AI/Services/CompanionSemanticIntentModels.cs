using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionSemanticIntent(
    string IntentFamily,
    string ActionKind,
    string? PlaceQuery,
    string? BrandOrEntity,
    CompanionLocationIntent Location,
    CompanionPlaceRoleIntent Role,
    IReadOnlyList<string> HardFilters,
    IReadOnlyList<string> NegativeFilters,
    IReadOnlyList<string> SoftPreferences,
    IReadOnlyList<string> NonSearchablePreferences,
    IReadOnlyList<string> RequestedDetailFields,
    string RankingGoal,
    int? RequestedMaxResults,
    double Confidence,
    IReadOnlyList<string> Ambiguities);

public sealed record CompanionPlaceRoleIntent(
    string? RequestedRole,
    IReadOnlyList<string> RequiredCoreRoles,
    IReadOnlyList<string> AcceptableSubRoles,
    IReadOnlyList<string> ExcludedSiblingRoles,
    IReadOnlyList<string> Modifiers,
    string CategoryStrictness);

public sealed record CompanionLocationIntent(
    string Mode,
    string? AreaText,
    double? Latitude,
    double? Longitude,
    bool RequiresLocation);

public sealed record CompanionPlacePoolCandidate(
    string PlaceId,
    string DisplayName,
    string? PrimaryType,
    string? PrimaryTypeDisplayName,
    IReadOnlyList<string> Types,
    double? Latitude,
    double? Longitude,
    double? DistanceMeters,
    string? ShortFormattedAddress,
    double? Rating,
    int? UserRatingCount,
    string? PriceLevel,
    bool? OpenNow,
    IReadOnlyDictionary<string, string> LightweightAttributes);

public sealed record CompanionPlaceCandidatePoolResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<string> QueryPasses,
    IReadOnlyList<string> Diagnostics,
    bool UsedCache,
    string? FailureReason);

public sealed record CompanionPlaceLocationBoundaryPlan(
    string BoundaryMode,
    string? RawLocationText,
    string? CanonicalLocationText,
    string? CountryCode,
    string? RegionCode,
    string? City,
    string? District,
    string? County,
    double? CenterLatitude,
    double? CenterLongitude,
    double? RadiusMeters,
    IReadOnlyList<string> AddressMustContain,
    IReadOnlyList<string> AddressShouldContain,
    IReadOnlyList<string> AddressMustNotContain,
    bool HardBoundary,
    bool NeedsGeocoding,
    double Confidence,
    IReadOnlyList<string> Warnings);

public sealed record CompanionPlaceLocationBoundaryDecision(
    string PlaceId,
    bool IsInsideBoundary,
    double Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record CompanionPlaceRetrievalPlan(
    IReadOnlyList<CompanionPlaceRetrievalPass> Passes,
    int TargetCandidateCount,
    int ProviderPageSize,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionPlaceRetrievalPass(
    string PassId,
    string Mode,
    string? Query,
    IReadOnlyList<string> IncludedTypes,
    double? Latitude,
    double? Longitude,
    double? RadiusMeters,
    string? CountryCode,
    bool RequiresLocation,
    string Purpose);

public sealed record CompanionPlaceRoleCompatibilityDecision(
    string PlaceId,
    bool Keep,
    string Status,
    double Confidence,
    IReadOnlyList<string> MatchedRoles,
    IReadOnlyList<string> ConflictingRoles,
    IReadOnlyList<string> Evidence,
    bool NeedsDetails);

public sealed record CompanionPlaceRejectedCandidate(
    string PlaceId,
    string DisplayName,
    string Reason);

public sealed record CompanionPlaceConstraintResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<CompanionPlaceRejectedCandidate> Rejected,
    IReadOnlyList<string> AppliedHardFilters,
    IReadOnlyList<string> AppliedSoftPreferences,
    IReadOnlyList<string> NonSearchablePreferences,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionPlaceIntelligenceRankingResult(
    IReadOnlyList<CompanionPlacePoolCandidate> RankedCandidates,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionPlaceFinalistResult(
    CompanionStructuredResults? StructuredResults,
    IReadOnlyList<CompanionPlacePoolCandidate> Finalists,
    IReadOnlyList<string> Diagnostics,
    int EnrichedCount);

public sealed record CompanionPlaceSearchContext(
    CompanionSemanticIntent Intent,
    IReadOnlyList<CompanionPlacePoolCandidate> CandidatePool,
    CompanionStructuredResults? VisibleCards,
    ResultContextSnapshot? ResultContext);

public interface ICompanionSemanticIntentService
{
    CompanionSemanticIntent Build(
        UserChatRequest request,
        ConversationStateSnapshot state,
        ResultContextSnapshot? resultContext,
        TurnInterpretationV2? interpretation,
        PlaceRetrievalPlanV1? retrievalPlan,
        ConversationIntelligenceResult? intelligence,
        CompanionResolvedAction? resolvedAction);
}

public interface ICompanionPlaceCandidatePoolService
{
    Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CancellationToken cancellationToken);

    Task<CompanionPlaceCandidatePoolResult> BuildPoolAsync(
        CompanionSemanticIntent intent,
        UserChatRequest request,
        CompanionPlaceSearchStrategy strategy,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceLocationBoundaryService
{
    CompanionPlaceLocationBoundaryPlan CreatePlan(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy? strategy = null);
}

public interface ICompanionPlaceLocationBoundaryFilter
{
    IReadOnlyList<CompanionPlacePoolCandidate> Apply(
        CompanionPlaceLocationBoundaryPlan plan,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public interface ICompanionPlaceRetrievalPlanner
{
    CompanionPlaceRetrievalPlan Build(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy? strategy,
        CompanionPlaceLocationBoundaryPlan? boundaryPlan = null);
}

public interface ICompanionPlaceConstraintEngine
{
    CompanionPlaceConstraintResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public interface ICompanionPlaceIntelligenceRankingService
{
    CompanionPlaceIntelligenceRankingResult Rank(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public interface ICompanionPlaceFinalistEnrichmentService
{
    Task<CompanionPlaceFinalistResult> EnrichAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        int maxCards,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceSessionMemoryService
{
    Task SaveSearchContextAsync(
        UserChatRequest request,
        ConversationStateSnapshot state,
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidatePool,
        CompanionStructuredResults? visibleCards,
        CancellationToken cancellationToken);

    Task<CompanionPlaceSearchContext?> LoadActiveSearchContextAsync(
        UserChatRequest request,
        ResultContextSnapshot? activeResultContext,
        CancellationToken cancellationToken);
}

public interface IPlacesShortLivedCache
{
    Task<T?> GetAsync<T>(string provider, string placeId, string fieldMaskHash, CancellationToken ct);
    Task SetAsync<T>(string provider, string placeId, string fieldMaskHash, T payload, TimeSpan ttl, CancellationToken ct);
}

public interface IPlaceRegistryService
{
    Task RegisterSeenAsync(
        string provider,
        string providerPlaceId,
        IReadOnlyList<string> internalTags,
        CancellationToken cancellationToken);
}

public sealed record CompanionPlaceResultContextBinding(
    ResultContextSnapshot? Context,
    string Source,
    string Reason,
    bool ClientContextWasStale);

public interface ICompanionPlaceResultContextBinder
{
    CompanionPlaceResultContextBinding Bind(
        UserChatRequest request,
        ResultContextReadResult readResult,
        ResultContextSnapshot? latestPlacesV2Context,
        CompanionSemanticIntent currentIntent);
}

public sealed record CompanionParkingEvidence(
    string PlaceId,
    string EvidenceLevel,
    double Confidence,
    string? NearestParkingPlaceId,
    double? NearestParkingDistanceMeters,
    IReadOnlyList<string> Reasons);

public sealed record CompanionParkingEvidenceResult(
    IReadOnlyDictionary<string, CompanionParkingEvidence> EvidenceByPlaceId,
    IReadOnlyList<string> QueryPasses,
    IReadOnlyList<string> Diagnostics);

public interface ICompanionPlaceParkingEvidenceService
{
    Task<CompanionParkingEvidenceResult> EvaluateAsync(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceDuplicateClusterService
{
    IReadOnlyList<CompanionPlacePoolCandidate> Cluster(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates);
}

public sealed record CompanionCategoryCompatibilityResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<CompanionPlaceRejectedCandidate> Rejected,
    IReadOnlyList<string> Diagnostics);

public interface ICompanionPlaceCategoryCompatibilityService
{
    CompanionCategoryCompatibilityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CompanionPlaceSearchStrategy? strategy = null);
}

public sealed record CompanionBrandIdentityResult(
    IReadOnlyList<CompanionPlacePoolCandidate> Candidates,
    IReadOnlyList<CompanionPlaceRejectedCandidate> Rejected,
    IReadOnlyList<string> Diagnostics);

public interface ICompanionPlaceBrandIdentityService
{
    CompanionBrandIdentityResult Apply(
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CompanionPlaceSearchStrategy? strategy = null);
}

public enum CompanionGuardEvidenceStatus
{
    ConfirmedMatch,
    LikelyMatch,
    Unknown,
    LikelyConflict,
    ConfirmedConflict
}

public sealed record CompanionGuardEvidence(
    string GuardId,
    string CandidatePlaceId,
    CompanionGuardEvidenceStatus Status,
    double Confidence,
    IReadOnlyList<string> EvidenceFields,
    IReadOnlyList<string> Reasons,
    bool RequiresDetailsEnrichment);

public sealed record CompanionGuardEvaluationResult(
    IReadOnlyDictionary<string, IReadOnlyList<CompanionGuardEvidence>> EvidenceByPlaceId,
    IReadOnlyList<string> AppliedGuardIds,
    IReadOnlyList<string> Diagnostics);

public sealed record CompanionAmbiguityGuardDefinition(
    string GuardId,
    string Domain,
    IReadOnlyList<string> RequestedConcepts,
    IReadOnlyList<string> DangerousSiblingConcepts,
    IReadOnlyList<string> CompatibleConcepts,
    IReadOnlyList<string> EvidenceFields,
    string DefaultAction,
    bool RequiresDetails,
    double Confidence,
    IReadOnlyList<string> Examples,
    string? Notes);

public interface ICompanionAmbiguityGuardCatalogueProvider
{
    IReadOnlyList<CompanionAmbiguityGuardDefinition> GetAll();
}

public interface ICompanionAmbiguityGuardMatcher
{
    IReadOnlyList<CompanionAmbiguityGuardDefinition> Match(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent);
}

public interface ICompanionPlaceGuardEvidenceService
{
    Task<CompanionGuardEvaluationResult> EvaluateAsync(
        CompanionPlaceSearchStrategy strategy,
        CompanionSemanticIntent intent,
        IReadOnlyList<CompanionPlacePoolCandidate> candidates,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceGuardAwareFilter
{
    IReadOnlyList<CompanionPlacePoolCandidate> Apply(
        CompanionPlaceSearchStrategy? strategy,
        IReadOnlyList<CompanionPlacePoolCandidate> rankedCandidates,
        CompanionGuardEvaluationResult evidence);
}
