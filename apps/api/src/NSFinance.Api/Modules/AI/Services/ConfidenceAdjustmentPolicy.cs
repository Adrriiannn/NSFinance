namespace NSFinance.Api.Modules.AI.Services;

public interface IConfidenceAdjustmentPolicy
{
    void Apply(FinancialAdvicePolicyEvaluationState state);
}

public sealed class ConfidenceAdjustmentPolicy : IConfidenceAdjustmentPolicy
{
    public void Apply(FinancialAdvicePolicyEvaluationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.OriginalFinding.UncertaintyMarkers.Count > 0 && state.Confidence > 0.75d)
        {
            state.Confidence = 0.75d;
            state.Warnings.Add("confidence_capped_due_to_uncertainty");
        }

        if (state.Confidence < 0.45d && state.Severity >= FinancialAdviceSeverity.Moderate)
        {
            state.Severity = LowerSeverity(state.Severity);
            state.Warnings.Add("severity_downgraded_due_to_weak_evidence");
        }
    }

    private static FinancialAdviceSeverity LowerSeverity(FinancialAdviceSeverity severity)
    {
        return severity switch
        {
            FinancialAdviceSeverity.Critical => FinancialAdviceSeverity.High,
            FinancialAdviceSeverity.High => FinancialAdviceSeverity.Moderate,
            FinancialAdviceSeverity.Moderate => FinancialAdviceSeverity.Low,
            FinancialAdviceSeverity.Low => FinancialAdviceSeverity.Info,
            _ => FinancialAdviceSeverity.Info
        };
    }
}
