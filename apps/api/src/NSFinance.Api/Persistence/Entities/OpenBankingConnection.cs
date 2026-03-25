namespace NSFinance.Api.Persistence.Entities;

public class OpenBankingConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderEnvironment { get; set; } = "sandbox";
    public string? ProviderConnectionReference { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderDisplayName { get; set; }
    public string? ProviderIconUri { get; set; }
    public string? ProviderLogoUri { get; set; }
    public string? ProviderBrandBgColor { get; set; }
    public DateTime? BrandingLastSyncedAtUtc { get; set; }
    public string Status { get; set; } = "not_connected";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastSuccessfulSyncUtc { get; set; }
    public DateTime? LastSyncAttemptedUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorReason { get; set; }
    public string? AuthStateNonce { get; set; }
    public DateTime? AuthStateExpiresUtc { get; set; }

    public User? User { get; set; }
    public BankConnectionToken? Token { get; set; }
    public ICollection<LinkedBankAccount> LinkedAccounts { get; set; } = [];
}
