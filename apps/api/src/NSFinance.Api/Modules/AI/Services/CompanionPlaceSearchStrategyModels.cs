namespace NSFinance.Api.Modules.AI.Services;

public sealed record CompanionPlaceSearchStrategy(
    string OriginalUserMessage,
    string? CanonicalQuery,
    CompanionPlaceEntityIntent? Entity,
    CompanionPlaceRoleIntent Role,
    IReadOnlyList<CompanionPlaceSearchVariant> SearchVariants,
    IReadOnlyList<string> HardRequirements,
    IReadOnlyList<string> NegativeRequirements,
    IReadOnlyList<string> SoftPreferences,
    IReadOnlyList<string> NonSearchablePreferences,
    CompanionLocationIntent Location,
    string RankingGoal,
    int MaxCandidatePoolSize,
    int MaxVisibleCards,
    double Confidence,
    IReadOnlyList<string> Warnings);

public sealed record CompanionPlaceEntityIntent(
    string? RawEntityText,
    string? CanonicalName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<CompanionEntityRelationshipAlias> RelationshipAliases,
    bool IsBrandOrNamedEntity,
    bool RequiresEntityLock,
    bool VerificationRequired,
    string VerificationStatus,
    double Confidence);

public sealed record CompanionEntityRelationshipAlias(
    string Name,
    string RelationshipType);

public sealed record CompanionPlaceSearchVariant(
    string Query,
    string Purpose,
    bool RequiresEntityMatch,
    bool RequiresRoleMatch,
    double Confidence);

public sealed record CompanionPlaceEntityVerificationResult(
    CompanionPlaceEntityIntent? Entity,
    IReadOnlyList<CompanionPlaceSearchVariant> VerifiedVariants,
    string Status,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public interface ICompanionPlaceSearchStrategyPlanner
{
    Task<CompanionPlaceSearchStrategy> PlanAsync(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CancellationToken cancellationToken);
}

public interface IDeterministicCompanionPlaceSearchStrategyFallback
{
    CompanionPlaceSearchStrategy Plan(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string fallbackReason);
}

public interface ICompanionPlaceSearchStrategyRetryPlanner
{
    Task<CompanionPlaceSearchStrategyRetryResult> TryPlanAsync(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string retryReason,
        CancellationToken cancellationToken);
}

public sealed record CompanionPlaceSearchStrategyRetryResult(
    bool Succeeded,
    CompanionPlaceSearchStrategy? Strategy,
    string? FailureReason);

public interface ICompanionPlacePhrasePreservingFallbackStrategyBuilder
{
    CompanionPlaceSearchStrategy Build(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        string fallbackReason);
}

public interface ICompanionPlaceAmbiguitySafetyClassifier
{
    CompanionPlaceAmbiguitySafetyResult Apply(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy strategy);
}

public sealed record CompanionPlaceAmbiguitySafetyResult(
    CompanionPlaceSearchStrategy Strategy,
    bool Applied,
    IReadOnlyList<string> ReasonCodes);

public interface ICompanionPlaceSearchStrategyPromptBuilder
{
    PromptBuildResult BuildPrompt(UserChatRequest request, CompanionSemanticIntent intent);
}

public interface ICompanionPlaceSearchStrategyJsonParser
{
    bool TryParse(
        AIResponse response,
        UserChatRequest request,
        CompanionSemanticIntent intent,
        out CompanionPlaceSearchStrategy? strategy,
        out IReadOnlyList<string> reasonCodes,
        out string? failureReason);
}

public interface ICompanionPlaceSearchStrategySanitizer
{
    CompanionPlaceSearchStrategy Sanitize(
        UserChatRequest request,
        CompanionSemanticIntent intent,
        CompanionPlaceSearchStrategy strategy);
}

public interface ICompanionPlaceEntityVerificationService
{
    Task<CompanionPlaceEntityVerificationResult> VerifyAsync(
        CompanionPlaceSearchStrategy strategy,
        CancellationToken cancellationToken);
}

public interface ICompanionPlaceSearchVariantValidator
{
    IReadOnlyList<CompanionPlaceSearchVariant> Validate(
        CompanionPlaceSearchStrategy strategy);
}

public interface ICompanionPlaceTypeFamilyClassifier
{
    IReadOnlySet<string> ClassifyFamilies(CompanionPlacePoolCandidate candidate);
}
