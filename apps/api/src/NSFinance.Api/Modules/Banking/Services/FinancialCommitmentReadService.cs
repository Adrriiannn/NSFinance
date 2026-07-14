using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Banking.Services.Deterministic;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class FinancialCommitmentReadService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider,
    IRecurringPatternService recurringPatternService,
    TransactionNormalizationService normalizationService)
{
    internal const int DefaultLimit = 100;
    internal const int MaximumLimit = 200;
    private static readonly TimeSpan ProviderFreshnessWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan FutureTimestampTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InferredLookbackWindow = TimeSpan.FromDays(800);
    private const int InferredTransactionLimit = 500;
    private const int InferredSeriesLimit = 64;

    public async Task<ServiceResult<FinancialCommitmentsDto>> ListAsync(
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var limit = requestedLimit ?? DefaultLimit;
        if (limit is < 1 or > MaximumLimit)
        {
            return ServiceResult<FinancialCommitmentsDto>.Fail(
                $"Limit must be between 1 and {MaximumLimit}.",
                "commitment_limit_invalid",
                StatusCodes.Status400BadRequest);
        }

        var queryLimit = limit + 1;
        var userId = currentUserProvider.UserId;
        var directDebits = await BuildDirectDebitQuery(userId, queryLimit)
            .ToListAsync(cancellationToken);
        var standingOrders = await BuildStandingOrderQuery(userId, queryLimit)
            .ToListAsync(cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var inferredRows = await BuildInferredTransactionQuery(userId, utcNow, InferredTransactionLimit)
            .ToListAsync(cancellationToken);
        var inferredCommitments = await BuildInferredCommitmentsAsync(
            inferredRows,
            utcNow,
            cancellationToken);

        var providerCommitments = directDebits
            .Select(row => MapDirectDebit(row, utcNow))
            .Concat(standingOrders.Select(row => MapStandingOrder(row, utcNow)))
            .ToList();
        var items = MergeProviderAndInferred(providerCommitments, inferredCommitments)
            .OrderBy(item => item.NextDateUtc is null)
            .ThenBy(item => item.NextDateUtc)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var isTruncated = items.Count > limit;
        if (isTruncated)
        {
            items = items.Take(limit).ToList();
        }

        return ServiceResult<FinancialCommitmentsDto>.Ok(
            new FinancialCommitmentsDto(utcNow, limit, isTruncated, items));
    }

    internal IQueryable<ProviderDirectDebitCommitmentRow> BuildDirectDebitQuery(Guid userId, int limit)
    {
        return dbContext.BankDirectDebits
            .AsNoTracking()
            .Where(debit => debit.LinkedBankAccount != null
                && debit.LinkedBankAccount.Connection != null
                && debit.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(debit => debit.NextPaymentDateUtc == null)
            .ThenBy(debit => debit.NextPaymentDateUtc)
            .ThenBy(debit => debit.MerchantName ?? debit.Reference)
            .ThenBy(debit => debit.Id)
            .Take(limit)
            .Select(debit => new ProviderDirectDebitCommitmentRow
            {
                Id = debit.Id,
                LinkedBankAccountId = debit.LinkedBankAccountId,
                FinancialAccountId = debit.LinkedBankAccount!.FinancialAccountId,
                AccountDisplayName = debit.LinkedBankAccount.DisplayName,
                Status = debit.Status,
                MandateType = debit.MandateType,
                Reference = debit.Reference,
                MerchantName = debit.MerchantName,
                PreviousPaymentDateUtc = debit.PreviousPaymentDateUtc,
                PreviousPaymentAmount = debit.PreviousPaymentAmount,
                PreviousPaymentCurrency = debit.PreviousPaymentCurrency,
                NextPaymentDateUtc = debit.NextPaymentDateUtc,
                NextPaymentAmount = debit.NextPaymentAmount,
                NextPaymentCurrency = debit.NextPaymentCurrency,
                UpdatedUtc = debit.UpdatedUtc
            });
    }

    internal IQueryable<ProviderStandingOrderCommitmentRow> BuildStandingOrderQuery(Guid userId, int limit)
    {
        return dbContext.BankStandingOrders
            .AsNoTracking()
            .Where(order => order.LinkedBankAccount != null
                && order.LinkedBankAccount.Connection != null
                && order.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(order => order.NextPaymentDateUtc == null)
            .ThenBy(order => order.NextPaymentDateUtc)
            .ThenBy(order => order.PayeeName ?? order.Reference)
            .ThenBy(order => order.Id)
            .Take(limit)
            .Select(order => new ProviderStandingOrderCommitmentRow
            {
                Id = order.Id,
                LinkedBankAccountId = order.LinkedBankAccountId,
                FinancialAccountId = order.LinkedBankAccount!.FinancialAccountId,
                AccountDisplayName = order.LinkedBankAccount.DisplayName,
                Status = order.Status,
                Frequency = order.Frequency,
                Reference = order.Reference,
                PayeeName = order.PayeeName,
                FirstPaymentDateUtc = order.FirstPaymentDateUtc,
                NextPaymentDateUtc = order.NextPaymentDateUtc,
                FinalPaymentDateUtc = order.FinalPaymentDateUtc,
                NextPaymentAmount = order.NextPaymentAmount,
                NextPaymentCurrency = order.NextPaymentCurrency,
                UpdatedUtc = order.UpdatedUtc
            });
    }

    internal IQueryable<InferredCommitmentTransactionRow> BuildInferredTransactionQuery(
        Guid userId,
        DateTime utcNow,
        int limit)
    {
        var fromUtc = utcNow - InferredLookbackWindow;
        var throughUtc = utcNow + FutureTimestampTolerance;

        return dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.FinancialAccount != null
                && transaction.FinancialAccount.UserId == userId
                && transaction.Amount < 0m
                && transaction.BookedAtUtc >= fromUtc
                && transaction.BookedAtUtc <= throughUtc
                && transaction.TransferKind == null
                && transaction.LinkedTransferTransactionId == null
                && transaction.DeterministicLinkedTransactionId == null
                && !dbContext.TransactionRelationships.Any(relationship =>
                    relationship.RelationshipStatus == TransactionRelationshipStatus.Active
                    && relationship.AnalyticsTreatment != null
                    && relationship.AnalyticsTreatment.StartsWith("exclude_income_expense")
                    && (relationship.SourceTransactionId == transaction.Id
                        || relationship.TargetTransactionId == transaction.Id)))
            .OrderByDescending(transaction => transaction.BookedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(limit)
            .Select(transaction => new InferredCommitmentTransactionRow
            {
                Id = transaction.Id,
                FinancialAccountId = transaction.FinancialAccountId,
                LinkedBankAccountId = dbContext.LinkedBankAccounts
                    .Where(account => account.FinancialAccountId == transaction.FinancialAccountId)
                    .OrderBy(account => account.Id)
                    .Select(account => (Guid?)account.Id)
                    .FirstOrDefault(),
                AccountDisplayName = transaction.FinancialAccount!.Name,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Description = transaction.Description,
                BookedAtUtc = transaction.BookedAtUtc,
                CreatedUtc = transaction.CreatedUtc,
                MetadataUpdatedUtc = transaction.MetadataUpdatedUtc
            });
    }

    private async Task<IReadOnlyList<FinancialCommitmentDto>> BuildInferredCommitmentsAsync(
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

            var currency = NormalizeCurrency(row.Currency) ?? string.Empty;
            var seriesKey = $"{row.FinancialAccountId:N}|{currency}|{descriptor.BillingSignatureKey}";
            if (!evaluatedSeries.Add(seriesKey))
            {
                continue;
            }

            var candidate = transactionById[row.Id];
            var result = await recurringPatternService.EvaluateAsync(
                candidate,
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
            var earliestObserved = evidenceRows.Min(item => EnsureUtc(item.BookedAtUtc));
            var nextDateUtc = EstimateNextDate(row.BookedAtUtc, result.Cadence);
            var isVariableAmount = result.AmountStabilityTier is
                RecurringAmountStabilityTier.Shifted or
                RecurringAmountStabilityTier.MajorShift;
            var typicalAmount = Math.Abs(result.HistoricalTypicalAmount ?? row.Amount);
            var exclusions = BuildInferredExclusions(result, nextDateUtc, utcNow);
            var reasonCodes = result.ReasonCodes
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();
            var evidence = evidenceRows
                .Take(8)
                .Select((item, index) => new FinancialCommitmentEvidenceDto(
                    "transaction_pattern",
                    item.Id,
                    EnsureUtc(item.BookedAtUtc),
                    "inferred_signal",
                    index == 0 ? reasonCodes : []))
                .ToList();

            inferred.Add(new FinancialCommitmentDto(
                BuildInferredId(row.FinancialAccountId, currency, descriptor.BillingSignatureKey),
                "inferred_recurring",
                "needs_review",
                "inferred",
                result.ConfidenceTier.ToString().ToLowerInvariant(),
                result.ConfidenceScore,
                "outflow",
                row.FinancialAccountId,
                row.LinkedBankAccountId,
                row.AccountDisplayName,
                ResolveLabel(row.Description, null, "Recurring payment"),
                NormalizeCadence(result.Cadence?.ToString()),
                earliestObserved,
                null,
                EnsureUtc(row.BookedAtUtc),
                Math.Abs(row.Amount),
                currency,
                nextDateUtc,
                nextDateUtc.HasValue ? "estimated" : "unknown",
                typicalAmount,
                currency,
                isVariableAmount ? "variable" : "estimated",
                isVariableAmount,
                EnsureUtc(row.MetadataUpdatedUtc ?? row.CreatedUtc),
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

    private IReadOnlyList<FinancialCommitmentDto> MergeProviderAndInferred(
        IReadOnlyList<FinancialCommitmentDto> providers,
        IReadOnlyList<FinancialCommitmentDto> inferred)
    {
        var effectiveProviders = providers.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var unmatchedInferred = new List<FinancialCommitmentDto>();

        foreach (var inferredItem in inferred.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var inferredDescriptor = new RecurringPatternOptions()
                .BuildDescriptor(normalizationService, inferredItem.Label);
            var match = effectiveProviders.Values
                .Where(provider => ProviderCanAbsorbInference(provider, inferredItem))
                .Select(provider => new
                {
                    Provider = provider,
                    Descriptor = new RecurringPatternOptions()
                        .BuildDescriptor(normalizationService, provider.Label)
                })
                .Where(candidate => IsDescriptorMatch(candidate.Descriptor, inferredDescriptor, candidate.Provider, inferredItem))
                .OrderBy(candidate => DescriptorRank(candidate.Descriptor, inferredDescriptor))
                .ThenBy(candidate => DateDistanceDays(candidate.Provider.NextDateUtc, inferredItem.NextDateUtc))
                .ThenBy(candidate => AmountDistanceRatio(candidate.Provider.NextAmount, inferredItem.NextAmount))
                .ThenBy(candidate => candidate.Provider.Id, StringComparer.Ordinal)
                .Select(candidate => candidate.Provider)
                .FirstOrDefault();

            if (match is null)
            {
                unmatchedInferred.Add(inferredItem);
                continue;
            }

            var mergedEvidence = match.Evidence
                .Concat(inferredItem.Evidence)
                .GroupBy(item => (item.Type, item.SourceRecordId))
                .Select(group => group.First())
                .OrderBy(item => item.Type, StringComparer.Ordinal)
                .ThenByDescending(item => item.ObservedUtc)
                .ThenBy(item => item.SourceRecordId)
                .ToList();
            effectiveProviders[match.Id] = match with { Evidence = mergedEvidence };
        }

        return effectiveProviders.Values
            .Concat(unmatchedInferred)
            .ToList();
    }

    private static bool ProviderCanAbsorbInference(
        FinancialCommitmentDto provider,
        FinancialCommitmentDto inferred)
    {
        if (provider.Source != "provider"
            || provider.Lifecycle is "cancelled" or "expired"
            || provider.AccountId != inferred.AccountId)
        {
            return false;
        }

        return provider.Currency is null
            || inferred.Currency is null
            || string.Equals(provider.Currency, inferred.Currency, StringComparison.Ordinal);
    }

    private static bool IsDescriptorMatch(
        RecurringPatternTextDescriptor provider,
        RecurringPatternTextDescriptor inferred,
        FinancialCommitmentDto providerItem,
        FinancialCommitmentDto inferredItem)
    {
        var exactSignature = !string.IsNullOrWhiteSpace(provider.BillingSignatureKey)
            && string.Equals(
                provider.BillingSignatureKey,
                inferred.BillingSignatureKey,
                StringComparison.OrdinalIgnoreCase);
        if (exactSignature)
        {
            return true;
        }

        var sameFamily = !string.IsNullOrWhiteSpace(provider.MerchantFamilyKey)
            && string.Equals(
                provider.MerchantFamilyKey,
                inferred.MerchantFamilyKey,
                StringComparison.OrdinalIgnoreCase);
        return sameFamily
            && (DateDistanceDays(providerItem.NextDateUtc, inferredItem.NextDateUtc) <= 14d
                || AmountDistanceRatio(providerItem.NextAmount, inferredItem.NextAmount) <= 0.15d);
    }

    private static int DescriptorRank(
        RecurringPatternTextDescriptor provider,
        RecurringPatternTextDescriptor inferred)
    {
        return string.Equals(
            provider.BillingSignatureKey,
            inferred.BillingSignatureKey,
            StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static double DateDistanceDays(DateTime? left, DateTime? right)
    {
        return left.HasValue && right.HasValue
            ? Math.Abs((EnsureUtc(left.Value) - EnsureUtc(right.Value)).TotalDays)
            : double.MaxValue;
    }

    private static double AmountDistanceRatio(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return double.MaxValue;
        }

        var denominator = Math.Max(Math.Abs(left.Value), Math.Abs(right.Value));
        return denominator == 0m
            ? 0d
            : (double)(Math.Abs(Math.Abs(left.Value) - Math.Abs(right.Value)) / denominator);
    }

    private static List<string> BuildInferredExclusions(
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
        var normalized = EnsureUtc(lastObservedUtc);
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
            BookedAtUtc = EnsureUtc(row.BookedAtUtc),
            CreatedUtc = EnsureUtc(row.CreatedUtc),
            MetadataUpdatedUtc = NormalizeUtc(row.MetadataUpdatedUtc)
        };
    }

    private static string BuildInferredId(Guid accountId, string currency, string signature)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}|{currency}|{signature}"));
        return $"inferred_recurring:{Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static FinancialCommitmentDto MapDirectDebit(
        ProviderDirectDebitCommitmentRow row,
        DateTime utcNow)
    {
        var exclusions = BuildCommonExclusions(
            row.FinancialAccountId,
            row.MerchantName,
            row.Reference,
            row.NextPaymentDateUtc,
            row.NextPaymentAmount,
            row.NextPaymentCurrency,
            row.UpdatedUtc,
            utcNow);
        var lifecycle = ResolveLifecycle(row.Status, null, utcNow, exclusions);
        var variableAmount = ResolveDirectDebitVariability(row.MandateType);

        return new FinancialCommitmentDto(
            $"provider_direct_debit:{row.Id:N}",
            "direct_debit",
            lifecycle,
            "provider",
            "confirmed",
            100d,
            "outflow",
            row.FinancialAccountId,
            row.LinkedBankAccountId,
            row.AccountDisplayName,
            ResolveLabel(row.MerchantName, row.Reference, "Direct debit"),
            null,
            null,
            null,
            NormalizeUtc(row.PreviousPaymentDateUtc),
            row.PreviousPaymentAmount,
            NormalizeCurrency(row.PreviousPaymentCurrency),
            NormalizeUtc(row.NextPaymentDateUtc),
            row.NextPaymentDateUtc.HasValue ? "provider_reported" : "unknown",
            row.NextPaymentAmount,
            NormalizeCurrency(row.NextPaymentCurrency),
            ResolveAmountCertainty(row.NextPaymentAmount, variableAmount),
            variableAmount,
            EnsureUtc(row.UpdatedUtc),
            ResolveFreshness(row.UpdatedUtc, utcNow),
            false,
            NormalizeOptional(row.Status),
            exclusions,
            [new FinancialCommitmentEvidenceDto(
                "provider_direct_debit",
                row.Id,
                EnsureUtc(row.UpdatedUtc),
                "provider_fact",
                [])]);
    }

    private static FinancialCommitmentDto MapStandingOrder(
        ProviderStandingOrderCommitmentRow row,
        DateTime utcNow)
    {
        var exclusions = BuildCommonExclusions(
            row.FinancialAccountId,
            row.PayeeName,
            row.Reference,
            row.NextPaymentDateUtc,
            row.NextPaymentAmount,
            row.NextPaymentCurrency,
            row.UpdatedUtc,
            utcNow);
        var lifecycle = ResolveLifecycle(row.Status, row.FinalPaymentDateUtc, utcNow, exclusions);

        return new FinancialCommitmentDto(
            $"provider_standing_order:{row.Id:N}",
            "standing_order",
            lifecycle,
            "provider",
            "confirmed",
            100d,
            "outflow",
            row.FinancialAccountId,
            row.LinkedBankAccountId,
            row.AccountDisplayName,
            ResolveLabel(row.PayeeName, row.Reference, "Standing order"),
            NormalizeCadence(row.Frequency),
            NormalizeUtc(row.FirstPaymentDateUtc),
            NormalizeUtc(row.FinalPaymentDateUtc),
            null,
            null,
            null,
            NormalizeUtc(row.NextPaymentDateUtc),
            row.NextPaymentDateUtc.HasValue ? "provider_reported" : "unknown",
            row.NextPaymentAmount,
            NormalizeCurrency(row.NextPaymentCurrency),
            row.NextPaymentAmount.HasValue ? "provider_reported" : "unknown",
            false,
            EnsureUtc(row.UpdatedUtc),
            ResolveFreshness(row.UpdatedUtc, utcNow),
            false,
            NormalizeOptional(row.Status),
            exclusions,
            [new FinancialCommitmentEvidenceDto(
                "provider_standing_order",
                row.Id,
                EnsureUtc(row.UpdatedUtc),
                "provider_fact",
                [])]);
    }

    private static List<string> BuildCommonExclusions(
        Guid? financialAccountId,
        string? primaryLabel,
        string? fallbackLabel,
        DateTime? nextDateUtc,
        decimal? nextAmount,
        string? nextCurrency,
        DateTime updatedUtc,
        DateTime utcNow)
    {
        var exclusions = new List<string>();
        if (!financialAccountId.HasValue)
        {
            exclusions.Add("financial_account_mapping_unavailable");
        }

        if (string.IsNullOrWhiteSpace(primaryLabel) && string.IsNullOrWhiteSpace(fallbackLabel))
        {
            exclusions.Add("label_unavailable");
        }

        if (!nextDateUtc.HasValue)
        {
            exclusions.Add("next_date_unavailable");
        }
        else if (EnsureUtc(nextDateUtc.Value) < utcNow)
        {
            exclusions.Add("next_date_elapsed");
        }

        if (!nextAmount.HasValue)
        {
            exclusions.Add("next_amount_unavailable");
        }
        else if (string.IsNullOrWhiteSpace(nextCurrency))
        {
            exclusions.Add("next_currency_unavailable");
        }

        var normalizedUpdatedUtc = EnsureUtc(updatedUtc);
        if (normalizedUpdatedUtc > utcNow.Add(FutureTimestampTolerance))
        {
            exclusions.Add("future_source_timestamp");
        }
        else if (utcNow - normalizedUpdatedUtc > ProviderFreshnessWindow)
        {
            exclusions.Add("stale_provider_source");
        }

        return exclusions;
    }

    private static string ResolveLifecycle(
        string? providerStatus,
        DateTime? finalPaymentDateUtc,
        DateTime utcNow,
        ICollection<string> exclusions)
    {
        var status = NormalizeToken(providerStatus);
        var lifecycle = status switch
        {
            "active" or "enabled" or "authorized" or "authorised" => "active",
            "paused" or "suspended" or "on_hold" => "paused",
            "cancelled" or "canceled" or "revoked" or "inactive" or "disabled" => "cancelled",
            "expired" => "expired",
            "failed" or "rejected" or "error" => "needs_review",
            "pending" or "pending_authorization" or "pending_authorisation" => "pending",
            _ => "unknown"
        };

        if (lifecycle == "unknown")
        {
            exclusions.Add(string.IsNullOrWhiteSpace(providerStatus)
                ? "provider_status_unavailable"
                : "provider_status_unrecognized");
        }

        if (finalPaymentDateUtc.HasValue
            && EnsureUtc(finalPaymentDateUtc.Value) < utcNow
            && lifecycle is not "cancelled")
        {
            exclusions.Add("final_date_elapsed");
            lifecycle = "expired";
        }

        return lifecycle;
    }

    private static string ResolveFreshness(DateTime updatedUtc, DateTime utcNow)
    {
        var normalizedUpdatedUtc = EnsureUtc(updatedUtc);
        if (normalizedUpdatedUtc > utcNow.Add(FutureTimestampTolerance))
        {
            return "unknown";
        }

        return utcNow - normalizedUpdatedUtc <= ProviderFreshnessWindow
            ? "fresh"
            : "stale";
    }

    private static bool? ResolveDirectDebitVariability(string? mandateType)
    {
        var normalized = NormalizeToken(mandateType);
        if (normalized.Contains("variable", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalized.Contains("fixed", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    private static string ResolveAmountCertainty(decimal? amount, bool? isVariable)
    {
        if (!amount.HasValue)
        {
            return "unknown";
        }

        return isVariable == true ? "variable" : "provider_reported";
    }

    private static string ResolveLabel(string? primary, string? fallback, string defaultLabel)
    {
        return NormalizeOptional(primary)
            ?? NormalizeOptional(fallback)
            ?? defaultLabel;
    }

    private static string? NormalizeCadence(string? value)
    {
        var normalized = NormalizeToken(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            '_',
            value.Trim()
                .ToLowerInvariant()
                .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? NormalizeCurrency(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime? NormalizeUtc(DateTime? value) => value.HasValue ? EnsureUtc(value.Value) : null;

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

internal sealed class ProviderDirectDebitCommitmentRow
{
    public Guid Id { get; init; }
    public Guid LinkedBankAccountId { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? MandateType { get; init; }
    public string? Reference { get; init; }
    public string? MerchantName { get; init; }
    public DateTime? PreviousPaymentDateUtc { get; init; }
    public decimal? PreviousPaymentAmount { get; init; }
    public string? PreviousPaymentCurrency { get; init; }
    public DateTime? NextPaymentDateUtc { get; init; }
    public decimal? NextPaymentAmount { get; init; }
    public string? NextPaymentCurrency { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

internal sealed class ProviderStandingOrderCommitmentRow
{
    public Guid Id { get; init; }
    public Guid LinkedBankAccountId { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? Frequency { get; init; }
    public string? Reference { get; init; }
    public string? PayeeName { get; init; }
    public DateTime? FirstPaymentDateUtc { get; init; }
    public DateTime? NextPaymentDateUtc { get; init; }
    public DateTime? FinalPaymentDateUtc { get; init; }
    public decimal? NextPaymentAmount { get; init; }
    public string? NextPaymentCurrency { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

internal sealed class InferredCommitmentTransactionRow
{
    public Guid Id { get; init; }
    public Guid FinancialAccountId { get; init; }
    public Guid? LinkedBankAccountId { get; init; }
    public string AccountDisplayName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EUR";
    public string Description { get; init; } = string.Empty;
    public DateTime BookedAtUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? MetadataUpdatedUtc { get; init; }
}
