using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Modules.Transactions.Services;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public class TransactionServiceTests
{
    [Fact]
    public async Task UpdateTransactionMetadataAsync_TransferEditOnLinkedPair_PropagatesTransferTaxonomyToCounterpart()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedTransferPairAsync(dbContext);
        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(seeded.UserId),
            new ExpenseTaxonomyService());

        var result = await service.UpdateTransactionMetadataAsync(
            seeded.DebitTransactionId,
            new UpdateTransactionMetadataRequest(
                Reason: "Moved into investments",
                Notes: "Internal transfer",
                TaxonomyCategoryId: 92010,
                TaxonomySubcategoryId: 920103),
            CancellationToken.None);

        Assert.NotNull(result.Transaction);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);

        var debit = await dbContext.Transactions.SingleAsync(x => x.Id == seeded.DebitTransactionId);
        var credit = await dbContext.Transactions.SingleAsync(x => x.Id == seeded.CreditTransactionId);

        Assert.Equal(TransactionTransferKind.LinkedInternal, debit.TransferKind);
        Assert.Equal(TransactionTransferKind.LinkedInternal, credit.TransferKind);
        Assert.Equal(seeded.CreditTransactionId, debit.LinkedTransferTransactionId);
        Assert.Equal(seeded.DebitTransactionId, credit.LinkedTransferTransactionId);

        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, debit.TaxonomyDomainId);
        Assert.Equal(92010, debit.TaxonomyCategoryId);
        Assert.Equal(920103, debit.TaxonomySubcategoryId);

        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, credit.TaxonomyDomainId);
        Assert.Equal(92010, credit.TaxonomyCategoryId);
        Assert.Equal(920103, credit.TaxonomySubcategoryId);
        Assert.NotNull(credit.MetadataUpdatedUtc);
    }

    [Fact]
    public async Task UpdateTransactionMetadataAsync_NonTransferEditOnLinkedPair_UnlinksBothSides()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedLinkedTransferPairAsync(dbContext);
        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(seeded.UserId),
            new ExpenseTaxonomyService());

        var result = await service.UpdateTransactionMetadataAsync(
            seeded.DebitTransactionId,
            new UpdateTransactionMetadataRequest(
                Reason: "Actually this was groceries",
                Notes: null,
                TaxonomyCategoryId: 13010,
                TaxonomySubcategoryId: 130101),
            CancellationToken.None);

        Assert.NotNull(result.Transaction);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);

        var debit = await dbContext.Transactions.SingleAsync(x => x.Id == seeded.DebitTransactionId);
        var credit = await dbContext.Transactions.SingleAsync(x => x.Id == seeded.CreditTransactionId);

        Assert.Null(debit.TransferKind);
        Assert.Null(debit.LinkedTransferTransactionId);
        Assert.Null(credit.TransferKind);
        Assert.Null(credit.LinkedTransferTransactionId);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"transaction-service-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(Guid UserId, Guid DebitTransactionId, Guid CreditTransactionId)> SeedLinkedTransferPairAsync(AppDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        var debitTransactionId = Guid.NewGuid();
        var creditTransactionId = Guid.NewGuid();

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "transfer-tests@local",
            NormalizedEmail = "transfer-tests@local",
            DisplayName = "Transfer Tester",
            Status = "active",
            OnboardingStatus = "profile_created",
            Role = "user",
            CreatedUtc = now,
            UpdatedUtc = now,
            EmailVerified = true,
            Timezone = "UTC",
            Locale = "en-IE",
            PreferredCurrency = "EUR",
            PlanTier = "standard"
        });

        dbContext.FinancialAccounts.AddRange(
            new FinancialAccount
            {
                Id = debitAccountId,
                UserId = userId,
                Name = "AIB Current",
                Type = "Current",
                Currency = "EUR",
                CreatedUtc = now
            },
            new FinancialAccount
            {
                Id = creditAccountId,
                UserId = userId,
                Name = "Revolut Main",
                Type = "Current",
                Currency = "EUR",
                CreatedUtc = now
            });

        dbContext.Transactions.AddRange(
            new Transaction
            {
                Id = debitTransactionId,
                FinancialAccountId = debitAccountId,
                Amount = -100m,
                Currency = "EUR",
                Description = "Transfer to Revolut",
                BookedAtUtc = now.AddMinutes(-5),
                CreatedUtc = now.AddMinutes(-5),
                TransferKind = TransactionTransferKind.LinkedInternal,
                LinkedTransferTransactionId = creditTransactionId,
                LinkedTransferMatchedUtc = now.AddMinutes(-5),
                TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId,
                TaxonomyCategoryId = ExpenseTaxonomyService.TransferDefaultCategoryId,
                TaxonomySubcategoryId = ExpenseTaxonomyService.TransferDefaultSubcategoryId
            },
            new Transaction
            {
                Id = creditTransactionId,
                FinancialAccountId = creditAccountId,
                Amount = 100m,
                Currency = "EUR",
                Description = "Transfer from AIB",
                BookedAtUtc = now.AddMinutes(-5),
                CreatedUtc = now.AddMinutes(-5),
                TransferKind = TransactionTransferKind.LinkedInternal,
                LinkedTransferTransactionId = debitTransactionId,
                LinkedTransferMatchedUtc = now.AddMinutes(-5),
                TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId,
                TaxonomyCategoryId = ExpenseTaxonomyService.TransferDefaultCategoryId,
                TaxonomySubcategoryId = ExpenseTaxonomyService.TransferDefaultSubcategoryId
            });

        await dbContext.SaveChangesAsync();
        return (userId, debitTransactionId, creditTransactionId);
    }

    private sealed class TestCurrentUserProvider(Guid userId) : ICurrentUserProvider
    {
        public Guid UserId => userId;

        public bool TryGetUserId(out Guid resolvedUserId)
        {
            resolvedUserId = userId;
            return true;
        }

        public bool TryGetSessionId(out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return false;
        }
    }
}
