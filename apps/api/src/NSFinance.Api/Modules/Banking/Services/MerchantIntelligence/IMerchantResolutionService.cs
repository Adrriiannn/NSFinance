namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantResolutionService
{
    Task<MerchantResolutionResult> ResolveAsync(string rawDescriptor, CancellationToken cancellationToken);

    Task<MerchantResolutionResult> ResolveAsync(MerchantResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAsync(request.RawDescriptor, cancellationToken);
    }
}
