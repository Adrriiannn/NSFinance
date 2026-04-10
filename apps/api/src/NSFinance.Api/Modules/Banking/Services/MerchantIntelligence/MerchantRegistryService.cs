using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

public sealed class MerchantRegistryService(
    AppDbContext dbContext,
    MerchantDescriptorNormalizer normalizer,
    ILogger<MerchantRegistryService> logger) : IMerchantRegistryService
{
    public async Task<Merchant> CreateMerchantAsync(MerchantCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalName = NormalizeRequired(request.CanonicalName, 160, nameof(request.CanonicalName));
        var normalizedCanonicalName = normalizer.Normalize(canonicalName);
        if (normalizedCanonicalName.Length == 0)
        {
            throw new ArgumentException("Canonical name cannot normalize to empty value.", nameof(request.CanonicalName));
        }

        var existing = await dbContext.Merchants
            .SingleOrDefaultAsync(
                x => x.NormalizedCanonicalName == normalizedCanonicalName,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var nowUtc = DateTime.UtcNow;
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            CanonicalName = canonicalName,
            NormalizedCanonicalName = normalizedCanonicalName,
            DisplayName = NormalizeRequired(request.DisplayName, 160, nameof(request.DisplayName)),
            MerchantStatus = request.MerchantStatus,
            MerchantType = request.MerchantType,
            MerchantUsageType = request.MerchantUsageType,
            PrimaryCountryCode = NormalizeCountryCode(request.PrimaryCountryCode),
            OfficialWebsite = NormalizeOptional(request.OfficialWebsite, 512),
            DescriptionSummary = NormalizeOptional(request.DescriptionSummary, 1024),
            ParentMerchantId = request.ParentMerchantId,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };

        dbContext.Merchants.Add(merchant);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Merchant created merchantId={MerchantId} canonicalName={CanonicalName} status={Status} type={Type} usage={Usage}",
            merchant.Id,
            merchant.CanonicalName,
            merchant.MerchantStatus,
            merchant.MerchantType,
            merchant.MerchantUsageType);

        return merchant;
    }

    public async Task<Merchant?> UpdateMerchantAsync(MerchantUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == request.MerchantId, cancellationToken);

        if (merchant is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            merchant.DisplayName = NormalizeRequired(request.DisplayName, 160, nameof(request.DisplayName));
        }

        if (request.MerchantStatus.HasValue)
        {
            merchant.MerchantStatus = request.MerchantStatus.Value;
        }

        if (request.MerchantType.HasValue)
        {
            merchant.MerchantType = request.MerchantType.Value;
        }

        if (request.MerchantUsageType.HasValue)
        {
            merchant.MerchantUsageType = request.MerchantUsageType.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.PrimaryCountryCode))
        {
            merchant.PrimaryCountryCode = NormalizeCountryCode(request.PrimaryCountryCode);
        }

        merchant.OfficialWebsite = NormalizeOptional(request.OfficialWebsite, 512);
        merchant.DescriptionSummary = NormalizeOptional(request.DescriptionSummary, 1024);
        merchant.ParentMerchantId = request.ParentMerchantId;
        merchant.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return merchant;
    }

    public async Task<MerchantAlias> AttachAliasAsync(MerchantAliasCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == request.MerchantId, cancellationToken);
        if (merchant is null)
        {
            throw new InvalidOperationException($"Merchant {request.MerchantId} was not found.");
        }

        var aliasText = NormalizeRequired(request.AliasText, 320, nameof(request.AliasText));
        var normalizedAliasText = normalizer.Normalize(aliasText);
        if (normalizedAliasText.Length == 0)
        {
            throw new ArgumentException("Alias cannot normalize to empty value.", nameof(request.AliasText));
        }

        var conflictingAliasExists = await dbContext.MerchantAliases
            .AnyAsync(
                x => x.MerchantId != request.MerchantId
                     && x.IsActive
                     && x.NormalizedAliasText == normalizedAliasText
                     && x.AliasType == request.AliasType,
                cancellationToken);

        if (conflictingAliasExists)
        {
            throw new InvalidOperationException(
                "Alias is already linked to a different active merchant. Manual review is required.");
        }

        var nowUtc = DateTime.UtcNow;
        var existing = await dbContext.MerchantAliases
            .SingleOrDefaultAsync(
                x => x.MerchantId == request.MerchantId
                     && x.NormalizedAliasText == normalizedAliasText
                     && x.AliasType == request.AliasType,
                cancellationToken);

        if (existing is not null)
        {
            existing.AliasText = aliasText;
            existing.Confidence = Math.Clamp(request.Confidence, 0d, 1d);
            existing.IsExactMatchPreferred = request.IsExactMatchPreferred;
            existing.Source = NormalizeRequired(request.Source, 120, nameof(request.Source));
            existing.LastSeenUtc = nowUtc;
            existing.IsActive = request.IsActive;
            merchant.UpdatedUtc = nowUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var alias = new MerchantAlias
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            AliasText = aliasText,
            NormalizedAliasText = normalizedAliasText,
            AliasType = request.AliasType,
            Confidence = Math.Clamp(request.Confidence, 0d, 1d),
            IsExactMatchPreferred = request.IsExactMatchPreferred,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc,
            Source = NormalizeRequired(request.Source, 120, nameof(request.Source)),
            IsActive = request.IsActive
        };

        dbContext.MerchantAliases.Add(alias);
        merchant.UpdatedUtc = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return alias;
    }

    public async Task<MerchantEvidence> AddEvidenceAsync(MerchantEvidenceCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == request.MerchantId, cancellationToken);
        if (merchant is null)
        {
            throw new InvalidOperationException($"Merchant {request.MerchantId} was not found.");
        }

        var nowUtc = DateTime.UtcNow;
        var evidence = new MerchantEvidence
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            EvidenceType = request.EvidenceType,
            EvidenceSummary = NormalizeRequired(request.EvidenceSummary, 1200, nameof(request.EvidenceSummary)),
            Confidence = Math.Clamp(request.Confidence, 0d, 1d),
            SourceReference = NormalizeOptional(request.SourceReference, 1024),
            CapturedUtc = nowUtc
        };

        dbContext.MerchantEvidence.Add(evidence);
        merchant.UpdatedUtc = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return evidence;
    }

    public async Task<MerchantCategoryHint> AddCategoryHintAsync(MerchantCategoryHintCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == request.MerchantId, cancellationToken);
        if (merchant is null)
        {
            throw new InvalidOperationException($"Merchant {request.MerchantId} was not found.");
        }

        var existing = await dbContext.MerchantCategoryHints
            .SingleOrDefaultAsync(
                x => x.MerchantId == request.MerchantId
                     && x.DomainId == request.DomainId
                     && x.CategoryId == request.CategoryId
                     && x.SubcategoryId == request.SubcategoryId
                     && x.Source == request.Source,
                cancellationToken);

        if (existing is not null)
        {
            existing.Confidence = Math.Clamp(request.Confidence, 0d, 1d);
            existing.HintStrength = request.HintStrength;
            existing.IsActive = request.IsActive;
            merchant.UpdatedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var hint = new MerchantCategoryHint
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            DomainId = request.DomainId,
            CategoryId = request.CategoryId,
            SubcategoryId = request.SubcategoryId,
            Confidence = Math.Clamp(request.Confidence, 0d, 1d),
            HintStrength = request.HintStrength,
            Source = NormalizeRequired(request.Source, 120, nameof(request.Source)),
            IsActive = request.IsActive
        };

        dbContext.MerchantCategoryHints.Add(hint);
        merchant.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return hint;
    }

    public async Task<MerchantBehaviorProfile> UpsertBehaviorProfileAsync(
        MerchantBehaviorProfileUpsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var merchant = await dbContext.Merchants
            .SingleOrDefaultAsync(x => x.Id == request.MerchantId, cancellationToken);
        if (merchant is null)
        {
            throw new InvalidOperationException($"Merchant {request.MerchantId} was not found.");
        }

        var profile = await dbContext.MerchantBehaviorProfiles
            .SingleOrDefaultAsync(x => x.MerchantId == request.MerchantId, cancellationToken);

        if (profile is null)
        {
            profile = new MerchantBehaviorProfile
            {
                MerchantId = request.MerchantId
            };
            dbContext.MerchantBehaviorProfiles.Add(profile);
        }

        profile.SupportsSubscriptions = request.SupportsSubscriptions;
        profile.SupportsRecurringPayments = request.SupportsRecurringPayments;
        profile.SupportsOneTimePurchases = request.SupportsOneTimePurchases;
        profile.SupportsMarketplacePayments = request.SupportsMarketplacePayments;
        profile.SupportsInAppPurchases = request.SupportsInAppPurchases;
        profile.AnnualRenewalsCommon = request.AnnualRenewalsCommon;
        profile.RefundsCommon = request.RefundsCommon;
        profile.MixedUseRisk = request.MixedUseRisk;
        profile.PaymentBehaviorConfidence = Math.Clamp(request.PaymentBehaviorConfidence, 0d, 1d);
        profile.BehaviorSummary = NormalizeRequired(request.BehaviorSummary, 1200, nameof(request.BehaviorSummary));

        merchant.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public Task<Merchant?> GetMerchantByIdAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        return dbContext.Merchants
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == merchantId, cancellationToken);
    }

    public async Task<MerchantIntelligencePackage?> GetMerchantIntelligencePackageAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var merchant = await dbContext.Merchants
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == merchantId, cancellationToken);
        if (merchant is null)
        {
            return null;
        }

        var aliasesTask = dbContext.MerchantAliases
            .AsNoTracking()
            .Where(x => x.MerchantId == merchantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.IsExactMatchPreferred)
            .ThenByDescending(x => x.Confidence)
            .ToListAsync(cancellationToken);
        var behaviorTask = dbContext.MerchantBehaviorProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.MerchantId == merchantId, cancellationToken);
        var hintsTask = dbContext.MerchantCategoryHints
            .AsNoTracking()
            .Where(x => x.MerchantId == merchantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Confidence)
            .ToListAsync(cancellationToken);
        var evidenceTask = dbContext.MerchantEvidence
            .AsNoTracking()
            .Where(x => x.MerchantId == merchantId)
            .OrderByDescending(x => x.CapturedUtc)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(aliasesTask, behaviorTask, hintsTask, evidenceTask);

        return new MerchantIntelligencePackage(
            Merchant: merchant,
            Aliases: aliasesTask.Result,
            BehaviorProfile: behaviorTask.Result,
            CategoryHints: hintsTask.Result,
            Evidence: evidenceTask.Result);
    }

    private static string NormalizeRequired(string value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string NormalizeCountryCode(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return "ZZ";
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized.Length < 2)
        {
            return "ZZ";
        }

        return normalized[..2];
    }
}
