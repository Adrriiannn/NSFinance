namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantAcceptancePolicy
{
    MerchantAcceptanceDecision Evaluate(MerchantInvestigationResult result);
}
