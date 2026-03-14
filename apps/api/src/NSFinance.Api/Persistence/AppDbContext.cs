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
    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();
    public DbSet<PolicyAcceptance> PolicyAcceptances => Set<PolicyAcceptance>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();
    public DbSet<ExportRequest> ExportRequests => Set<ExportRequest>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionCategory> TransactionCategories => Set<TransactionCategory>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OpenBankingConnection> OpenBankingConnections => Set<OpenBankingConnection>();
    public DbSet<BankConnectionToken> BankConnectionTokens => Set<BankConnectionToken>();
    public DbSet<LinkedBankAccount> LinkedBankAccounts => Set<LinkedBankAccount>();
    public DbSet<BankBalanceSnapshot> BankBalanceSnapshots => Set<BankBalanceSnapshot>();
    public DbSet<RawBankTransaction> RawBankTransactions => Set<RawBankTransaction>();
    public DbSet<ExpenseTrackerEntry> ExpenseTrackerEntries => Set<ExpenseTrackerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
