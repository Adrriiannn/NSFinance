using System.Security.Cryptography;
using System.Text;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class InferredFinancialCommitmentService(
    IRecurringPatternService recurringPatternService,
    TransactionNormalizationService normalizationService)
{
    private const int InferredSeriesLimit = 64;

    internal async Task<IReadOnlyList<FinancialCommitmentDto>> BuildAsync(
        IReadOnlyList<InferredCommitmentTransactionRow> rows,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (rows.Count < 3)
        {
            return [];
        }

        var transactions = rows
            .Select(ToTransaction)
            .ToList();
        var transactionById = transactions.ToDictionary(transaction => transaction.Id);
        var rowById = rows.ToDictionary(row => row.Id);
        var descriptorBuilder = new RecurringPatternOptions();
        var descriptors = transactions.ToDictionary(
            transaction => transaction.Id,
            transaction => descriptorBuilder.BuildDescriptor(normalizationService, transaction.Description));
        var options = new RecurringPatternOptions
        {
            PrecomputedTextByTransactionId = descriptors
        };
        var evaluatedSeries = new HashSet<string>(StringComparer.Ordinal);
        var inferred = new List<FinancialCommitmentDto>();

        foreach (var row in rows
                     .OrderByDescending(item => item.BookedAtUtc)
                     .ThenByDescending(item => item.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evaluatedSeries.Count >= InferredSeriesLimit)
            {
                break;
            }

            var descriptor = descriptors[row.Id];
            if (string.IsNullOrWhiteSpace(descriptor.BillingSignatureKey))
            {
                continue;
            }

            var currency = FinancialCommitmentContractPolicy.NormalizeCurrency(row.Currency) ?? string.Empty;
            var seriesKey = $"{row.FinancialAccountId:N}|{currency}|{descriptor.BillingSignatureKey}";
            if (!evaluatedSeries.Add(seriesKey))
            {
                continue;
            }

            var result = await recurringPatternService.EvaluateAsync(
                transactionById[row.Id],
                transactions,
                options,
                cancellationToken);
            if (!result.IsRecurring)
            {
                continue;
            }

            var evidenceRows = result.MatchedTransactionIds
                .Prepend(row.Id)
                .Distinct()
                .Select(id => rowById.GetValueOrDefault(id))
                .Where(item => item is not null)
                .Cast<InferredCommitmentTransactionRow>()
                .OrderByDescending(item => item.BookedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToList();
            var earliestObserved = evidenceRows.Min(
                item => FinancialCommitmentContractPolicy.EnsureUtc(item.BookedAtUtc));
            var nextDateUtc = EstimateNextDate(row.BookedAtUtc, result.Cadence);
            var isVariableAmount = result.AmountStabilityTier is
                RecurringAmountStabilityTier.Shifted or
                RecurringAmountStabilityTier.MajorShift;
            var typicalAmount = Math.Abs(result.HistoricalTypicalAmount ?? row.Amount);
            var exclusions = BuildExclusions(result, nextDateUtc, utcNow);
            var reasonCodes = result.ReasonCodes
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();
            var evidence = evidenceRows
                .Take(8)
                .Select((item, index) => new FinancialCommitmentEvidenceDto(
                    "transaction_pattern",
                    item.Id,
                    FinancialCommitmentContractPolicy.EnsureUtc(item.BookedAtUtc),
                    "inferred_signal",
                    index == 0 ? reasonCodes : []))
                .ToList();

            inferred.Add(new FinancialCommitmentDto(
                BuildId(row.FinancialAccountId, currency, descriptor.BillingSignatureKey),
                "inferred_recurring",
                "needs_review",
                "inferred",
                result.ConfidenceTier.ToString().ToLowerInvariant(),
                result.ConfidenceScore,
                "outflow",
                row.FinancialAccountId,
                row.LinkedBankAccountId,
                row.AccountDisplayName,
                FinancialCommitmentContractPolicy.ResolveLabel(row.Description, null, "Recurring payment"),
                FinancialCommitmentContractPolicy.NormalizeCadence(result.Cadence?.ToString()),
                earliestObserved,
                null,
                FinancialCommitmentContractPolicy.EnsureUtc(row.BookedAtUtc),
                Math.Abs(row.Amount),
                currency,
                nextDateUtc,
                nextDateUtc.HasValue ? "estimated" : "unknown",
                typicalAmount,
                currency,
                isVariableAmount ? "variable" : "estimated",
                isVariableAmount,
                FinancialCommitmentContractPolicy.EnsureUtc(row.MetadataUpdatedUtc ?? row.CreatedUtc),
                nextDateUtc switch
                {
                    null => "unknown",
                    var date when date < utcNow => "stale",
                    _ => "fresh"
                },
                false,
                null,
                exclusions,
                evidence));
        }

        return inferred;
    }

    private static List<string> BuildExclusions(
        RecurringPatternResult result,
        DateTime? nextDateUtc,
        DateTime utcNow)
    {
        var exclusions = new List<string>
        {
            "requires_user_confirmation",
            "inferred_from_transaction_pattern"
        };
        if (!nextDateUtc.HasValue)
        {
            exclusions.Add("next_date_unavailable");
        }
        else if (nextDateUtc.Value < utcNow)
        {
            exclusions.Add("estimated_next_date_elapsed");
        }

        if (result.HasSkippedCycle)
        {
            exclusions.Add("skipped_cycle_detected");
        }

        if (result.HasCadenceDrift)
        {
            exclusions.Add("cadence_drift_detected");
        }

        return exclusions;
    }

    private static DateTime? EstimateNextDate(DateTime lastObservedUtc, RecurringCadence? cadence)
    {
        var normalized = FinancialCommitmentContractPolicy.EnsureUtc(lastObservedUtc);
        return cadence switch
        {
            RecurringCadence.Weekly => normalized.AddDays(7),
            RecurringCadence.BiWeekly => normalized.AddDays(14),
            RecurringCadence.Monthly => normalized.AddMonths(1),
            RecurringCadence.Quarterly => normalized.AddMonths(3),
            RecurringCadence.Yearly => normalized.AddYears(1),
            _ => null
        };
    }

    private static Transaction ToTransaction(InferredCommitmentTransactionRow row)
    {
        return new Transaction
        {
            Id = row.Id,
            FinancialAccountId = row.FinancialAccountId,
            Amount = row.Amount,
            Currency = row.Currency,
            Description = row.Description,
            BookedAtUtc = FinancialCommitmentContractPolicy.EnsureUtc(row.BookedAtUtc),
            CreatedUtc = FinancialCommitmentContractPolicy.EnsureUtc(row.CreatedUtc),
            MetadataUpdatedUtc = FinancialCommitmentContractPolicy.NormalizeUtc(row.MetadataUpdatedUtc)
        };
    }

    private static string BuildId(Guid accountId, string currency, string signature)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}|{currency}|{signature}"));
        return $"inferred_recurring:{Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant()}";
    }
}
