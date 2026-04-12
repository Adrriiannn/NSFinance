namespace NSFinance.Api.Persistence.Entities;

public class Merchant
{
    public Guid Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string NormalizedCanonicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MerchantStatus MerchantStatus { get; set; } = MerchantStatus.Active;
    public MerchantType MerchantType { get; set; } = MerchantType.Unknown;
    public MerchantUsageType MerchantUsageType { get; set; } = MerchantUsageType.NarrowUse;
    public string PrimaryCountryCode { get; set; } = "ZZ";
    public string? OfficialWebsite { get; set; }
    public string? DescriptionSummary { get; set; }
    public Guid? ParentMerchantId { get; set; }
    public DateTime? LastValidatedUtc { get; set; }
    public DateTime? NextValidationDueUtc { get; set; }
    public int ValidationAttemptCount { get; set; }
    public string? LastValidationResultCode { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public Merchant? ParentMerchant { get; set; }
    public ICollection<Merchant> ChildMerchants { get; set; } = [];
    public ICollection<MerchantAlias> Aliases { get; set; } = [];
    public MerchantBehaviorProfile? BehaviorProfile { get; set; }
    public ICollection<MerchantCategoryHint> CategoryHints { get; set; } = [];
    public ICollection<MerchantEvidence> Evidence { get; set; } = [];
    public ICollection<MerchantRevalidationRecord> RevalidationRecords { get; set; } = [];
}
