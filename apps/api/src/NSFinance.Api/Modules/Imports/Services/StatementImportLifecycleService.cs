using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Infrastructure.RequestContext;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

public sealed class StatementImportLifecycleService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider,
    IRequestContextAccessor requestContext)
{
    public async Task<ServiceResult<StatementImportLifecycleMutationDto>> CommitAsync(
        Guid batchId,
        StatementImportRevisionCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await LoadOwnedBatchAsync(batchId, cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }

        if (command.ExpectedRevision is not { } expectedRevision)
        {
            return RevisionRequired();
        }

        if (batch.Status == StatementImportBatchStatuses.Committed)
        {
            return ReplayCommitted(batch);
        }

        var revisionError = ValidateRevision(batch, expectedRevision);
        if (revisionError is not null)
        {
            return revisionError;
        }

        if (batch.Status is not (
            StatementImportBatchStatuses.ReadyForReview
            or StatementImportBatchStatuses.Undone))
        {
            return Conflict(
                "This statement import batch cannot be committed.",
                "statement_import_batch_not_committable");
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (batch.Status == StatementImportBatchStatuses.ReadyForReview
            && batch.ExpiresUtc.HasValue
            && batch.ExpiresUtc.Value <= utcNow)
        {
            return await ExpireDuringCommitAsync(batch, utcNow, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(batch.SourceFingerprint))
        {
            return InvalidState();
        }

        var sourceAlreadyCommitted = await dbContext.ImportJobs
            .AsNoTracking()
            .AnyAsync(
                item => item.Id != batch.Id
                    && item.UserId == batch.UserId
                    && item.FinancialAccountId == batch.FinancialAccountId
                    && item.Kind == ImportJobKinds.StatementCsv
                    && item.SourceFingerprint == batch.SourceFingerprint
                    && item.Status == StatementImportBatchStatuses.Committed,
                cancellationToken);
        if (sourceAlreadyCommitted)
        {
            return SourceAlreadyCommitted();
        }

        if (!TryResolveTimeZone(batch.TimeZoneId, out var timeZone))
        {
            return Conflict(
                "The statement time zone is no longer available.",
                "statement_import_time_zone_unavailable");
        }

        var planError = StatementImportLifecyclePolicy.TryBuildCommitPlan(
            batch,
            timeZone!,
            utcNow,
            out var plan);
        if (planError is not null)
        {
            return Fail(planError);
        }

        foreach (var planned in plan!.Transactions)
        {
            planned.Row.CommittedTransactionId = planned.Transaction.Id;
            planned.Row.CommittedTransaction = planned.Transaction;
            planned.Row.UpdatedUtc = utcNow;
        }

        dbContext.Transactions.AddRange(
            plan.Transactions.Select(item => item.Transaction));
        batch.Status = StatementImportBatchStatuses.Committed;
        batch.CommittedRowCount = plan.Transactions.Count;
        batch.CommittedUtc = utcNow;
        batch.UndoneUtc = null;
        batch.ExpiresUtc = null;
        batch.UpdatedUtc = utcNow;
        batch.Revision++;
        AddAuditEvent(
            batch,
            "statement_import_committed",
            new { CommittedRowCount = plan.Transactions.Count });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await RecoverCommitAsync(batchId, cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: StatementImportIndexNames.ImportJobCommittedSource
            })
        {
            dbContext.ChangeTracker.Clear();
            var replay = await RecoverCommitAsync(batchId, cancellationToken);
            return replay.Succeeded ? replay : SourceAlreadyCommitted();
        }

        return ServiceResult<StatementImportLifecycleMutationDto>.Ok(
            ToDto(batch, wasReplay: false));
    }

    public async Task<ServiceResult<StatementImportLifecycleMutationDto>> DiscardAsync(
        Guid batchId,
        StatementImportRevisionCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await LoadOwnedBatchAsync(batchId, cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }

        if (command.ExpectedRevision is not { } expectedRevision)
        {
            return RevisionRequired();
        }

        if (batch.Status == StatementImportBatchStatuses.Discarded)
        {
            var replayError = StatementImportLifecyclePolicy.ValidateDiscardedState(batch);
            return replayError is null
                ? ServiceResult<StatementImportLifecycleMutationDto>.Ok(ToDto(batch, wasReplay: true))
                : Fail(replayError);
        }

        var revisionError = ValidateRevision(batch, expectedRevision);
        if (revisionError is not null)
        {
            return revisionError;
        }

        if (batch.Status is not (
            StatementImportBatchStatuses.ReadyForReview
            or StatementImportBatchStatuses.Expired
            or StatementImportBatchStatuses.Failed
            or StatementImportBatchStatuses.Undone))
        {
            return Conflict(
                "This statement import batch cannot be discarded.",
                "statement_import_batch_not_discardable");
        }

        var stateError = StatementImportLifecyclePolicy.ValidateUnlinkedState(batch);
        if (stateError is not null)
        {
            return Fail(stateError);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in batch.Rows.Where(row =>
                     row.SourceEvidenceJson is not null || row.EvidenceExpiresUtc.HasValue))
        {
            row.SourceEvidenceJson = null;
            row.EvidenceExpiresUtc = null;
            row.UpdatedUtc = utcNow;
        }

        batch.Status = StatementImportBatchStatuses.Discarded;
        batch.ExpiresUtc = null;
        batch.UpdatedUtc = utcNow;
        batch.Revision++;
        AddAuditEvent(batch, "statement_import_discarded", new { });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await RecoverTerminalStateAsync(
                batchId,
                StatementImportBatchStatuses.Discarded,
                cancellationToken);
        }

        return ServiceResult<StatementImportLifecycleMutationDto>.Ok(
            ToDto(batch, wasReplay: false));
    }

    public async Task<ServiceResult<StatementImportLifecycleMutationDto>> UndoAsync(
        Guid batchId,
        StatementImportRevisionCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await LoadOwnedBatchAsync(batchId, cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }

        if (command.ExpectedRevision is not { } expectedRevision)
        {
            return RevisionRequired();
        }

        if (batch.Status == StatementImportBatchStatuses.Undone)
        {
            var replayError = StatementImportLifecyclePolicy.ValidateUndoneState(batch);
            return replayError is null
                ? ServiceResult<StatementImportLifecycleMutationDto>.Ok(ToDto(batch, wasReplay: true))
                : Fail(replayError);
        }

        var revisionError = ValidateRevision(batch, expectedRevision);
        if (revisionError is not null)
        {
            return revisionError;
        }

        if (batch.Status != StatementImportBatchStatuses.Committed)
        {
            return Conflict(
                "Only a committed statement import can be undone.",
                "statement_import_batch_not_undoable");
        }

        if (!TryResolveTimeZone(batch.TimeZoneId, out var timeZone))
        {
            return Conflict(
                "The statement time zone is no longer available.",
                "statement_import_time_zone_unavailable");
        }

        var stateError = StatementImportLifecyclePolicy.ValidateCommittedState(
            batch,
            timeZone!,
            out var transactions);
        if (stateError is not null)
        {
            return Fail(stateError);
        }

        var changedError = StatementImportLifecyclePolicy.ValidateTransactionsUnmodifiedForUndo(
            transactions);
        if (changedError is not null)
        {
            return Fail(changedError);
        }

        if (await HasUndoDependenciesAsync(batch, transactions, cancellationToken))
        {
            return Conflict(
                "Imported transactions are referenced by later financial data.",
                "statement_import_undo_has_downstream_references");
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in batch.Rows.Where(row => row.CommittedTransactionId.HasValue))
        {
            row.CommittedTransactionId = null;
            row.CommittedTransaction = null;
            row.UpdatedUtc = utcNow;
        }

        dbContext.Transactions.RemoveRange(transactions);
        batch.Status = StatementImportBatchStatuses.Undone;
        batch.CommittedRowCount = 0;
        batch.UndoneUtc = utcNow;
        batch.ExpiresUtc = null;
        batch.UpdatedUtc = utcNow;
        batch.Revision++;
        AddAuditEvent(
            batch,
            "statement_import_undone",
            new { RemovedTransactionCount = transactions.Count });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await RecoverTerminalStateAsync(
                batchId,
                StatementImportBatchStatuses.Undone,
                cancellationToken);
        }

        return ServiceResult<StatementImportLifecycleMutationDto>.Ok(
            ToDto(batch, wasReplay: false));
    }

    private async Task<ServiceResult<StatementImportLifecycleMutationDto>> ExpireDuringCommitAsync(
        ImportJob batch,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        batch.Status = StatementImportBatchStatuses.Expired;
        batch.UpdatedUtc = utcNow;
        batch.Revision++;
        AddAuditEvent(
            batch,
            "statement_import_expired",
            new { Reason = "commit_after_review_window" });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await RecoverCommitAsync(batch.Id, cancellationToken);
        }

        return Conflict(
            "This statement import batch has expired.",
            "statement_import_batch_expired");
    }

    private async Task<ServiceResult<StatementImportLifecycleMutationDto>> RecoverCommitAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var reloaded = await LoadOwnedBatchAsync(batchId, cancellationToken);
        return reloaded?.Status == StatementImportBatchStatuses.Committed
            ? ReplayCommitted(reloaded)
            : RevisionConflict();
    }

    private async Task<ServiceResult<StatementImportLifecycleMutationDto>> RecoverTerminalStateAsync(
        Guid batchId,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        var reloaded = await LoadOwnedBatchAsync(batchId, cancellationToken);
        if (reloaded?.Status != targetStatus)
        {
            return RevisionConflict();
        }

        var stateError = targetStatus switch
        {
            StatementImportBatchStatuses.Discarded =>
                StatementImportLifecyclePolicy.ValidateDiscardedState(reloaded),
            StatementImportBatchStatuses.Undone =>
                StatementImportLifecyclePolicy.ValidateUndoneState(reloaded),
            _ => StatementImportLifecyclePolicy.ValidateUnlinkedState(reloaded)
        };
        return stateError is null
            ? ServiceResult<StatementImportLifecycleMutationDto>.Ok(ToDto(reloaded, wasReplay: true))
            : Fail(stateError);
    }

    private ServiceResult<StatementImportLifecycleMutationDto> ReplayCommitted(ImportJob batch)
    {
        if (!TryResolveTimeZone(batch.TimeZoneId, out var timeZone))
        {
            return Conflict(
                "The statement time zone is no longer available.",
                "statement_import_time_zone_unavailable");
        }

        var stateError = StatementImportLifecyclePolicy.ValidateCommittedState(
            batch,
            timeZone!,
            out _);
        return stateError is null
            ? ServiceResult<StatementImportLifecycleMutationDto>.Ok(ToDto(batch, wasReplay: true))
            : Fail(stateError);
    }

    private async Task<bool> HasUndoDependenciesAsync(
        ImportJob batch,
        IReadOnlyList<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        var transactionIds = transactions.Select(transaction => transaction.Id).ToList();
        if (await dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(
                transaction => !transactionIds.Contains(transaction.Id)
                    && ((transaction.LinkedTransferTransactionId.HasValue
                            && transactionIds.Contains(
                                transaction.LinkedTransferTransactionId.Value))
                        || (transaction.DeterministicLinkedTransactionId.HasValue
                            && transactionIds.Contains(
                                transaction.DeterministicLinkedTransactionId.Value))),
                cancellationToken))
        {
            return true;
        }

        if (await dbContext.TransactionRelationships
            .AsNoTracking()
            .AnyAsync(
                relationship => transactionIds.Contains(relationship.SourceTransactionId)
                    || (relationship.TargetTransactionId.HasValue
                        && transactionIds.Contains(relationship.TargetTransactionId.Value)),
                cancellationToken))
        {
            return true;
        }

        if (await dbContext.StatementImportRows
            .AsNoTracking()
            .AnyAsync(
                row => row.ImportJobId != batch.Id
                    && row.DuplicateCandidateTransactionId.HasValue
                    && transactionIds.Contains(row.DuplicateCandidateTransactionId.Value)
                    && row.ImportJob != null
                    && (row.ImportJob.Status == StatementImportBatchStatuses.Staging
                        || row.ImportJob.Status == StatementImportBatchStatuses.ReadyForReview
                        || row.ImportJob.Status == StatementImportBatchStatuses.Undone
                        || row.ImportJob.Status == StatementImportBatchStatuses.Committed),
                cancellationToken))
        {
            return true;
        }

        if (await dbContext.RawBankTransactions
            .AsNoTracking()
            .AnyAsync(
                transaction => transaction.ProjectedTransactionId.HasValue
                    && transactionIds.Contains(transaction.ProjectedTransactionId.Value),
                cancellationToken))
        {
            return true;
        }

        return await dbContext.NormalizedBankTransactions
            .AsNoTracking()
            .AnyAsync(
                transaction => transaction.ProjectedTransactionId.HasValue
                    && transactionIds.Contains(transaction.ProjectedTransactionId.Value),
                cancellationToken);
    }

    private Task<ImportJob?> LoadOwnedBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken) =>
        dbContext.ImportJobs
            .Include(item => item.FinancialAccount)
            .Include(item => item.Rows)
                .ThenInclude(row => row.CommittedTransaction)
            .SingleOrDefaultAsync(
                item => item.Id == batchId
                    && item.UserId == currentUserProvider.UserId
                    && item.Kind == ImportJobKinds.StatementCsv,
                cancellationToken);

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

    private static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo? timeZone)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static ServiceResult<StatementImportLifecycleMutationDto>? ValidateRevision(
        ImportJob batch,
        int expectedRevision)
    {
        return expectedRevision == batch.Revision ? null : RevisionConflict();
    }

    private static ServiceResult<StatementImportLifecycleMutationDto> RevisionRequired() =>
        Conflict(
            "Expected revision is required.",
            "statement_import_revision_required");

    private static StatementImportLifecycleMutationDto ToDto(
        ImportJob batch,
        bool wasReplay) =>
        new(
            batch.Id,
            batch.Status,
            batch.Revision,
            batch.IncludedRowCount,
            batch.CommittedRowCount,
            batch.UpdatedUtc,
            batch.CommittedUtc,
            batch.UndoneUtc,
            wasReplay);

    private static ServiceResult<StatementImportLifecycleMutationDto> NotFound() =>
        ServiceResult<StatementImportLifecycleMutationDto>.Fail(
            "Statement import batch not found.",
            "statement_import_batch_not_found",
            StatusCodes.Status404NotFound);

    private static ServiceResult<StatementImportLifecycleMutationDto> RevisionConflict() =>
        Conflict(
            "Statement import changed since it was read.",
            "statement_import_revision_conflict");

    private static ServiceResult<StatementImportLifecycleMutationDto> SourceAlreadyCommitted() =>
        Conflict(
            "This statement source has already been committed to the account.",
            "statement_import_source_already_committed");

    private static ServiceResult<StatementImportLifecycleMutationDto> InvalidState() =>
        Conflict(
            "Statement import state is inconsistent and cannot be changed.",
            "statement_import_state_invalid");

    private static ServiceResult<StatementImportLifecycleMutationDto> Conflict(
        string message,
        string code) =>
        ServiceResult<StatementImportLifecycleMutationDto>.Fail(
            message,
            code,
            StatusCodes.Status409Conflict);

    private static ServiceResult<StatementImportLifecycleMutationDto> Fail(ServiceError error) =>
        ServiceResult<StatementImportLifecycleMutationDto>.Fail(
            error.Message,
            error.Code,
            error.StatusCode);
}
