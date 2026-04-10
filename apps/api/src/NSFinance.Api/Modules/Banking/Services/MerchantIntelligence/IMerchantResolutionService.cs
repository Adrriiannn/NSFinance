namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantResolutionService
{
    Task<MerchantResolutionResult> ResolveAsync(string rawDescriptor, CancellationToken cancellationToken);
}
