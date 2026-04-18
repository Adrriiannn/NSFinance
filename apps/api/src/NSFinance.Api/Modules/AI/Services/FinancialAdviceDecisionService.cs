using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialAdviceDecisionService
{
    Task<FinancialAdviceDecisionResult> DecideAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        AIModelClass preferredModelClass,
        CancellationToken cancellationToken);
}

public sealed class FinancialAdviceDecisionService(
    IFinancialAdviceEngine adviceEngine,
    IFinancialAdvicePolicyService policyService,
    IFinancialAdviceAdjudicationService adjudicationService,
    IFinancialAdviceAdjudicationPlanSelector adjudicationPlanSelector,
    IAdviceEvidenceSummaryBuilder evidenceSummaryBuilder,
    IAdvicePacketBuilder packetBuilder,
    IOptions<CompanionAdviceOptions> options) : IFinancialAdviceDecisionService
{
    private readonly CompanionAdviceOptions adviceOptions = options.Value;

    public async Task<FinancialAdviceDecisionResult> DecideAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        AIModelClass preferredModelClass,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        var nowUtc = DateTime.UtcNow;
        var deterministicFindings = adviceEngine.ComputeDeterministicFindings(request, routing, context, nowUtc);
        var policyReviewed = policyService.ApplyPolicy(context, deterministicFindings);
        var adjudicationPlan = adjudicationPlanSelector.SelectPlan(routing, policyReviewed);
        var evidenceSummary = evidenceSummaryBuilder.Build(policyReviewed);
        var adjudication = await RunAdjudicationAsync(
            request,
            routing,
            context,
            preferredModelClass,
            policyReviewed,
            evidenceSummary,
            adjudicationPlan,
            cancellationToken);

        var packet = packetBuilder.Build(
            new FinancialAdvicePacketBuildRequest(
                ComputedAtUtc: nowUtc,
                Intent: routing.PrimaryIntent,
                DeterministicFindings: deterministicFindings,
                PolicyReviewedFindings: policyReviewed,
                Adjudication: adjudication,
                EvidenceSummary: evidenceSummary));

        return new FinancialAdviceDecisionResult(
            Packet: packet,
            ModelUsed: adjudication.UsedAi ? adjudication.ModelUsed : "deterministic_only",
            InputTokens: adjudication.InputTokens,
            OutputTokens: adjudication.OutputTokens,
            Warnings: packet.Warnings);
    }

    private Task<FinancialAdviceAdjudicationResult> RunAdjudicationAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        FinancialCompanionContext context,
        AIModelClass preferredModelClass,
        IReadOnlyList<FinancialAdvicePolicyReviewedFinding> policyReviewed,
        IReadOnlyList<string> evidenceSummary,
        FinancialAdviceAdjudicationPlan adjudicationPlan,
        CancellationToken cancellationToken)
    {
        if (!adviceOptions.EnableAiAdjudication
            || adjudicationPlan.Mode == FinancialAdviceAdjudicationMode.Skipped)
        {
            return Task.FromResult(
                new FinancialAdviceAdjudicationResult(
                    UsedAi: false,
                    Succeeded: true,
                    Mode: FinancialAdviceAdjudicationMode.Skipped,
                    ModelUsed: "deterministic_only",
                    InputTokens: 0,
                    OutputTokens: 0,
                    ResponseSummary: null,
                    FindingOutcomes: [],
                    Warnings: adviceOptions.EnableAiAdjudication
                        ? adjudicationPlan.ReasonCodes
                        : ["adjudication_disabled"]));
        }

        return adjudicationService.AdjudicateAsync(
            new FinancialAdviceAdjudicationExecutionRequest(
                UserQuery: request.UserQuery,
                Intent: routing.PrimaryIntent,
                Profile: context.Profile,
                Plan: adjudicationPlan,
                PolicyReviewedFindings: policyReviewed,
                EvidenceSummary: evidenceSummary,
                PreferredModelClass: preferredModelClass,
                CorrelationId: string.IsNullOrWhiteSpace(request.CorrelationId)
                    ? Guid.NewGuid().ToString("N")
                    : request.CorrelationId,
                Metadata: request.Metadata),
            cancellationToken);
    }
}
