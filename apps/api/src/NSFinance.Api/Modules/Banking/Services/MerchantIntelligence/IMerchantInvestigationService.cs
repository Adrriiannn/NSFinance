namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantInvestigationService
{
    Task<MerchantInvestigationResult> InvestigateAsync(
        MerchantInvestigationRequest request,
        CancellationToken cancellationToken);
}
