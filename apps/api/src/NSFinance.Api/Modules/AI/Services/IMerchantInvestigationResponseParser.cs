using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public interface IMerchantInvestigationResponseParser
{
    bool TryParse(AIResponse response, out MerchantInvestigationResult result, out IReadOnlyList<string> reasonCodes);
}
