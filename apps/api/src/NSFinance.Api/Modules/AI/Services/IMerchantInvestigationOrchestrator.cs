using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public interface IMerchantInvestigationOrchestrator
{
    Task<MerchantInvestigationResult> InvestigateAsync(
        MerchantInvestigationRequest request,
        CancellationToken cancellationToken);
}
