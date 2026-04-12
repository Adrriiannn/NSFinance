using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public interface IMerchantInvestigationResultCache
{
    bool TryGet(string normalizedDescriptor, DateTime nowUtc, out MerchantInvestigationResult result);
    void Set(string normalizedDescriptor, MerchantInvestigationResult result, DateTime nowUtc, AIExecutionOptions options);
}
