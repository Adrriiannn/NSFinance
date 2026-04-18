namespace NSFinance.Api.Modules.AI.Services;

public sealed class FinancialAdvicePolicyEvaluationState
{
    public FinancialAdvicePolicyEvaluationState(FinancialAdviceFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        OriginalFinding = finding;
        Confidence = finding.Confidence;
        Severity = finding.Severity;
        AiAdjudicationAllowed = finding.AiAdjudicationAllowed;
    }

    public FinancialAdviceFinding OriginalFinding { get; }

    public List<FinancialAdviceActionCandidate> ApprovedActions { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Exclusions { get; } = [];

    public double Confidence { get; set; }

    public FinancialAdviceSeverity Severity { get; set; }

    public bool AiAdjudicationAllowed { get; set; }

    public FinancialAdvicePolicyDecision Decision { get; set; } = FinancialAdvicePolicyDecision.Approved;

    public bool HasAdjustments =>
        Warnings.Count > 0
        || Exclusions.Count > 0
        || ApprovedActions.Count != OriginalFinding.RecommendedActions.Count
        || Math.Abs(Confidence - OriginalFinding.Confidence) > 0.001d
        || Severity != OriginalFinding.Severity;
}

public sealed record FinancialAdvicePolicyActionContext(
    FinancialAdviceFinding Finding,
    FinancialAdviceActionCandidate Action,
    IReadOnlyList<string> ProtectedPreferenceHints);
