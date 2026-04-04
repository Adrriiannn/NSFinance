using System.Diagnostics;

namespace NSFinance.Api.Modules.Banking.Services.Deterministic;

public sealed class DeterministicTransactionCategorizationService(
    DeterministicClassificationPersistenceService persistenceService,
    ILogger<DeterministicTransactionCategorizationService> logger)
{
    public async Task<DeterministicCategorizationSummary> CategorizeWindowAsync(
        Guid userId,
        DateTime selectionStartUtc,
        DateTime selectionEndUtc,
        DateTime contextStartUtc,
        DateTime contextEndUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var summary = await persistenceService.EvaluateWindowAsync(
            userId,
            selectionStartUtc,
            selectionEndUtc,
            contextStartUtc,
            contextEndUtc,
            now,
            cancellationToken);
        sw.Stop();

        LogSummary(
            userId,
            trigger: "window",
            summary,
            sw.Elapsed.TotalMilliseconds);

        return summary;
    }

    public async Task<DeterministicCategorizationSummary> CategorizeTransactionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> transactionIds,
        DateTime contextStartUtc,
        DateTime contextEndUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var summary = await persistenceService.EvaluateTransactionsAsync(
            userId,
            transactionIds,
            contextStartUtc,
            contextEndUtc,
            now,
            cancellationToken);
        sw.Stop();

        LogSummary(
            userId,
            trigger: "id_batch",
            summary,
            sw.Elapsed.TotalMilliseconds);

        return summary;
    }

    private void LogSummary(
        Guid userId,
        string trigger,
        DeterministicCategorizationSummary summary,
        double durationMs)
    {
        logger.LogInformation(
            "Deterministic categorization run userId={UserId} trigger={Trigger} version={Version} rowsSelected={RowsSelected} rowsEvaluated={RowsEvaluated} rowsTerminal={RowsTerminal} rowsClassifiedBankTransfer={RowsClassifiedBankTransfer} rowsClassifiedSavingsTransfer={RowsClassifiedSavingsTransfer} rowsNoMatch={RowsNoMatch} rowsDeferredCounterparty={RowsDeferredCounterparty} rowsDeferredContext={RowsDeferredContext} rowsRejectedAmbiguous={RowsRejectedAmbiguous} pairingAttempts={PairingAttempts} pairingSuccess={PairingSuccess} relationshipRowsUpserted={RelationshipRowsUpserted} retryQueueAdditions={RetryQueueAdditions} durationMs={DurationMs}",
            userId,
            trigger,
            DeterministicCategorizationConstants.CurrentClassificationVersion,
            summary.RowsSelected,
            summary.RowsEvaluated,
            summary.RowsTerminal,
            summary.RowsClassifiedBankTransfer,
            summary.RowsClassifiedSavingsTransfer,
            summary.RowsNoMatch,
            summary.RowsDeferredCounterparty,
            summary.RowsDeferredContext,
            summary.RowsRejectedAmbiguous,
            summary.PairingAttemptCount,
            summary.PairingSuccessCount,
            summary.RelationshipRowsUpserted,
            summary.RowsRetryQueued,
            Math.Round(durationMs, 2, MidpointRounding.AwayFromZero));
    }
}
