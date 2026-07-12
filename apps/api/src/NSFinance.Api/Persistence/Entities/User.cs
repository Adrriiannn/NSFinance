namespace NSFinance.Api.Persistence.Entities;

public class User
{
    public Guid Id { get; set; }
    public string PrimaryEmail { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? ProfileSubtitle { get; set; }
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
    public bool TwoFactorEnabled { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? PhoneVerifiedUtc { get; set; }
    public string? PendingPhoneNumber { get; set; }
    public DateTime? PendingPhoneRequestedUtc { get; set; }
    public bool PhoneRecoveryEnabled { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? CountryRegion { get; set; }
    public string? FinancialFocusJson { get; set; }
    public string? EmploymentStatus { get; set; }
    public string? IncomeStability { get; set; }
    public string? PrimaryFinancialConcern { get; set; }
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
    public ICollection<IdentityChallenge> IdentityChallenges { get; set; } = [];
    public ICollection<TransactionalMessage> TransactionalMessages { get; set; } = [];
    public ICollection<TotpAuthenticator> TotpAuthenticators { get; set; } = [];
    public ICollection<MfaTrustedDevice> MfaTrustedDevices { get; set; } = [];
    public ICollection<OpenBankingConnection> OpenBankingConnections { get; set; } = [];
    public ICollection<ExpenseTrackerEntry> ExpenseTrackerEntries { get; set; } = [];
    public ICollection<ExpensePlan> ExpensePlans { get; set; } = [];
    public ICollection<ExpensePlanPublication> ExpensePlanPublications { get; set; } = [];
    public ICollection<ExpensePlanPublicationLike> ExpensePlanPublicationLikes { get; set; } = [];
    public ICollection<ExpensePlanPublicationDownload> ExpensePlanPublicationDownloads { get; set; } = [];
    public ICollection<ExpensePlanPublicationReport> ExpensePlanPublicationReports { get; set; } = [];
    public UserPreference? Preferences { get; set; }
    public UserFinancialContextProfile? FinancialContextProfile { get; set; }
    public PasswordCredential? PasswordCredential { get; set; }
}

