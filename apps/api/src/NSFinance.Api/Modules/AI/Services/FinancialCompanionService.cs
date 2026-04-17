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
    IUserFinancialSummaryService summaryService,
    ISpendingAnalysisService spendingAnalysisService,
    IRecurringObligationsService recurringObligationsService,
    IBudgetStatusService budgetStatusService,
    ITransactionQueryService transactionQueryService,
    IPlacesSearchService placesSearchService,
    IPlaceDetailsService placeDetailsService,
    IReviewInsightsService reviewInsightsService,
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
                Intent: FinancialCompanionIntent.GeneralQuestion,
                ToolsUsed: [],
                Warnings: ["companion_disabled"],
                Succeeded: false,
                FailureReason: "companion_disabled",
                ModelUsed: "none",
                InputTokens: 0,
                OutputTokens: 0);
        }

        var nowUtc = DateTime.UtcNow;
        var intent = ClassifyIntent(request.UserQuery);
        var toolsUsed = new List<string>(8);
        var warnings = new List<string>(2);
        var profile = await profileService.GetOrCreateAsync(request.UserId, cancellationToken);
        var toolOutputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

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
                    intent,
                    toolsUsed,
                    warnings,
                    replyText: "You've reached your daily AI guidance cap. Please try again tomorrow.",
                    modelUsed: "soft_cap_block",
                    responseTimeMs: 0,
                    tokensInput: 0,
                    tokensOutput: 0,
                    succeeded: false,
                    failureReason: "daily_soft_cap_reached",
                    cancellationToken);
            }
        }

        await PopulateToolContextAsync(
            request,
            intent,
            toolsUsed,
            toolOutputs,
            profile,
            cancellationToken);

        var context = new FinancialCompanionContext(
            Intent: intent,
            Profile: profile,
            ToolOutputs: toolOutputs,
            ToolsUsed: toolsUsed);

        var route = modelRouter.Resolve(
            AITaskType.FinancialReasoning,
            modelClass,
            complexityHint: intent.ToString());

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
                intent,
                toolsUsed,
                warnings,
                fallback,
                route.Model,
                elapsedMs,
                aiResponse.InputTokenEstimate ?? 0,
                aiResponse.OutputTokenEstimate ?? 0,
                succeeded: false,
                failureReason: parsed.FailureReason ?? aiResponse.FailureReason ?? "companion_parse_failed",
                cancellationToken);
        }

        warnings.AddRange(parsed.Warnings);
        var safeReply = ApplySafetyPostProcessing(parsed.ReplyText, toolOutputs);
        return await PersistAndReturnAsync(
            request,
            intent,
            toolsUsed,
            warnings,
            safeReply,
            route.Model,
            elapsedMs,
            aiResponse.InputTokenEstimate ?? 0,
            aiResponse.OutputTokenEstimate ?? 0,
            succeeded: true,
            failureReason: null,
            cancellationToken);
    }

    private async Task PopulateToolContextAsync(
        FinancialCompanionRequest request,
        FinancialCompanionIntent intent,
        List<string> toolsUsed,
        Dictionary<string, object?> toolOutputs,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken)
    {
        var summary = await summaryService.GetSummaryAsync(request.UserId, cancellationToken);
        toolOutputs["financial_summary"] = summary;
        toolsUsed.Add("IUserFinancialSummaryService");

        switch (intent)
        {
            case FinancialCompanionIntent.Budgeting:
            {
                var spending = await spendingAnalysisService.AnalyzeAsync(request.UserId, 60, cancellationToken);
                var budget = await budgetStatusService.GetBudgetStatusAsync(request.UserId, cancellationToken);
                var recurring = await recurringObligationsService.GetRecurringAsync(request.UserId, cancellationToken);
                toolOutputs["spending_analysis"] = spending;
                toolOutputs["budget_status"] = budget;
                toolOutputs["recurring_obligations"] = recurring;
                toolsUsed.Add("ISpendingAnalysisService");
                toolsUsed.Add("IBudgetStatusService");
                toolsUsed.Add("IRecurringObligationsService");
                break;
            }
            case FinancialCompanionIntent.SavingsAdvice:
            {
                var spending = await spendingAnalysisService.AnalyzeAsync(request.UserId, 90, cancellationToken);
                var recurring = await recurringObligationsService.GetRecurringAsync(request.UserId, cancellationToken);
                toolOutputs["spending_analysis"] = spending;
                toolOutputs["recurring_obligations"] = recurring;
                toolsUsed.Add("ISpendingAnalysisService");
                toolsUsed.Add("IRecurringObligationsService");
                break;
            }
            case FinancialCompanionIntent.Affordability:
            {
                var budget = await budgetStatusService.GetBudgetStatusAsync(request.UserId, cancellationToken);
                var matches = await transactionQueryService.QueryAsync(
                    request.UserId,
                    request.UserQuery,
                    maxRows: 20,
                    cancellationToken);
                toolOutputs["budget_status"] = budget;
                toolOutputs["transaction_matches"] = matches;
                toolsUsed.Add("IBudgetStatusService");
                toolsUsed.Add("ITransactionQueryService");
                break;
            }
            case FinancialCompanionIntent.LifestylePlaces:
            {
                var budget = await budgetStatusService.GetBudgetStatusAsync(request.UserId, cancellationToken);
                var placeSearch = await placesSearchService.SearchAsync(
                    request.UserQuery,
                    profile.Country,
                    cancellationToken);
                toolOutputs["budget_status"] = budget;
                toolOutputs["place_search"] = placeSearch;
                toolsUsed.Add("IBudgetStatusService");
                toolsUsed.Add("IPlacesSearchService");
                if (placeSearch.Items.Count > 0)
                {
                    var top = placeSearch.Items[0];
                    var details = await placeDetailsService.GetDetailsAsync(top.PlaceId, cancellationToken);
                    var reviews = await reviewInsightsService.GetInsightsAsync(top.PlaceId, cancellationToken);
                    toolOutputs["place_details"] = details;
                    toolOutputs["review_insights"] = reviews;
                    toolsUsed.Add("IPlaceDetailsService");
                    toolsUsed.Add("IReviewInsightsService");
                }

                break;
            }
            default:
            {
                var budget = await budgetStatusService.GetBudgetStatusAsync(request.UserId, cancellationToken);
                toolOutputs["budget_status"] = budget;
                toolsUsed.Add("IBudgetStatusService");
                break;
            }
        }
    }

    private static FinancialCompanionIntent ClassifyIntent(string query)
    {
        var value = (query ?? string.Empty).ToLowerInvariant();
        if (value.Contains("budget", StringComparison.Ordinal)
            || value.Contains("overspend", StringComparison.Ordinal)
            || value.Contains("plan", StringComparison.Ordinal))
        {
            return FinancialCompanionIntent.Budgeting;
        }

        if (value.Contains("save", StringComparison.Ordinal)
            || value.Contains("savings", StringComparison.Ordinal)
            || value.Contains("reduce spend", StringComparison.Ordinal))
        {
            return FinancialCompanionIntent.SavingsAdvice;
        }

        if (value.Contains("afford", StringComparison.Ordinal)
            || value.Contains("can i", StringComparison.Ordinal)
            || value.Contains("purchase", StringComparison.Ordinal))
        {
            return FinancialCompanionIntent.Affordability;
        }

        if (value.Contains("restaurant", StringComparison.Ordinal)
            || value.Contains("place", StringComparison.Ordinal)
            || value.Contains("near me", StringComparison.Ordinal))
        {
            return FinancialCompanionIntent.LifestylePlaces;
        }

        return FinancialCompanionIntent.GeneralQuestion;
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
            OutputTokens: tokensOutput);
    }
}
