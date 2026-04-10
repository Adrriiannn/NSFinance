namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class StubMerchantInvestigationService(
    ILogger<StubMerchantInvestigationService> logger) : IMerchantInvestigationService
{
    public Task<MerchantInvestigationResult> InvestigateAsync(
        MerchantInvestigationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Merchant investigation stub invoked normalizedDescriptor={NormalizedDescriptor} triggerSource={TriggerSource} outcome=insufficient_evidence",
            request.NormalizedDescriptor,
            request.TriggerSource);

        return Task.FromResult(
            new MerchantInvestigationResult(
                Succeeded: true,
                InsufficientEvidence: true,
                Candidates: [],
                Evidence: [],
                FailureReason: "Investigation provider is not configured yet."));
    }
}
