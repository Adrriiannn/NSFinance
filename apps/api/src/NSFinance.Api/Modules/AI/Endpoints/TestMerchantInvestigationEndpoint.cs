using NSFinance.Api.Modules.AI.DTOs;
using NSFinance.Api.Modules.AI.Services;
using NSFinance.Api.Modules.AI.Validators;
using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Endpoints;

public static class TestMerchantInvestigationEndpoint
{
    public static async Task<IResult> HandleAsync(
        MerchantInvestigationTestRequest request,
        IMerchantInvestigationOrchestrator merchantInvestigationOrchestrator,
        IMerchantAcceptancePolicy merchantAcceptancePolicy,
        MerchantDescriptorNormalizer merchantDescriptorNormalizer,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("AI.TestMerchantInvestigationEndpoint");
        var errors = AiEndpointRequestValidators.ValidateMerchantInvestigationRequest(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var normalizedDescriptor = string.IsNullOrWhiteSpace(request.NormalizedDescriptor)
            ? merchantDescriptorNormalizer.Normalize(request.RawDescriptor)
            : merchantDescriptorNormalizer.Normalize(request.NormalizedDescriptor);
        var triggerSource = string.IsNullOrWhiteSpace(request.TriggerSource)
            ? "internal_diagnostic"
            : request.TriggerSource.Trim();

        var result = await merchantInvestigationOrchestrator.InvestigateAsync(
            new MerchantInvestigationRequest(
                RawDescriptor: request.RawDescriptor.Trim(),
                NormalizedDescriptor: normalizedDescriptor,
                TriggerSource: triggerSource),
            cancellationToken);

        var acceptance = merchantAcceptancePolicy.Evaluate(result);
        var response = new MerchantInvestigationTestResponse(
            DryRun: true,
            NormalizedDescriptor: normalizedDescriptor,
            Succeeded: result.Succeeded,
            InsufficientEvidence: result.InsufficientEvidence,
            Recommendation: result.Recommendation.ToString(),
            OverallConfidence: result.OverallConfidence,
            AmbiguityLevel: result.AmbiguityLevel,
            ParserRejected: result.ParserRejected,
            FailureReason: result.FailureReason,
            AcceptanceDecision: acceptance.DecisionType.ToString(),
            AcceptanceConfidence: acceptance.Confidence,
            AcceptanceReasonCodes: acceptance.ReasonCodes,
            InvestigationReasonCodes: result.InvestigationReasonCodes ?? [],
            Candidates: result.Candidates
                .Select(candidate => new MerchantInvestigationCandidateDto(
                    ExistingMerchantId: candidate.ExistingMerchantId,
                    CanonicalName: candidate.CanonicalName,
                    DisplayName: candidate.DisplayName,
                    MerchantType: candidate.MerchantType.ToString(),
                    MerchantUsageType: candidate.MerchantUsageType.ToString(),
                    PrimaryCountryCode: candidate.PrimaryCountryCode,
                    Confidence: candidate.Confidence,
                    DescriptorMatchStrength: candidate.DescriptorMatchStrength,
                    EntityMatchStrength: candidate.EntityMatchStrength,
                    MixedUseRisk: candidate.MixedUseRisk,
                    HasContradictions: candidate.HasContradictions,
                    DomainNameMismatchRisk: candidate.DomainNameMismatchRisk,
                    WeakSourceRisk: candidate.WeakSourceRisk,
                    SuspiciousIdentityRisk: candidate.SuspiciousIdentityRisk,
                    WhyItMayMatch: candidate.WhyItMayMatch,
                    WhyItMayBeWrong: candidate.WhyItMayBeWrong,
                    OfficialWebsite: candidate.OfficialWebsite,
                    DescriptionSummary: candidate.DescriptionSummary))
                .ToArray(),
            Evidence: result.Evidence
                .Select(evidence => new MerchantInvestigationEvidenceDto(
                    EvidenceType: evidence.EvidenceType.ToString(),
                    Summary: evidence.EvidenceSummary,
                    Confidence: evidence.Confidence,
                    SourceReference: evidence.SourceReference,
                    SourceClass: evidence.SourceClass,
                    Relevance: evidence.Relevance,
                    SourceTrustLevel: evidence.SourceTrustLevel.ToString()))
                .ToArray(),
            AliasSuggestions: (result.AliasSuggestions ?? [])
                .Select(alias => new MerchantInvestigationAliasSuggestionDto(
                    AliasText: alias.AliasText,
                    AliasType: alias.AliasType,
                    Confidence: alias.Confidence,
                    Notes: alias.Notes,
                    IsPreferred: alias.IsPreferred))
                .ToArray());

        logger.LogInformation(
            "Merchant investigation diagnostic completed normalizedDescriptor={NormalizedDescriptor} dryRun={DryRun} succeeded={Succeeded} recommendation={Recommendation} candidates={Candidates} acceptanceDecision={AcceptanceDecision} acceptanceConfidence={AcceptanceConfidence}",
            response.NormalizedDescriptor,
            response.DryRun,
            response.Succeeded,
            response.Recommendation,
            response.Candidates.Count,
            response.AcceptanceDecision,
            response.AcceptanceConfidence);

        if (!response.Succeeded)
        {
            if ((response.FailureReason ?? string.Empty).Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                || (response.FailureReason ?? string.Empty).Contains("provider", StringComparison.OrdinalIgnoreCase)
                || (response.FailureReason ?? string.Empty).Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        return Results.Ok(response);
    }
}
