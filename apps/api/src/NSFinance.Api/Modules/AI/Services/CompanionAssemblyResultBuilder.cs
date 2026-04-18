namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionAssemblyResultBuilder
{
    CompanionContextAssemblyResult Build(
        FinancialCompanionIntent primaryIntent,
        UserFinancialContextSnapshot profile,
        CompanionExecutionPlan plan,
        CompanionToolExecutionResult execution,
        CompanionContextTrimResult trim,
        CompanionInsufficiencyDecision insufficiency);
}

public sealed class CompanionAssemblyResultBuilder(
    ICompanionEvidenceBuilder evidenceBuilder) : ICompanionAssemblyResultBuilder
{
    public CompanionContextAssemblyResult Build(
        FinancialCompanionIntent primaryIntent,
        UserFinancialContextSnapshot profile,
        CompanionExecutionPlan plan,
        CompanionToolExecutionResult execution,
        CompanionContextTrimResult trim,
        CompanionInsufficiencyDecision insufficiency)
    {
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
            Intent: primaryIntent,
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
