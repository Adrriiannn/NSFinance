namespace NSFinance.Api.Modules.AI.Services;

public sealed class UserChatOrchestrator(
    IUserChatComplexityClassifier complexityClassifier,
    IConversationContextService contextService,
    IAIModelRouter modelRouter,
    IPromptBuilder promptBuilder,
    IUserChatResponseParser responseParser,
    IAIClient aiClient,
    ILogger<UserChatOrchestrator> logger) : IUserChatOrchestrator
{
    public async Task<UserChatResponse> ExecuteAsync(UserChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var complexity = complexityClassifier.Evaluate(request);

        var taskType = complexity.Complexity == UserChatComplexity.Complex
            ? AITaskType.UserChatComplex
            : AITaskType.UserChatSimple;

        var context = contextService.BuildContext(
            new ConversationContextBuildRequest(
                TaskType: taskType,
                RecentTurns: request.RecentTurns,
                State: request.State,
                CurrentUserMessage: request.UserMessage,
                CorrelationId: request.CorrelationId));

        var prompt = promptBuilder.BuildUserChatPrompt(
            new UserChatPromptInput(request, context, complexity));

        var route = modelRouter.Resolve(
            taskType,
            complexity.Complexity == UserChatComplexity.Complex ? AIModelClass.HeavyReasoning : AIModelClass.Fast,
            complexityHint: string.Join(',', complexity.ReasonCodes));

        if (route.Reason == "heavy_model_disabled_fail_fast")
        {
            return new UserChatResponse(
                ReplyText: "I can't process that complex request right now because the heavy model is unavailable.",
                ModelUsed: route.Model,
                ReasoningClass: route.ModelClass,
                SuggestedStructuredStateUpdates: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ReferencedContextSummary: context.ContextSummary,
                Warnings: ["heavy_model_unavailable"],
                FollowUpIntentHints: ["retry_shorter_question"],
                Succeeded: false,
                FailureReason: "heavy_model_unavailable");
        }

        var aiRequest = AIRequest.Create(
            taskType: taskType,
            preferredModelClass: route.ModelClass,
            messages: prompt.Messages,
            correlationId: request.CorrelationId,
            systemInstructions: prompt.SystemInstructions,
            structuredOutputSchemaName: prompt.StructuredSchemaName,
            temperature: complexity.Complexity == UserChatComplexity.Complex ? 0.2d : 0.1d,
            maxOutputTokens: complexity.Complexity == UserChatComplexity.Complex ? 1200 : 600,
            metadata: request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var response = await aiClient.SendAsync(aiRequest, route, cancellationToken);
        responseParser.TryParse(response, route, out var parsedResponse, out var reasonCodes);

        logger.LogInformation(
            "User chat orchestrated correlationId={CorrelationId} complexity={Complexity} task={TaskType} model={Model} deployment={Deployment} succeeded={Succeeded} reasonCodes={ReasonCodes}",
            request.CorrelationId,
            complexity.Complexity,
            taskType,
            route.Model,
            route.Deployment,
            parsedResponse.Succeeded,
            string.Join(',', reasonCodes));

        return parsedResponse;
    }
}
