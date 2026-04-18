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
    ICompanionAssemblyResultBuilder assemblyResultBuilder) : IFinancialCompanionContextAssembler
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
        return assemblyResultBuilder.Build(
            primaryIntent: routing.PrimaryIntent,
            profile: profile,
            plan: plan,
            execution: execution,
            trim: trim,
            insufficiency: insufficiency);
    }
}
