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
    bool IsBrandOrNamedEntity,
    bool RequiresEntityLock,
    bool VerificationRequired,
    string VerificationStatus,
    double Confidence);

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
