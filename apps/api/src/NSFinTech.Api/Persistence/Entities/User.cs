namespace NSFinTech.Api.Persistence.Entities;

public class User
{
    public Guid Id { get; set; }
    public string PrimaryEmail { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string OnboardingStatus { get; set; } = "not_started";
    public string Role { get; set; } = "user";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public bool EmailVerified { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsSuspended { get; set; }
    public bool DeletionRequested { get; set; }
    public DateTime? DeletionRequestedUtc { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string Locale { get; set; } = "en-US";
    public string PreferredCurrency { get; set; } = "EUR";
    public string PlanTier { get; set; } = "standard";
    public bool BiometricUnlockEnabled { get; set; }
    public string? SupportFlagsJson { get; set; }

    public ICollection<FinancialAccount> FinancialAccounts { get; set; } = [];
    public ICollection<ImportJob> ImportJobs { get; set; } = [];
    public ICollection<UserAuthProvider> AuthProviders { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<PolicyAcceptance> PolicyAcceptances { get; set; } = [];
    public ICollection<ConsentRecord> ConsentRecords { get; set; } = [];
    public ICollection<SupportRequest> SupportRequests { get; set; } = [];
    public ICollection<DeletionRequest> DeletionRequests { get; set; } = [];
    public ICollection<ExportRequest> ExportRequests { get; set; } = [];
    public ICollection<EmailActionToken> EmailActionTokens { get; set; } = [];
    public UserPreference? Preferences { get; set; }
    public PasswordCredential? PasswordCredential { get; set; }
}
