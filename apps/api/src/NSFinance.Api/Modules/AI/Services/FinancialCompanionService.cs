using System.Text.Json;
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
    IAIModelRouter modelRouter,
    IAIClient aiClient,
    IUserChatResponseParser responseParser,
    IOptions<CompanionAISettingsOptions> options,
    ILogger<FinancialCompanionService> logger) : IFinancialCompanionService
{
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
                InsufficientDataReasons: ["companion_disabled"]);
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
                cancellationToken);
        }

        var context = assembly.Context;
        var route = modelRouter.Resolve(
            AITaskType.FinancialReasoning,
            modelClass,
            complexityHint: $"{routing.IntentFamily}:{routing.PrimaryIntent}");

        var prompt = BuildPrompt(request.UserQuery, context);
        var aiRequest = AIRequest.Create(
            taskType: AITaskType.FinancialReasoning,
            preferredModelClass: route.ModelClass,
            messages: [AIMessage.User(prompt)],
            correlationId: string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId,
            systemInstructions: """
                                You are NSFinance Companion AI.
                                Use only provided tool context.
                                Never invent user financial numbers.
                                Avoid aggressive guidance and avoid proposing cuts to essential spending categories.
                                Return strict JSON matching user_chat_response_v1.
                                """,
            structuredOutputSchemaName: "user_chat_response_v1",
            temperature: 0.2d,
            maxOutputTokens: Math.Clamp(_settings.MaxTokensPerResponse, 120, 2_000),
            metadata: request.Metadata);

        var start = DateTime.UtcNow;
        var aiResponse = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        var elapsedMs = (long)Math.Max(0d, (DateTime.UtcNow - start).TotalMilliseconds);

        if (!responseParser.TryParse(aiResponse, route, out var parsed, out var parseReasonCodes))
        {
            warnings.AddRange(parseReasonCodes);
            var fallback = "I couldn't build a reliable answer from current data. Try rephrasing your question.";
            return await PersistAndReturnAsync(
                request,
                responseIntent,
                assembly.ToolsUsed,
                warnings,
                fallback,
                route.Model,
                elapsedMs,
                aiResponse.InputTokenEstimate ?? 0,
                aiResponse.OutputTokenEstimate ?? 0,
                succeeded: false,
                failureReason: parsed.FailureReason ?? aiResponse.FailureReason ?? "companion_parse_failed",
                evidence: ToResponseEvidence(assembly.Evidence),
                hasInsufficientData: assembly.HasInsufficientData,
                insufficientDataReasons: assembly.InsufficientDataReasons,
                cancellationToken);
        }

        warnings.AddRange(parsed.Warnings);
        if (assembly.HasInsufficientData)
        {
            warnings.AddRange(assembly.InsufficientDataReasons.Select(x => $"insufficient_{x}"));
        }

        var safeReply = ApplySafetyPostProcessing(parsed.ReplyText, context.ToolOutputs);
        return await PersistAndReturnAsync(
            request,
            responseIntent,
            assembly.ToolsUsed,
            warnings,
            safeReply,
            route.Model,
            elapsedMs,
            aiResponse.InputTokenEstimate ?? 0,
            aiResponse.OutputTokenEstimate ?? 0,
            succeeded: true,
            failureReason: null,
            evidence: ToResponseEvidence(assembly.Evidence),
            hasInsufficientData: assembly.HasInsufficientData,
            insufficientDataReasons: assembly.InsufficientDataReasons,
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
            SkippedTools: evidence.SkippedTools);
    }

    private static string BuildInsufficientDataReply(
        CompanionIntentRoutingResult routing,
        IReadOnlyList<string> reasons)
    {
        if (routing.IsUnsupported)
        {
            return "I can help with budgeting, affordability, spending, and savings guidance, but this request is outside my supported scope.";
        }

        if (routing.IsAmbiguous)
        {
            return "I need a bit more detail to help. You can ask about budget status, spending analysis, affordability, or where to cut back.";
        }

        if (reasons.Any(reason => reason.Contains("financial_summary", StringComparison.Ordinal)))
        {
            return "I don't have enough grounded financial summary data yet to answer that reliably.";
        }

        if (reasons.Any(reason => reason.Contains("budget_status", StringComparison.Ordinal)))
        {
            return "I don't have enough grounded budget data yet to answer that reliably.";
        }

        return "I don't have enough grounded data yet to answer that reliably. I can provide a partial answer once more data is available.";
    }

    private static string BuildPrompt(string userQuery, FinancialCompanionContext context)
    {
        var contextJson = JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        return $$"""
                 UserQuery: {{userQuery}}
                 ToolContextJson:
                 {{contextJson}}
                 Respond with strict JSON:
                 {
                   "replyText": "string",
                   "referencedContextSummary": "string|null",
                   "suggestedStructuredStateUpdates": {},
                   "warnings": [],
                   "followUpIntentHints": []
                 }
                 """;
    }

    private static string ApplySafetyPostProcessing(string replyText, IReadOnlyDictionary<string, object?> toolOutputs)
    {
        if (string.IsNullOrWhiteSpace(replyText))
        {
            return "I don't have enough grounded data yet to answer safely.";
        }

        var result = replyText.Trim();
        if (result.Contains("stop paying rent", StringComparison.OrdinalIgnoreCase)
            || result.Contains("cut essentials", StringComparison.OrdinalIgnoreCase))
        {
            result += " Keep essentials like housing, food, utilities, and healthcare protected.";
        }

        if (!toolOutputs.ContainsKey("budget_status"))
        {
            result += " Budget status is limited, so treat this as directional guidance.";
        }

        return result;
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
        CancellationToken cancellationToken)
    {
        var toolsUsedText = toolsUsed.Count == 0 ? "none" : string.Join(",", toolsUsed);
        logger.LogInformation(
            "[AI_COMPANION] userId={UserId} sessionId={SessionId} intent={Intent} tools_used={ToolsUsed} tokens_input={TokensInput} tokens_output={TokensOutput} model={Model} response_time={ResponseTimeMs}",
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
            InsufficientDataReasons: insufficientDataReasons);
    }
}
