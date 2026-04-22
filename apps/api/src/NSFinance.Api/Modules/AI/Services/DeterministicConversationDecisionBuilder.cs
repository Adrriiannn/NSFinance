namespace NSFinance.Api.Modules.AI.Services;

public interface IDeterministicConversationDecisionBuilder
{
    ConversationTurnStrategyDecision BuildPrimaryDecision(
        ConversationBehaviorRequest request,
        ConversationModelSelectionPlan modelSelection);
}

public sealed class DeterministicConversationDecisionBuilder : IDeterministicConversationDecisionBuilder
{
    public ConversationTurnStrategyDecision BuildPrimaryDecision(
        ConversationBehaviorRequest request,
        ConversationModelSelectionPlan modelSelection)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(modelSelection);

        var userMessage = request.Request.UserMessage;
        var normalized = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var signals = ConversationSignalAnalyzer.Analyze(userMessage);
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(userMessage);
        var currentReadiness = request.EffectiveState.ReadinessLevel;

        if (ConversationPolicyHelpers.HasDeterministicStructuredFollowUpIntent(
                request.ResultContext,
                extraction,
                signals,
                normalized))
        {
            var suggestedOptions = signals.HasComparisonSignal
                ? new[] { "Compare the shortlist", "Filter by a new preference" }
                : new[] { "Refine the shortlist", "Compare options" };

            return BuildDecision(
                request,
                modelSelection,
                strategy: ConversationBehaviorStrategy.RefinePriorResultSet,
                modeCandidate: ConversationMode.Exploration,
                readinessTo: ConversationReadinessLevel.R4_ToolReady,
                confidence: 0.92d,
                followUpBindingType: signals.HasBranchingSignal
                    ? FollowUpBindingType.NewBranch
                    : signals.HasComparisonSignal || signals.HasResultReferenceSignal
                        ? FollowUpBindingType.BindPrior
                        : FollowUpBindingType.Refine,
                clarificationQuestion: null,
                suggestedOptions: suggestedOptions,
                toolPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                reasonCodes:
                [
                    "deterministic_primary",
                    "deterministic_structured_followup"
                ]);
        }

        if (ConversationPolicyHelpers.HasConcreteStructuredSearchFrame(extraction, signals)
            && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals))
        {
            return BuildDecision(
                request,
                modelSelection,
                strategy: ConversationBehaviorStrategy.ToolReadyHandoff,
                modeCandidate: ConversationMode.Exploration,
                readinessTo: ConversationReadinessLevel.R4_ToolReady,
                confidence: 0.9d,
                followUpBindingType: request.ResultContext is null
                    ? FollowUpBindingType.None
                    : FollowUpBindingType.Refine,
                clarificationQuestion: null,
                suggestedOptions: [],
                toolPermission: ToolExecutionPermission.EligibleIfGuardPasses,
                reasonCodes:
                [
                    "deterministic_primary",
                    "deterministic_structured_search"
                ]);
        }

        if (signals.HasFinancialSignal
            && !ConversationPolicyHelpers.HasFinancialThreadEngagement(request.EffectiveState))
        {
            var strategy = signals.HasEmotionalFraming
                ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                : ConversationBehaviorStrategy.SuggestAndClarify;

            return BuildDecision(
                request,
                modelSelection,
                strategy: strategy,
                modeCandidate: ConversationMode.Conversation,
                readinessTo: ConversationReadinessLevel.R1_Vague,
                confidence: 0.8d,
                followUpBindingType: FollowUpBindingType.None,
                clarificationQuestion: "Do you want to focus on subscriptions, overall spending, or a specific budget concern?",
                suggestedOptions: ["Review subscriptions", "Look at spending trends", "Set a clearer goal"],
                toolPermission: ToolExecutionPermission.Forbidden,
                reasonCodes:
                [
                    "deterministic_primary",
                    "deterministic_financial_validation"
                ]);
        }

        if (signals.HasFactualQuestion
            && signals.HasCompleteQuestion
            && !signals.HasExplorationSignal
            && !signals.HasFinancialSignal)
        {
            return BuildDecision(
                request,
                modelSelection,
                strategy: ConversationBehaviorStrategy.DirectAnswer,
                modeCandidate: ConversationMode.GeneralKnowledge,
                readinessTo: ConversationReadinessLevel.R1_Vague,
                confidence: 0.76d,
                followUpBindingType: FollowUpBindingType.None,
                clarificationQuestion: null,
                suggestedOptions: [],
                toolPermission: ToolExecutionPermission.Forbidden,
                reasonCodes:
                [
                    "deterministic_primary",
                    "deterministic_factual_question"
                ]);
        }

        return BuildDecision(
            request,
            modelSelection,
            strategy: signals.HasEmotionalFraming || signals.HasSubjectiveLanguage
                ? ConversationBehaviorStrategy.AcknowledgeAndGuide
                : ConversationBehaviorStrategy.SuggestAndClarify,
            modeCandidate: ConversationMode.Conversation,
            readinessTo: ConversationReadinessLevel.R1_Vague,
            confidence: 0.58d,
            followUpBindingType: FollowUpBindingType.None,
            clarificationQuestion: "What would feel most useful to focus on first?",
            suggestedOptions: ["Clarify the goal", "Share a constraint", "Ask for general guidance"],
            toolPermission: ToolExecutionPermission.Forbidden,
            reasonCodes:
            [
                "deterministic_primary",
                "deterministic_general_guidance"
            ]);
    }

    private static ConversationTurnStrategyDecision BuildDecision(
        ConversationBehaviorRequest request,
        ConversationModelSelectionPlan modelSelection,
        ConversationBehaviorStrategy strategy,
        ConversationMode modeCandidate,
        ConversationReadinessLevel readinessTo,
        double confidence,
        FollowUpBindingType followUpBindingType,
        string? clarificationQuestion,
        IReadOnlyList<string> suggestedOptions,
        ToolExecutionPermission toolPermission,
        IReadOnlyList<string> reasonCodes)
    {
        var mergedReasonCodes = reasonCodes
            .Concat(modelSelection.ReasonCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ConversationTurnStrategyDecision(
            Strategy: strategy,
            ModeCandidate: modeCandidate,
            Readiness: new ReadinessTransition(
                From: request.EffectiveState.ReadinessLevel,
                To: readinessTo),
            Confidence: confidence,
            FollowUpBindingType: followUpBindingType,
            ClarificationQuestion: clarificationQuestion,
            SuggestedOptions: suggestedOptions,
            ToolExecutionPermission: toolPermission,
            ReasonCodes: mergedReasonCodes);
    }
}
