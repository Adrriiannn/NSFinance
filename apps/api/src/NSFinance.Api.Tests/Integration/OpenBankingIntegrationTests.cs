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
using NSFinance.Api.Modules.ExpenseTracker.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Integration;

public class OpenBankingIntegrationTests
{
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

        var debitToMarius = transactions.Single(x => x.Description.Contains("To Marius Albu", StringComparison.OrdinalIgnoreCase));
        var debitToPocket = transactions.Single(x => x.Description.Contains("Flexible Cash Funds", StringComparison.OrdinalIgnoreCase));
        var aibIncoming = transactions.Single(x => x.Description.Contains("ALBU MARIUS", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(aibIncoming.Id, debitToMarius.LinkedTransferTransactionId);
        Assert.Equal(debitToMarius.Id, aibIncoming.LinkedTransferTransactionId);
        Assert.Equal(TransactionTransferKind.LinkedInternal, debitToMarius.TransferKind);
        Assert.Equal(TransactionTransferKind.LinkedInternal, aibIncoming.TransferKind);

        Assert.Null(debitToPocket.LinkedTransferTransactionId);
        Assert.NotEqual(TransactionTransferKind.LinkedInternal, debitToPocket.TransferKind);
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
                          "display_name": "AIB Current",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "aib-ie-ob",
                            "display_name": "Allied Irish Bank"
                          }
                        },
                        {
                          "account_id": "acc-revolut-001",
                          "display_name": "Revolut Main",
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
                          "display_name": "AIB Current",
                          "currency": "EUR",
                          "account_type": "TRANSACTION",
                          "provider": {
                            "provider_id": "ob-aib",
                            "display_name": "AIB"
                          }
                        },
                        {
                          "account_id": "acc-revolut-pocket-001",
                          "display_name": "Revolut Main",
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
                          "description":"ALBU MARIUS IE260401",
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
                          "description":"To Marius Albu",
                          "transaction_type":"TRANSFER",
                          "status":"booked"
                        },
                        {
                          "transaction_id":"tx-revolut-pocket-out-001",
                          "normalised_provider_transaction_id":"norm-revolut-pocket-out-001",
                          "amount":-1.00,
                          "currency":"EUR",
                          "timestamp":"2026-03-31T21:37:00Z",
                          "description":"To Flexible Cash Funds",
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
                          "display_name": "Revolut Current",
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

        private BankSyncService CreateSyncService(TrueLayerOptions options)
        {
            var configurationService = new TrueLayerConfigurationService(Options.Create(options));
            var httpClient = new TrueLayerHttpClient(new HttpClient(_httpHandler));
            var tokenService = new TrueLayerTokenService(httpClient, NullLogger<TrueLayerTokenService>.Instance);
            var dataService = new TrueLayerDataService(httpClient, NullLogger<TrueLayerDataService>.Instance);
            var connectionService = CreateConnectionService();
            return new BankSyncService(
                DbContext,
                connectionService,
                configurationService,
                tokenService,
                dataService,
                new TestSecretProtector(),
                _auditService,
                NullLogger<BankSyncService>.Instance);
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
}


