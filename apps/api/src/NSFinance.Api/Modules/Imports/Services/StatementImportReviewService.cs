using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

public sealed class StatementImportReviewService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider,
    IRequestContextAccessor requestContext)
{
    public async Task<ServiceResult<StatementImportReviewMutationDto>> ReviewRowsAsync(
        Guid batchId,
        ReviewStatementImportRowsCommand command,
        CancellationToken cancellationToken)
    {
        var commandError = StatementImportReviewPolicy.TryNormalize(command, out var decisions);
        if (commandError is not null)
        {
            return Fail(commandError);
        }

        var batch = await dbContext.ImportJobs
            .SingleOrDefaultAsync(
                item => item.Id == batchId
                    && item.UserId == currentUserProvider.UserId
                    && item.Kind == ImportJobKinds.StatementCsv,
                cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }

        if (!command.ExpectedRevision.HasValue)
        {
            return ServiceResult<StatementImportReviewMutationDto>.Fail(
                "Expected revision is required.",
                "statement_import_revision_required",
                StatusCodes.Status409Conflict);
        }

        if (command.ExpectedRevision.Value != batch.Revision)
        {
            return RevisionConflict();
        }

        if (batch.Status is not (
            StatementImportBatchStatuses.ReadyForReview
            or StatementImportBatchStatuses.Undone))
        {
            return ServiceResult<StatementImportReviewMutationDto>.Fail(
                "This statement import batch is not open for review.",
                "statement_import_batch_not_reviewable",
                StatusCodes.Status409Conflict);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (batch.Status == StatementImportBatchStatuses.ReadyForReview
            && batch.ExpiresUtc.HasValue
            && batch.ExpiresUtc.Value <= utcNow)
        {
            return await ExpireAsync(batch, utcNow, cancellationToken);
        }

        var rowIds = decisions.Select(decision => decision.RowId).ToList();
        var rows = await dbContext.StatementImportRows
            .Where(row => row.ImportJobId == batch.Id && rowIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count != rowIds.Count)
        {
            return ServiceResult<StatementImportReviewMutationDto>.Fail(
                "Statement import row not found.",
                "statement_import_row_not_found",
                StatusCodes.Status404NotFound);
        }

        var rowsById = rows.ToDictionary(row => row.Id);
        foreach (var decision in decisions)
        {
            var rowError = StatementImportReviewPolicy.ValidateRowDecision(
                rowsById[decision.RowId],
                decision.ReviewDisposition);
            if (rowError is not null)
            {
                return Fail(rowError);
            }
        }

        var changes = new List<(StatementImportRow Row, string Disposition)>();
        var includedDelta = 0;
        var pendingDelta = 0;
        foreach (var decision in decisions)
        {
            var row = rowsById[decision.RowId];
            if (row.ReviewDisposition == decision.ReviewDisposition)
            {
                continue;
            }

            if (row.ReviewDisposition == StatementImportReviewDispositions.Included)
            {
                includedDelta--;
            }
            if (decision.ReviewDisposition == StatementImportReviewDispositions.Included)
            {
                includedDelta++;
            }
            if (row.ReviewDisposition == StatementImportReviewDispositions.Pending)
            {
                pendingDelta--;
            }
            if (decision.ReviewDisposition == StatementImportReviewDispositions.Pending)
            {
                pendingDelta++;
            }

            changes.Add((row, decision.ReviewDisposition));
        }

        if (changes.Count == 0)
        {
            var currentPending = await CountPendingRowsAsync(batch.Id, cancellationToken);
            var persistedRevision = await dbContext.ImportJobs
                .AsNoTracking()
                .Where(item => item.Id == batch.Id
                    && item.UserId == currentUserProvider.UserId
                    && item.Kind == ImportJobKinds.StatementCsv)
                .Select(item => item.Revision)
                .SingleAsync(cancellationToken);
            if (persistedRevision != batch.Revision)
            {
                return RevisionConflict();
            }

            var currentExcluded = batch.TotalRowCount - batch.IncludedRowCount - currentPending;
            if (!HasConsistentCounts(batch, currentPending, currentExcluded))
            {
                return InvalidReviewState();
            }

            return ServiceResult<StatementImportReviewMutationDto>.Ok(
                BuildDto(batch, rows, currentPending, currentExcluded));
        }

        var includedRowCount = batch.IncludedRowCount + includedDelta;
        var currentPendingRowCount = await CountPendingRowsAsync(batch.Id, cancellationToken);
        var pendingRowCount = currentPendingRowCount + pendingDelta;
        var excludedRowCount = batch.TotalRowCount - includedRowCount - pendingRowCount;
        if (!HasConsistentCounts(
                batch,
                pendingRowCount,
                excludedRowCount,
                includedRowCount))
        {
            return InvalidReviewState();
        }

        foreach (var change in changes)
        {
            change.Row.ReviewDisposition = change.Disposition;
            change.Row.UpdatedUtc = utcNow;
        }

        batch.IncludedRowCount = includedRowCount;
        batch.Revision++;
        batch.UpdatedUtc = utcNow;
        AddAuditEvent(
            batch,
            "statement_import_rows_reviewed",
            new
            {
                ChangedRowCount = changes.Count,
                Included = changes.Count(change =>
                    change.Disposition == StatementImportReviewDispositions.Included),
                Excluded = changes.Count(change =>
                    change.Disposition == StatementImportReviewDispositions.Excluded),
                Pending = changes.Count(change =>
                    change.Disposition == StatementImportReviewDispositions.Pending)
            });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RevisionConflict();
        }

        return ServiceResult<StatementImportReviewMutationDto>.Ok(
            BuildDto(batch, rows, pendingRowCount, excludedRowCount));
    }

    private async Task<ServiceResult<StatementImportReviewMutationDto>> ExpireAsync(
        ImportJob batch,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        batch.Status = StatementImportBatchStatuses.Expired;
        batch.Revision++;
        batch.UpdatedUtc = utcNow;
        AddAuditEvent(
            batch,
            "statement_import_expired",
            new { Reason = "review_window_elapsed" });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return RevisionConflict();
        }

        return ServiceResult<StatementImportReviewMutationDto>.Fail(
            "This statement import batch has expired.",
            "statement_import_batch_expired",
            StatusCodes.Status409Conflict);
    }

    private Task<int> CountPendingRowsAsync(
        Guid batchId,
        CancellationToken cancellationToken) =>
        dbContext.StatementImportRows
            .AsNoTracking()
            .CountAsync(
                row => row.ImportJobId == batchId
                    && row.ReviewDisposition == StatementImportReviewDispositions.Pending,
                cancellationToken);

    private static StatementImportReviewMutationDto BuildDto(
        ImportJob batch,
        IReadOnlyList<StatementImportRow> reviewedRows,
        int pendingRowCount,
        int excludedRowCount)
    {
        return new StatementImportReviewMutationDto(
            batch.Id,
            batch.Status,
            batch.Revision,
            batch.IncludedRowCount,
            pendingRowCount,
            excludedRowCount,
            batch.UpdatedUtc,
            reviewedRows
                .OrderBy(row => row.RowNumber)
                .Select(row => new StatementImportReviewedRowDto(
                    row.Id,
                    row.RowNumber,
                    row.ReviewDisposition,
                    row.UpdatedUtc))
                .ToList());
    }

    private static bool HasConsistentCounts(
        ImportJob batch,
        int pendingRowCount,
        int excludedRowCount,
        int? includedRowCount = null)
    {
        var included = includedRowCount ?? batch.IncludedRowCount;
        return included >= 0
            && included <= batch.ValidRowCount
            && pendingRowCount >= 0
            && excludedRowCount >= 0
            && included + pendingRowCount + excludedRowCount == batch.TotalRowCount;
    }

    private static ServiceResult<StatementImportReviewMutationDto> InvalidReviewState() =>
        ServiceResult<StatementImportReviewMutationDto>.Fail(
            "Statement import review counts are inconsistent.",
            "statement_import_review_state_invalid",
            StatusCodes.Status500InternalServerError);

    private void AddAuditEvent(ImportJob batch, string eventName, object metadata)
    {
        dbContext.AuditEvents.Add(AuditEventFactory.Create(
            requestContext,
            "financial_data",
            eventName,
            "import_job",
            batch.Id.ToString("N"),
            currentUserProvider.UserId,
            "user",
            new
            {
                batch.FinancialAccountId,
                batch.Status,
                batch.Revision,
                Details = metadata
            }));
    }

    private static ServiceResult<StatementImportReviewMutationDto> NotFound() =>
        ServiceResult<StatementImportReviewMutationDto>.Fail(
            "Statement import batch not found.",
            "statement_import_batch_not_found",
            StatusCodes.Status404NotFound);

    private static ServiceResult<StatementImportReviewMutationDto> RevisionConflict() =>
        ServiceResult<StatementImportReviewMutationDto>.Fail(
            "Statement import changed since it was read.",
            "statement_import_revision_conflict",
            StatusCodes.Status409Conflict);

    private static ServiceResult<StatementImportReviewMutationDto> Fail(ServiceError error) =>
        ServiceResult<StatementImportReviewMutationDto>.Fail(
            error.Message,
            error.Code,
            error.StatusCode);
}
