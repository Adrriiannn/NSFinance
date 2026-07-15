using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

internal sealed record PlannedStatementImportTransaction(
    StatementImportRow Row,
    Transaction Transaction);

internal sealed record StatementImportCommitPlan(
    IReadOnlyList<PlannedStatementImportTransaction> Transactions);

internal static class StatementImportLifecyclePolicy
{
    public const long MaximumRequestBodyBytes = 8 * 1024;

    public static ServiceError? TryBuildCommitPlan(
        ImportJob batch,
        TimeZoneInfo timeZone,
        DateTime utcNow,
        out StatementImportCommitPlan? plan)
    {
        plan = null;
        var stateError = ValidateBatchCounts(batch);
        if (stateError is not null)
        {
            return stateError;
        }

        var accountError = ValidateOwnedManualAccount(batch, out var account);
        if (accountError is not null)
        {
            return accountError;
        }

        if (batch.CommittedRowCount != 0
            || batch.Rows.Any(row => row.CommittedTransactionId.HasValue))
        {
            return StateInvalid();
        }

        if (batch.Rows.Any(row =>
                row.ReviewDisposition == StatementImportReviewDispositions.Pending))
        {
            return Conflict(
                "Every pending duplicate decision must be resolved before import.",
                "statement_import_review_incomplete");
        }

        var includedRows = batch.Rows
            .Where(row => row.ReviewDisposition == StatementImportReviewDispositions.Included)
            .OrderBy(row => row.RowNumber)
            .ToList();
        if (includedRows.Count == 0)
        {
            return Conflict(
                "At least one statement row must be included before import.",
                "statement_import_no_rows_included");
        }

        var transactions = new List<PlannedStatementImportTransaction>(includedRows.Count);
        foreach (var row in includedRows)
        {
            if (row.ValidationStatus != StatementImportValidationStatuses.Valid
                || row.DuplicateClassification == StatementImportDuplicateClassifications.Exact
                || string.IsNullOrWhiteSpace(row.Description)
                || !row.Amount.HasValue
                || string.IsNullOrWhiteSpace(row.Currency)
                || !string.Equals(row.Currency, batch.AccountCurrency, StringComparison.Ordinal))
            {
                return StateInvalid();
            }

            if (row.Amount.Value == 0m)
            {
                return Conflict(
                    "Zero-value statement rows cannot be imported.",
                    "statement_import_zero_amount");
            }

            if (!StatementImportTimePolicy.TryResolveBookedAtUtc(
                    row,
                    timeZone,
                    out var bookedAtUtc))
            {
                return Conflict(
                    "A statement row date cannot be represented safely.",
                    "statement_import_row_date_unrepresentable");
            }

            transactions.Add(new PlannedStatementImportTransaction(
                row,
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    FinancialAccountId = account!.Id,
                    Amount = row.Amount.Value,
                    Currency = row.Currency,
                    Description = row.Description.Trim(),
                    EntryKind = TransactionEntryKinds.StatementImport,
                    AnalyticsTreatment = TransactionAnalyticsTreatments.Ordinary,
                    BookedAtUtc = bookedAtUtc,
                    DeterministicClassificationStatus = DeterministicClassificationStatus.NotEvaluated,
                    DeterministicClassificationTerminal = false,
                    DeterministicDeferredRetryEligible = false,
                    NeedsDeterministicReclassification = false,
                    CreatedUtc = utcNow
                }));
        }

        plan = new StatementImportCommitPlan(transactions);
        return null;
    }

    public static ServiceError? ValidateCommittedState(
        ImportJob batch,
        TimeZoneInfo timeZone,
        out IReadOnlyList<Transaction> committedTransactions)
    {
        committedTransactions = [];
        var stateError = ValidateBatchCounts(batch);
        if (stateError is not null)
        {
            return stateError;
        }

        var accountError = ValidateOwnedManualAccount(batch, out _);
        if (accountError is not null)
        {
            return accountError;
        }

        var includedRows = batch.Rows
            .Where(row => row.ReviewDisposition == StatementImportReviewDispositions.Included)
            .ToList();
        if (batch.Status != StatementImportBatchStatuses.Committed
            || batch.CommittedRowCount != includedRows.Count
            || batch.CommittedRowCount <= 0
            || string.IsNullOrWhiteSpace(batch.SourceFingerprint)
            || string.IsNullOrWhiteSpace(batch.MappingFingerprint)
            || string.IsNullOrWhiteSpace(batch.ParserVersion)
            || string.IsNullOrWhiteSpace(batch.MappingVersion)
            || !batch.CommittedUtc.HasValue
            || batch.UndoneUtc.HasValue
            || batch.ExpiresUtc.HasValue)
        {
            return StateInvalid();
        }

        var transactions = new List<Transaction>(includedRows.Count);
        foreach (var row in batch.Rows)
        {
            if (row.ImportJobId != batch.Id
                || string.IsNullOrWhiteSpace(row.RowFingerprint))
            {
                return StateInvalid();
            }

            if (row.ReviewDisposition != StatementImportReviewDispositions.Included)
            {
                if (row.CommittedTransactionId.HasValue
                    || row.CommittedTransaction is not null)
                {
                    return StateInvalid();
                }

                continue;
            }

            var transaction = row.CommittedTransaction;
            if (!StatementImportTimePolicy.TryResolveBookedAtUtc(
                    row,
                    timeZone,
                    out var expectedBookedAtUtc))
            {
                return StateInvalid();
            }

            if (!row.CommittedTransactionId.HasValue
                || transaction is null
                || row.CommittedTransactionId.Value != transaction.Id
                || transaction.FinancialAccountId != batch.FinancialAccountId
                || transaction.EntryKind != TransactionEntryKinds.StatementImport
                || transaction.AnalyticsTreatment != TransactionAnalyticsTreatments.Ordinary
                || transaction.Amount != row.Amount
                || !string.Equals(transaction.Currency, row.Currency, StringComparison.Ordinal)
                || !string.Equals(transaction.Description, row.Description, StringComparison.Ordinal)
                || transaction.BookedAtUtc != expectedBookedAtUtc
                || transaction.CreatedUtc != batch.CommittedUtc.Value)
            {
                return StateInvalid();
            }

            transactions.Add(transaction);
        }

        if (transactions.Select(transaction => transaction.Id).Distinct().Count()
            != transactions.Count)
        {
            return StateInvalid();
        }

        committedTransactions = transactions;
        return null;
    }

    public static ServiceError? ValidateTransactionsUnmodifiedForUndo(
        IReadOnlyList<Transaction> transactions) =>
        transactions.All(transaction =>
            !transaction.CategoryId.HasValue
            && !transaction.TaxonomyDomainId.HasValue
            && !transaction.TaxonomyCategoryId.HasValue
            && !transaction.TaxonomySubcategoryId.HasValue
            && transaction.Reason is null
            && transaction.Notes is null
            && !transaction.TransferKind.HasValue
            && !transaction.LinkedTransferTransactionId.HasValue
            && !transaction.LinkedTransferMatchedUtc.HasValue
            && !transaction.TransferMatchConfidenceScore.HasValue
            && transaction.TransferMatchConfidenceTier is null
            && transaction.TransferMatchReason is null
            && !transaction.DeterministicEnrichmentVersion.HasValue
            && !transaction.LastDeterministicEnrichedUtc.HasValue
            && transaction.DeterministicClassificationStatus
                == DeterministicClassificationStatus.NotEvaluated
            && !transaction.DeterministicClassificationVersion.HasValue
            && transaction.DeterministicClassificationRuleKey is null
            && !transaction.DeterministicClassificationCategoryId.HasValue
            && !transaction.DeterministicClassificationSubcategoryId.HasValue
            && !transaction.DeterministicLinkedTransactionId.HasValue
            && transaction.DeterministicRelationshipType is null
            && !transaction.DeterministicRelationshipGroupId.HasValue
            && !transaction.DeterministicMatchScore.HasValue
            && transaction.DeterministicReasonCode is null
            && transaction.DeterministicReasonDetailJson is null
            && !transaction.DeterministicClassificationEvaluatedUtc.HasValue
            && !transaction.DeterministicClassificationTerminal
            && !transaction.DeterministicDeferredRetryEligible
            && !transaction.DeterministicLastRetryConsideredUtc.HasValue
            && !transaction.NeedsDeterministicReclassification
            && transaction.DeterministicSourceSignature is null
            && !transaction.MetadataUpdatedUtc.HasValue)
            ? null
            : Conflict(
                "Imported transactions changed after the batch was committed.",
                "statement_import_undo_transactions_changed");

    public static ServiceError? ValidateUnlinkedState(ImportJob batch)
    {
        return batch.CommittedRowCount == 0
            && batch.Rows.All(row =>
                !row.CommittedTransactionId.HasValue
                && row.CommittedTransaction is null)
            ? null
            : StateInvalid();
    }

    public static ServiceError? ValidateDiscardedState(ImportJob batch)
    {
        var unlinkedError = ValidateUnlinkedState(batch);
        return unlinkedError is null
            && batch.Status == StatementImportBatchStatuses.Discarded
            && !batch.ExpiresUtc.HasValue
            && batch.Rows.All(row =>
                row.SourceEvidenceJson is null
                && !row.EvidenceExpiresUtc.HasValue)
            ? null
            : StateInvalid();
    }

    public static ServiceError? ValidateUndoneState(ImportJob batch)
    {
        var unlinkedError = ValidateUnlinkedState(batch);
        return unlinkedError is null
            && batch.Status == StatementImportBatchStatuses.Undone
            && batch.UndoneUtc.HasValue
            && !batch.ExpiresUtc.HasValue
            ? null
            : StateInvalid();
    }

    private static ServiceError? ValidateBatchCounts(ImportJob batch)
    {
        var rows = batch.Rows;
        var valid = rows.Count(row =>
            row.ValidationStatus == StatementImportValidationStatuses.Valid);
        var invalid = rows.Count(row =>
            row.ValidationStatus == StatementImportValidationStatuses.Invalid);
        var exact = rows.Count(row =>
            row.DuplicateClassification == StatementImportDuplicateClassifications.Exact);
        var likely = rows.Count(row =>
            row.DuplicateClassification == StatementImportDuplicateClassifications.Likely);
        var notDuplicate = rows.Count(row =>
            row.DuplicateClassification == StatementImportDuplicateClassifications.None);
        var included = rows.Count(row =>
            row.ReviewDisposition == StatementImportReviewDispositions.Included);
        var excluded = rows.Count(row =>
            row.ReviewDisposition == StatementImportReviewDispositions.Excluded);
        var pending = rows.Count(row =>
            row.ReviewDisposition == StatementImportReviewDispositions.Pending);
        return rows.Count == batch.TotalRowCount
            && valid == batch.ValidRowCount
            && invalid == batch.InvalidRowCount
            && exact == batch.ExactDuplicateRowCount
            && likely == batch.LikelyDuplicateRowCount
            && included == batch.IncludedRowCount
            && valid + invalid == rows.Count
            && notDuplicate + exact + likely == rows.Count
            && included + excluded + pending == rows.Count
            && rows.All(HasValidReviewState)
            ? null
            : StateInvalid();
    }

    private static bool HasValidReviewState(StatementImportRow row) =>
        (
            row.ValidationStatus,
            row.DuplicateClassification,
            row.ReviewDisposition,
            string.IsNullOrWhiteSpace(row.ValidationCode),
            row.DuplicateCandidateTransactionId.HasValue) switch
        {
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.None,
                StatementImportReviewDispositions.Included or StatementImportReviewDispositions.Excluded,
                true,
                false) => true,
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.Exact,
                StatementImportReviewDispositions.Excluded,
                true,
                true) => true,
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.Likely,
                StatementImportReviewDispositions.Pending
                    or StatementImportReviewDispositions.Included
                    or StatementImportReviewDispositions.Excluded,
                true,
                true) => true,
            (StatementImportValidationStatuses.Invalid,
                StatementImportDuplicateClassifications.None,
                StatementImportReviewDispositions.Excluded,
                false,
                false) => true,
            _ => false
        };

    private static ServiceError? ValidateOwnedManualAccount(
        ImportJob batch,
        out FinancialAccount? account)
    {
        account = batch.FinancialAccount;
        if (account is null
            || batch.FinancialAccountId != account.Id
            || batch.UserId != account.UserId
            || account.Source != FinancialAccountSources.Manual)
        {
            return Conflict(
                "The destination account is not an owned manual account.",
                "statement_import_account_not_manual");
        }

        if (!string.Equals(batch.AccountCurrency, account.Currency, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(batch.AccountCurrency))
        {
            return Conflict(
                "The destination account currency changed after preview.",
                "statement_import_account_currency_changed");
        }

        return null;
    }

    private static ServiceError StateInvalid() =>
        Conflict(
            "Statement import state is inconsistent and cannot be changed.",
            "statement_import_state_invalid");

    private static ServiceError Conflict(string message, string code) =>
        new(message, code, StatusCodes.Status409Conflict);
}
