using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
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

    [Fact]
    public async Task GetTransactionsAsync_SavingsRoundupRelationship_RemainsSourceSideOnly()
    {
        await using var dbContext = CreateDbContext();
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var merchantTransactionId = Guid.NewGuid();
        var roundupTransactionId = Guid.NewGuid();

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "relationship-tests@local",
            NormalizedEmail = "relationship-tests@local",
            DisplayName = "Relationship Tester",
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

        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Revolut Main",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now
        });

        dbContext.Transactions.AddRange(
            new Transaction
            {
                Id = merchantTransactionId,
                FinancialAccountId = accountId,
                Amount = -14.47m,
                Currency = "EUR",
                Description = "Tesco Stores",
                BookedAtUtc = now.AddMinutes(-2),
                CreatedUtc = now.AddMinutes(-2)
            },
            new Transaction
            {
                Id = roundupTransactionId,
                FinancialAccountId = accountId,
                Amount = -0.53m,
                Currency = "EUR",
                Description = "Spare change to Pocket",
                BookedAtUtc = now.AddMinutes(-1),
                CreatedUtc = now.AddMinutes(-1),
                TransferKind = TransactionTransferKind.SavingsRoundup,
                TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId,
                TaxonomyCategoryId = 92010,
                TaxonomySubcategoryId = 920102,
                DeterministicClassificationStatus = DeterministicClassificationStatus.ClassifiedMatchedRule,
                DeterministicClassificationVersion = currentVersion,
                DeterministicClassificationRuleKey = "savings_transfer.paired_signal_v3",
                DeterministicClassificationCategoryId = 92010,
                DeterministicClassificationSubcategoryId = 920102,
                DeterministicRelationshipType = "savings_transfer",
                DeterministicClassificationTerminal = true,
                DeterministicClassificationEvaluatedUtc = now.AddMinutes(-1)
            });

        dbContext.TransactionRelationships.Add(new TransactionRelationship
        {
            Id = Guid.NewGuid(),
            RelationshipKey = $"SavingsRoundup:{roundupTransactionId:N}:{merchantTransactionId:N}",
            RelationshipType = TransactionRelationshipType.SavingsRoundup,
            RelationshipStatus = TransactionRelationshipStatus.Active,
            RelationshipDirection = TransactionRelationshipDirection.OutflowToSavings,
            SourceTransactionId = roundupTransactionId,
            TargetTransactionId = merchantTransactionId,
            SourceFinancialAccountId = accountId,
            TargetFinancialAccountId = accountId,
            ConfidenceScore = 12,
            ConfidenceTier = "high",
            MatchReasonsJson = """{"reason":"roundup_pattern_match"}""",
            AnalyticsTreatment = "exclude_income_expense_include_savings_roundup",
            VirtualDestinationLabel = "Pocket",
            CreatedUtc = now,
            UpdatedUtc = now
        });

        await dbContext.SaveChangesAsync();

        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new ExpenseTaxonomyService());

        var rows = await service.GetTransactionsAsync(accountId, CancellationToken.None);
        var merchant = rows.Single(x => x.Id == merchantTransactionId);
        var roundup = rows.Single(x => x.Id == roundupTransactionId);

        Assert.Equal("real_transaction", merchant.DisplaySemantic);
        Assert.Null(merchant.RelationshipType);
        Assert.False(merchant.IsGloballyNeutralized);

        Assert.Equal("savings_manual_move", roundup.DisplaySemantic);
        Assert.Equal("savings_roundup", roundup.RelationshipType);
        Assert.True(roundup.IsGloballyNeutralized);
        Assert.Equal("savingsallocation", roundup.ReportingBucket);
    }

    [Fact]
    public async Task GetTransactionsAsync_DeterministicSemantic_OverridesLegacyTransferKindFallback()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "semantic-override@local",
            NormalizedEmail = "semantic-override@local",
            DisplayName = "Semantic Override Tester",
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

        dbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = accountId,
            UserId = userId,
            Name = "Main",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now
        });

        dbContext.Transactions.Add(new Transaction
        {
            Id = transactionId,
            FinancialAccountId = accountId,
            Amount = -0.55m,
            Currency = "EUR",
            Description = "Aux move",
            BookedAtUtc = now.AddMinutes(-10),
            CreatedUtc = now.AddMinutes(-10),
            TransferKind = TransactionTransferKind.LinkedInternal,
            DeterministicClassificationStatus = DeterministicClassificationStatus.ClassifiedMatchedRule,
            DeterministicClassificationVersion = currentVersion,
            DeterministicClassificationRuleKey = "savings_transfer.contextual_pattern_v4",
            DeterministicReasonCode = DeterministicClassificationReasonCodes.SavingsContextNearbySpend,
            DeterministicRelationshipType = "savings_transfer",
            DeterministicClassificationTerminal = true,
            DeterministicClassificationEvaluatedUtc = now.AddMinutes(-9)
        });

        await dbContext.SaveChangesAsync();

        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(userId),
            new ExpenseTaxonomyService());

        var rows = await service.GetTransactionsAsync(accountId, CancellationToken.None);
        var row = Assert.Single(rows);

        Assert.Equal("savings_roundup", row.DisplaySemantic);
    }

    [Fact]
    public async Task GetTransactionByIdAsync_DeterministicInternalTransfer_MaterializesTransferCategoryAndLinkedCounterpart()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDeterministicInternalTransferWithoutLegacyMaterializationAsync(dbContext);
        var taxonomy = new ExpenseTaxonomyService();
        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(seeded.UserId),
            taxonomy);

        var detail = await service.GetTransactionByIdAsync(seeded.OutflowTransactionId, CancellationToken.None);
        Assert.NotNull(detail);

        Assert.Equal(seeded.InflowTransactionId, detail!.LinkedTransferTransactionId);
        Assert.Equal(seeded.InflowTransactionId, detail.DeterministicLinkedTransactionId);
        Assert.Equal(seeded.RelationshipGroupId, detail.DeterministicRelationshipGroupId);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, detail.TaxonomyDomainId);
        Assert.Equal(ExpenseTaxonomyService.TransferDefaultCategoryId, detail.TaxonomyCategoryId);
        Assert.Equal(ExpenseTaxonomyService.TransferDefaultSubcategoryId, detail.TaxonomySubcategoryId);
        Assert.Equal(taxonomy.GetCategoryName(ExpenseTaxonomyService.TransferDefaultCategoryId), detail.TaxonomyCategoryName);
        Assert.Equal(taxonomy.GetSubcategoryName(ExpenseTaxonomyService.TransferDefaultSubcategoryId), detail.TaxonomySubcategoryName);
        Assert.Equal("linked_internal_transfer", detail.TransferKind);
        Assert.Equal("internal_transfer", detail.DeterministicRelationshipType);
        Assert.Equal("internal_transfer", detail.DisplaySemantic);
    }

    [Fact]
    public async Task GetTransactionsAsync_DeterministicInternalTransfer_ListAndDetailSemanticsAgree()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedDeterministicInternalTransferWithoutLegacyMaterializationAsync(dbContext);
        var service = new TransactionService(
            dbContext,
            new TestCurrentUserProvider(seeded.UserId),
            new ExpenseTaxonomyService());

        var list = await service.GetTransactionsAsync(null, CancellationToken.None);
        var listRow = list.Single(x => x.Id == seeded.OutflowTransactionId);
        var detailRow = await service.GetTransactionByIdAsync(seeded.OutflowTransactionId, CancellationToken.None);
        Assert.NotNull(detailRow);

        Assert.Equal(listRow.LinkedTransferTransactionId, detailRow!.LinkedTransferTransactionId);
        Assert.Equal(listRow.TaxonomyDomainId, detailRow.TaxonomyDomainId);
        Assert.Equal(listRow.TaxonomyCategoryId, detailRow.TaxonomyCategoryId);
        Assert.Equal(listRow.TaxonomySubcategoryId, detailRow.TaxonomySubcategoryId);
        Assert.Equal(listRow.CategoryName, detailRow.CategoryName);
        Assert.Equal(listRow.DisplaySemantic, detailRow.DisplaySemantic);
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

    private static async Task<(Guid UserId, Guid OutflowTransactionId, Guid InflowTransactionId, Guid RelationshipGroupId)> SeedDeterministicInternalTransferWithoutLegacyMaterializationAsync(AppDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var outflowAccountId = Guid.NewGuid();
        var inflowAccountId = Guid.NewGuid();
        var outflowTransactionId = Guid.NewGuid();
        var inflowTransactionId = Guid.NewGuid();
        var relationshipGroupId = Guid.NewGuid();
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        dbContext.Users.Add(new User
        {
            Id = userId,
            PrimaryEmail = "deterministic-transfer-materialization@local",
            NormalizedEmail = "deterministic-transfer-materialization@local",
            DisplayName = "Deterministic Materialization Tester",
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
                Id = outflowAccountId,
                UserId = userId,
                Name = "Revolut Main",
                Type = "Current",
                Currency = "EUR",
                CreatedUtc = now
            },
            new FinancialAccount
            {
                Id = inflowAccountId,
                UserId = userId,
                Name = "AIB Current",
                Type = "Current",
                Currency = "EUR",
                CreatedUtc = now
            });

        dbContext.Transactions.AddRange(
            new Transaction
            {
                Id = outflowTransactionId,
                FinancialAccountId = outflowAccountId,
                Amount = -1.00m,
                Currency = "EUR",
                Description = "To Marius",
                BookedAtUtc = now.AddMinutes(-5),
                CreatedUtc = now.AddMinutes(-5),
                DeterministicClassificationStatus = DeterministicClassificationStatus.ClassifiedMatchedRule,
                DeterministicClassificationVersion = currentVersion,
                DeterministicClassificationRuleKey = "bank_transfer.duplicate_cluster_stable_sequence_v3",
                DeterministicClassificationCategoryId = ExpenseTaxonomyService.TransferDefaultCategoryId,
                DeterministicClassificationSubcategoryId = ExpenseTaxonomyService.TransferDefaultSubcategoryId,
                DeterministicLinkedTransactionId = inflowTransactionId,
                DeterministicRelationshipType = "internal_transfer",
                DeterministicRelationshipGroupId = relationshipGroupId,
                DeterministicReasonCode = DeterministicClassificationReasonCodes.TransferPairStrictMatch,
                DeterministicClassificationTerminal = true,
                DeterministicClassificationEvaluatedUtc = now
            },
            new Transaction
            {
                Id = inflowTransactionId,
                FinancialAccountId = inflowAccountId,
                Amount = 1.00m,
                Currency = "EUR",
                Description = "Sent from Revolut",
                BookedAtUtc = now.AddMinutes(-4),
                CreatedUtc = now.AddMinutes(-4),
                DeterministicClassificationStatus = DeterministicClassificationStatus.ClassifiedMatchedRule,
                DeterministicClassificationVersion = currentVersion,
                DeterministicClassificationRuleKey = "bank_transfer.duplicate_cluster_stable_sequence_v3",
                DeterministicClassificationCategoryId = ExpenseTaxonomyService.TransferDefaultCategoryId,
                DeterministicClassificationSubcategoryId = ExpenseTaxonomyService.TransferDefaultSubcategoryId,
                DeterministicLinkedTransactionId = outflowTransactionId,
                DeterministicRelationshipType = "internal_transfer",
                DeterministicRelationshipGroupId = relationshipGroupId,
                DeterministicReasonCode = DeterministicClassificationReasonCodes.TransferPairStrictMatch,
                DeterministicClassificationTerminal = true,
                DeterministicClassificationEvaluatedUtc = now
            });

        await dbContext.SaveChangesAsync();

        return (userId, outflowTransactionId, inflowTransactionId, relationshipGroupId);
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
