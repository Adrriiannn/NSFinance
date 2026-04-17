namespace NSFinance.Api.Modules.AI.Services;

public interface IFinancialCompanionContextAssembler
{
    Task<CompanionContextAssemblyResult> AssembleAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken);
}

public sealed record CompanionContextAssemblyResult(
    FinancialCompanionContext Context,
    IReadOnlyList<string> ToolsUsed,
    CompanionContextEvidence Evidence,
    IReadOnlyList<string> Warnings,
    bool HasInsufficientData,
    IReadOnlyList<string> InsufficientDataReasons,
    bool CanProceedToAI);

public sealed class FinancialCompanionContextAssembler(
    ICompanionExecutionPlanBuilder planBuilder,
    ICompanionToolExecutor toolExecutor,
    ICompanionContextShaper contextShaper,
    ICompanionInsufficiencyEvaluator insufficiencyEvaluator,
    ICompanionEvidenceBuilder evidenceBuilder) : IFinancialCompanionContextAssembler
{
    public async Task<CompanionContextAssemblyResult> AssembleAsync(
        FinancialCompanionRequest request,
        CompanionIntentRoutingResult routing,
        UserFinancialContextSnapshot profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var plan = planBuilder.Build(routing);
        var execution = await toolExecutor.ExecuteAsync(request, plan, profile, cancellationToken);
        var trim = contextShaper.TrimToPayloadBudget(execution.ContextOutputs, execution.Records);
        var insufficiency = insufficiencyEvaluator.Evaluate(routing, plan, trim.AdjustedRecords, trim.TrimmedIndicators);

        var warnings = plan.Warnings
            .Concat(execution.Warnings)
            .Concat(trim.Warnings)
            .Concat(insufficiency.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var evidence = evidenceBuilder.Build(
            plan: plan,
            records: trim.AdjustedRecords,
            contextOutputs: trim.Outputs,
            insufficiency: insufficiency,
            trimIndicators: trim.TrimmedIndicators,
            warnings: warnings);

        var context = new FinancialCompanionContext(
            Intent: routing.PrimaryIntent,
            Profile: profile,
            ToolOutputs: trim.Outputs,
            ToolsUsed: evidence.ToolsUsed,
            Evidence: evidence);

        return new CompanionContextAssemblyResult(
            Context: context,
            ToolsUsed: evidence.ToolsUsed,
            Evidence: evidence,
            Warnings: warnings,
            HasInsufficientData: insufficiency.HasInsufficientData,
            InsufficientDataReasons: insufficiency.Reasons,
            CanProceedToAI: insufficiency.CanProceedToAI);
    }
}
