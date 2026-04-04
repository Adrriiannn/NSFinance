using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Modules.Transactions.TransferPolicy;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Integration;

public class OpenBankingIntegrationTests
{
    private const string SyntheticOutboundTransferDescription = "Outbound Transfer Holder Alpha";
    private const string SyntheticInboundTransferDescription = "Inbound Transfer Holder Alpha";
    private const string SyntheticSavingsDestinationLabel = "Internal Savings Pocket";

    [Fact]
    public async Task CallbackFlow_SuccessfullyIngestsAccountsBalancesAndTransactions()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.success@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.Synced, connection.Status);
        Assert.NotNull(connection.Token);
        Assert.False(string.IsNullOrWhiteSpace(connection.Token!.EncryptedRefreshToken));
        Assert.NotEqual("refresh-token-1", connection.Token!.EncryptedRefreshToken);

        Assert.Single(await harness.DbContext.LinkedBankAccounts.ToListAsync());
        Assert.Single(await harness.DbContext.BankBalanceSnapshots.ToListAsync());
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Single(await harness.DbContext.FinancialAccounts.ToListAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());
    }

    [Fact]
    public async Task CallbackFlow_PersistsBalanceSnapshot_WhenTransactionStageFails()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: BalanceSuccessTransactionFailureFlowHandler());

        var user = await harness.CreateUserAsync("bank.balance-durable@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-balance-durable", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);

        Assert.Equal(BankConnectionStatuses.Failed, connection.Status);
        Assert.Single(await harness.DbContext.BankBalanceSnapshots.ToListAsync());
        Assert.Equal(0, await harness.DbContext.RawBankTransactions.CountAsync());
    }

    [Fact]
    public async Task CallbackFlow_MatchesLinkedInternalTransfers_ForAibLikeDateOnlyAndCounterpartyHints()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulCrossBankTransferFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-match@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-link", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var transactions = await harness.DbContext.Transactions
            .OrderBy(x => x.Amount)
            .ToListAsync();

        Assert.Equal(2, transactions.Count);

        var debit = transactions.Single(x => x.Amount < 0);
        var credit = transactions.Single(x => x.Amount > 0);

        Assert.Equal(TransactionTransferKind.LinkedInternal, debit.TransferKind);
        Assert.Equal(TransactionTransferKind.LinkedInternal, credit.TransferKind);
        Assert.Equal(credit.Id, debit.LinkedTransferTransactionId);
        Assert.Equal(debit.Id, credit.LinkedTransferTransactionId);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, debit.TaxonomyDomainId);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, credit.TaxonomyDomainId);
    }

    [Fact]
    public async Task CallbackFlow_DoesNotMislinkSavingsPocketTransfer_WhenCrossBankCounterpartyMatchExists()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: CrossBankTransferWithSavingsPocketFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-pocket-guard@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-pocket-guard", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var transactions = await harness.DbContext.Transactions
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        Assert.Equal(3, transactions.Count);

        var outboundLinkedTransfer = transactions.Single(x => x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase));
        var outboundSavingsMovement = transactions.Single(x => x.Description.Contains(SyntheticSavingsDestinationLabel, StringComparison.OrdinalIgnoreCase));
        var inboundLinkedTransfer = transactions.Single(x => x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(inboundLinkedTransfer.Id, outboundLinkedTransfer.LinkedTransferTransactionId);
        Assert.Equal(outboundLinkedTransfer.Id, inboundLinkedTransfer.LinkedTransferTransactionId);
        Assert.Equal(TransactionTransferKind.LinkedInternal, outboundLinkedTransfer.TransferKind);
        Assert.Equal(TransactionTransferKind.LinkedInternal, inboundLinkedTransfer.TransferKind);

        Assert.Null(outboundSavingsMovement.LinkedTransferTransactionId);
        Assert.Equal(TransactionTransferKind.SavingsManualDeposit, outboundSavingsMovement.TransferKind);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, outboundSavingsMovement.TaxonomyDomainId);
        Assert.Equal(920102, outboundSavingsMovement.TaxonomySubcategoryId);

        var savingsRelationship = await harness.DbContext.TransactionRelationships
            .SingleAsync(x =>
                x.SourceTransactionId == outboundSavingsMovement.Id
                && x.RelationshipType == TransactionRelationshipType.SavingsManualDeposit
                && x.RelationshipStatus == TransactionRelationshipStatus.Active);
        Assert.Equal("exclude_income_expense_include_savings_flow", savingsRelationship.AnalyticsTreatment);
    }

    [Fact]
    public async Task CallbackFlow_MatchesRepeatedSameAmountTransfers_WithSameDayPreference()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: RepeatedSameAmountTransferChainFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-repeated-chain@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-repeated-chain", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var transactions = await harness.DbContext.Transactions
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        var aibIncoming = transactions
            .Where(x => x.Amount > 0m && x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToList();
        var revolutOutgoing = transactions
            .Where(x => x.Amount < 0m && x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToList();
        var pocketMovement = transactions
            .Single(x => x.Description.Contains(SyntheticSavingsDestinationLabel, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(4, aibIncoming.Count);
        Assert.Equal(4, revolutOutgoing.Count);
        Assert.Equal(TransactionTransferKind.SavingsManualDeposit, pocketMovement.TransferKind);
        Assert.Null(pocketMovement.LinkedTransferTransactionId);

        foreach (var credit in aibIncoming)
        {
            Assert.True(credit.LinkedTransferTransactionId.HasValue);
            var linkedDebit = revolutOutgoing.Single(x => x.Id == credit.LinkedTransferTransactionId.Value);
            Assert.Equal(credit.BookedAtUtc.Date, linkedDebit.BookedAtUtc.Date);
            Assert.Equal(TransactionTransferKind.LinkedInternal, credit.TransferKind);
            Assert.Equal(TransactionTransferKind.LinkedInternal, linkedDebit.TransferKind);
        }
    }

    [Fact]
    public async Task GlobalSync_DeterministicOrganization_ReappliesLinkedTransferAndSavingsClassification_InSinglePipeline()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: RepeatedSameAmountTransferChainFlowHandler());

        var user = await harness.CreateUserAsync("bank.unified-deterministic-organization@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-unified-deterministic-organization", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var transactions = await harness.DbContext.Transactions
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        var transferRows = transactions
            .Where(x =>
                x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var savingsRow = transactions.Single(x =>
            x.Description.Contains(SyntheticSavingsDestinationLabel, StringComparison.OrdinalIgnoreCase));

        var transferRowIds = transferRows.Select(x => x.Id).ToHashSet();

        foreach (var row in transferRows)
        {
            row.TransferKind = null;
            row.LinkedTransferTransactionId = null;
            row.LinkedTransferMatchedUtc = null;
            row.TransferMatchConfidenceScore = null;
            row.TransferMatchConfidenceTier = null;
            row.TransferMatchReason = null;
            row.TaxonomyDomainId = null;
            row.TaxonomyCategoryId = null;
            row.TaxonomySubcategoryId = null;
            row.DeterministicEnrichmentVersion = null;
            row.LastDeterministicEnrichedUtc = null;
        }

        savingsRow.TransferKind = null;
        savingsRow.LinkedTransferTransactionId = null;
        savingsRow.LinkedTransferMatchedUtc = null;
        savingsRow.TransferMatchConfidenceScore = null;
        savingsRow.TransferMatchConfidenceTier = null;
        savingsRow.TransferMatchReason = null;
        savingsRow.TaxonomyDomainId = null;
        savingsRow.TaxonomyCategoryId = null;
        savingsRow.TaxonomySubcategoryId = null;
        savingsRow.DeterministicEnrichmentVersion = null;
        savingsRow.LastDeterministicEnrichedUtc = null;

        var relationshipsToRemove = await harness.DbContext.TransactionRelationships
            .Where(x =>
                transferRowIds.Contains(x.SourceTransactionId)
                || (x.TargetTransactionId.HasValue && transferRowIds.Contains(x.TargetTransactionId.Value))
                || x.SourceTransactionId == savingsRow.Id)
            .ToListAsync();
        harness.DbContext.TransactionRelationships.RemoveRange(relationshipsToRemove);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var secondSync = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_unified_deterministic_organization",
            force: true,
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", secondSync.Outcome);

        var refreshed = await harness.DbContext.Transactions
            .Where(x => transferRowIds.Contains(x.Id) || x.Id == savingsRow.Id)
            .ToListAsync();

        var linkedTransferRows = refreshed
            .Where(x =>
                x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var refreshedSavingsRow = refreshed.Single(x => x.Id == savingsRow.Id);

        Assert.NotEmpty(linkedTransferRows);
        Assert.All(linkedTransferRows, row =>
        {
            Assert.Equal(TransactionTransferKind.LinkedInternal, row.TransferKind);
            Assert.True(row.LinkedTransferTransactionId.HasValue);
            Assert.True(row.DeterministicEnrichmentVersion.HasValue && row.DeterministicEnrichmentVersion.Value >= 2);
        });

        Assert.Equal(TransactionTransferKind.SavingsManualDeposit, refreshedSavingsRow.TransferKind);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, refreshedSavingsRow.TaxonomyDomainId);
        Assert.Equal(920102, refreshedSavingsRow.TaxonomySubcategoryId);
        Assert.True(refreshedSavingsRow.DeterministicEnrichmentVersion.HasValue && refreshedSavingsRow.DeterministicEnrichmentVersion.Value >= 2);
    }

    [Fact]
    public async Task GlobalSync_RepairsStaleWrongRepeatedSameAmountLinks_ToSameDayCounterparts()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: RepeatedSameAmountTransferChainFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-repair-chain@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-repair-chain", state, null, null),
            CancellationToken.None);
        Assert.True(outcome.Succeeded);

        var incoming = await harness.DbContext.Transactions
            .Where(x => x.Amount > 0m && x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();
        var outgoing = await harness.DbContext.Transactions
            .Where(x => x.Amount < 0m && x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        Assert.True(incoming.Count >= 4);
        Assert.True(outgoing.Count >= 4);

        var latestIncoming = incoming[^1];
        var correctLatestOutgoing = outgoing.Single(x => x.BookedAtUtc.Date == latestIncoming.BookedAtUtc.Date);
        var wrongPriorDayOutgoing = outgoing[^2];

        latestIncoming.LinkedTransferTransactionId = wrongPriorDayOutgoing.Id;
        latestIncoming.TransferKind = TransactionTransferKind.LinkedInternal;
        latestIncoming.TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId;

        wrongPriorDayOutgoing.LinkedTransferTransactionId = latestIncoming.Id;
        wrongPriorDayOutgoing.TransferKind = TransactionTransferKind.LinkedInternal;
        wrongPriorDayOutgoing.TaxonomyDomainId = ExpenseTaxonomyService.TransferDomainId;

        correctLatestOutgoing.LinkedTransferTransactionId = null;
        correctLatestOutgoing.TransferKind = null;
        correctLatestOutgoing.TaxonomyDomainId = null;
        correctLatestOutgoing.TaxonomyCategoryId = null;
        correctLatestOutgoing.TaxonomySubcategoryId = null;

        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var secondSync = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_repeated_chain_repair",
            force: true,
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", secondSync.Outcome);

        var repairedIncoming = await harness.DbContext.Transactions
            .Where(x => x.Amount > 0m && x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();
        var repairedOutgoing = await harness.DbContext.Transactions
            .Where(x => x.Amount < 0m && x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        foreach (var credit in repairedIncoming)
        {
            Assert.True(credit.LinkedTransferTransactionId.HasValue);
            var linkedDebit = repairedOutgoing.Single(x => x.Id == credit.LinkedTransferTransactionId.Value);
            Assert.Equal(credit.BookedAtUtc.Date, linkedDebit.BookedAtUtc.Date);
        }
    }

    [Fact]
    public async Task CallbackFlow_DoesNotForceMatch_WhenRepeatedCandidatesAreAmbiguous()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: AmbiguousRepeatedAmountTransferFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-ambiguous-repeated@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-ambiguous-repeated", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var aibIncoming = await harness.DbContext.Transactions.SingleAsync(
            x => x.Amount > 0m && x.Description.Contains(SyntheticInboundTransferDescription, StringComparison.OrdinalIgnoreCase));
        var revolutOutgoing = await harness.DbContext.Transactions
            .Where(x => x.Amount < 0m && x.Description.Contains(SyntheticOutboundTransferDescription, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();

        Assert.Equal(2, revolutOutgoing.Count);
        Assert.Null(aibIncoming.LinkedTransferTransactionId);
        Assert.All(revolutOutgoing, x => Assert.Null(x.LinkedTransferTransactionId));
        Assert.All(revolutOutgoing, x => Assert.NotEqual(TransactionTransferKind.LinkedInternal, x.TransferKind));
    }

    [Fact]
    public async Task CallbackFlow_DetectsRoundupSavingsRelationship_AndKeepsMerchantExpenseVisible()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: RevolutRoundupSavingsFlowHandler());

        var user = await harness.CreateUserAsync("bank.roundup-savings@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-roundup-savings", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var transactions = await harness.DbContext.Transactions
            .OrderBy(x => x.BookedAtUtc)
            .ToListAsync();
        Assert.Equal(2, transactions.Count);

        var merchant = transactions.Single(x => x.Description.Contains("Tesco", StringComparison.OrdinalIgnoreCase));
        var roundup = transactions.Single(x => x.Description.Contains("spare change", StringComparison.OrdinalIgnoreCase));

        Assert.Null(merchant.LinkedTransferTransactionId);
        Assert.Equal(TransactionTransferKind.SavingsRoundup, roundup.TransferKind);
        Assert.Equal(ExpenseTaxonomyService.TransferDomainId, roundup.TaxonomyDomainId);
        Assert.Equal(920102, roundup.TaxonomySubcategoryId);

        var relationship = await harness.DbContext.TransactionRelationships
            .SingleAsync(x =>
                x.SourceTransactionId == roundup.Id
                && x.TargetTransactionId == merchant.Id
                && x.RelationshipType == TransactionRelationshipType.SavingsRoundup);
        Assert.Equal(TransactionRelationshipStatus.Active, relationship.RelationshipStatus);

        var merchantPolicy = TransferPolicyEngine.Evaluate(
            merchant.TaxonomyDomainId,
            merchant.TaxonomyCategoryId,
            merchant.TaxonomySubcategoryId,
            merchant.TransferKind,
            merchant.LinkedTransferTransactionId,
            merchant.Amount);
        var roundupPolicy = TransferPolicyEngine.Evaluate(
            roundup.TaxonomyDomainId,
            roundup.TaxonomyCategoryId,
            roundup.TaxonomySubcategoryId,
            roundup.TransferKind,
            roundup.LinkedTransferTransactionId,
            roundup.Amount);

        Assert.True(merchantPolicy.CountsTowardExpense);
        Assert.False(roundupPolicy.CountsTowardExpense);
    }

    [Fact]
    public async Task CallbackFlow_DetectsSavingsWithdrawal_AndKeepsItAnalyticsNeutral()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: RevolutSavingsWithdrawalFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-withdrawal@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-withdrawal", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);

        var withdrawal = await harness.DbContext.Transactions.SingleAsync(
            x => x.Description.Contains(SyntheticSavingsDestinationLabel, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(TransactionTransferKind.SavingsManualWithdrawal, withdrawal.TransferKind);

        var relationship = await harness.DbContext.TransactionRelationships
            .SingleAsync(x =>
                x.SourceTransactionId == withdrawal.Id
                && x.RelationshipType == TransactionRelationshipType.SavingsManualWithdrawal);
        Assert.Equal(TransactionRelationshipDirection.InflowFromSavings, relationship.RelationshipDirection);

        var policy = TransferPolicyEngine.Evaluate(
            withdrawal.TaxonomyDomainId,
            withdrawal.TaxonomyCategoryId,
            withdrawal.TaxonomySubcategoryId,
            withdrawal.TransferKind,
            withdrawal.LinkedTransferTransactionId,
            withdrawal.Amount);

        Assert.False(policy.CountsTowardIncome);
    }

    [Fact]
    public async Task CallbackFlow_PersistsPendingRawTransactionsWithoutProjectingIntoLedger()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: PendingOnlyFlowHandler());

        var user = await harness.CreateUserAsync("bank.pending-only@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-pending-only", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Empty(await harness.DbContext.Transactions.ToListAsync());

        var raw = await harness.DbContext.RawBankTransactions.SingleAsync();
        Assert.Equal("pending", raw.TransactionStatus);
    }

    [Fact]
    public async Task CallbackFlow_ProjectsSettledEndpointTransactions_WhenProviderStatusLooksPending()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SettledEndpointPendingStatusFlowHandler());

        var user = await harness.CreateUserAsync("bank.settled-endpoint-pending-status@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-settled-pending-status", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Single(await harness.DbContext.RawBankTransactions.ToListAsync());
        Assert.Single(await harness.DbContext.Transactions.ToListAsync());

        var raw = await harness.DbContext.RawBankTransactions.SingleAsync();
        Assert.Equal("booked", raw.TransactionStatus);
    }

    [Fact]
    public async Task GlobalSync_DoesNotCollapseDistinctTransactions_WithSameAmountTimestampAndDescription()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SameFingerprintDistinctTransactionsFlowHandler());

        var user = await harness.CreateUserAsync("bank.same-fingerprint-distinct@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-same-fingerprint-distinct", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var secondSync = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_same_fingerprint_replay",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", secondSync.Outcome);
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());
    }

    [Fact]
    public async Task GlobalSync_DoesNotCollapseDistinctTransactions_WhenNormalizedProviderIdIsShared()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SharedNormalizedIdDistinctTransactionsFlowHandler());

        var user = await harness.CreateUserAsync("bank.shared-normalized-id-distinct@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-shared-normalized-id-distinct", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());

        var projectedRows = await harness.DbContext.Transactions
            .OrderBy(x => x.Amount)
            .ToListAsync();

        Assert.Contains(projectedRows, x => x.Description.Contains("Tesco", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectedRows, x => x.Description.Contains("Round up", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GlobalSync_PromotesPendingAccountTransactionToBooked_WhenProviderIdIsReused()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: PendingThenBookedAccountFlowHandler());

        var user = await harness.CreateUserAsync("bank.pending-promote@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-pending-promote", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);
        Assert.Single(await harness.DbContext.RawBankTransactions.ToListAsync());
        Assert.Empty(await harness.DbContext.Transactions.ToListAsync());

        var firstRaw = await harness.DbContext.RawBankTransactions.SingleAsync();
        Assert.Equal("pending", firstRaw.TransactionStatus);

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var secondSync = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_pending_to_booked_promotion",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", secondSync.Outcome);
        Assert.True(secondSync.ChangedConnectionCount >= 1);

        var rawRows = await harness.DbContext.RawBankTransactions.ToListAsync();
        Assert.Single(rawRows);
        Assert.Equal("booked", rawRows[0].TransactionStatus);
        Assert.Equal("tx-pending-booked-1", rawRows[0].ProviderTransactionId);

        var projected = await harness.DbContext.Transactions.ToListAsync();
        Assert.Single(projected);
        Assert.Equal(-42.00m, projected[0].Amount);
    }

    [Fact]
    public async Task GlobalSync_BoundsLegacyProjectionBackfillPerRun_ToAvoidUnboundedReconcileCost()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.backfill-bounded@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-backfill-bounded", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var linkedAccount = await harness.DbContext.LinkedBankAccounts.SingleAsync();
        Assert.True(linkedAccount.FinancialAccountId.HasValue);

        var accountId = linkedAccount.FinancialAccountId!.Value;
        var seedBaseUtc = DateTime.UtcNow.AddDays(-3);
        const int seededLegacyRows = 650;
        const int expectedBackfillLimitPerRun = 500;

        for (var i = 0; i < seededLegacyRows; i++)
        {
            var bookedAtUtc = seedBaseUtc.AddMinutes(i);
            var description = $"Legacy bounded row {i:D4}";
            var amount = -(1000 + i);

            var projected = new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = accountId,
                Amount = amount,
                Currency = "EUR",
                Description = description,
                BookedAtUtc = bookedAtUtc,
                CreatedUtc = seedBaseUtc
            };

            harness.DbContext.Transactions.Add(projected);
            harness.DbContext.RawBankTransactions.Add(new RawBankTransaction
            {
                Id = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                ProviderTransactionId = $"legacy-bounded-provider-{i:D4}",
                DedupeKey = $"legacy-bounded-dedupe-{i:D4}",
                Amount = amount,
                Currency = "EUR",
                BookedAtUtc = bookedAtUtc,
                Description = description,
                TransactionType = "transfer",
                TransactionStatus = "booked",
                ProjectedTransactionId = null,
                RawPayloadJson = "{}",
                ImportedUtc = seedBaseUtc
            });
        }

        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var syncResult = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_bounded_backfill_cost",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", syncResult.Outcome);

        var linkedLegacyCount = await harness.DbContext.RawBankTransactions
            .Where(x =>
                x.LinkedBankAccountId == linkedAccount.Id
                && x.DedupeKey.StartsWith("legacy-bounded-dedupe-")
                && x.ProjectedTransactionId.HasValue)
            .CountAsync();

        Assert.Equal(expectedBackfillLimitPerRun, linkedLegacyCount);
    }

    [Fact]
    public async Task GlobalSync_HistoricalDeterministicEnrichment_ProcessesInBatchesUntilCompleted()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.historical-enrichment-batch@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-historical-enrichment-batch", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id);
        Assert.True(linkedAccount.FinancialAccountId.HasValue);

        var accountId = linkedAccount.FinancialAccountId!.Value;
        var seedBaseUtc = DateTime.UtcNow.AddDays(-130);
        var seededTransactionIds = new List<Guid>();
        const int seededRows = 620;

        for (var i = 0; i < seededRows; i++)
        {
            var transactionId = Guid.NewGuid();
            seededTransactionIds.Add(transactionId);

            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = transactionId,
                FinancialAccountId = accountId,
                Amount = -(20 + i),
                Currency = "EUR",
                Description = $"Historical enrichment row {i:D4}",
                BookedAtUtc = seedBaseUtc.AddMinutes(i),
                CreatedUtc = seedBaseUtc,
                DeterministicEnrichmentVersion = null,
                LastDeterministicEnrichedUtc = null
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddHours(-2);
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());

        var firstRun = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_historical_enrichment_batch_run_1",
            force: true,
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", firstRun.Outcome);

        var staleAfterFirstRun = await harness.DbContext.Transactions
            .CountAsync(x =>
                seededTransactionIds.Contains(x.Id)
                && (!x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < currentVersion
                    || !x.DeterministicClassificationTerminal));

        Assert.Equal(20, staleAfterFirstRun);

        connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.True(connection.NeedsHistoricalReclassification);
        Assert.Null(connection.HistoricalEnrichmentCompletedUtc);
        Assert.Null(connection.HistoricalEnrichmentVersion);

        var secondRun = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_historical_enrichment_batch_run_2",
            force: true,
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", secondRun.Outcome);

        var staleAfterSecondRun = await harness.DbContext.Transactions
            .CountAsync(x =>
                seededTransactionIds.Contains(x.Id)
                && (!x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < currentVersion
                    || !x.DeterministicClassificationTerminal));

        Assert.Equal(0, staleAfterSecondRun);

        connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedFinancialAccountIdsAfterSecondRun = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();
        var overallStaleAfterSecondRun = await harness.DbContext.Transactions
            .CountAsync(x =>
                linkedFinancialAccountIdsAfterSecondRun.Contains(x.FinancialAccountId)
                && (!x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < currentVersion
                    || !x.DeterministicClassificationTerminal));
        Assert.Equal(0, overallStaleAfterSecondRun);
    }

    [Fact]
    public async Task GlobalSync_HistoricalDeterministicEnrichment_DoesNotPrematurelyComplete_WhenBatchRowsShareBookedTimestamp()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.historical-enrichment-same-timestamp@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-historical-enrichment-same-timestamp", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id);
        Assert.True(linkedAccount.FinancialAccountId.HasValue);

        var accountId = linkedAccount.FinancialAccountId!.Value;
        var sharedBookedAtUtc = DateTime.UtcNow.AddDays(-70).Date;
        const int seededRows = 1_000;

        for (var i = 0; i < seededRows; i++)
        {
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = accountId,
                Amount = -(150 + i),
                Currency = "EUR",
                Description = $"Same booked timestamp enrichment row {i:D4}",
                BookedAtUtc = sharedBookedAtUtc,
                CreatedUtc = sharedBookedAtUtc.AddMinutes(i),
                DeterministicEnrichmentVersion = null,
                LastDeterministicEnrichedUtc = null
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddHours(-1);
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddHours(-1);
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var syncResult = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_historical_enrichment_same_timestamp_batch",
            force: true,
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", syncResult.Outcome);

        var staleRows = await harness.DbContext.Transactions
            .CountAsync(x => x.FinancialAccountId == accountId
                && (!x.DeterministicClassificationVersion.HasValue
                    || x.DeterministicClassificationVersion.Value < currentVersion
                    || !x.DeterministicClassificationTerminal));
        Assert.Equal(400, staleRows);

        connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.True(connection.NeedsHistoricalReclassification);
        Assert.Null(connection.HistoricalEnrichmentCompletedUtc);

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);
        Assert.True(progress.InProgress);
        Assert.Equal("categorizing", progress.Stage);
        Assert.True(progress.TotalCount > 0);
        Assert.True(progress.ProcessedCount > 0);
        Assert.True(progress.RemainingCount > 0);
        Assert.True(progress.ProcessedCount < progress.TotalCount);
    }

    [Fact]
    public async Task EnrichmentProgress_ConnectionAwaitingSyncWithPendingReclassification_IsQueuedForSync()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-queue@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-queue", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.ConnectionStarted;
        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.UpdatedUtc = DateTime.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        var debugProgress = JsonSerializer.Serialize(progress);
        Assert.True(progress.InProgress, $"Expected in-progress enrichment, got: {debugProgress}");
        Assert.Equal("queued_for_sync", progress.Stage);
        Assert.Contains(progress.Connections, x => x.ConnectionId == connection.Id && x.Stage == "queued_for_sync");
    }

    [Fact]
    public async Task EnrichmentProgress_SyncedLegacyConnectionWithoutDurableState_IsNeedsReclassification()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-needs-reclassification@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-needs-reclassification", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.Synced;
        connection.NeedsHistoricalReclassification = false;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddMinutes(-5);
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddMinutes(-5);

        var linkedAccountIds = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();

        var seededNow = DateTime.UtcNow;
        var linkedTransactions = await harness.DbContext.Transactions
            .Where(x => linkedAccountIds.Contains(x.FinancialAccountId))
            .ToListAsync();

        foreach (var transaction in linkedTransactions)
        {
            transaction.DeterministicEnrichmentVersion = 2;
            transaction.LastDeterministicEnrichedUtc = seededNow;
        }

        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        Assert.True(progress.InProgress);
        Assert.Equal("needs_reclassification", progress.Stage);
        Assert.Equal(0, progress.TotalCount);
        Assert.Equal(0, progress.ProcessedCount);
        Assert.Equal(0, progress.RemainingCount);
        Assert.Equal(0d, progress.ProgressPercent);
        Assert.Contains(progress.Connections, x =>
            x.ConnectionId == connection.Id
            && x.Stage == "needs_reclassification"
            && x.InProgress == false);
    }

    [Fact]
    public async Task EnrichmentProgress_DoesNotCountCompletedConnections_WhenAnotherConnectionIsWaitingForFirstSync()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-active-scope@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-active-scope", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var seededNow = DateTime.UtcNow;
        var completedConnection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        completedConnection.Status = BankConnectionStatuses.Synced;
        completedConnection.NeedsHistoricalReclassification = false;
        completedConnection.HistoricalEnrichmentStartedUtc = seededNow.AddMinutes(-3);
        completedConnection.HistoricalEnrichmentCompletedUtc = seededNow.AddMinutes(-1);
        completedConnection.HistoricalEnrichmentVersion = currentVersion;
        completedConnection.LastSyncAttemptedUtc = seededNow.AddMinutes(-2);
        completedConnection.LastSuccessfulSyncUtc = seededNow.AddMinutes(-2);

        var completedFinancialAccountIds = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == completedConnection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();
        var completedTransactions = await harness.DbContext.Transactions
            .Where(x => completedFinancialAccountIds.Contains(x.FinancialAccountId))
            .ToListAsync();
        Assert.NotEmpty(completedTransactions);

        foreach (var transaction in completedTransactions)
        {
            transaction.DeterministicClassificationStatus = DeterministicClassificationStatus.EvaluatedNoMatchingRule;
            transaction.DeterministicClassificationVersion = currentVersion;
            transaction.DeterministicClassificationTerminal = true;
            transaction.DeterministicClassificationEvaluatedUtc = seededNow.AddMinutes(-1);
            transaction.DeterministicEnrichmentVersion = currentVersion;
            transaction.LastDeterministicEnrichedUtc = seededNow.AddMinutes(-1);
        }

        var waitingConnection = new OpenBankingConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Waiting Connection",
            Status = BankConnectionStatuses.ConnectedPendingSync,
            NeedsHistoricalReclassification = true,
            HistoricalEnrichmentStartedUtc = null,
            HistoricalEnrichmentCompletedUtc = null,
            HistoricalEnrichmentVersion = null,
            LastSyncAttemptedUtc = null,
            LastSuccessfulSyncUtc = null,
            CreatedUtc = seededNow.AddMinutes(-5),
            UpdatedUtc = seededNow
        };

        harness.DbContext.OpenBankingConnections.Add(waitingConnection);
        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        Assert.True(progress.InProgress);
        Assert.Equal("waiting_for_first_sync", progress.Stage);
        Assert.Equal(0, progress.TotalCount);
        Assert.Equal(0, progress.ProcessedCount);
        Assert.Equal(0, progress.RemainingCount);
        Assert.Equal(0d, progress.ProgressPercent);

        Assert.Contains(progress.Connections, x =>
            x.ConnectionId == completedConnection.Id
            && x.Stage == "completed"
            && x.Completed);
        Assert.Contains(progress.Connections, x =>
            x.ConnectionId == waitingConnection.Id
            && x.Stage == "waiting_for_first_sync"
            && x.InProgress);
    }

    [Fact]
    public async Task EnrichmentProgress_EvaluatedNoMatchingRuleCountsAsTerminalCompletion()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-terminal-no-match@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-terminal-no-match", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.Synced;
        connection.NeedsHistoricalReclassification = false;
        connection.HistoricalEnrichmentStartedUtc = now.AddMinutes(-4);
        connection.HistoricalEnrichmentCompletedUtc = now.AddMinutes(-1);
        connection.HistoricalEnrichmentVersion = currentVersion;
        connection.LastSyncAttemptedUtc = now.AddMinutes(-2);
        connection.LastSuccessfulSyncUtc = now.AddMinutes(-2);

        var linkedAccountIds = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();
        var transactions = await harness.DbContext.Transactions
            .Where(x => linkedAccountIds.Contains(x.FinancialAccountId))
            .ToListAsync();
        Assert.NotEmpty(transactions);

        foreach (var transaction in transactions)
        {
            transaction.DeterministicClassificationStatus = DeterministicClassificationStatus.EvaluatedNoMatchingRule;
            transaction.DeterministicClassificationVersion = currentVersion;
            transaction.DeterministicClassificationTerminal = true;
            transaction.DeterministicClassificationEvaluatedUtc = now.AddMinutes(-1);
            transaction.DeterministicDeferredRetryEligible = false;
            transaction.NeedsDeterministicReclassification = false;
            transaction.DeterministicEnrichmentVersion = currentVersion;
            transaction.LastDeterministicEnrichedUtc = now.AddMinutes(-1);
        }

        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        Assert.False(progress.InProgress);
        Assert.True(progress.Completed);
        Assert.Equal("completed", progress.Stage);
        Assert.Equal(transactions.Count, progress.TotalCount);
        Assert.Equal(transactions.Count, progress.ProcessedCount);
        Assert.Equal(0, progress.RemainingCount);
        Assert.Equal(100d, progress.ProgressPercent);
    }

    [Fact]
    public async Task EnrichmentProgress_DeferredCounterpartyStateRemainsWaitingForCounterparty()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-waiting-counterparty@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-waiting-counterparty", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.Synced;
        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = now.AddMinutes(-5);
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = currentVersion - 1;
        connection.LastSyncAttemptedUtc = now.AddMinutes(-2);
        connection.LastSuccessfulSyncUtc = now.AddMinutes(-2);

        var linkedAccountIds = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();
        var transactions = await harness.DbContext.Transactions
            .Where(x => linkedAccountIds.Contains(x.FinancialAccountId))
            .ToListAsync();
        Assert.NotEmpty(transactions);

        foreach (var transaction in transactions)
        {
            transaction.DeterministicClassificationStatus = DeterministicClassificationStatus.DeferredWaitingForCounterparty;
            transaction.DeterministicClassificationVersion = currentVersion;
            transaction.DeterministicClassificationTerminal = false;
            transaction.DeterministicClassificationEvaluatedUtc = now.AddMinutes(-1);
            transaction.DeterministicDeferredRetryEligible = true;
            transaction.NeedsDeterministicReclassification = false;
            transaction.DeterministicEnrichmentVersion = currentVersion;
            transaction.LastDeterministicEnrichedUtc = now.AddMinutes(-1);
        }

        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        Assert.True(progress.InProgress);
        Assert.False(progress.Completed);
        Assert.Equal("waiting_for_counterparty", progress.Stage);
        Assert.Equal(transactions.Count, progress.TotalCount);
        Assert.Equal(0, progress.ProcessedCount);
        Assert.Equal(transactions.Count, progress.RemainingCount);
    }

    [Fact]
    public async Task DeterministicEnrichment_SmallTransferLikeResidual_FinalizesAsTerminalNoMatch()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.deterministic-small-residual@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-deterministic-small-residual", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        var primaryLinkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var primaryFinancialAccountId = primaryLinkedAccount.FinancialAccountId!.Value;

        var secondaryFinancialAccountId = Guid.NewGuid();
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = secondaryFinancialAccountId,
            UserId = user.Id,
            Name = "Counterparty Account",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-3)
        });

        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            ProviderAccountId = "counterparty-account-seed",
            DisplayName = "Counterparty Account",
            AccountType = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-3),
            UpdatedUtc = now.AddDays(-3),
            FinancialAccountId = secondaryFinancialAccountId
        });

        var residualIds = new List<Guid>();
        var seedBaseUtc = now.AddDays(-28);
        for (var i = 0; i < 38; i++)
        {
            var id = Guid.NewGuid();
            residualIds.Add(id);
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = id,
                FinancialAccountId = primaryFinancialAccountId,
                Amount = -(1_000 + i),
                Currency = "EUR",
                Description = $"Transfer memo reference row {i:D2}",
                BookedAtUtc = seedBaseUtc.AddMinutes(i),
                CreatedUtc = seedBaseUtc.AddMinutes(i)
            });
        }

        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = secondaryFinancialAccountId,
            Amount = 12.34m,
            Currency = "EUR",
            Description = "Counterparty activity marker",
            BookedAtUtc = now.AddDays(-7),
            CreatedUtc = now.AddDays(-7)
        });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        BankSyncService.DeterministicEnrichmentRunResult? lastRun = null;
        for (var run = 0; run < 6; run++)
        {
            var outcome = await syncService.RunDeterministicEnrichmentAsync(
                user.Id,
                connection.Id,
                trigger: "test_small_residual_completion",
                cancellationToken: CancellationToken.None);
            Assert.True(outcome.Succeeded);
            lastRun = outcome.Value;
            if (lastRun.Value.RowsActionableRemaining == 0)
            {
                break;
            }
        }

        Assert.True(lastRun.HasValue);
        Assert.Equal(0, lastRun.Value.RowsActionableRemaining);
        Assert.Equal(0, lastRun.Value.RowsRemaining);

        var residualRows = await harness.DbContext.Transactions
            .Where(x => residualIds.Contains(x.Id))
            .ToListAsync();

        Assert.Equal(38, residualRows.Count);
        Assert.All(residualRows, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, row.DeterministicClassificationStatus);
            Assert.True(row.DeterministicClassificationTerminal);
            Assert.True(row.DeterministicClassificationVersion.HasValue && row.DeterministicClassificationVersion.Value >= currentVersion);
        });

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);
        Assert.Equal("completed", progress.Stage);
        Assert.True(progress.Completed);
    }

    [Fact]
    public async Task DeterministicEnrichment_LargeHistoricalRows_WithInitialBackfillUnset_CompletesWithoutPlateau()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.deterministic-large-plateau@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-deterministic-large-plateau", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        const int seededRows = 4_476;
        var seedBaseUtc = now.AddDays(-120);
        for (var i = 0; i < seededRows; i++)
        {
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = accountId,
                Amount = -(40 + i),
                Currency = "EUR",
                Description = $"Merchant historical row {i:D4}",
                BookedAtUtc = seedBaseUtc.AddMinutes(i),
                CreatedUtc = seedBaseUtc.AddMinutes(i)
            });
        }

        connection.InitialBackfillCompletedUtc = null;
        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentCheckpointUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var remainingSequence = new List<int>();
        BankSyncService.DeterministicEnrichmentRunResult? lastRun = null;

        for (var run = 0; run < 16; run++)
        {
            var outcome = await syncService.RunDeterministicEnrichmentAsync(
                user.Id,
                connection.Id,
                trigger: $"test_large_historical_run_{run}",
                cancellationToken: CancellationToken.None);
            Assert.True(outcome.Succeeded);

            lastRun = outcome.Value;
            remainingSequence.Add(lastRun.Value.RowsRemaining);

            if (lastRun.Value.RowsActionableRemaining == 0)
            {
                break;
            }
        }

        Assert.True(lastRun.HasValue);
        Assert.Equal(0, lastRun.Value.RowsActionableRemaining);
        Assert.Equal(0, lastRun.Value.RowsRemaining);
        Assert.True(remainingSequence.Count >= 2);
        Assert.True(remainingSequence.First() > remainingSequence.Last());
        Assert.DoesNotContain(remainingSequence.Zip(remainingSequence.Skip(1)), pair => pair.Second > pair.First);

        var terminalCurrentVersionCount = await harness.DbContext.Transactions
            .CountAsync(x =>
                x.FinancialAccountId == accountId
                && x.DeterministicClassificationVersion.HasValue
                && x.DeterministicClassificationVersion.Value >= currentVersion
                && x.DeterministicClassificationTerminal);
        Assert.True(terminalCurrentVersionCount >= seededRows);
    }

    [Fact]
    public async Task EnrichmentProgress_DoesNotReportWaitingForFirstSync_WhenDeterministicProgressExists()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.enrichment-stage-contradiction@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-enrichment-stage-contradiction", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var activeConnection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        activeConnection.Status = BankConnectionStatuses.Synced;
        activeConnection.NeedsHistoricalReclassification = true;
        activeConnection.HistoricalEnrichmentStartedUtc = now.AddMinutes(-10);
        activeConnection.HistoricalEnrichmentCompletedUtc = null;
        activeConnection.HistoricalEnrichmentVersion = currentVersion - 1;
        activeConnection.LastSyncAttemptedUtc = now.AddMinutes(-11);
        activeConnection.LastSuccessfulSyncUtc = now.AddMinutes(-11);

        var activeAccountIds = await harness.DbContext.LinkedBankAccounts
            .Where(x => x.ConnectionId == activeConnection.Id && x.FinancialAccountId.HasValue)
            .Select(x => x.FinancialAccountId!.Value)
            .ToListAsync();
        var activeRows = await harness.DbContext.Transactions
            .Where(x => activeAccountIds.Contains(x.FinancialAccountId))
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        Assert.True(activeRows.Count >= 2);

        activeRows[0].DeterministicClassificationStatus = DeterministicClassificationStatus.EvaluatedNoMatchingRule;
        activeRows[0].DeterministicClassificationVersion = currentVersion;
        activeRows[0].DeterministicClassificationTerminal = true;
        activeRows[0].DeterministicClassificationEvaluatedUtc = now.AddMinutes(-5);
        activeRows[0].NeedsDeterministicReclassification = false;

        activeRows[1].DeterministicClassificationStatus = DeterministicClassificationStatus.NotEvaluated;
        activeRows[1].DeterministicClassificationVersion = currentVersion;
        activeRows[1].DeterministicClassificationTerminal = false;
        activeRows[1].DeterministicClassificationEvaluatedUtc = null;
        activeRows[1].NeedsDeterministicReclassification = false;

        var waitingConnection = new OpenBankingConnection
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Waiting First Sync Connection",
            Status = BankConnectionStatuses.ConnectedPendingSync,
            NeedsHistoricalReclassification = true,
            HistoricalEnrichmentStartedUtc = null,
            HistoricalEnrichmentCompletedUtc = null,
            HistoricalEnrichmentVersion = null,
            LastSyncAttemptedUtc = null,
            LastSuccessfulSyncUtc = null,
            CreatedUtc = now.AddMinutes(-20),
            UpdatedUtc = now
        };
        harness.DbContext.OpenBankingConnections.Add(waitingConnection);

        await harness.DbContext.SaveChangesAsync();

        var progress = await harness.CreateConnectionService().GetEnrichmentProgressAsync(user.Id, CancellationToken.None);

        Assert.True(progress.TotalCount > 0);
        Assert.True(progress.ProcessedCount > 0);
        Assert.True(progress.RemainingCount > 0);
        Assert.NotEqual("waiting_for_first_sync", progress.Stage);
        Assert.Equal("categorizing", progress.Stage);
    }

    [Fact]
    public async Task DeterministicEnrichment_OrdinaryTransferLikeWording_DoesNotFalseDefer()
    {
        var currentVersion = DeterministicCategorizationConstants.CurrentClassificationVersion;

        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.false-defer-ordinary@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-false-defer-ordinary", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var primaryLinked = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);

        var secondaryAccountId = Guid.NewGuid();
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = secondaryAccountId,
            UserId = user.Id,
            Name = "Extra Account",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-5)
        });
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            ProviderAccountId = "extra-account-ordinary",
            DisplayName = "Extra Account",
            AccountType = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-5),
            UpdatedUtc = now.AddDays(-5),
            FinancialAccountId = secondaryAccountId
        });

        var suspiciousOrdinaryId = Guid.NewGuid();
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = suspiciousOrdinaryId,
            FinancialAccountId = primaryLinked.FinancialAccountId!.Value,
            Amount = -48.50m,
            Currency = "EUR",
            Description = "Transfer to friend John for rent share",
            BookedAtUtc = now.AddDays(-2),
            CreatedUtc = now.AddDays(-2),
            MetadataUpdatedUtc = now
        });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_false_defer_ordinary",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var row = await harness.DbContext.Transactions.SingleAsync(x => x.Id == suspiciousOrdinaryId);
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, row.DeterministicClassificationStatus);
        Assert.True(row.DeterministicClassificationTerminal);
        Assert.True(row.DeterministicClassificationVersion.HasValue && row.DeterministicClassificationVersion.Value >= currentVersion);
    }

    [Fact]
    public async Task DeterministicEnrichment_LegacySavingsHintWithValidPair_TransferTakesPrecedence()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.transfer-precedence-over-savings@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-transfer-precedence-over-savings", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var primaryLinked = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var primaryAccountId = primaryLinked.FinancialAccountId!.Value;

        var secondaryAccountId = Guid.NewGuid();
        var secondaryLinkedId = Guid.NewGuid();
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = secondaryAccountId,
            UserId = user.Id,
            Name = "Counterpart Current",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-8)
        });
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = secondaryLinkedId,
            ConnectionId = connection.Id,
            ProviderAccountId = "extra-account-transfer-precedence",
            DisplayName = "Counterpart Current",
            AccountType = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-8),
            UpdatedUtc = now.AddDays(-8),
            FinancialAccountId = secondaryAccountId
        });

        var debitId = Guid.NewGuid();
        var creditId = Guid.NewGuid();
        var t0 = now.AddDays(-2).Date.AddHours(9);
        harness.DbContext.Transactions.AddRange(
            new Transaction
            {
                Id = debitId,
                FinancialAccountId = primaryAccountId,
                Amount = -245m,
                Currency = "EUR",
                Description = "Transfer to Pocket Reserve ref 7712",
                BookedAtUtc = t0,
                CreatedUtc = t0,
                TransferKind = TransactionTransferKind.SavingsRoundup
            },
            new Transaction
            {
                Id = creditId,
                FinancialAccountId = secondaryAccountId,
                Amount = 245m,
                Currency = "EUR",
                Description = "Transfer from Main Reserve ref 7712",
                BookedAtUtc = t0.AddMinutes(3),
                CreatedUtc = t0.AddMinutes(3)
            });

        harness.DbContext.NormalizedBankTransactions.AddRange(
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = primaryLinked.Id,
                FinancialAccountId = primaryAccountId,
                ProjectedTransactionId = debitId,
                DedupeKey = "diag-transfer-precedence-debit",
                Amount = -245m,
                Currency = "EUR",
                BookedAtUtc = t0,
                Description = "Transfer to Pocket Reserve ref 7712",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = secondaryLinkedId,
                FinancialAccountId = secondaryAccountId,
                ProjectedTransactionId = creditId,
                DedupeKey = "diag-transfer-precedence-credit",
                Amount = 245m,
                Currency = "EUR",
                BookedAtUtc = t0.AddMinutes(3),
                Description = "Transfer from Main Reserve ref 7712",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_transfer_precedence_over_savings",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var rows = await harness.DbContext.Transactions
            .Where(x => x.Id == debitId || x.Id == creditId)
            .ToListAsync();

        Assert.All(rows, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, row.DeterministicClassificationStatus);
            Assert.Equal("internal_transfer", row.DeterministicRelationshipType);
            Assert.NotEqual("savings_transfer", row.DeterministicRelationshipType);
            Assert.NotEqual(DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal, row.DeterministicReasonCode);
        });
    }

    [Fact]
    public async Task DeterministicEnrichment_NearbyPurchaseAlone_DoesNotAutoClassifySavings()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-nearby-alone@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-nearby-alone", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var merchantId = Guid.NewGuid();
        var nearbyAuxId = Guid.NewGuid();
        var booked = now.AddDays(-1).Date.AddHours(11);
        harness.DbContext.Transactions.AddRange(
            new Transaction
            {
                Id = merchantId,
                FinancialAccountId = accountId,
                Amount = -18.15m,
                Currency = "EUR",
                Description = "Supermarket main spend",
                BookedAtUtc = booked,
                CreatedUtc = booked
            },
            new Transaction
            {
                Id = nearbyAuxId,
                FinancialAccountId = accountId,
                Amount = -0.45m,
                Currency = "EUR",
                Description = "Aux move once",
                BookedAtUtc = booked.AddMinutes(5),
                CreatedUtc = booked.AddMinutes(5)
            });

        harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
        {
            Id = Guid.NewGuid(),
            RawBankTransactionId = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccount.Id,
            FinancialAccountId = accountId,
            ProjectedTransactionId = nearbyAuxId,
            DedupeKey = "diag-savings-nearby-alone",
            Amount = -0.45m,
            Currency = "EUR",
            BookedAtUtc = booked.AddMinutes(5),
            Description = "Aux move once",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            ImportedUtc = now,
            LastNormalizedUtc = now
        });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_nearby_alone",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var aux = await harness.DbContext.Transactions.SingleAsync(x => x.Id == nearbyAuxId);
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, aux.DeterministicClassificationStatus);
        Assert.NotEqual("savings_transfer", aux.DeterministicRelationshipType);
        Assert.Equal(DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily, aux.DeterministicReasonCode);
    }

    [Fact]
    public async Task DeterministicEnrichment_DuplicateClusterStablePairs_AreMatchedOneToOne()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.duplicate-cluster-stable@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-duplicate-cluster-stable", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var primaryLinked = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var primaryAccountId = primaryLinked.FinancialAccountId!.Value;

        var secondaryAccountId = Guid.NewGuid();
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = secondaryAccountId,
            UserId = user.Id,
            Name = "Counterpart Account",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-10)
        });
        var secondaryLinkedAccountId = Guid.NewGuid();
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = secondaryLinkedAccountId,
            ConnectionId = connection.Id,
            ProviderAccountId = "extra-account-cluster-stable",
            DisplayName = "Counterpart Account",
            AccountType = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-10),
            UpdatedUtc = now.AddDays(-10),
            FinancialAccountId = secondaryAccountId
        });

        var t0 = now.AddDays(-3);
        var debitAId = Guid.NewGuid();
        var debitBId = Guid.NewGuid();
        var creditAId = Guid.NewGuid();
        var creditBId = Guid.NewGuid();

        harness.DbContext.Transactions.AddRange(
            new Transaction
            {
                Id = debitAId,
                FinancialAccountId = primaryAccountId,
                Amount = -901.23m,
                Currency = "EUR",
                Description = "Bank transfer ref 1101",
                BookedAtUtc = t0.AddHours(9),
                CreatedUtc = t0.AddHours(9)
            },
            new Transaction
            {
                Id = creditAId,
                FinancialAccountId = secondaryAccountId,
                Amount = 901.23m,
                Currency = "EUR",
                Description = "Bank transfer ref 1101",
                BookedAtUtc = t0.AddHours(9).AddMinutes(2),
                CreatedUtc = t0.AddHours(9).AddMinutes(2)
            },
            new Transaction
            {
                Id = debitBId,
                FinancialAccountId = primaryAccountId,
                Amount = -901.23m,
                Currency = "EUR",
                Description = "Bank transfer ref 1102",
                BookedAtUtc = t0.AddHours(11),
                CreatedUtc = t0.AddHours(11)
            },
            new Transaction
            {
                Id = creditBId,
                FinancialAccountId = secondaryAccountId,
                Amount = 901.23m,
                Currency = "EUR",
                Description = "Bank transfer ref 1102",
                BookedAtUtc = t0.AddHours(11).AddMinutes(1),
                CreatedUtc = t0.AddHours(11).AddMinutes(1)
            });

        harness.DbContext.NormalizedBankTransactions.AddRange(
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = primaryLinked.Id,
                FinancialAccountId = primaryAccountId,
                ProjectedTransactionId = debitAId,
                DedupeKey = "diag-cluster-stable-debit-a",
                Amount = -901.23m,
                Currency = "EUR",
                BookedAtUtc = t0.AddHours(9),
                Description = "Bank transfer ref 1101",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = secondaryLinkedAccountId,
                FinancialAccountId = secondaryAccountId,
                ProjectedTransactionId = creditAId,
                DedupeKey = "diag-cluster-stable-credit-a",
                Amount = 901.23m,
                Currency = "EUR",
                BookedAtUtc = t0.AddHours(9).AddMinutes(2),
                Description = "Bank transfer ref 1101",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = primaryLinked.Id,
                FinancialAccountId = primaryAccountId,
                ProjectedTransactionId = debitBId,
                DedupeKey = "diag-cluster-stable-debit-b",
                Amount = -901.23m,
                Currency = "EUR",
                BookedAtUtc = t0.AddHours(11),
                Description = "Bank transfer ref 1102",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = secondaryLinkedAccountId,
                FinancialAccountId = secondaryAccountId,
                ProjectedTransactionId = creditBId,
                DedupeKey = "diag-cluster-stable-credit-b",
                Amount = 901.23m,
                Currency = "EUR",
                BookedAtUtc = t0.AddHours(11).AddMinutes(1),
                Description = "Bank transfer ref 1102",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_duplicate_cluster_stable",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var refreshed = await harness.DbContext.Transactions
            .Where(x => x.Id == debitAId || x.Id == debitBId || x.Id == creditAId || x.Id == creditBId)
            .ToListAsync();

        Assert.All(refreshed, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, row.DeterministicClassificationStatus);
            Assert.Equal("internal_transfer", row.DeterministicRelationshipType);
            Assert.True(row.DeterministicLinkedTransactionId.HasValue);
        });
    }

    [Fact]
    public async Task DeterministicEnrichment_DuplicateClusterAmbiguous_IsNotForcePaired()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.duplicate-cluster-ambiguous@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-duplicate-cluster-ambiguous", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var primaryLinked = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var primaryAccountId = primaryLinked.FinancialAccountId!.Value;

        var secondaryAccountId = Guid.NewGuid();
        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = secondaryAccountId,
            UserId = user.Id,
            Name = "Counterpart Ambiguous",
            Type = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-10)
        });
        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            ProviderAccountId = "extra-account-cluster-ambiguous",
            DisplayName = "Counterpart Ambiguous",
            AccountType = "Current",
            Currency = "EUR",
            CreatedUtc = now.AddDays(-10),
            UpdatedUtc = now.AddDays(-10),
            FinancialAccountId = secondaryAccountId
        });

        var t0 = now.AddDays(-4);
        var seededIds = new List<Guid>();
        for (var i = 0; i < 9; i++)
        {
            var debitId = Guid.NewGuid();
            var creditId = Guid.NewGuid();
            seededIds.Add(debitId);
            seededIds.Add(creditId);
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = debitId,
                FinancialAccountId = primaryAccountId,
                Amount = -77.77m,
                Currency = "EUR",
                Description = "Bank transfer payment",
                BookedAtUtc = t0.AddMinutes(i),
                CreatedUtc = t0.AddMinutes(i),
                MetadataUpdatedUtc = now
            });
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = creditId,
                FinancialAccountId = secondaryAccountId,
                Amount = 77.77m,
                Currency = "EUR",
                Description = "Bank transfer payment",
                BookedAtUtc = t0.AddMinutes(i + 1),
                CreatedUtc = t0.AddMinutes(i + 1),
                MetadataUpdatedUtc = now
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_duplicate_cluster_ambiguous",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var rows = await harness.DbContext.Transactions
            .Where(x => seededIds.Contains(x.Id))
            .ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Null(row.DeterministicLinkedTransactionId));
        Assert.Contains(rows, row =>
            row.DeterministicClassificationStatus == DeterministicClassificationStatus.RejectedAmbiguousMatch
            && row.DeterministicReasonCode == DeterministicClassificationReasonCodes.RejectedAmbiguousDuplicateCluster);

        var diagnostics = await harness.CreateConnectionService().GetDeterministicCategorizationDiagnosticsAsync(
            user.Id,
            connection.Id,
            CancellationToken.None);
        Assert.True(diagnostics.Succeeded);
        Assert.Contains(diagnostics.Value!.UnresolvedBreakdown, item =>
            item.ReasonCode == DeterministicClassificationReasonCodes.RejectedAmbiguousDuplicateCluster
            && item.CandidateFamily == "bank_account_transfer"
            && item.DuplicateClusterMember);
    }

    [Fact]
    public async Task DeterministicEnrichment_SavingsCustomName_ContextualPatternIsClassified()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-custom-name@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-custom-name", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var candidateIds = new List<Guid>();
        var baseDay = now.AddDays(-6).Date;
        for (var i = 0; i < 3; i++)
        {
            var merchantId = Guid.NewGuid();
            var savingsId = Guid.NewGuid();
            candidateIds.Add(savingsId);
            var day = baseDay.AddDays(i);
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = merchantId,
                FinancialAccountId = accountId,
                Amount = -12.75m,
                Currency = "EUR",
                Description = $"Local grocery purchase {i}",
                BookedAtUtc = day.AddHours(10),
                CreatedUtc = day.AddHours(10)
            });
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = savingsId,
                FinancialAccountId = accountId,
                Amount = -0.50m,
                Currency = "EUR",
                Description = $"Aux JarA move {i}",
                BookedAtUtc = day.AddHours(10).AddMinutes(6),
                CreatedUtc = day.AddHours(10).AddMinutes(6)
            });
            harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = accountId,
                ProjectedTransactionId = savingsId,
                DedupeKey = $"diag-savings-custom-{i}",
                Amount = -0.50m,
                Currency = "EUR",
                BookedAtUtc = day.AddHours(10).AddMinutes(6),
                Description = $"Aux JarA move {i}",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_custom_name",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var rows = await harness.DbContext.Transactions
            .Where(x => candidateIds.Contains(x.Id))
            .ToListAsync();
        Assert.Contains(rows, row =>
            row.DeterministicClassificationStatus == DeterministicClassificationStatus.ClassifiedMatchedRule
            && row.DeterministicRelationshipType == "savings_transfer"
            && row.DeterministicLinkedTransactionId is null
            && row.DeterministicReasonCode == DeterministicClassificationReasonCodes.SavingsContextNearbySpend);
    }

    [Fact]
    public async Task DeterministicEnrichment_SavingsContext_MainSpendPostedAfterCandidate_Classifies()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-posting-order@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-posting-order", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var savingsIds = new List<Guid>();
        var baseDay = now.AddDays(-4).Date;
        for (var i = 0; i < 2; i++)
        {
            var savingsId = Guid.NewGuid();
            var merchantId = Guid.NewGuid();
            savingsIds.Add(savingsId);
            var day = baseDay.AddDays(i);

            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = savingsId,
                FinancialAccountId = accountId,
                Amount = -0.60m,
                Currency = "EUR",
                Description = $"Aux jar sweep {i}",
                BookedAtUtc = day.AddHours(10),
                CreatedUtc = day.AddHours(10)
            });
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = merchantId,
                FinancialAccountId = accountId,
                Amount = -14.20m,
                Currency = "EUR",
                Description = $"Main purchase {i}",
                BookedAtUtc = day.AddHours(10).AddMinutes(6),
                CreatedUtc = day.AddHours(10).AddMinutes(6)
            });

            harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = accountId,
                ProjectedTransactionId = savingsId,
                DedupeKey = $"diag-savings-posting-order-{i}",
                Amount = -0.60m,
                Currency = "EUR",
                BookedAtUtc = day.AddHours(10),
                Description = $"Aux jar sweep {i}",
                TransactionType = "DEBIT",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_posting_order_asymmetry",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var rows = await harness.DbContext.Transactions
            .Where(x => savingsIds.Contains(x.Id))
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, row.DeterministicClassificationStatus);
            Assert.Equal("savings_transfer", row.DeterministicRelationshipType);
            Assert.Equal(DeterministicClassificationReasonCodes.SavingsContextNearbySpend, row.DeterministicReasonCode);
        });
    }

    [Fact]
    public async Task DeterministicEnrichment_SavingsProviderStructuralLargeManualMove_Classifies()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-large-manual@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-large-manual", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var savingsId = Guid.NewGuid();
        var booked = now.AddDays(-2).Date.AddHours(8);
        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = savingsId,
            FinancialAccountId = accountId,
            Amount = -185m,
            Currency = "EUR",
            Description = "Monthly vault transfer",
            BookedAtUtc = booked,
            CreatedUtc = booked
        });
        harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
        {
            Id = Guid.NewGuid(),
            RawBankTransactionId = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccount.Id,
            FinancialAccountId = accountId,
            ProjectedTransactionId = savingsId,
            DedupeKey = "diag-savings-large-manual",
            Amount = -185m,
            Currency = "EUR",
            BookedAtUtc = booked,
            Description = "Monthly vault transfer",
            TransactionType = "TRANSFER",
            TransactionStatus = "booked",
            ImportedUtc = now,
            LastNormalizedUtc = now
        });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_large_manual_provider_structural",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var row = await harness.DbContext.Transactions.SingleAsync(x => x.Id == savingsId);
        Assert.Equal(DeterministicClassificationStatus.ClassifiedMatchedRule, row.DeterministicClassificationStatus);
        Assert.Equal("savings_transfer", row.DeterministicRelationshipType);
        Assert.Equal(DeterministicClassificationReasonCodes.SavingsProviderStructuralSignal, row.DeterministicReasonCode);

        Assert.Contains("\"routingTier\":\"tier_a_provider_structural\"", row.DeterministicReasonDetailJson, StringComparison.Ordinal);
        Assert.Contains("\"amountRiskModifier\":0", row.DeterministicReasonDetailJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeterministicEnrichment_WeakGenericSavingsWordingPlusNearbyPurchase_DoesNotClassify()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-weak-generic-nearby@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-weak-generic-nearby", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var merchantId = Guid.NewGuid();
        var weakId = Guid.NewGuid();
        var booked = now.AddDays(-2).Date.AddHours(15);
        harness.DbContext.Transactions.AddRange(
            new Transaction
            {
                Id = merchantId,
                FinancialAccountId = accountId,
                Amount = -17.15m,
                Currency = "EUR",
                Description = "Main groceries",
                BookedAtUtc = booked,
                CreatedUtc = booked
            },
            new Transaction
            {
                Id = weakId,
                FinancialAccountId = accountId,
                Amount = -0.90m,
                Currency = "EUR",
                Description = "Cash fund move",
                BookedAtUtc = booked.AddMinutes(4),
                CreatedUtc = booked.AddMinutes(4)
            });

        harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
        {
            Id = Guid.NewGuid(),
            RawBankTransactionId = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccount.Id,
            FinancialAccountId = accountId,
            ProjectedTransactionId = weakId,
            DedupeKey = "diag-savings-weak-generic-nearby",
            Amount = -0.90m,
            Currency = "EUR",
            BookedAtUtc = booked.AddMinutes(4),
            Description = "Cash fund move",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            ImportedUtc = now,
            LastNormalizedUtc = now
        });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_weak_generic_nearby",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var row = await harness.DbContext.Transactions.SingleAsync(x => x.Id == weakId);
        Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, row.DeterministicClassificationStatus);
        Assert.NotEqual("savings_transfer", row.DeterministicRelationshipType);
    }

    [Fact]
    public async Task DeterministicEnrichment_SavingsSafety_ExternalOrOneOffSignalsStayUnmatched()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-safety-unmatched@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-safety-unmatched", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var merchantId = Guid.NewGuid();
        var externalPayeeId = Guid.NewGuid();
        var oneOffNearbyId = Guid.NewGuid();

        harness.DbContext.Transactions.AddRange(
            new Transaction
            {
                Id = merchantId,
                FinancialAccountId = accountId,
                Amount = -18.20m,
                Currency = "EUR",
                Description = "Groceries weekly run",
                BookedAtUtc = now.AddDays(-2).AddHours(9),
                CreatedUtc = now.AddDays(-2).AddHours(9)
            },
            new Transaction
            {
                Id = externalPayeeId,
                FinancialAccountId = accountId,
                Amount = -0.75m,
                Currency = "EUR",
                Description = "To John gift jar",
                BookedAtUtc = now.AddDays(-2).AddHours(9).AddMinutes(4),
                CreatedUtc = now.AddDays(-2).AddHours(9).AddMinutes(4)
            },
            new Transaction
            {
                Id = oneOffNearbyId,
                FinancialAccountId = accountId,
                Amount = -0.55m,
                Currency = "EUR",
                Description = "Aux transfer one-off",
                BookedAtUtc = now.AddDays(-1).AddHours(11).AddMinutes(2),
                CreatedUtc = now.AddDays(-1).AddHours(11).AddMinutes(2)
            });

        harness.DbContext.NormalizedBankTransactions.AddRange(
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = accountId,
                ProjectedTransactionId = externalPayeeId,
                DedupeKey = "diag-savings-safety-external",
                Amount = -0.75m,
                Currency = "EUR",
                BookedAtUtc = now.AddDays(-2).AddHours(9).AddMinutes(4),
                Description = "To John gift jar",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            },
            new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = accountId,
                ProjectedTransactionId = oneOffNearbyId,
                DedupeKey = "diag-savings-safety-one-off",
                Amount = -0.55m,
                Currency = "EUR",
                BookedAtUtc = now.AddDays(-1).AddHours(11).AddMinutes(2),
                Description = "Aux transfer one-off",
                TransactionType = "TRANSFER",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_safety_unmatched",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var rows = await harness.DbContext.Transactions
            .Where(x => x.Id == externalPayeeId || x.Id == oneOffNearbyId)
            .ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, row.DeterministicClassificationStatus);
            Assert.NotEqual("savings_transfer", row.DeterministicRelationshipType);
        });
    }

    [Fact]
    public async Task DeterministicEnrichment_GenericSavingsWords_DoNotRouteSavingsOrDefer()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.savings-generic-keywords@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-savings-generic-keywords", state, null, null),
            CancellationToken.None);
        Assert.True(callback.Succeeded);

        var now = DateTime.UtcNow;
        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        var linkedAccount = await harness.DbContext.LinkedBankAccounts
            .SingleAsync(x => x.ConnectionId == connection.Id && x.FinancialAccountId.HasValue);
        var accountId = linkedAccount.FinancialAccountId!.Value;

        var genericRows = new[]
        {
            (Id: Guid.NewGuid(), Description: "Cash allocation for monthly planning"),
            (Id: Guid.NewGuid(), Description: "Flexible fund adjustment"),
            (Id: Guid.NewGuid(), Description: "Pot contribution"),
            (Id: Guid.NewGuid(), Description: "Round movement")
        };

        var bookedBase = now.AddDays(-2).Date.AddHours(12);
        for (var index = 0; index < genericRows.Length; index++)
        {
            var row = genericRows[index];
            var bookedAt = bookedBase.AddMinutes(index * 3);
            harness.DbContext.Transactions.Add(new Transaction
            {
                Id = row.Id,
                FinancialAccountId = accountId,
                Amount = -(0.40m + (index * 0.10m)),
                Currency = "EUR",
                Description = row.Description,
                BookedAtUtc = bookedAt,
                CreatedUtc = bookedAt
            });
            harness.DbContext.NormalizedBankTransactions.Add(new NormalizedBankTransaction
            {
                Id = Guid.NewGuid(),
                RawBankTransactionId = Guid.NewGuid(),
                LinkedBankAccountId = linkedAccount.Id,
                FinancialAccountId = accountId,
                ProjectedTransactionId = row.Id,
                DedupeKey = $"diag-savings-generic-{index}",
                Amount = -(0.40m + (index * 0.10m)),
                Currency = "EUR",
                BookedAtUtc = bookedAt,
                Description = row.Description,
                TransactionType = "DEBIT",
                TransactionStatus = "booked",
                ImportedUtc = now,
                LastNormalizedUtc = now
            });
        }

        connection.NeedsHistoricalReclassification = true;
        connection.HistoricalEnrichmentStartedUtc = null;
        connection.HistoricalEnrichmentCompletedUtc = null;
        connection.HistoricalEnrichmentVersion = null;
        connection.LastSyncAttemptedUtc = now.AddHours(-2);
        connection.LastSuccessfulSyncUtc = now.AddHours(-2);
        await harness.DbContext.SaveChangesAsync();

        var syncService = harness.CreateSyncServiceForTesting(ValidSandboxOptions());
        var run = await syncService.RunDeterministicEnrichmentAsync(
            user.Id,
            connection.Id,
            trigger: "test_savings_generic_keywords",
            cancellationToken: CancellationToken.None);
        Assert.True(run.Succeeded);

        var ids = genericRows.Select(x => x.Id).ToArray();
        var rows = await harness.DbContext.Transactions
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();
        Assert.Equal(genericRows.Length, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(DeterministicClassificationStatus.EvaluatedNoMatchingRule, row.DeterministicClassificationStatus);
            Assert.True(row.DeterministicClassificationTerminal);
            Assert.NotEqual(DeterministicClassificationStatus.DeferredWaitingForCounterparty, row.DeterministicClassificationStatus);
            Assert.NotEqual("savings_transfer", row.DeterministicRelationshipType);
            Assert.Equal(DeterministicClassificationReasonCodes.EvaluatedUnsupportedFamily, row.DeterministicReasonCode);
        });
    }

    [Fact]
    public async Task GlobalSync_AibCappedIncrementalWindow_RecoversRecentTransactions()
    {
        var handler = AibCappedOldestSliceFlowHandler(out var getTransactionsCallCount, out var referenceNowUtc);
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: handler);

        var user = await harness.CreateUserAsync("bank.aib-capped-window@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-aib-capped-window", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var initialLatestRawUtc = await harness.DbContext.RawBankTransactions
            .MaxAsync(x => x.BookedAtUtc);

        Assert.True(initialLatestRawUtc < referenceNowUtc.AddDays(-20));

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var secondSync = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_aib_capped_incremental_window",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", secondSync.Outcome);
        Assert.True(secondSync.ChangedConnectionCount >= 1);

        var latestRawUtc = await harness.DbContext.RawBankTransactions
            .MaxAsync(x => x.BookedAtUtc);

        Assert.True(latestRawUtc >= referenceNowUtc.AddDays(-2));
        Assert.True(await harness.DbContext.Transactions.AnyAsync(x => x.BookedAtUtc >= referenceNowUtc.AddDays(-2)));
        Assert.True(getTransactionsCallCount() > 2);
    }

    [Fact]
    public async Task GlobalSync_ManualTrigger_EnforcesCooldown()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-manual-cooldown@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-cooldown", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());

        var first = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_first",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", first.Outcome);

        var second = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_second",
            cancellationToken: CancellationToken.None);

        Assert.Equal("skipped_cooldown", second.Outcome);
        Assert.True(second.CooldownRemainingSeconds > 0);
        Assert.NotNull(second.CooldownUntilUtc);
    }

    [Fact]
    public async Task GlobalSync_ManualCooldown_UsesConfiguredTenMinuteWindow()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.manual-cooldown-config@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-manual-cooldown-config", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var globalSyncService = harness.CreateGlobalSyncService(
            ValidSandboxOptions(),
            bankingSyncOptions: new BankingSyncOptions
            {
                ManualCooldownMinutes = 10,
                AutoSyncIntervalMinutes = 10
            });

        var first = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_cooldown_config_first",
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", first.Outcome);

        var latestManualTriggerAudit = await harness.DbContext.AuditEvents
            .Where(x => x.ActorId == user.Id && x.EventName == "global_manual_sync_triggered")
            .OrderByDescending(x => x.EventTimestampUtc)
            .FirstAsync();

        latestManualTriggerAudit.EventTimestampUtc = DateTime.UtcNow.AddMinutes(-11);
        await harness.DbContext.SaveChangesAsync();

        var second = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_cooldown_config_second",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", second.Outcome);
    }

    [Fact]
    public async Task GlobalSync_ManualTrigger_ForceTrue_BypassesCooldown()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.manual-force-cooldown@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-manual-force-cooldown", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var globalSyncService = harness.CreateGlobalSyncService(
            ValidSandboxOptions(),
            bankingSyncOptions: new BankingSyncOptions
            {
                ManualCooldownMinutes = 10,
                AutoSyncIntervalMinutes = 10
            });

        var first = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_force_first",
            cancellationToken: CancellationToken.None);
        Assert.Equal("completed", first.Outcome);

        var second = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_manual_force_second",
            force: true,
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", second.Outcome);
    }

    [Fact]
    public async Task GlobalSync_RecoversStaleSyncPendingConnection_AndRunsSync()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-stale-sync-pending@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-stale-sync-pending", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.SyncPending;
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddMinutes(-20);
        connection.UpdatedUtc = DateTime.UtcNow.AddMinutes(-20);
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_stale_sync_pending_recovery",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
        Assert.Single(result.Connections);
        Assert.NotEqual("skipped_sync_in_progress", result.Connections[0].Outcome);

        var refreshedConnection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.NotEqual(BankConnectionStatuses.SyncPending, refreshedConnection.Status);
    }

    [Fact]
    public async Task GlobalSync_SkipsFreshSyncPendingConnection_AsInProgress()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-fresh-sync-pending@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-fresh-sync-pending", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.Status = BankConnectionStatuses.SyncPending;
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddMinutes(-2);
        connection.UpdatedUtc = DateTime.UtcNow.AddMinutes(-2);
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_fresh_sync_pending_skip",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
        Assert.Single(result.Connections);
        Assert.Equal("skipped_sync_in_progress", result.Connections[0].Outcome);
    }

    [Fact]
    public async Task GlobalSync_AutoTrigger_SkipsWhenNotDue()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-auto-not-due@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-not-due", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "auto",
            source: "test_auto_not_due",
            cancellationToken: CancellationToken.None);

        Assert.Equal("skipped_not_due", result.Outcome);
        Assert.False(result.DueNow);
    }

    [Fact]
    public async Task GlobalSync_AutoTrigger_SkipsWhenProviderBackoffActive()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-auto-provider-backoff@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-backoff", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.LastErrorCode = "provider_too_many_requests";
        connection.LastSyncAttemptedUtc = DateTime.UtcNow.AddMinutes(-2);
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddMinutes(-30);
        connection.UpdatedUtc = DateTime.UtcNow.AddMinutes(-2);
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(
            ValidSandboxOptions(),
            bankingSyncOptions: new BankingSyncOptions
            {
                ManualCooldownMinutes = 10,
                AutoSyncIntervalMinutes = 10,
                ProviderRateLimitBackoffMinutes = 10
            });

        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "auto",
            source: "test_auto_provider_backoff",
            cancellationToken: CancellationToken.None);

        Assert.Equal("skipped_provider_backoff", result.Outcome);
        Assert.Equal(1, result.ProviderBackoffConnectionCount);
        Assert.False(result.DueNow);
    }

    [Fact]
    public async Task GlobalSync_AutoTrigger_ExecutesWhenDue()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-auto-due@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-global-due", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddHours(-2);
        connection.UpdatedUtc = DateTime.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "auto",
            source: "test_auto_due",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
    }

    [Fact]
    public async Task GlobalSync_AutoTrigger_UsesConfiguredTenMinuteInterval()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.auto-interval-config@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-auto-interval-config", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var connection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == start.Value.ConnectionId);
        connection.LastSuccessfulSyncUtc = DateTime.UtcNow.AddMinutes(-11);
        connection.UpdatedUtc = DateTime.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(
            ValidSandboxOptions(),
            bankingSyncOptions: new BankingSyncOptions
            {
                ManualCooldownMinutes = 10,
                AutoSyncIntervalMinutes = 10
            });

        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "auto",
            source: "test_auto_due_with_10_min_interval",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
    }

    [Fact]
    public async Task GlobalSync_Continues_WhenAuditWriteFails()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.global-audit-failure@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-audit-failure", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        var globalSyncService = harness.CreateGlobalSyncService(
            ValidSandboxOptions(),
            new ThrowingAuditService());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_audit_failure",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
    }

    [Fact]
    public async Task GlobalSync_HandlesPerConnectionExceptions_AsStructuredFailures()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: ThrowingNetworkHandler());

        var user = await harness.CreateUserAsync("bank.global-connection-throw@test.local");
        var now = DateTime.UtcNow;
        var connectionId = Guid.NewGuid();

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Throwing Bank",
            Status = BankConnectionStatuses.Synced,
            CreatedUtc = now.AddDays(-1),
            UpdatedUtc = now.AddMinutes(-2),
            Token = new BankConnectionToken
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                EncryptedRefreshToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("refresh-token")),
                AccessTokenExpiresUtc = now.AddHours(1),
                TokenObtainedUtc = now.AddDays(-1),
                IsRevoked = false
            }
        });
        await harness.DbContext.SaveChangesAsync();

        var globalSyncService = harness.CreateGlobalSyncService(ValidSandboxOptions());
        var result = await globalSyncService.ExecuteAsync(
            user.Id,
            trigger: "manual",
            source: "test_connection_exception",
            cancellationToken: CancellationToken.None);

        Assert.Equal("completed", result.Outcome);
        Assert.Equal(1, result.FailedConnectionCount);
        Assert.Single(result.Connections);
        Assert.Equal("failed", result.Connections[0].Outcome);
        Assert.Equal("sync_unexpected_exception", result.Connections[0].ErrorCode);
    }

    [Fact]
    public async Task CallbackFlow_InvalidAuthorizationCode_MarksReauthRequired()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: InvalidCodeHandler());

        var user = await harness.CreateUserAsync("bank.invalid-code@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("reused-code", state, null, null),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("truelayer_authorization_code_invalid", outcome.Code);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.ReauthRequired, connection.Status);
    }


    [Fact]
    public async Task CallbackFlow_PreservesCustomAppReturnUri_ForEnvironmentAwareReturn()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.return-uri@test.local");
        const string appReturnUri = "exp://192.168.0.11:8081/--/modals/add-account";

        var start = await harness.AuthService.StartLinkAsync(user.Id, appReturnUri, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.AppReturnUri);
        Assert.StartsWith("exp://192.168.0.11:8081/--/modals/add-account", outcome.AppReturnUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallbackFlow_PreservesCurrentAppReturnUri_ForEnvironmentAwareReturn()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.return-uri-current@test.local");
        const string appReturnUri = "exp://192.168.0.11:8081/--/(tabs)/accounts/connect-bank?intent=new";

        var start = await harness.AuthService.StartLinkAsync(user.Id, appReturnUri, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.AppReturnUri);
        Assert.StartsWith(
            "exp://192.168.0.11:8081/--/(tabs)/accounts/connect-bank",
            outcome.AppReturnUri,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartLink_InvalidConfiguration_ReturnsActionableError()
    {
        var options = ValidSandboxOptions();
        options.ClientSecret = string.Empty;

        await using var harness = new OpenBankingTestHarness(
            options: options,
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.invalid-config@test.local");
        var result = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("truelayer_not_configured", result.Error?.Code);
    }

    [Fact]
    public async Task CallbackFlow_EnvironmentMismatch_IsRejectedAndLoggedAsFailed()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.env-mismatch@test.local");
        var start = await harness.AuthService.StartLinkAsync(user.Id, null, null, CancellationToken.None);
        Assert.True(start.Succeeded);

        var liveOptions = ValidSandboxOptions();
        liveOptions.Environment = "live";
        liveOptions.AuthBaseUrl = "https://auth.truelayer.com";
        liveOptions.ApiBaseUrl = "https://api.truelayer.com";
        liveOptions.RedirectUri = "https://api.finance.nsireland.ie/api/banking/truelayer/callback";

        var liveAuthService = harness.BuildAuthService(
            liveOptions);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var outcome = await liveAuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-1", state, null, null),
            CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("truelayer_environment_mismatch", outcome.Code);

        var connection = await harness.DbContext.OpenBankingConnections
            .SingleAsync(x => x.Id == start.Value.ConnectionId);
        Assert.Equal(BankConnectionStatuses.Failed, connection.Status);
        Assert.Equal("truelayer_environment_mismatch", connection.LastErrorCode);
    }

    [Fact]
    public async Task ReconfirmFlow_ReusesExistingConnectionAndPreservesHistoricalRows()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.reconfirm@test.local");
        var now = DateTime.UtcNow;
        var connectionId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Mock Bank Plc",
            ProviderConnectionReference = "mock-bank",
            Status = BankConnectionStatuses.ReauthRequired,
            CreatedUtc = now.AddDays(-30),
            UpdatedUtc = now.AddDays(-1),
            LastSuccessfulSyncUtc = now.AddDays(-1)
        });

        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = user.Id,
            Name = "Sandbox Main Account",
            Type = "Current",
            Currency = "GBP",
            CreatedUtc = now.AddDays(-30)
        });

        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "acc-001",
            DisplayName = "Sandbox Main Account",
            Currency = "GBP",
            CurrentConnectionHealth = "healthy",
            RawPayloadJson = "{}",
            FinancialAccountId = financialAccountId,
            CreatedUtc = now.AddDays(-30),
            UpdatedUtc = now.AddDays(-1)
        });

        harness.DbContext.RawBankTransactions.Add(new RawBankTransaction
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            ProviderTransactionId = "tx-legacy-001",
            DedupeKey = "legacy-dedupe",
            Amount = -4.50m,
            Currency = "GBP",
            BookedAtUtc = now.AddDays(-20),
            Description = "Legacy coffee",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            RawPayloadJson = "{}",
            ImportedUtc = now.AddDays(-20)
        });

        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = financialAccountId,
            Amount = -4.50m,
            Currency = "GBP",
            Description = "Legacy coffee",
            BookedAtUtc = now.AddDays(-20),
            CreatedUtc = now.AddDays(-20)
        });

        await harness.DbContext.SaveChangesAsync();

        var start = await harness.AuthService.StartLinkAsync(user.Id, null, connectionId, CancellationToken.None);
        Assert.True(start.Succeeded);
        Assert.Equal(connectionId, start.Value!.ConnectionId);

        var state = GetQueryValue(start.Value.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-reconfirm", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);

        Assert.Equal(1, await harness.DbContext.OpenBankingConnections.CountAsync());
        Assert.Equal(1, await harness.DbContext.LinkedBankAccounts.CountAsync());
        Assert.Equal(3, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Equal(3, await harness.DbContext.Transactions.CountAsync());

        var refreshedConnection = await harness.DbContext.OpenBankingConnections.SingleAsync(x => x.Id == connectionId);
        Assert.Equal(BankConnectionStatuses.Synced, refreshedConnection.Status);
        Assert.NotNull(refreshedConnection.InitialBackfillCompletedUtc);
        Assert.NotNull(refreshedConnection.EarliestImportedTransactionUtc);
        Assert.NotNull(refreshedConnection.LatestImportedTransactionUtc);
    }

    [Fact]
    public async Task ReconfirmFlow_BackfillsMissingProjectionForExistingRawTransaction()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.reconfirm-backfill@test.local");
        var now = DateTime.UtcNow;
        var connectionId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Mock Bank Plc",
            ProviderConnectionReference = "mock-bank",
            Status = BankConnectionStatuses.ReauthRequired,
            CreatedUtc = now.AddDays(-30),
            UpdatedUtc = now.AddDays(-1),
            LastSuccessfulSyncUtc = now.AddDays(-1)
        });

        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = user.Id,
            Name = "Sandbox Main Account",
            Type = "Current",
            Currency = "GBP",
            CreatedUtc = now.AddDays(-30)
        });

        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "acc-001",
            DisplayName = "Sandbox Main Account",
            Currency = "GBP",
            CurrentConnectionHealth = "healthy",
            RawPayloadJson = "{}",
            FinancialAccountId = financialAccountId,
            CreatedUtc = now.AddDays(-30),
            UpdatedUtc = now.AddDays(-1)
        });

        harness.DbContext.RawBankTransactions.Add(new RawBankTransaction
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            ProviderTransactionId = "tx-001",
            DedupeKey = "raw-legacy-dedupe-key",
            Amount = -25.20m,
            Currency = "GBP",
            BookedAtUtc = new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc),
            Description = "Coffee Shop",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            RawPayloadJson = "{}",
            ImportedUtc = now.AddDays(-20)
        });

        await harness.DbContext.SaveChangesAsync();

        var start = await harness.AuthService.StartLinkAsync(user.Id, null, connectionId, CancellationToken.None);
        Assert.True(start.Succeeded);

        var state = GetQueryValue(start.Value!.AuthorizationUrl, "state");
        var callback = await harness.AuthService.HandleCallbackAsync(
            new TrueLayerCallbackQuery("auth-code-reconfirm", state, null, null),
            CancellationToken.None);

        Assert.True(callback.Succeeded);
        Assert.Equal(2, await harness.DbContext.RawBankTransactions.CountAsync());
        Assert.Equal(2, await harness.DbContext.Transactions.CountAsync());
    }


    [Fact]
    public async Task ListUserVisibleConnectionsAsync_ShowsOnlyActiveAndAttentionConnectionsWithoutHistoryNoise()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.visible@test.local");
        var now = DateTime.UtcNow;

        harness.DbContext.OpenBankingConnections.AddRange(
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Mock Bank Plc",
                ProviderConnectionReference = "provider-connection-1",
                Status = BankConnectionStatuses.Failed,
                CreatedUtc = now.AddMinutes(-40),
                UpdatedUtc = now.AddMinutes(-40)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Mock Bank Plc",
                ProviderConnectionReference = "provider-connection-1",
                Status = BankConnectionStatuses.Synced,
                CreatedUtc = now.AddMinutes(-20),
                UpdatedUtc = now.AddMinutes(-5)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Needs Attention Bank",
                ProviderConnectionReference = "provider-connection-2",
                Status = BankConnectionStatuses.ReauthRequired,
                CreatedUtc = now.AddMinutes(-10),
                UpdatedUtc = now.AddMinutes(-4)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Pending Bank",
                ProviderConnectionReference = "provider-connection-3",
                Status = BankConnectionStatuses.ConsentInProgress,
                CreatedUtc = now.AddMinutes(-6),
                UpdatedUtc = now.AddMinutes(-6)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Already Active Bank",
                ProviderConnectionReference = "provider-connection-4",
                Status = BankConnectionStatuses.Expired,
                CreatedUtc = now.AddMinutes(-30),
                UpdatedUtc = now.AddMinutes(-30)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Already Active Bank",
                ProviderConnectionReference = "provider-connection-4",
                Status = BankConnectionStatuses.ConnectedPendingSync,
                CreatedUtc = now.AddMinutes(-8),
                UpdatedUtc = now.AddMinutes(-2)
            },
            new OpenBankingConnection
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = BankingProviders.TrueLayer,
                ProviderEnvironment = "sandbox",
                ProviderDisplayName = "Revoked Bank",
                ProviderConnectionReference = "provider-connection-5",
                Status = BankConnectionStatuses.Revoked,
                CreatedUtc = now.AddMinutes(-50),
                UpdatedUtc = now.AddMinutes(-50)
            });

        await harness.DbContext.SaveChangesAsync();

        var overview = await harness.CreateConnectionService()
            .ListUserVisibleConnectionsAsync(user.Id, CancellationToken.None);

        Assert.Equal(2, overview.ActiveConnections.Count);
        Assert.Contains(overview.ActiveConnections, x => x.ProviderDisplayName == "Mock Bank Plc" && x.Status == BankConnectionStatuses.Synced);
        Assert.Contains(overview.ActiveConnections, x => x.ProviderDisplayName == "Already Active Bank" && x.Status == BankConnectionStatuses.ConnectedPendingSync);

        Assert.Single(overview.AttentionConnections);
        Assert.Equal("Needs Attention Bank", overview.AttentionConnections[0].ProviderDisplayName);
        Assert.Equal(BankConnectionStatuses.ReauthRequired, overview.AttentionConnections[0].Status);

        Assert.DoesNotContain(overview.ActiveConnections, x => x.Status == BankConnectionStatuses.Failed || x.Status == BankConnectionStatuses.ConsentInProgress || x.Status == BankConnectionStatuses.Revoked);
        Assert.DoesNotContain(overview.AttentionConnections, x => x.ProviderDisplayName == "Already Active Bank");
    }

    [Fact]
    public async Task DisconnectAsync_MarksPendingAndRevokesTokenBeforeBackgroundCleanup()
    {
        await using var harness = new OpenBankingTestHarness(
            options: ValidSandboxOptions(),
            httpHandler: SuccessfulFlowHandler());

        var user = await harness.CreateUserAsync("bank.disconnect@test.local");
        var now = DateTime.UtcNow;
        var connectionId = Guid.NewGuid();
        var financialAccountId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();

        harness.DbContext.OpenBankingConnections.Add(new OpenBankingConnection
        {
            Id = connectionId,
            UserId = user.Id,
            ProviderName = BankingProviders.TrueLayer,
            ProviderEnvironment = "sandbox",
            ProviderDisplayName = "Disconnect Bank",
            ProviderConnectionReference = "provider-disconnect-1",
            Status = BankConnectionStatuses.Synced,
            CreatedUtc = now.AddMinutes(-15),
            UpdatedUtc = now.AddMinutes(-1),
            Token = new BankConnectionToken
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                EncryptedRefreshToken = "ciphertext",
                AccessTokenExpiresUtc = now.AddHours(1),
                TokenObtainedUtc = now.AddMinutes(-5),
                IsRevoked = false
            }
        });

        harness.DbContext.FinancialAccounts.Add(new FinancialAccount
        {
            Id = financialAccountId,
            UserId = user.Id,
            Name = "Disconnect Projection",
            Type = "Current",
            Currency = "GBP",
            CreatedUtc = now.AddMinutes(-15)
        });

        harness.DbContext.LinkedBankAccounts.Add(new LinkedBankAccount
        {
            Id = linkedAccountId,
            ConnectionId = connectionId,
            ProviderAccountId = "provider-account-1",
            DisplayName = "Disconnect Account",
            Currency = "GBP",
            CurrentConnectionHealth = "healthy",
            RawPayloadJson = "{}",
            FinancialAccountId = financialAccountId,
            CreatedUtc = now.AddMinutes(-15),
            UpdatedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.BankBalanceSnapshots.Add(new BankBalanceSnapshot
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            Available = 10,
            Current = 10,
            Overdraft = 0,
            Currency = "GBP",
            CapturedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.RawBankTransactions.Add(new RawBankTransaction
        {
            Id = Guid.NewGuid(),
            LinkedBankAccountId = linkedAccountId,
            ProviderTransactionId = "raw-1",
            DedupeKey = "norm-1",
            Amount = -12.34m,
            Currency = "GBP",
            BookedAtUtc = now.AddDays(-1),
            Description = "Coffee",
            TransactionType = "DEBIT",
            TransactionStatus = "booked",
            RawPayloadJson = "{}",
            ImportedUtc = now.AddMinutes(-1)
        });

        harness.DbContext.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            FinancialAccountId = financialAccountId,
            Amount = -12.34m,
            Currency = "GBP",
            Description = "Coffee",
            BookedAtUtc = now.AddDays(-1),
            CreatedUtc = now.AddMinutes(-1)
        });

        await harness.DbContext.SaveChangesAsync();

        var service = harness.CreateConnectionService();
        var result = await service.DisconnectAsync(user.Id, connectionId, CancellationToken.None);

        Assert.True(result.Succeeded);

        var pendingConnection = await harness.DbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleAsync(x => x.Id == connectionId);
        Assert.Equal(BankConnectionStatuses.DisconnectPending, pendingConnection.Status);
        Assert.NotNull(pendingConnection.Token);
        Assert.True(pendingConnection.Token!.IsRevoked);
        Assert.Null(pendingConnection.Token.EncryptedRefreshToken);

        var connection = await harness.DbContext.OpenBankingConnections
            .Include(x => x.Token)
            .SingleAsync(x => x.Id == connectionId);
        Assert.Equal(BankConnectionStatuses.DisconnectPending, connection.Status);
        Assert.NotNull(connection.Token);
        Assert.True(connection.Token!.IsRevoked);
        Assert.Null(connection.Token.EncryptedRefreshToken);

        Assert.Single(await harness.DbContext.LinkedBankAccounts.ToListAsync());
        Assert.Single(await harness.DbContext.BankBalanceSnapshots.ToListAsync());
        Assert.Single(await harness.DbContext.RawBankTransactions.ToListAsync());
        Assert.Single(await harness.DbContext.FinancialAccounts.ToListAsync());
        Assert.Single(await harness.DbContext.Transactions.ToListAsync());

        var overview = await service.ListUserVisibleConnectionsAsync(user.Id, CancellationToken.None);
        Assert.Empty(overview.ActiveConnections);
        Assert.Single(overview.AttentionConnections);
        Assert.Equal(BankConnectionStatuses.DisconnectPending, overview.AttentionConnections[0].Status);
    }

    private static TrueLayerOptions ValidSandboxOptions() => new()
    {
        ClientId = "sandbox-client",
        ClientSecret = "sandbox-secret",
        RedirectUri = "http://localhost:5080/api/banking/truelayer/callback",
        Environment = "sandbox",
        AuthBaseUrl = "https://auth.truelayer-sandbox.com",
        ApiBaseUrl = "https://api.truelayer-sandbox.com"
    };

    private static HttpMessageHandler SuccessfulFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-1",
                      "refresh_token":"refresh-token-1",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-001",
                          "display_name": "Sandbox Main Account",
                          "currency": "GBP",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "mock-bank",
                            "display_name": "Mock Bank Plc"
                          },
                          "account_number": {
                            "sort_code": "010101",
                            "account_number": "12345678"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 2400.50,
                          "current": 2450.50,
                          "overdraft": -100.00,
                          "currency": "GBP",
                          "update_timestamp": "2026-03-09T02:10:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-001",
                          "normalised_provider_transaction_id":"norm-001",
                          "amount":-25.20,
                          "currency":"GBP",
                          "timestamp":"2026-03-08T12:00:00Z",
                          "description":"Coffee Shop",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-002",
                          "normalised_provider_transaction_id":"norm-002",
                          "amount":1500.00,
                          "currency":"GBP",
                          "timestamp":"2026-03-07T09:30:00Z",
                          "description":"Salary",
                          "transaction_type":"CREDIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler InvalidCodeHandler()
    {
        return new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "error":"invalid_grant",
                      "error_description":"Authorization code is expired or already used"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.NotFound, """{ "error": "not_found" }"""));
        });
    }

    private static HttpMessageHandler ThrowingNetworkHandler()
    {
        return new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Simulated network failure"));
    }

    private static HttpMessageHandler SuccessfulCrossBankTransferFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-transfer",
                      "refresh_token":"refresh-token-transfer",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-aib-001",
                          "display_name": "Primary Current Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        },
                        {
                          "account_id": "acc-revolut-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "revolut-ie-ob",
                            "display_name": "Revolut"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 900.00,
                          "current": 900.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-20T00:05:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1100.00,
                          "current": 1100.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-20T13:05:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-aib-100",
                          "normalised_provider_transaction_id":"norm-aib-100",
                          "amount":-100.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-20",
                          "description":"REVOLUT 85701",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-revolut-100",
                          "normalised_provider_transaction_id":"norm-revolut-100",
                          "amount":100.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-20T13:00:00Z",
                          "description":"AIB 85701",
                          "transaction_type":"CREDIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler CrossBankTransferWithSavingsPocketFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-transfer-pocket-guard",
                      "refresh_token":"refresh-token-transfer-pocket-guard",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-aib-pocket-001",
                          "display_name": "Primary Current Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-aib",
                            "display_name": "AIB"
                          }
                        },
                        {
                          "account_id": "acc-revolut-pocket-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-pocket-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 901.00,
                          "current": 901.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-01T00:05:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-pocket-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1099.00,
                          "current": 1099.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-01T09:10:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-pocket-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-aib-pocket-in-001",
                          "normalised_provider_transaction_id":"norm-aib-pocket-in-001",
                          "amount":1.00,
                          "currency":"EUR",
                          "timestamp":"2026-04-01",
                          "description":"Inbound Transfer Holder Alpha REF-CROSS-1",
                          "transaction_type":"CREDIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-pocket-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-revolut-main-out-001",
                          "normalised_provider_transaction_id":"norm-revolut-main-out-001",
                          "amount":-1.00,
                          "currency":"EUR",
                          "timestamp":"2026-04-01T09:07:00Z",
                          "description":"Outbound Transfer Holder Alpha",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-revolut-pocket-out-001",
                          "normalised_provider_transaction_id":"norm-revolut-pocket-out-001",
                          "amount":-1.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-31T21:37:00Z",
                          "description":"To Internal Savings Pocket",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler RepeatedSameAmountTransferChainFlowHandler()
    {
        var baseDateUtc = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc);
        var day1 = baseDateUtc;
        var day2 = baseDateUtc.AddDays(1);
        var day3 = baseDateUtc.AddDays(2);
        var day4 = baseDateUtc.AddDays(3);

        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-transfer-repeated-chain",
                      "refresh_token":"refresh-token-transfer-repeated-chain",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-aib-repeated-001",
                          "display_name": "Primary Current Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-aib",
                            "display_name": "AIB"
                          }
                        },
                        {
                          "account_id": "acc-revolut-repeated-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-repeated-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 2000.00,
                          "current": 2000.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-02T01:00:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-repeated-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 2000.00,
                          "current": 2000.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-02T07:30:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-repeated-001/transactions", StringComparison.Ordinal))
            {
                var scenario = new RepeatedAmountClusterScenarioBuilder()
                    .AddDateOnlyInbound("tx-chain-inbound-a", "norm-chain-inbound-a", 1.00m, day1, "REF-A")
                    .AddDateOnlyInbound("tx-chain-inbound-b", "norm-chain-inbound-b", 1.00m, day2, "REF-B")
                    .AddDateOnlyInbound("tx-chain-inbound-c", "norm-chain-inbound-c", 1.00m, day3, "REF-C")
                    .AddDateOnlyInbound("tx-chain-inbound-d", "norm-chain-inbound-d", 1.00m, day4, "REF-D");

                return Json(HttpStatusCode.OK, scenario.BuildResultsJson());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-repeated-001/transactions", StringComparison.Ordinal))
            {
                var scenario = new RepeatedAmountClusterScenarioBuilder()
                    .AddPreciseOutbound("tx-chain-outbound-a", "norm-chain-outbound-a", -1.00m, day1.AddHours(5).AddMinutes(33), "LEG-A")
                    .AddPreciseOutbound("tx-chain-outbound-b", "norm-chain-outbound-b", -1.00m, day2.AddHours(3).AddMinutes(19), "LEG-B")
                    .AddPreciseOutbound("tx-chain-outbound-c", "norm-chain-outbound-c", -1.00m, day3.AddHours(9).AddMinutes(7), "LEG-C")
                    .AddPreciseOutbound("tx-chain-outbound-d", "norm-chain-outbound-d", -1.00m, day4.AddHours(7).AddMinutes(26), "LEG-D")
                    .AddSavingsOutflow("tx-chain-savings-a", "norm-chain-savings-a", -1.00m, day2.AddHours(21).AddMinutes(37));

                return Json(HttpStatusCode.OK, scenario.BuildResultsJson());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler AmbiguousRepeatedAmountTransferFlowHandler()
    {
        var baseDateUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc);

        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-transfer-ambiguous-repeated",
                      "refresh_token":"refresh-token-transfer-ambiguous-repeated",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-aib-ambiguous-001",
                          "display_name": "Primary Current Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-aib",
                            "display_name": "AIB"
                          }
                        },
                        {
                          "account_id": "acc-revolut-ambiguous-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-ambiguous-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1500.00,
                          "current": 1500.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-02T00:10:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-ambiguous-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1500.00,
                          "current": 1500.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-02T08:10:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-ambiguous-001/transactions", StringComparison.Ordinal))
            {
                var scenario = new AmbiguousClusterScenarioBuilder()
                    .AddDateOnlyInbound(
                        "tx-ambiguous-inbound-a",
                        "norm-ambiguous-inbound-a",
                        1.00m,
                        baseDateUtc,
                        "REF-AMB-A");

                return Json(HttpStatusCode.OK, scenario.BuildResultsJson());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-ambiguous-001/transactions", StringComparison.Ordinal))
            {
                var scenario = new AmbiguousClusterScenarioBuilder()
                    .AddPreciseOutbound("tx-ambiguous-outbound-a", "norm-ambiguous-outbound-a", -1.00m, baseDateUtc.AddHours(7).AddMinutes(50), "AMB-A")
                    .AddPreciseOutbound("tx-ambiguous-outbound-b", "norm-ambiguous-outbound-b", -1.00m, baseDateUtc.AddHours(7).AddMinutes(55), "AMB-B");

                return Json(HttpStatusCode.OK, scenario.BuildResultsJson());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler RevolutRoundupSavingsFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-roundup-savings",
                      "refresh_token":"refresh-token-roundup-savings",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-revolut-roundup-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-roundup-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 980.10,
                          "current": 980.10,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-01T09:12:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-roundup-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-revolut-roundup-merchant-001",
                          "normalised_provider_transaction_id":"norm-revolut-roundup-merchant-001",
                          "amount":-14.47,
                          "currency":"EUR",
                          "timestamp":"2026-04-01T09:07:00Z",
                          "description":"Tesco Stores",
                          "merchant_name":"Tesco",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-revolut-roundup-savings-001",
                          "normalised_provider_transaction_id":"norm-revolut-roundup-savings-001",
                          "amount":-0.53,
                          "currency":"EUR",
                          "timestamp":"2026-04-01T09:08:00Z",
                          "description":"Spare change to Pocket",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-roundup-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler RevolutSavingsWithdrawalFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-savings-withdrawal",
                      "refresh_token":"refresh-token-savings-withdrawal",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-revolut-withdrawal-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-withdrawal-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1200.00,
                          "current": 1200.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-04-01T11:12:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-withdrawal-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-revolut-pocket-withdraw-001",
                          "normalised_provider_transaction_id":"norm-revolut-pocket-withdraw-001",
                          "amount":1.00,
                          "currency":"EUR",
                          "timestamp":"2026-04-01T11:10:00Z",
                          "description":"From Internal Savings Pocket",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-withdrawal-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler PendingOnlyFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-pending",
                      "refresh_token":"refresh-token-pending",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-pending-001",
                          "display_name": "Pending Feed Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "pending-bank",
                            "display_name": "Pending Bank"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1200.00,
                          "current": 1200.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-20T15:00:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-pending-001",
                          "normalised_provider_transaction_id":"norm-pending-001",
                          "amount":-42.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-20T15:15:00Z",
                          "description":"Cafe hold",
                          "transaction_type":"DEBIT",
                          "status":"pending"
                        }
                      ]
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler SettledEndpointPendingStatusFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-settled-status",
                      "refresh_token":"refresh-token-settled-status",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-settled-status-001",
                          "display_name": "Settled Endpoint Status Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-settled-status-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1380.00,
                          "current": 1400.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-30T15:00:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-settled-status-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-settled-pending-status-001",
                          "normalised_provider_transaction_id":"norm-settled-pending-status-001",
                          "amount":-20.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-30T15:04:00Z",
                          "description":"Lunch charge",
                          "transaction_type":"DEBIT",
                          "status":"pending"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-settled-status-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler SameFingerprintDistinctTransactionsFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-same-fingerprint",
                      "refresh_token":"refresh-token-same-fingerprint",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-same-fingerprint-001",
                          "display_name": "Same Fingerprint Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-aib",
                            "display_name": "AIB"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-same-fingerprint-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 500.00,
                          "current": 500.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-30T08:00:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-same-fingerprint-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-same-1",
                          "normalised_provider_transaction_id":"norm-same-1",
                          "amount":-15.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-30T09:00:00Z",
                          "description":"Coffee Shop",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-same-2",
                          "normalised_provider_transaction_id":"norm-same-2",
                          "amount":-15.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-30T09:00:00Z",
                          "description":"Coffee Shop",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-same-fingerprint-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler BalanceSuccessTransactionFailureFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-balance-durable",
                      "refresh_token":"refresh-token-balance-durable",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-balance-durable-001",
                          "display_name": "AIB Balance Durable",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-balance-durable-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 777.00,
                          "current": 800.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-31T10:05:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-balance-durable-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.InternalServerError,
                    """
                    {
                      "error": "provider_http_error",
                      "error_description": "Synthetic transaction endpoint failure"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-balance-durable-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler PendingThenBookedAccountFlowHandler()
    {
        var transactionCallCount = 0;

        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-pending-promote",
                      "refresh_token":"refresh-token-pending-promote",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-pending-booked-001",
                          "display_name": "AIB Pending Promote",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-booked-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 950.00,
                          "current": 1000.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-30T08:15:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-booked-001/transactions", StringComparison.Ordinal))
            {
                var currentCall = Interlocked.Increment(ref transactionCallCount);
                if (currentCall == 1)
                {
                    return Json(HttpStatusCode.OK, """{ "results": [] }""");
                }

                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-pending-booked-1",
                          "normalised_provider_transaction_id":"norm-pending-booked-1",
                          "amount":-42.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-30T09:40:00Z",
                          "description":"AIB Card Payment",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-pending-booked-001/transactions/pending", StringComparison.Ordinal))
            {
                if (Volatile.Read(ref transactionCallCount) <= 1)
                {
                    return Json(HttpStatusCode.OK,
                        """
                        {
                          "results": [
                            {
                              "transaction_id":"tx-pending-booked-1",
                              "normalised_provider_transaction_id":"norm-pending-booked-1",
                              "amount":-42.00,
                              "currency":"EUR",
                              "timestamp":"2026-03-30T09:40:00Z",
                              "description":"AIB Card Payment",
                              "transaction_type":"DEBIT",
                              "status":"pending"
                            }
                          ]
                        }
                        """);
                }

                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler SharedNormalizedIdDistinctTransactionsFlowHandler()
    {
        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-shared-normalized-distinct",
                      "refresh_token":"refresh-token-shared-normalized-distinct",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-revolut-shared-normalized-001",
                          "display_name": "Linked External Account",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-revolut-ie",
                            "display_name": "REVOLUT-IE"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-shared-normalized-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1200.00,
                          "current": 1200.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-31T10:00:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-shared-normalized-001/transactions", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "transaction_id":"tx-revolut-merchant-001",
                          "normalised_provider_transaction_id":"norm-revolut-shared-001",
                          "amount":-15.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-31T02:19:34Z",
                          "description":"Tesco Stores",
                          "merchant_name":"Tesco",
                          "transaction_type":"DEBIT",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-revolut-roundup-001",
                          "normalised_provider_transaction_id":"norm-revolut-shared-001",
                          "amount":-1.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-31T02:19:34Z",
                          "description":"Round up to Pocket",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-revolut-shared-normalized-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static HttpMessageHandler AibCappedOldestSliceFlowHandler(
        out Func<int> getTransactionsCallCount,
        out DateTime referenceNowUtc)
    {
        var transactionCallCount = 0;
        referenceNowUtc = DateTime.UtcNow;
        var referenceNow = referenceNowUtc;
        var catalog = BuildAibCappedTransactionCatalog(referenceNowUtc);
        getTransactionsCallCount = () => Volatile.Read(ref transactionCallCount);

        return new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "access_token":"access-token-aib-capped-window",
                      "refresh_token":"refresh-token-aib-capped-window",
                      "expires_in":1800,
                      "scope":"accounts balance transactions offline_access"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "account_id": "acc-aib-capped-001",
                          "display_name": "AIB Capped Feed",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-capped-001/balance", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "available": 1500.00,
                          "current": 1500.00,
                          "currency": "EUR",
                          "update_timestamp": "2026-03-30T08:15:00Z"
                        }
                      ]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-capped-001/transactions", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref transactionCallCount);
                var (fromUtc, toUtc) = ParseRequestedWindow(request.RequestUri, referenceNow);

                var cappedSlice = catalog
                    .Where(x => x.BookedAtUtc >= fromUtc && x.BookedAtUtc <= toUtc)
                    .OrderBy(x => x.BookedAtUtc)
                    .Take(100)
                    .Select(x => new
                    {
                        transaction_id = x.TransactionId,
                        normalised_provider_transaction_id = x.NormalizedProviderTransactionId,
                        amount = x.Amount,
                        currency = "EUR",
                        timestamp = x.BookedAtUtc.ToString("O"),
                        description = x.Description,
                        transaction_type = "DEBIT",
                        status = "booked"
                    })
                    .ToList();

                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { results = cappedSlice }));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/data/v1/accounts/acc-aib-capped-001/transactions/pending", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "results": [] }""");
            }

            return Json(HttpStatusCode.NotFound, """{ "error": "not_found", "error_description":"Missing mock route." }""");
        });
    }

    private static string BuildTransactionsResponseJson(IEnumerable<TransferScenarioTransactionSeed> rows)
    {
        var payload = new
        {
            results = rows.Select(row => new
            {
                transaction_id = row.TransactionId,
                normalised_provider_transaction_id = row.NormalizedProviderTransactionId,
                amount = row.Amount,
                currency = row.Currency,
                timestamp = row.Timestamp,
                description = row.Description,
                transaction_type = row.TransactionType,
                status = row.Status
            })
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<CappedProviderTransactionSeed> BuildAibCappedTransactionCatalog(DateTime referenceNowUtc)
    {
        var catalog = new List<CappedProviderTransactionSeed>();
        var cursor = referenceNowUtc.AddDays(-45);
        var index = 0;

        while (cursor < referenceNowUtc.AddMinutes(-15))
        {
            index++;
            catalog.Add(new CappedProviderTransactionSeed(
                TransactionId: $"tx-aib-capped-{index:D4}",
                NormalizedProviderTransactionId: $"norm-aib-capped-{index:D4}",
                Amount: -(10m + (index % 30)),
                BookedAtUtc: cursor,
                Description: $"AIB synthetic payment {index:D4}"));
            cursor = cursor.AddHours(2);
        }

        return catalog;
    }

    private static (DateTime FromUtc, DateTime ToUtc) ParseRequestedWindow(Uri? requestUri, DateTime fallbackNowUtc)
    {
        var defaults = (FromUtc: fallbackNowUtc.AddYears(-5), ToUtc: fallbackNowUtc);
        if (requestUri is null)
        {
            return defaults;
        }

        var fromRaw = GetQueryParameter(requestUri.Query, "from");
        var toRaw = GetQueryParameter(requestUri.Query, "to");

        var fromUtc = DateTimeOffset.TryParse(fromRaw, out var parsedFrom)
            ? parsedFrom.UtcDateTime
            : defaults.FromUtc;
        var toUtc = DateTimeOffset.TryParse(toRaw, out var parsedTo)
            ? parsedTo.UtcDateTime
            : defaults.ToUtc;

        return (fromUtc, toUtc);
    }

    private static string? GetQueryParameter(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2, StringSplitOptions.None);
            if (pair.Length == 0)
            {
                continue;
            }

            var decodedKey = Uri.UnescapeDataString(pair[0]);
            if (!string.Equals(decodedKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : null;
        }

        return null;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string GetQueryValue(string url, string key)
    {
        var uri = new Uri(url);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);

        return query[key];
    }

    private sealed class OpenBankingTestHarness : IAsyncDisposable
    {
        private readonly IRequestContextAccessor _requestContext = new TestRequestContextAccessor();
        private readonly IAuditService _auditService;
        private readonly HttpMessageHandler _httpHandler;

        public AppDbContext DbContext { get; }
        public TrueLayerAuthService AuthService { get; private set; }

        public OpenBankingTestHarness(TrueLayerOptions options, HttpMessageHandler httpHandler)
        {
            _httpHandler = httpHandler;
            DbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"open-banking-tests-{Guid.NewGuid():N}")
                .Options);

            _auditService = new AuditService(DbContext, _requestContext, NullLogger<AuditService>.Instance);
            AuthService = BuildAuthService(options);
        }

        public TrueLayerAuthService BuildAuthService(TrueLayerOptions options)
        {
            var syncService = CreateSyncService(options);
            var configurationService = new TrueLayerConfigurationService(Options.Create(options));
            var tokenService = new TrueLayerTokenService(
                new TrueLayerHttpClient(new HttpClient(_httpHandler)),
                NullLogger<TrueLayerTokenService>.Instance);

            return new TrueLayerAuthService(
                configurationService,
                CreateConnectionService(),
                tokenService,
                syncService,
                new TestTrueLayerSyncQueue(syncService),
                _auditService,
                NullLogger<TrueLayerAuthService>.Instance);
        }

        public BankGlobalSyncService CreateGlobalSyncService(
            TrueLayerOptions options,
            IAuditService? auditService = null,
            BankingSyncOptions? bankingSyncOptions = null)
        {
            return new BankGlobalSyncService(
                DbContext,
                CreateSyncService(options),
                auditService ?? _auditService,
                Options.Create(bankingSyncOptions ?? new BankingSyncOptions
                {
                    ManualCooldownMinutes = 60,
                    AutoSyncIntervalMinutes = 60
                }),
                NullLogger<BankGlobalSyncService>.Instance);
        }

        public BankSyncService CreateSyncServiceForTesting(TrueLayerOptions options)
        {
            return CreateSyncService(options);
        }

        private BankSyncService CreateSyncService(TrueLayerOptions options)
        {
            var configurationService = new TrueLayerConfigurationService(Options.Create(options));
            var httpClient = new TrueLayerHttpClient(new HttpClient(_httpHandler));
            var tokenService = new TrueLayerTokenService(httpClient, NullLogger<TrueLayerTokenService>.Instance);
            var dataService = new TrueLayerDataService(httpClient, NullLogger<TrueLayerDataService>.Instance);
            var connectionService = CreateConnectionService();
            var enrichmentQueue = new ImmediateBankDeterministicEnrichmentQueue();
            var normalizationService = new TransactionNormalizationService();
            var featureExtractor = new TransactionFeatureExtractor(normalizationService);
            var transferPairingEngine = new TransferPairingEngine();
            var savingsRoutingPolicy = new SavingsRoutingPolicy();
            var savingsTransferClassifier = new SavingsTransferClassifier();
            var retryPlanner = new DeterministicClassificationRetryPlanner();
            var metrics = new DeterministicCategorizationMetrics();
            var persistenceService = new DeterministicClassificationPersistenceService(
                DbContext,
                normalizationService,
                featureExtractor,
                transferPairingEngine,
                savingsRoutingPolicy,
                savingsTransferClassifier,
                retryPlanner,
                metrics,
                NullLogger<DeterministicClassificationPersistenceService>.Instance);
            var categorizationService = new DeterministicTransactionCategorizationService(
                persistenceService,
                NullLogger<DeterministicTransactionCategorizationService>.Instance);
            var syncService = new BankSyncService(
                DbContext,
                connectionService,
                configurationService,
                tokenService,
                dataService,
                new TestSecretProtector(),
                _auditService,
                enrichmentQueue,
                categorizationService,
                metrics,
                NullLogger<BankSyncService>.Instance);
            enrichmentQueue.Attach(syncService);
            return syncService;
        }

        public BankConnectionService CreateConnectionService()
        {
            return new BankConnectionService(
                DbContext,
                _auditService,
                new NoOpBankDisconnectQueue(),
                NullLogger<BankConnectionService>.Instance);
        }

        public async Task<User> CreateUserAsync(string email)
        {
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                PrimaryEmail = email,
                NormalizedEmail = email.ToLowerInvariant(),
                DisplayName = "Banking Test User",
                Status = "active",
                OnboardingStatus = "profile_created",
                Role = "user",
                CreatedUtc = now,
                UpdatedUtc = now,
                LastLoginUtc = now,
                EmailVerified = true,
                IsDisabled = false,
                IsSuspended = false,
                DeletionRequested = false,
                Timezone = "UTC",
                Locale = "en-GB",
                PreferredCurrency = "GBP",
                PlanTier = "standard",
                BiometricUnlockEnabled = false
            };

            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class TestRequestContextAccessor : IRequestContextAccessor
    {
        public string CorrelationId => "banking-tests-correlation";
        public string SourceChannel => "api";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "test-agent";
        public string? Platform => "ios";
        public string? AppVersion => "1.0.0";
    }

    private sealed class TestTrueLayerSyncQueue(BankSyncService syncService) : ITrueLayerSyncQueue
    {
        public async ValueTask QueueInitialSyncAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
        {
            await syncService.SyncConnectionAsync(userId, connectionId, cancellationToken);
        }
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task WriteEventAsync(
            string category,
            string eventName,
            string targetEntityType,
            string? targetEntityId,
            Guid? actorId,
            string actorType,
            object? metadata,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated audit write failure");
        }
    }

    private sealed class NoOpBankDisconnectQueue : IBankDisconnectQueue
    {
        public async ValueTask QueueDisconnectCleanupAsync(
            Guid userId,
            Guid connectionId,
            CancellationToken cancellationToken = default)
        {
            await ValueTask.CompletedTask;
        }
    }

    private sealed class ImmediateBankDeterministicEnrichmentQueue : IBankDeterministicEnrichmentQueue
    {
        private BankSyncService? _syncService;

        public void Attach(BankSyncService syncService)
        {
            _syncService = syncService;
        }

        public async ValueTask QueueConnectionAsync(
            Guid userId,
            Guid connectionId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            if (_syncService is null)
            {
                return;
            }

            await _syncService.RunDeterministicEnrichmentAsync(
                userId,
                connectionId,
                trigger: $"test_queue:{reason}",
                cancellationToken);
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        }

        public string Unprotect(string ciphertext)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
        }
    }

    private sealed record CappedProviderTransactionSeed(
        string TransactionId,
        string NormalizedProviderTransactionId,
        decimal Amount,
        DateTime BookedAtUtc,
        string Description);

    private sealed record TransferScenarioTransactionSeed(
        string TransactionId,
        string NormalizedProviderTransactionId,
        decimal Amount,
        string Currency,
        string Timestamp,
        string Description,
        string TransactionType,
        string Status = "booked");

    private class TransferPairScenarioBuilder
    {
        protected readonly List<TransferScenarioTransactionSeed> rows = [];

        public TransferPairScenarioBuilder AddDateOnlyInbound(
            string transactionId,
            string normalizedProviderTransactionId,
            decimal amount,
            DateTime bookedDateUtc,
            string referenceSuffix)
        {
            rows.Add(new TransferScenarioTransactionSeed(
                transactionId,
                normalizedProviderTransactionId,
                amount,
                "EUR",
                bookedDateUtc.ToString("yyyy-MM-dd"),
                $"{SyntheticInboundTransferDescription} {referenceSuffix}",
                "CREDIT"));
            return this;
        }

        public TransferPairScenarioBuilder AddPreciseOutbound(
            string transactionId,
            string normalizedProviderTransactionId,
            decimal amount,
            DateTime timestampUtc,
            string referenceSuffix)
        {
            rows.Add(new TransferScenarioTransactionSeed(
                transactionId,
                normalizedProviderTransactionId,
                amount,
                "EUR",
                timestampUtc.ToString("O"),
                $"{SyntheticOutboundTransferDescription} {referenceSuffix}",
                "TRANSFER"));
            return this;
        }

        public TransferPairScenarioBuilder AddSavingsOutflow(
            string transactionId,
            string normalizedProviderTransactionId,
            decimal amount,
            DateTime timestampUtc)
        {
            rows.Add(new TransferScenarioTransactionSeed(
                transactionId,
                normalizedProviderTransactionId,
                amount,
                "EUR",
                timestampUtc.ToString("O"),
                $"To {SyntheticSavingsDestinationLabel}",
                "TRANSFER"));
            return this;
        }

        public string BuildResultsJson() => BuildTransactionsResponseJson(rows);
    }

    private sealed class RepeatedAmountClusterScenarioBuilder : TransferPairScenarioBuilder;
    private sealed class AmbiguousClusterScenarioBuilder : TransferPairScenarioBuilder;
}


