using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAuthProvider> UserAuthProviders => Set<UserAuthProvider>();
    public DbSet<PasswordCredential> PasswordCredentials => Set<PasswordCredential>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionRefreshToken> SessionRefreshTokens => Set<SessionRefreshToken>();
    public DbSet<EmailActionToken> EmailActionTokens => Set<EmailActionToken>();
    public DbSet<AuthAttempt> AuthAttempts => Set<AuthAttempt>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserFinancialContextProfile> UserFinancialContextProfiles => Set<UserFinancialContextProfile>();
    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();
    public DbSet<PolicyAcceptance> PolicyAcceptances => Set<PolicyAcceptance>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();
    public DbSet<ExportRequest> ExportRequests => Set<ExportRequest>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionRelationship> TransactionRelationships => Set<TransactionRelationship>();
    public DbSet<TransactionCategory> TransactionCategories => Set<TransactionCategory>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OpenBankingConnection> OpenBankingConnections => Set<OpenBankingConnection>();
    public DbSet<BankConnectionAttempt> BankConnectionAttempts => Set<BankConnectionAttempt>();
    public DbSet<BankConnectionToken> BankConnectionTokens => Set<BankConnectionToken>();
    public DbSet<BankConnectionIdentityInfo> BankConnectionIdentityInfos => Set<BankConnectionIdentityInfo>();
    public DbSet<LinkedBankAccount> LinkedBankAccounts => Set<LinkedBankAccount>();
    public DbSet<LinkedBankCard> LinkedBankCards => Set<LinkedBankCard>();
    public DbSet<BankBalanceSnapshot> BankBalanceSnapshots => Set<BankBalanceSnapshot>();
    public DbSet<BankCardBalanceSnapshot> BankCardBalanceSnapshots => Set<BankCardBalanceSnapshot>();
    public DbSet<RawBankTransaction> RawBankTransactions => Set<RawBankTransaction>();
    public DbSet<NormalizedBankTransaction> NormalizedBankTransactions => Set<NormalizedBankTransaction>();
    public DbSet<RawBankCardTransaction> RawBankCardTransactions => Set<RawBankCardTransaction>();
    public DbSet<BankDirectDebit> BankDirectDebits => Set<BankDirectDebit>();
    public DbSet<BankStandingOrder> BankStandingOrders => Set<BankStandingOrder>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantAlias> MerchantAliases => Set<MerchantAlias>();
    public DbSet<MerchantBehaviorProfile> MerchantBehaviorProfiles => Set<MerchantBehaviorProfile>();
    public DbSet<MerchantCategoryHint> MerchantCategoryHints => Set<MerchantCategoryHint>();
    public DbSet<MerchantEvidence> MerchantEvidence => Set<MerchantEvidence>();
    public DbSet<UnresolvedMerchant> UnresolvedMerchants => Set<UnresolvedMerchant>();
    public DbSet<MerchantAIDecisionLog> MerchantAIDecisionLogs => Set<MerchantAIDecisionLog>();
    public DbSet<CompanionAIInteractionLog> CompanionAIInteractionLogs => Set<CompanionAIInteractionLog>();
    public DbSet<MerchantAliasConflict> MerchantAliasConflicts => Set<MerchantAliasConflict>();
    public DbSet<MerchantRevalidationRecord> MerchantRevalidationRecords => Set<MerchantRevalidationRecord>();
    public DbSet<OperationalFailureRecord> OperationalFailureRecords => Set<OperationalFailureRecord>();
    public DbSet<ConversationThread> ConversationThreads => Set<ConversationThread>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationStateSnapshot> ConversationStateSnapshots => Set<ConversationStateSnapshot>();
    public DbSet<ConversationSummary> ConversationSummaries => Set<ConversationSummary>();
    public DbSet<ConversationContextBuildLog> ConversationContextBuildLogs => Set<ConversationContextBuildLog>();
    public DbSet<ExpenseTrackerEntry> ExpenseTrackerEntries => Set<ExpenseTrackerEntry>();
    public DbSet<ExpensePlan> ExpensePlans => Set<ExpensePlan>();
    public DbSet<ExpensePlanLineItem> ExpensePlanLineItems => Set<ExpensePlanLineItem>();
    public DbSet<ExpensePlanPublication> ExpensePlanPublications => Set<ExpensePlanPublication>();
    public DbSet<ExpensePlanPublicationLike> ExpensePlanPublicationLikes => Set<ExpensePlanPublicationLike>();
    public DbSet<ExpensePlanPublicationDownload> ExpensePlanPublicationDownloads => Set<ExpensePlanPublicationDownload>();
    public DbSet<ExpensePlanPublicationReport> ExpensePlanPublicationReports => Set<ExpensePlanPublicationReport>();
    public DbSet<ExpensePlanPublicationModerationEvent> ExpensePlanPublicationModerationEvents => Set<ExpensePlanPublicationModerationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

