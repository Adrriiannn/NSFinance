using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

public sealed class StatementImportBatchService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider)
{
    public async Task<ServiceResult<StatementImportBatchDto>> StageAsync(
        StageStatementImportBatchCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = StatementImportStagingPolicy.Validate(command);
        if (validationError is not null)
        {
            return ServiceResult<StatementImportBatchDto>.Fail(
                validationError.Message,
                validationError.Code,
                validationError.StatusCode);
        }

        var account = await dbContext.FinancialAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == command.AccountId
                    && item.UserId == currentUserProvider.UserId,
                cancellationToken);
        if (account is null)
        {
            return ServiceResult<StatementImportBatchDto>.Fail(
                "Account not found.",
                "statement_import_account_not_found",
                StatusCodes.Status404NotFound);
        }

        if (account.Source != FinancialAccountSources.Manual)
        {
            return ServiceResult<StatementImportBatchDto>.Fail(
                "Statements can be imported only into a manual account.",
                "statement_import_account_not_manual",
                StatusCodes.Status409Conflict);
        }

        var currencyError = StatementImportStagingPolicy.ValidateAccountCurrency(
            command,
            account.Currency);
        if (currencyError is not null)
        {
            return ServiceResult<StatementImportBatchDto>.Fail(
                currencyError.Message,
                currencyError.Code,
                currencyError.StatusCode);
        }

        var duplicateCandidateIds = command.Rows
            .Where(row => row.DuplicateCandidateTransactionId.HasValue)
            .Select(row => row.DuplicateCandidateTransactionId!.Value)
            .Distinct()
            .ToList();
        if (duplicateCandidateIds.Count > 0)
        {
            var ownedCandidateCount = await dbContext.Transactions
                .AsNoTracking()
                .CountAsync(
                    transaction => transaction.FinancialAccountId == command.AccountId
                        && duplicateCandidateIds.Contains(transaction.Id),
                    cancellationToken);
            if (ownedCandidateCount != duplicateCandidateIds.Count)
            {
                return ServiceResult<StatementImportBatchDto>.Fail(
                    "A duplicate candidate does not belong to the destination account.",
                    "statement_import_duplicate_candidate_invalid",
                    StatusCodes.Status400BadRequest);
            }
        }

        var sourceFingerprint = StatementImportStagingPolicy.NormalizeFingerprint(command.SourceFingerprint);
        var mappingFingerprint = StatementImportStagingPolicy.NormalizeFingerprint(command.MappingFingerprint);
        var parserVersion = StatementImportStagingPolicy.NormalizeVersion(command.ParserVersion);
        var mappingVersion = StatementImportStagingPolicy.NormalizeVersion(command.MappingVersion);
        var existing = await FindExistingAsync(
            command.AccountId,
            sourceFingerprint,
            mappingFingerprint,
            parserVersion,
            mappingVersion,
            cancellationToken);
        if (existing is not null)
        {
            return ServiceResult<StatementImportBatchDto>.Ok(ToDto(existing, wasReplay: true));
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var rows = command.Rows.Select(row => new StatementImportRow
        {
            Id = Guid.NewGuid(),
            RowNumber = row.RowNumber,
            RowFingerprint = StatementImportStagingPolicy.NormalizeFingerprint(row.RowFingerprint),
            SourceReferenceFingerprint = row.SourceReferenceFingerprint is null
                ? null
                : StatementImportStagingPolicy.NormalizeFingerprint(row.SourceReferenceFingerprint),
            ValidationStatus = row.ValidationStatus,
            ValidationCode = row.ValidationCode,
            DuplicateClassification = row.DuplicateClassification,
            ReviewDisposition = row.ReviewDisposition,
            DuplicateCandidateTransactionId = row.DuplicateCandidateTransactionId,
            SourceEvidenceJson = row.SourceEvidenceJson,
            EvidenceExpiresUtc = utcNow.AddHours(24),
            EffectiveDate = row.EffectiveDate,
            EffectiveAtUtc = row.EffectiveAtUtc,
            TimestampPrecision = row.TimestampPrecision,
            Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
            Amount = row.Amount,
            Currency = StatementImportStagingPolicy.NormalizeCurrency(row.Currency),
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        }).ToList();
        var batch = new ImportJob
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            FinancialAccountId = command.AccountId,
            FileName = StatementImportStagingPolicy.NormalizeFileName(command.FileName),
            Kind = ImportJobKinds.StatementCsv,
            Status = StatementImportBatchStatuses.ReadyForReview,
            SourceFingerprint = sourceFingerprint,
            MappingFingerprint = mappingFingerprint,
            ParserVersion = parserVersion,
            MappingVersion = mappingVersion,
            MappingJson = command.MappingJson,
            AccountCurrency = StatementImportStagingPolicy.NormalizeCurrency(account.Currency),
            Locale = StatementImportStagingPolicy.NormalizeLocale(command.Locale),
            TimeZoneId = StatementImportStagingPolicy.NormalizeTimeZone(command.TimeZoneId),
            FileSizeBytes = command.FileSizeBytes,
            TotalRowCount = rows.Count,
            ValidRowCount = rows.Count(
                row => row.ValidationStatus == StatementImportValidationStatuses.Valid),
            InvalidRowCount = rows.Count(
                row => row.ValidationStatus == StatementImportValidationStatuses.Invalid),
            ExactDuplicateRowCount = rows.Count(
                row => row.DuplicateClassification == StatementImportDuplicateClassifications.Exact),
            LikelyDuplicateRowCount = rows.Count(
                row => row.DuplicateClassification == StatementImportDuplicateClassifications.Likely),
            IncludedRowCount = rows.Count(
                row => row.ReviewDisposition == StatementImportReviewDispositions.Included),
            CommittedRowCount = 0,
            Revision = 1,
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow,
            ReadyForReviewUtc = utcNow,
            ExpiresUtc = utcNow.AddDays(30),
            Rows = rows
        };

        dbContext.ImportJobs.Add(batch);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: StatementImportIndexNames.ImportJobIdempotency
            })
        {
            dbContext.ChangeTracker.Clear();
            existing = await FindExistingAsync(
                command.AccountId,
                sourceFingerprint,
                mappingFingerprint,
                parserVersion,
                mappingVersion,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return ServiceResult<StatementImportBatchDto>.Ok(ToDto(existing, wasReplay: true));
        }

        return ServiceResult<StatementImportBatchDto>.Ok(ToDto(batch, wasReplay: false));
    }

    public async Task<ServiceResult<StatementImportBatchDto>> GetAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.ImportJobs
            .AsNoTracking()
            .Include(item => item.Rows)
            .SingleOrDefaultAsync(
                item => item.Id == batchId
                    && item.UserId == currentUserProvider.UserId
                    && item.Kind == ImportJobKinds.StatementCsv,
                cancellationToken);
        return batch is null
            ? ServiceResult<StatementImportBatchDto>.Fail(
                "Statement import batch not found.",
                "statement_import_batch_not_found",
                StatusCodes.Status404NotFound)
            : ServiceResult<StatementImportBatchDto>.Ok(ToDto(batch, wasReplay: false));
    }

    private Task<ImportJob?> FindExistingAsync(
        Guid accountId,
        string sourceFingerprint,
        string mappingFingerprint,
        string parserVersion,
        string mappingVersion,
        CancellationToken cancellationToken) =>
        dbContext.ImportJobs
            .AsNoTracking()
            .Include(item => item.Rows)
            .SingleOrDefaultAsync(
                item => item.UserId == currentUserProvider.UserId
                    && item.FinancialAccountId == accountId
                    && item.Kind == ImportJobKinds.StatementCsv
                    && item.SourceFingerprint == sourceFingerprint
                    && item.MappingFingerprint == mappingFingerprint
                    && item.ParserVersion == parserVersion
                    && item.MappingVersion == mappingVersion,
                cancellationToken);

    private static StatementImportBatchDto ToDto(ImportJob batch, bool wasReplay) =>
        new(
            batch.Id,
            batch.FinancialAccountId!.Value,
            batch.FileName,
            batch.Status,
            batch.AccountCurrency!,
            batch.Locale!,
            batch.TimeZoneId!,
            batch.FileSizeBytes!.Value,
            batch.TotalRowCount,
            batch.ValidRowCount,
            batch.InvalidRowCount,
            batch.ExactDuplicateRowCount,
            batch.LikelyDuplicateRowCount,
            batch.IncludedRowCount,
            batch.CommittedRowCount,
            batch.Revision,
            batch.CreatedUtc,
            batch.UpdatedUtc,
            batch.ReadyForReviewUtc,
            batch.CommittedUtc,
            batch.UndoneUtc,
            batch.ExpiresUtc,
            wasReplay,
            batch.Rows
                .OrderBy(row => row.RowNumber)
                .Select(row => new StatementImportRowDto(
                    row.Id,
                    row.RowNumber,
                    row.ValidationStatus,
                    row.ValidationCode,
                    row.DuplicateClassification,
                    row.ReviewDisposition,
                    row.DuplicateCandidateTransactionId,
                    row.EffectiveDate,
                    row.EffectiveAtUtc,
                    row.TimestampPrecision,
                    row.Description,
                    row.Amount,
                    row.Currency,
                    row.CommittedTransactionId))
                .ToList());
}
