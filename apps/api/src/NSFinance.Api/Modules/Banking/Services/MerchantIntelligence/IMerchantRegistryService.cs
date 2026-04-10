using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public interface IMerchantRegistryService
{
    Task<Merchant> CreateMerchantAsync(MerchantCreateRequest request, CancellationToken cancellationToken);
    Task<Merchant?> UpdateMerchantAsync(MerchantUpdateRequest request, CancellationToken cancellationToken);
    Task<MerchantAlias> AttachAliasAsync(MerchantAliasCreateRequest request, CancellationToken cancellationToken);
    Task<MerchantEvidence> AddEvidenceAsync(MerchantEvidenceCreateRequest request, CancellationToken cancellationToken);
    Task<MerchantCategoryHint> AddCategoryHintAsync(MerchantCategoryHintCreateRequest request, CancellationToken cancellationToken);
    Task<MerchantBehaviorProfile> UpsertBehaviorProfileAsync(MerchantBehaviorProfileUpsertRequest request, CancellationToken cancellationToken);
    Task<Merchant?> GetMerchantByIdAsync(Guid merchantId, CancellationToken cancellationToken);
    Task<MerchantIntelligencePackage?> GetMerchantIntelligencePackageAsync(Guid merchantId, CancellationToken cancellationToken);
}
