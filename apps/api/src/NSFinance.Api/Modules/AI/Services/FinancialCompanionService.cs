using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialCompanionService
{
    Task<FinancialCompanionResponse> ExecuteAsync(
        FinancialCompanionRequest request,
        CancellationToken cancellationToken);
}

public sealed class FinancialCompanionService(
    AppDbContext dbContext,
    IUserFinancialContextProfileService profileService,
    ICompanionIntentRouter intentRouter,
    IFinancialCompanionContextAssembler contextAssembler,
    ICompanionPlacesResponseBuilder placesResponseBuilder,
    IFinancialAdviceDecisionService adviceDecisionService,
    IOptions<CompanionAISettingsOptions> options,
    ILogger<FinancialCompanionService> logger) : IFinancialCompanionService
{
    private const string CompanionLogTemplate =
        "[AI_COMPANION] userId={UserId} sessionId={SessionId} intent={Intent} "
        + "tools_used={ToolsUsed} tokens_input={TokensInput} tokens_output={TokensOutput} "
        + "model={Model} response_time={ResponseTimeMs}";

    private readonly CompanionAISettingsOptions _settings = options.Value;

    public async Task<FinancialCompanionResponse> ExecuteAsync(
        FinancialCompanionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.Enabled)
        {
            return new FinancialCompanionResponse(
                ReplyText: "Financial companion is currently disabled.",
                Intent: FinancialCompanionIntent.GeneralFinancialQuestion,
                ToolsUsed: [],
                Warnings: ["companion_disabled"],
                Succeeded: false,
                FailureReason: "companion_disabled",
                ModelUsed: "none",
                InputTokens: 0,
                OutputTokens: 0,
                Evidence: null,
                HasInsufficientData: true,
                InsufficientDataReasons: ["companion_disabled"],
                AdvicePacket: null);
        }

        var nowUtc = DateTime.UtcNow;
        var routing = intentRouter.Route(request.UserQuery);
        var responseIntent = routing.IntentFamily;
        var warnings = new List<string>(4);
        warnings.AddRange(routing.ReasonCodes.Select(code => $"route_{code}"));
        if (routing.IsAmbiguous)
        {
            warnings.Add("intent_ambiguous");
        }

        if (routing.IsUnsupported)
        {
            warnings.Add("intent_unsupported");
        }

        var profile = await profileService.GetOrCreateAsync(request.UserId, cancellationToken);

        var dailyCount = await dbContext.CompanionAIInteractionLogs
            .AsNoTracking()
            .CountAsync(
                x => x.UserId == request.UserId
                     && x.CreatedUtc >= nowUtc.AddHours(-24),
                cancellationToken);
        var modelClass = _settings.PreferredModelClass;
        if (dailyCount >= Math.Max(1, _settings.DailySoftCapPerUser))
        {
            warnings.Add("daily_soft_cap_reached");
            modelClass = _settings.SoftCapFallbackModelClass;
            if (_settings.EnforceDailySoftCap)
            {
                return await PersistAndReturnAsync(
                    request,
                    responseIntent,
                    toolsUsed: [],
                    warnings,
                    replyText: "You've reached your daily AI guidance cap. Please try again tomorrow.",
                    modelUsed: "soft_cap_block",
                    responseTimeMs: 0,
                    tokensInput: 0,
                    tokensOutput: 0,
                    succeeded: false,
                    failureReason: "daily_soft_cap_reached",
                    evidence: null,
                    hasInsufficientData: true,
                    insufficientDataReasons: ["daily_soft_cap_reached"],
                    advicePacket: null,
                    cancellationToken);
            }
        }

        var assembly = await contextAssembler.AssembleAsync(request, routing, profile, cancellationToken);
        warnings.AddRange(assembly.Warnings.Select(x => $"orchestration_{x}"));

        if (!assembly.CanProceedToAI)
        {
            var insufficientReasons = assembly.InsufficientDataReasons.Count == 0
                ? ["insufficient_grounding_data"]
                : assembly.InsufficientDataReasons;
            warnings.AddRange(insufficientReasons.Select(x => $"insufficient_{x}"));
            var fallback = BuildInsufficientDataReply(routing, insufficientReasons);
            var succeeded = routing.IsAmbiguous || routing.IsUnsupported;
            var failureReason = succeeded ? null : "insufficient_grounding_data";
            return await PersistAndReturnAsync(
                request,
                responseIntent,
                assembly.ToolsUsed,
                warnings,
                fallback,
                modelUsed: "orchestration_fallback",
                responseTimeMs: 0,
                tokensInput: 0,
                tokensOutput: 0,
                succeeded: succeeded,
                failureReason: failureReason,
                evidence: ToResponseEvidence(assembly.Evidence),
                hasInsufficientData: true,
                insufficientDataReasons: insufficientReasons,
                advicePacket: null,
                cancellationToken);
        }

        if (ShouldUsePlacesResponsePath(routing))
        {
            var placesResponse = placesResponseBuilder.Build(assembly.Context);
            warnings.AddRange(placesResponse.Warnings);
            var placesEvidence = ToResponseEvidence(assembly.Evidence);
            if (placesEvidence is not null)
            {
                placesEvidence = placesEvidence with
                {
                    BasisSummary = placesEvidence.BasisSummary
                        .Concat(placesResponse.BasisSummaryAdditions)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    ExecutionWarnings = (placesEvidence.ExecutionWarnings ?? [])
                        .Concat(placesResponse.Warnings)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                };
            }

            return await PersistAndReturnAsync(
                request,
                responseIntent,
                assembly.ToolsUsed,
                warnings,
                placesResponse.ReplyText,
                modelUsed: "places_grounded_response",
                responseTimeMs: 0,
                tokensInput: 0,
                tokensOutput: 0,
                succeeded: placesResponse.Succeeded,
                failureReason: placesResponse.Succeeded
                    ? null
                    : "local_places_grounding_unavailable",
                evidence: placesEvidence,
                hasInsufficientData: placesResponse.HasInsufficientData || assembly.HasInsufficientData,
                insufficientDataReasons: placesResponse.HasInsufficientData
                    ? placesResponse.InsufficientDataReasons
                    : assembly.InsufficientDataReasons,
                advicePacket: null,
                cancellationToken);
        }

        var start = DateTime.UtcNow;
        var decision = await adviceDecisionService.DecideAsync(
            request,
            routing,
            assembly.Context,
            modelClass,
            cancellationToken);
        var elapsedMs = (long)Math.Max(0d, (DateTime.UtcNow - start).TotalMilliseconds);

        warnings.AddRange(decision.Warnings);
        if (assembly.HasInsufficientData)
        {
            warnings.AddRange(assembly.InsufficientDataReasons.Select(x => $"insufficient_{x}"));
        }

        var safeReply = decision.Packet.UserSafeSummary;
        return await PersistAndReturnAsync(
            request,
            responseIntent,
            assembly.ToolsUsed,
            warnings,
            safeReply,
            decision.ModelUsed,
            elapsedMs,
            decision.InputTokens,
            decision.OutputTokens,
            succeeded: true,
            failureReason: null,
            evidence: ToResponseEvidence(assembly.Evidence),
            hasInsufficientData: assembly.HasInsufficientData,
            insufficientDataReasons: assembly.InsufficientDataReasons,
            advicePacket: decision.Packet,
            cancellationToken);
    }

    private static CompanionResponseEvidence? ToResponseEvidence(CompanionContextEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }

        return new CompanionResponseEvidence(
            ToolsUsed: evidence.ToolsUsed,
            RequiredToolsUsed: evidence.RequiredToolsUsed,
            OptionalToolsUsed: evidence.OptionalToolsUsed,
            MissingRequiredTools: evidence.MissingRequiredTools,
            BasisSummary: evidence.BasisSummary,
            SkippedTools: evidence.SkippedTools,
            PlannedTools: evidence.PlannedTools,
            TrimmedPayloadIndicators: evidence.TrimmedPayloadIndicators,
            InsufficiencySummary: evidence.InsufficiencySummary,
            ExecutionWarnings: evidence.ExecutionWarnings);
    }

    private static string BuildInsufficientDataReply(
        CompanionIntentRoutingResult routing,
        IReadOnlyList<string> reasons)
    {
        if (routing.IsUnsupported)
        {
            return
                "I can help with budgeting, affordability, spending, and savings guidance, "
                + "but this request is outside my supported scope.";
        }

        if (routing.IsAmbiguous)
        {
            return
                "I need a bit more detail to help. You can ask about budget status, spending "
            + "analysis, affordability, or where to cut back.";
        }

        if (reasons.Any(reason => reason.Contains("places_search", StringComparison.Ordinal)))
        {
            if (reasons.Any(reason => reason.Contains("required_tool_returned_no_data:places_search", StringComparison.Ordinal)))
            {
                return
                    "I couldn't find matching places for that request right now. "
                    + "Try a nearby area or a more specific place type.";
            }

            return
                "I couldn't retrieve grounded place results right now. "
                + "Please try again in a moment.";
        }

        if (reasons.Any(reason => reason.Contains("local_places_intent_missing_places_grounding", StringComparison.Ordinal)))
        {
            return
                "I don't have enough place-grounding data yet to answer that reliably. "
                + "Please try the request again shortly.";
        }

        if (reasons.Any(reason => reason.Contains("financial_summary", StringComparison.Ordinal)))
        {
            return "I don't have enough grounded financial summary data yet to answer that reliably.";
        }

        if (reasons.Any(reason => reason.Contains("budget_status", StringComparison.Ordinal)))
        {
            return "I don't have enough grounded budget data yet to answer that reliably.";
        }

        return
            "I don't have enough grounded data yet to answer that reliably. "
            + "I can provide a partial answer once more data is available.";
    }

    private static bool ShouldUsePlacesResponsePath(CompanionIntentRoutingResult routing)
    {
        return routing.PrimaryIntent == FinancialCompanionIntent.LocalPlacesOutings
               || routing.IntentFamily == FinancialCompanionIntent.LocalPlacesOutings;
    }

    private async Task<FinancialCompanionResponse> PersistAndReturnAsync(
        FinancialCompanionRequest request,
        FinancialCompanionIntent intent,
        IReadOnlyList<string> toolsUsed,
        IReadOnlyList<string> warnings,
        string replyText,
        string modelUsed,
        long responseTimeMs,
        int tokensInput,
        int tokensOutput,
        bool succeeded,
        string? failureReason,
        CompanionResponseEvidence? evidence,
        bool hasInsufficientData,
        IReadOnlyList<string>? insufficientDataReasons,
        FinancialAdviceDecisionPacket? advicePacket,
        CancellationToken cancellationToken)
    {
        var toolsUsedText = toolsUsed.Count == 0 ? "none" : string.Join(",", toolsUsed);
        logger.LogInformation(
            CompanionLogTemplate,
            request.UserId,
            request.SessionId,
            intent,
            toolsUsedText,
            tokensInput,
            tokensOutput,
            modelUsed,
            responseTimeMs);

        dbContext.CompanionAIInteractionLogs.Add(new CompanionAIInteractionLog
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "default" : request.SessionId,
            Intent = intent.ToString(),
            ToolsUsed = toolsUsedText,
            TokensInput = tokensInput,
            TokensOutput = tokensOutput,
            Model = string.IsNullOrWhiteSpace(modelUsed) ? "unknown_model" : modelUsed,
            ResponseTimeMs = responseTimeMs,
            Succeeded = succeeded,
            FailureReason = failureReason,
            CreatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new FinancialCompanionResponse(
            ReplyText: replyText,
            Intent: intent,
            ToolsUsed: toolsUsed,
            Warnings: warnings,
            Succeeded: succeeded,
            FailureReason: failureReason,
            ModelUsed: modelUsed,
            InputTokens: tokensInput,
            OutputTokens: tokensOutput,
            Evidence: evidence,
            HasInsufficientData: hasInsufficientData,
            InsufficientDataReasons: insufficientDataReasons,
            AdvicePacket: advicePacket);
    }
}
