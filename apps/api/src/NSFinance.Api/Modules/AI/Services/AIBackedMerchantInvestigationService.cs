using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class AIBackedMerchantInvestigationService(
    IMerchantInvestigationOrchestrator orchestrator,
    ILogger<AIBackedMerchantInvestigationService> logger) : IMerchantInvestigationService
{
    public async Task<MerchantInvestigationResult> InvestigateAsync(
        MerchantInvestigationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await orchestrator.InvestigateAsync(request, cancellationToken);
        logger.LogDebug(
            "AI-backed merchant investigation completed normalizedDescriptor={NormalizedDescriptor} succeeded={Succeeded} insufficientEvidence={InsufficientEvidence} candidates={CandidateCount}",
            request.NormalizedDescriptor,
            result.Succeeded,
            result.InsufficientEvidence,
            result.Candidates.Count);

        return result;
    }
}
