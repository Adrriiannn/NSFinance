using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

internal sealed record NormalizedStatementImportReviewDecision(
    Guid RowId,
    string ReviewDisposition);

internal static class StatementImportReviewPolicy
{
    public const int MaximumDecisionsPerRequest = 100;
    public const long MaximumRequestBodyBytes = 64 * 1024;

    public static ServiceError? TryNormalize(
        ReviewStatementImportRowsCommand command,
        out IReadOnlyList<NormalizedStatementImportReviewDecision> normalized)
    {
        normalized = [];
        if (command.Decisions is not { Count: > 0 })
        {
            return new ServiceError(
                "At least one statement row decision is required.",
                "statement_import_review_decisions_required",
                StatusCodes.Status400BadRequest);
        }

        if (command.Decisions.Count > MaximumDecisionsPerRequest)
        {
            return new ServiceError(
                $"No more than {MaximumDecisionsPerRequest} statement rows can be reviewed at once.",
                "statement_import_review_decision_limit_exceeded",
                StatusCodes.Status400BadRequest);
        }

        var rowIds = new HashSet<Guid>();
        var decisions = new List<NormalizedStatementImportReviewDecision>(command.Decisions.Count);
        foreach (var decision in command.Decisions)
        {
            if (decision.RowId == Guid.Empty)
            {
                return new ServiceError(
                    "Statement import row ID is invalid.",
                    "statement_import_review_row_id_invalid",
                    StatusCodes.Status400BadRequest);
            }

            if (!rowIds.Add(decision.RowId))
            {
                return new ServiceError(
                    "A statement import row can be reviewed only once per request.",
                    "statement_import_review_row_repeated",
                    StatusCodes.Status400BadRequest);
            }

            var disposition = NormalizeDisposition(decision.ReviewDisposition);
            if (disposition is not (
                StatementImportReviewDispositions.Included
                or StatementImportReviewDispositions.Excluded
                or StatementImportReviewDispositions.Pending))
            {
                return new ServiceError(
                    "Review disposition must be included, excluded, or pending.",
                    "statement_import_review_disposition_invalid",
                    StatusCodes.Status400BadRequest);
            }

            decisions.Add(new NormalizedStatementImportReviewDecision(
                decision.RowId,
                disposition));
        }

        normalized = decisions;
        return null;
    }

    public static ServiceError? ValidateRowDecision(
        StatementImportRow row,
        string disposition)
    {
        if (row.CommittedTransactionId.HasValue
            || row.ValidationStatus != StatementImportValidationStatuses.Valid
            || row.DuplicateClassification == StatementImportDuplicateClassifications.Exact)
        {
            return new ServiceError(
                "This statement import row cannot be changed during review.",
                "statement_import_row_not_reviewable",
                StatusCodes.Status409Conflict);
        }

        if (disposition == StatementImportReviewDispositions.Pending
            && row.DuplicateClassification != StatementImportDuplicateClassifications.Likely)
        {
            return new ServiceError(
                "Only a likely duplicate can remain pending review.",
                "statement_import_pending_requires_likely_duplicate",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static string? NormalizeDisposition(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
