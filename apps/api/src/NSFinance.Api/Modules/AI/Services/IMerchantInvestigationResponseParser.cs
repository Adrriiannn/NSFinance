using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Modules.AI.Services;

public interface IMerchantInvestigationResponseParser
{
    MerchantInvestigationParseResult Parse(AIResponse response);
}
