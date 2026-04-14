using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.DTOs;

public sealed record ChatTurnDto(
    string Role,
    string Content,
    DateTime? TimestampUtc,
    string? Topic,
    bool IsResolved);

public sealed record ChatStateDto(
    string? ActiveTopic,
    string? UserIntent,
    Dictionary<string, string>? Constraints,
    IReadOnlyList<string>? Summaries,
    string? BudgetPreference,
    string? LocationPreference,
    string? MerchantInvestigationSubject,
    IReadOnlyList<string>? RecentConclusions);

public sealed record SendChatMessageRequest(
    string Message,
    string ClientRequestId,
    Guid? ConversationThreadId,
    bool RequirePersistentMemory = false,
    bool AllowFallbackOnPersistentFailure = false,
    ChatStateDto? State = null,
    IReadOnlyList<ChatTurnDto>? RecentTurns = null,
    Dictionary<string, string>? Metadata = null,
    string? CorrelationId = null);

public sealed record SendChatMessageResponse(
    Guid? ConversationThreadId,
    Guid? TurnId,
    string Status,
    string Message,
    string ModelUsed,
    string ReasoningClass,
    bool Succeeded,
    bool Deduped,
    bool InProgress,
    bool FallbackUsed,
    string? FailureCode,
    string? FailureReason,
    IReadOnlyDictionary<string, string> SuggestedStateUpdates,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> FollowUpIntentHints,
    string? ContextSummary);

public sealed record ChatThreadSummaryDto(
    Guid ThreadId,
    string? Title,
    string Status,
    DateTime StartedUtc,
    DateTime LastMessageUtc,
    DateTime? LastContextRefreshUtc,
    int ActiveSummaryVersion);

public sealed record ChatThreadDetailResponse(
    ChatThreadSummaryDto Thread,
    IReadOnlyList<ChatMessageDto> Messages);

public sealed record ChatMessageDto(
    Guid MessageId,
    Guid? TurnId,
    string Role,
    string Content,
    int MessageOrder,
    string? Topic,
    string? ModelUsed,
    string? TaskType,
    bool IsResolved,
    DateTime CreatedUtc);

public sealed record ArchiveChatThreadResponse(
    Guid ThreadId,
    string Status);

public sealed record MerchantInvestigationTestRequest(
    string RawDescriptor,
    string? NormalizedDescriptor = null,
    string? TriggerSource = null,
    bool DryRun = true,
    string? ProviderContext = null,
    string? CountryHint = null,
    decimal? Amount = null,
    string? Currency = null);

public sealed record MerchantInvestigationTestResponse(
    bool DryRun,
    string NormalizedDescriptor,
    bool Succeeded,
    bool InsufficientEvidence,
    string Recommendation,
    double OverallConfidence,
    double AmbiguityLevel,
    bool ParserRejected,
    string? FailureReason,
    string AcceptanceDecision,
    double AcceptanceConfidence,
    IReadOnlyList<string> AcceptanceReasonCodes,
    IReadOnlyList<string> InvestigationReasonCodes,
    IReadOnlyList<MerchantInvestigationCandidateDto> Candidates,
    IReadOnlyList<MerchantInvestigationEvidenceDto> Evidence,
    IReadOnlyList<MerchantInvestigationAliasSuggestionDto> AliasSuggestions);

public sealed record MerchantInvestigationCandidateDto(
    Guid? ExistingMerchantId,
    string CanonicalName,
    string DisplayName,
    string MerchantType,
    string MerchantUsageType,
    string PrimaryCountryCode,
    double Confidence,
    double DescriptorMatchStrength,
    double EntityMatchStrength,
    bool MixedUseRisk,
    bool HasContradictions,
    bool DomainNameMismatchRisk,
    bool WeakSourceRisk,
    bool SuspiciousIdentityRisk,
    string WhyItMayMatch,
    string WhyItMayBeWrong,
    string? OfficialWebsite,
    string? DescriptionSummary);

public sealed record MerchantInvestigationEvidenceDto(
    string EvidenceType,
    string Summary,
    double Confidence,
    string? SourceReference,
    string? SourceClass,
    double Relevance,
    string SourceTrustLevel);

public sealed record MerchantInvestigationAliasSuggestionDto(
    string AliasText,
    string AliasType,
    double Confidence,
    string? Notes,
    bool IsPreferred);
