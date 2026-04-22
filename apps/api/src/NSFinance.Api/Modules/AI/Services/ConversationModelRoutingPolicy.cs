namespace NSFinance.Api.Modules.AI.Services;

public interface IConversationModelRoutingPolicy
{
    ConversationModelSelectionPlan SelectPrimaryDecision(
        ConversationBehaviorRequest request,
        ConversationSignals signals,
        FollowUpBindingType bindingType,
        ConversationStateSnapshot effectiveState);

    ConversationModelSelectionPlan SelectExplorationSubtype(
        ConversationModeRequest request,
        ConversationSignals signals,
        ExplorationSubtypeResolutionPlan resolutionPlan,
        ConversationModelSelectionPlan primaryDecisionSelection);
}

public sealed class ConversationModelRoutingPolicy : IConversationModelRoutingPolicy
{
    public ConversationModelSelectionPlan SelectPrimaryDecision(
        ConversationBehaviorRequest request,
        ConversationSignals signals,
        FollowUpBindingType bindingType,
        ConversationStateSnapshot effectiveState)
    {
        var extraction = ConversationPolicyHelpers.ExtractLocalDiscovery(request.Request.UserMessage);
        var semanticFamily = ConversationPolicyHelpers.ResolveSemanticFamily(effectiveState, signals);
        var normalized = (request.Request.UserMessage ?? string.Empty).Trim().ToLowerInvariant();
        var reasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (HasDeterministicStructuredFollowUp(
                request.ResultContext,
                bindingType,
                extraction,
                signals,
                normalized))
        {
            reasonCodes.Add("model_selection_structured_followup_deterministic");
            return BuildDeterministicPlan(
                "structured_followup_deterministic",
                null,
                reasonCodes);
        }

        if (HasStructuredFollowUp(request.ResultContext, bindingType, extraction, signals))
        {
            reasonCodes.Add("model_selection_structured_followup_fast");
            return new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "structured_followup_fast",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (ConversationPolicyHelpers.HasConcreteStructuredSearchFrame(extraction, signals)
            && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals))
        {
            reasonCodes.Add("model_selection_structured_search_deterministic");
            return BuildDeterministicPlan(
                "structured_search_deterministic",
                null,
                reasonCodes);
        }

        if (signals.HasFactualQuestion
            && signals.HasCompleteQuestion
            && !signals.HasExplorationSignal
            && !signals.HasFinancialSignal)
        {
            reasonCodes.Add("model_selection_factual_question_deterministic");
            return BuildDeterministicPlan(
                "factual_question_deterministic",
                null,
                reasonCodes);
        }

        if (signals.HasFinancialSignal
            && !ConversationPolicyHelpers.HasFinancialThreadEngagement(effectiveState))
        {
            reasonCodes.Add("model_selection_financial_validation_deterministic");
            return BuildDeterministicPlan(
                "financial_validation_deterministic",
                null,
                reasonCodes);
        }

        if (ShouldEscalatePrimaryToHeavy(
                normalized,
                extraction,
                signals,
                semanticFamily,
                bindingType,
                effectiveState,
                out var escalationReason,
                out var escalationJustification,
                out var escalationReasonCodes))
        {
            reasonCodes.UnionWith(escalationReasonCodes);
            return new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.HeavyReasoning,
                ModelClass: AIModelClass.HeavyReasoning,
                SelectionReason: escalationReason,
                EscalationJustification: escalationJustification,
                CouldAvoidEscalation: false,
                ReasonCodes: reasonCodes.ToArray());
        }

        reasonCodes.Add("model_selection_default_fast");
        return new ConversationModelSelectionPlan(
            SelectionKind: ConversationModelSelectionKind.Fast,
            ModelClass: AIModelClass.Fast,
            SelectionReason: "default_fast_conversation_decision",
            EscalationJustification: null,
            CouldAvoidEscalation: false,
            ReasonCodes: reasonCodes.ToArray());
    }

    public ConversationModelSelectionPlan SelectExplorationSubtype(
        ConversationModeRequest request,
        ConversationSignals signals,
        ExplorationSubtypeResolutionPlan resolutionPlan,
        ConversationModelSelectionPlan primaryDecisionSelection)
    {
        var reasonCodes = new HashSet<string>(resolutionPlan.ReasonCodes, StringComparer.OrdinalIgnoreCase);

        if (!resolutionPlan.RequiresModelReasoning)
        {
            reasonCodes.Add("model_selection_subtype_deterministic");
            return BuildDeterministicPlan(
                $"subtype_{resolutionPlan.ResolutionSource}",
                null,
                reasonCodes);
        }

        if (primaryDecisionSelection.SelectionKind == ConversationModelSelectionKind.HeavyReasoning)
        {
            reasonCodes.Add("model_selection_subtype_fast_heavy_budget_guard");
            return new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "subtype_fast_after_primary_heavy",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: reasonCodes.ToArray());
        }

        if (request.ResultContext?.SourceMode == ConversationMode.Exploration
            && request.ResultContext.SourceSubtype == ExplorationSubtype.Structured
            && (signals.HasComparisonSignal || signals.HasResultReferenceSignal || signals.HasBranchingSignal))
        {
            reasonCodes.Add("model_selection_subtype_fast_followup_refinement");
            return new ConversationModelSelectionPlan(
                SelectionKind: ConversationModelSelectionKind.Fast,
                ModelClass: AIModelClass.Fast,
                SelectionReason: "subtype_fast_followup_refinement",
                EscalationJustification: null,
                CouldAvoidEscalation: false,
                ReasonCodes: reasonCodes.ToArray());
        }

        reasonCodes.Add("model_selection_subtype_default_fast");
        return new ConversationModelSelectionPlan(
            SelectionKind: ConversationModelSelectionKind.Fast,
            ModelClass: AIModelClass.Fast,
            SelectionReason: "subtype_default_fast",
            EscalationJustification: null,
            CouldAvoidEscalation: false,
            ReasonCodes: reasonCodes.ToArray());
    }

    private static ConversationModelSelectionPlan BuildDeterministicPlan(
        string selectionReason,
        string? escalationJustification,
        IEnumerable<string> reasonCodes)
    {
        return new ConversationModelSelectionPlan(
            SelectionKind: ConversationModelSelectionKind.Deterministic,
            ModelClass: null,
            SelectionReason: selectionReason,
            EscalationJustification: escalationJustification,
            CouldAvoidEscalation: false,
            ReasonCodes: reasonCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool HasStructuredFollowUp(
        ResultContextSnapshot? resultContext,
        FollowUpBindingType bindingType,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals)
    {
        return resultContext?.SourceMode == ConversationMode.Exploration
               && resultContext.SourceSubtype == ExplorationSubtype.Structured
               && bindingType is FollowUpBindingType.BindPrior
                   or FollowUpBindingType.Refine
                   or FollowUpBindingType.NewBranch
               && !ConversationPolicyHelpers.ShouldPreferExperientialOpen(extraction, signals);
    }

    private static bool HasDeterministicStructuredFollowUp(
        ResultContextSnapshot? resultContext,
        FollowUpBindingType bindingType,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals,
        string normalized)
    {
        return (bindingType is FollowUpBindingType.BindPrior
                   or FollowUpBindingType.Refine
                   or FollowUpBindingType.NewBranch)
               && ConversationPolicyHelpers.HasDeterministicStructuredFollowUpIntent(
                   resultContext,
                   extraction,
                   signals,
                   normalized);
    }

    private static bool ShouldEscalatePrimaryToHeavy(
        string normalized,
        LocalDiscoveryConstraintExtractionResult extraction,
        ConversationSignals signals,
        string semanticFamily,
        FollowUpBindingType bindingType,
        ConversationStateSnapshot state,
        out string escalationReason,
        out string escalationJustification,
        out IReadOnlyList<string> escalationReasonCodes)
    {
        var reasonCodes = new List<string>();

        if (string.Equals(semanticFamily, ConversationSemanticFamilies.Exploration, StringComparison.OrdinalIgnoreCase)
            && (ConversationPolicyHelpers.HasStrongOpenExplorationIntent(extraction, signals)
                || ContainsAny(
                    normalized,
                    "somewhere nice to go",
                    "where should i go",
                    "i don't know what to do",
                    "something nice to do",
                    "somewhere safe, scenic",
                    "calm but lively")))
        {
            reasonCodes.Add("model_selection_open_exploration_heavy");
            escalationReason = "open_exploration_heavy";
            escalationJustification = "The request is experiential or vibe-led, and misreading the user's intent would materially degrade the conversation.";
            escalationReasonCodes = reasonCodes;
            return true;
        }

        if (string.Equals(semanticFamily, ConversationSemanticFamilies.Financial, StringComparison.OrdinalIgnoreCase)
            && ConversationPolicyHelpers.HasFinancialThreadEngagement(state)
            && ContainsAny(
                normalized,
                "should i",
                "which should",
                "what should i prioritize",
                "better to",
                "tradeoff",
                "trade-off",
                "cut first",
                "what first"))
        {
            reasonCodes.Add("model_selection_financial_tradeoff_heavy");
            escalationReason = "financial_tradeoff_heavy";
            escalationJustification = "The turn asks for prioritization or trade-off reasoning in a financial thread, where shallow interpretation would reduce usefulness.";
            escalationReasonCodes = reasonCodes;
            return true;
        }

        if (signals.HasCorrectionSignal
            && state.Constraints.Count >= 3
            && bindingType is FollowUpBindingType.Refine or FollowUpBindingType.NewBranch)
        {
            reasonCodes.Add("model_selection_complex_correction_heavy");
            escalationReason = "complex_correction_heavy";
            escalationJustification = "The turn is rewriting multiple active constraints at once, so the system should reason carefully before interpreting the new intent.";
            escalationReasonCodes = reasonCodes;
            return true;
        }

        escalationReason = string.Empty;
        escalationJustification = string.Empty;
        escalationReasonCodes = [];
        return false;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.Ordinal));
    }
}
