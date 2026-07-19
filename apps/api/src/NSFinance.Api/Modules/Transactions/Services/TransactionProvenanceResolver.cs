using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Transactions.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Transactions.Services;

internal sealed record TransactionReadProvenance(
    string AccountSource,
    string AccountCurrency,
    TransactionEffectiveTimeDto EffectiveTime,
    StatementImportProvenanceDto? StatementImport);

internal sealed record StatementImportProvenanceReadModel(
    Guid TransactionId,
    Guid BatchId,
    Guid FinancialAccountId,
    int RowNumber,
    string TimestampPrecision,
    DateOnly? EffectiveDate,
    DateTime? EffectiveAtUtc,
    string AccountCurrency,
    DateTime CommittedUtc);

internal static class TransactionProvenanceResolver
{
    public static async Task<IReadOnlyDictionary<Guid, StatementImportProvenanceReadModel>>
        GetStatementImportsByTransactionIdAsync(
            AppDbContext dbContext,
            Guid userId,
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return new Dictionary<Guid, StatementImportProvenanceReadModel>();
        }

        var distinctTransactionIds = transactionIds.Distinct().ToArray();
        var rows = await BuildStatementImportQuery(dbContext, userId, distinctTransactionIds)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.TransactionId);
    }

    internal static IQueryable<StatementImportProvenanceReadModel> BuildStatementImportQuery(
        AppDbContext dbContext,
        Guid userId,
        Guid[] transactionIds)
    {
        return dbContext.StatementImportRows
            .AsNoTracking()
            .Where(row =>
                row.CommittedTransactionId.HasValue
                && transactionIds.Contains(row.CommittedTransactionId.Value)
                && row.ReviewDisposition == StatementImportReviewDispositions.Included
                && row.ImportJob != null
                && row.ImportJob.UserId == userId
                && row.ImportJob.Kind == ImportJobKinds.StatementCsv
                && row.ImportJob.Status == StatementImportBatchStatuses.Committed
                && row.ImportJob.FinancialAccountId.HasValue
                && row.ImportJob.CommittedUtc.HasValue)
            .Select(row => new StatementImportProvenanceReadModel(
                row.CommittedTransactionId!.Value,
                row.ImportJobId,
                row.ImportJob!.FinancialAccountId!.Value,
                row.RowNumber,
                row.TimestampPrecision!,
                row.EffectiveDate,
                row.EffectiveAtUtc,
                row.ImportJob.AccountCurrency!,
                row.ImportJob.CommittedUtc!.Value));
    }

    public static TransactionReadProvenance Resolve(
        Guid transactionId,
        Guid financialAccountId,
        string accountSource,
        string accountCurrency,
        string transactionCurrency,
        string entryKind,
        DateTime bookedAtUtc,
        IReadOnlyDictionary<Guid, StatementImportProvenanceReadModel> statementImportsByTransactionId)
    {
        statementImportsByTransactionId.TryGetValue(transactionId, out var statementImport);

        if (entryKind != TransactionEntryKinds.StatementImport)
        {
            if (statementImport is not null)
            {
                throw Inconsistent(transactionId);
            }

            // Providers such as AIB book transactions with date-level granularity that
            // TrueLayer surfaces as exact-midnight instants, either in UTC or in the
            // bank's local Irish day (23:00Z during Irish summer time). Reporting those
            // as real instants invents a "00:00" time the provider never supplied, so
            // classify them as date precision on the local calendar day. A genuine
            // midnight-exact purchase loses only its implied time display, never its
            // booked ordering, which stays on bookedAtUtc.
            if (bookedAtUtc.Kind == DateTimeKind.Utc
                && TryResolveProviderDateGranularity(bookedAtUtc, out var providerLocalDate))
            {
                return new TransactionReadProvenance(
                    accountSource,
                    accountCurrency,
                    new TransactionEffectiveTimeDto(
                        StatementImportTimestampPrecisions.Date,
                        providerLocalDate,
                        InstantUtc: null),
                    StatementImport: null);
            }

            return new TransactionReadProvenance(
                accountSource,
                accountCurrency,
                new TransactionEffectiveTimeDto(
                    StatementImportTimestampPrecisions.Instant,
                    Date: null,
                    InstantUtc: bookedAtUtc),
                StatementImport: null);
        }

        if (statementImport is null
            || accountSource != FinancialAccountSources.Manual
            || statementImport.FinancialAccountId != financialAccountId
            || !string.Equals(statementImport.AccountCurrency, accountCurrency, StringComparison.Ordinal)
            || !string.Equals(transactionCurrency, accountCurrency, StringComparison.Ordinal)
            || bookedAtUtc.Kind != DateTimeKind.Utc
            || statementImport.CommittedUtc.Kind != DateTimeKind.Utc)
        {
            throw Inconsistent(transactionId);
        }

        TransactionEffectiveTimeDto effectiveTime;
        if (statementImport.TimestampPrecision == StatementImportTimestampPrecisions.Date
            && statementImport.EffectiveDate.HasValue
            && !statementImport.EffectiveAtUtc.HasValue)
        {
            effectiveTime = new TransactionEffectiveTimeDto(
                StatementImportTimestampPrecisions.Date,
                statementImport.EffectiveDate,
                InstantUtc: null);
        }
        else if (statementImport.TimestampPrecision == StatementImportTimestampPrecisions.Instant
            && !statementImport.EffectiveDate.HasValue
            && statementImport.EffectiveAtUtc is { Kind: DateTimeKind.Utc } effectiveAtUtc
            && effectiveAtUtc == bookedAtUtc)
        {
            effectiveTime = new TransactionEffectiveTimeDto(
                StatementImportTimestampPrecisions.Instant,
                Date: null,
                effectiveAtUtc);
        }
        else
        {
            throw Inconsistent(transactionId);
        }

        return new TransactionReadProvenance(
            accountSource,
            accountCurrency,
            effectiveTime,
            new StatementImportProvenanceDto(
                statementImport.BatchId,
                statementImport.RowNumber,
                statementImport.CommittedUtc));
    }

    private static readonly TimeZoneInfo IrishTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    private static bool TryResolveProviderDateGranularity(
        DateTime bookedAtUtc,
        out DateOnly localDate)
    {
        if (bookedAtUtc.TimeOfDay == TimeSpan.Zero)
        {
            localDate = DateOnly.FromDateTime(bookedAtUtc);
            return true;
        }

        var irishLocal = TimeZoneInfo.ConvertTimeFromUtc(bookedAtUtc, IrishTimeZone);
        if (irishLocal.TimeOfDay == TimeSpan.Zero)
        {
            localDate = DateOnly.FromDateTime(irishLocal);
            return true;
        }

        localDate = default;
        return false;
    }

    private static InvalidOperationException Inconsistent(Guid transactionId) =>
        new($"Statement import provenance is inconsistent for transaction '{transactionId}'.");
}
