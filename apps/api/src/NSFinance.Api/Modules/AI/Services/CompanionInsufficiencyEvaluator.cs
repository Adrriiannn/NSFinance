namespace NSFinance.Api.Modules.AI.Services;

public interface ICompanionInsufficiencyEvaluator
{
    CompanionInsufficiencyDecision Evaluate(
        CompanionIntentRoutingResult routing,
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyList<string> trimIndicators);
}

public sealed class CompanionInsufficiencyEvaluator : ICompanionInsufficiencyEvaluator
{
    public CompanionInsufficiencyDecision Evaluate(
        CompanionIntentRoutingResult routing,
        CompanionExecutionPlan plan,
        IReadOnlyList<CompanionToolExecutionRecord> records,
        IReadOnlyList<string> trimIndicators)
    {
        var warnings = new List<string>(4);
        var reasons = new List<string>(4);
        var missingRequired = new List<string>(4);

        if (routing.IsUnsupported)
        {
            reasons.Add(CompanionOrchestrationReasonCodes.UnsupportedQueryScope);
            return new CompanionInsufficiencyDecision(
                CanProceedToAI: false,
                HasInsufficientData: true,
                Reasons: reasons,
                MissingRequiredTools: missingRequired,
                Warnings: warnings);
        }

        if (routing.IsAmbiguous)
        {
            reasons.Add(CompanionOrchestrationReasonCodes.AmbiguousQueryRequiresClarification);
            return new CompanionInsufficiencyDecision(
                CanProceedToAI: false,
                HasInsufficientData: true,
                Reasons: reasons,
                MissingRequiredTools: missingRequired,
                Warnings: warnings);
        }

        var requiredTools = plan.PlannedTools
            .Where(x => x.IsRequired)
            .ToList();
        foreach (var required in requiredTools)
        {
            var record = records.FirstOrDefault(x => x.PlannedTool.Tool == required.Tool);
            if (record is null)
            {
                var reason = CompanionOrchestrationReasonCodes.WithTool(
                    CompanionOrchestrationReasonCodes.GroundingIncomplete,
                    required.Tool);
                reasons.Add(reason);
                missingRequired.Add(required.Tool.ToContractName());
                continue;
            }

            switch (record.Status)
            {
                case CompanionToolExecutionStatus.Success:
                    break;
                case CompanionToolExecutionStatus.NoData:
                    reasons.Add(CompanionOrchestrationReasonCodes.WithTool(
                        CompanionOrchestrationReasonCodes.RequiredToolReturnedNoDataPrefix,
                        required.Tool));
                    reasons.Add($"missing_required_{CompanionOrchestrationReasonCodes.ToReasonSuffix(required.Tool)}");
                    missingRequired.Add(required.Tool.ToContractName());
                    break;
                case CompanionToolExecutionStatus.SkippedCap:
                case CompanionToolExecutionStatus.SkippedContextCap:
                case CompanionToolExecutionStatus.SkippedDependency:
                    reasons.Add(CompanionOrchestrationReasonCodes.WithTool(
                        CompanionOrchestrationReasonCodes.CapExceededOrSkippedPrefix,
                        required.Tool));
                    reasons.Add($"missing_required_{CompanionOrchestrationReasonCodes.ToReasonSuffix(required.Tool)}");
                    missingRequired.Add(required.Tool.ToContractName());
                    break;
                default:
                    reasons.Add(CompanionOrchestrationReasonCodes.WithTool(
                        CompanionOrchestrationReasonCodes.RequiredToolFailedPrefix,
                        required.Tool));
                    reasons.Add($"missing_required_{CompanionOrchestrationReasonCodes.ToReasonSuffix(required.Tool)}");
                    missingRequired.Add(required.Tool.ToContractName());
                    break;
            }
        }

        foreach (var optionalRecord in records.Where(record => !record.PlannedTool.IsRequired))
        {
            if (optionalRecord.Status == CompanionToolExecutionStatus.Failed)
            {
                warnings.Add(CompanionOrchestrationReasonCodes.WithTool(
                    CompanionOrchestrationReasonCodes.OptionalToolFailedPrefix,
                    optionalRecord.PlannedTool.Tool));
            }
            else if (optionalRecord.Status == CompanionToolExecutionStatus.NoData)
            {
                warnings.Add(CompanionOrchestrationReasonCodes.WithTool(
                    CompanionOrchestrationReasonCodes.OptionalToolReturnedNoDataPrefix,
                    optionalRecord.PlannedTool.Tool));
            }
        }

        if (trimIndicators.Count > 0)
        {
            warnings.Add(CompanionOrchestrationReasonCodes.PayloadTrimmed);
        }

        var hasInsufficient = reasons.Count > 0;
        return new CompanionInsufficiencyDecision(
            CanProceedToAI: !hasInsufficient,
            HasInsufficientData: hasInsufficient,
            Reasons: reasons.Distinct(StringComparer.Ordinal).ToArray(),
            MissingRequiredTools: missingRequired.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
}
