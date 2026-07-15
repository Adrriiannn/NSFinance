using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;
using NSFinance.Api.Modules.Imports.Mapping;
using NSFinance.Api.Modules.Imports.Parsing;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

internal sealed class StatementImportUploadService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IStatementCsvParser parser,
    IStatementImportMappingEngine mappingEngine,
    StatementImportBatchService batchService)
{
    private const string MappingVersion = "statement-mapping-v1";
    private const int InitialPreviewPageSize = 50;

    public async Task<ServiceResult<StatementCsvInspectionDto>> InspectAsync(
        IFormFile? file,
        string? delimiterValue,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateUpload(file, delimiterValue, out var delimiter);
        if (requestError is not null)
        {
            return Fail<StatementCsvInspectionDto>(requestError);
        }

        try
        {
            await using var source = file!.OpenReadStream();
            var parsed = await parser.ParseAsync(
                source,
                new StatementCsvParserOptions(delimiter),
                cancellationToken);

            return ServiceResult<StatementCsvInspectionDto>.Ok(new StatementCsvInspectionDto(
                StatementImportStagingPolicy.NormalizeFileName(file.FileName),
                parsed.ParserVersion,
                parsed.Delimiter,
                parsed.SourceByteCount,
                parsed.DataRowCount,
                parsed.Columns
                    .Select(column => new StatementCsvColumnDto(column.Index, column.Name))
                    .ToList(),
                parsed.SampleRows
                    .Select(row => new StatementCsvSampleRowDto(row.RowNumber, row.Fields.ToArray()))
                    .ToList()));
        }
        catch (StatementCsvParserException exception)
        {
            return ServiceResult<StatementCsvInspectionDto>.Fail(
                exception.Message,
                exception.Code,
                exception.RecommendedStatusCode);
        }
        catch (IOException)
        {
            return ServiceResult<StatementCsvInspectionDto>.Fail(
                "The CSV file could not be read.",
                "statement_csv_read_failed",
                StatusCodes.Status400BadRequest);
        }
    }

    public async Task<ServiceResult<StatementImportPreviewDto>> PreviewAsync(
        IFormFile? file,
        StatementImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateUpload(file, request.Delimiter, out var delimiter);
        if (requestError is not null)
        {
            return Fail<StatementImportPreviewDto>(requestError);
        }

        var accountContext = await dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(account => account.Id == request.AccountId
                && account.UserId == currentUserProvider.UserId)
            .Join(
                dbContext.Users.AsNoTracking(),
                account => account.UserId,
                user => user.Id,
                (account, user) => new
                {
                    account.Source,
                    account.Currency,
                    user.Locale,
                    user.Timezone
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (accountContext is null)
        {
            return ServiceResult<StatementImportPreviewDto>.Fail(
                "Destination account not found.",
                "statement_import_account_not_found",
                StatusCodes.Status404NotFound);
        }

        if (!string.Equals(
                accountContext.Source,
                FinancialAccountSources.Manual,
                StringComparison.Ordinal))
        {
            return ServiceResult<StatementImportPreviewDto>.Fail(
                "Statements can only be imported into a manual account.",
                "statement_import_account_not_manual",
                StatusCodes.Status409Conflict);
        }

        var definition = new StatementImportMappingDefinition(
            request.DateColumn,
            request.DescriptionColumn,
            request.AmountColumn,
            request.DebitColumn,
            request.CreditColumn,
            request.CurrencyColumn,
            request.ReferenceColumn,
            request.DateFormat?.Trim() ?? string.Empty,
            NormalizeToken(request.DateValueKind, StatementImportDateValueKinds.Date),
            NormalizeToken(request.AmountMode, StatementImportAmountModes.Signed),
            NormalizeToken(request.AmountSign, StatementImportAmountSigns.AsIs),
            NormalizeValue(request.Locale, accountContext.Locale, "en-IE"),
            NormalizeValue(request.TimeZoneId, accountContext.Timezone, "UTC"));

        try
        {
            StatementCsvParseResult inspection;
            await using (var inspectionSource = file!.OpenReadStream())
            {
                inspection = await parser.ParseAsync(
                    inspectionSource,
                    new StatementCsvParserOptions(delimiter, SampleRowLimit: 0),
                    cancellationToken);
            }

            var mappingError = mappingEngine.ValidateDefinition(definition, inspection.Columns);
            if (mappingError is not null)
            {
                return Fail<StatementImportPreviewDto>(mappingError);
            }

            var mappedRows = new List<StatementImportMappedRow>(inspection.DataRowCount);
            StatementCsvParseResult parsedRows;
            await using (var previewSource = file.OpenReadStream())
            {
                parsedRows = await parser.ParseRowsAsync(
                    previewSource,
                    new StatementCsvParserOptions(delimiter, SampleRowLimit: 0),
                    (row, _) =>
                    {
                        mappedRows.Add(mappingEngine.MapRow(
                            row,
                            definition,
                            accountContext.Currency));
                        return ValueTask.CompletedTask;
                    },
                    cancellationToken);
            }

            if (!string.Equals(
                    inspection.SourceSha256,
                    parsedRows.SourceSha256,
                    StringComparison.Ordinal)
                || inspection.DataRowCount != parsedRows.DataRowCount)
            {
                return ServiceResult<StatementImportPreviewDto>.Fail(
                    "The CSV file changed while it was being inspected.",
                    "statement_csv_source_changed",
                    StatusCodes.Status409Conflict);
            }

            mappedRows = MarkRepeatedSourceRows(mappedRows);
            var classifiedRows = await ClassifyExistingDuplicatesAsync(
                request.AccountId,
                definition.TimeZoneId,
                mappedRows,
                cancellationToken);
            var mappingJson = mappingEngine.CreateCanonicalMappingJson(definition);
            var command = new StageStatementImportBatchCommand(
                request.AccountId,
                StatementImportStagingPolicy.NormalizeFileName(file.FileName),
                parsedRows.SourceByteCount,
                parsedRows.SourceSha256,
                Sha256(mappingJson),
                parsedRows.ParserVersion,
                MappingVersion,
                mappingJson,
                definition.Locale,
                definition.TimeZoneId,
                classifiedRows.Select(ToStageCommand).ToList());
            var staged = await batchService.StageAsync(command, cancellationToken);
            if (!staged.Succeeded)
            {
                return Fail<StatementImportPreviewDto>(staged.Error!);
            }

            var rows = await batchService.GetRowsAsync(
                staged.Value!.Id,
                new StatementImportRowsQuery(PageSize: InitialPreviewPageSize),
                cancellationToken);
            return rows.Succeeded
                ? ServiceResult<StatementImportPreviewDto>.Ok(
                    new StatementImportPreviewDto(staged.Value, rows.Value!))
                : Fail<StatementImportPreviewDto>(rows.Error!);
        }
        catch (StatementCsvParserException exception)
        {
            return ServiceResult<StatementImportPreviewDto>.Fail(
                exception.Message,
                exception.Code,
                exception.RecommendedStatusCode);
        }
        catch (IOException)
        {
            return ServiceResult<StatementImportPreviewDto>.Fail(
                "The CSV file could not be read.",
                "statement_csv_read_failed",
                StatusCodes.Status400BadRequest);
        }
    }

    private static ServiceError? ValidateUpload(
        IFormFile? file,
        string? delimiterValue,
        out string delimiter)
    {
        delimiter = string.Empty;
        var fileError = StatementImportUploadPolicy.ValidateFile(file);
        if (fileError is not null)
        {
            return fileError;
        }

        StatementImportUploadPolicy.TryNormalizeDelimiter(
            delimiterValue,
            out delimiter,
            out var delimiterError);
        return delimiterError;
    }

    private async Task<List<StatementImportClassifiedRow>> ClassifyExistingDuplicatesAsync(
        Guid accountId,
        string timeZoneId,
        IReadOnlyList<StatementImportMappedRow> rows,
        CancellationToken cancellationToken)
    {
        var validRows = rows
            .Where(row => row.ValidationStatus == StatementImportValidationStatuses.Valid)
            .ToList();
        if (validRows.Count == 0)
        {
            return rows.Select(StatementImportClassifiedRow.None).ToList();
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var bounds = validRows
            .Select(row => GetUtcBounds(row, timeZone))
            .ToList();
        var minimumUtc = bounds.Min(bound => bound.StartUtc);
        var maximumUtc = bounds.Max(bound => bound.EndUtc);
        var candidates = await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.FinancialAccountId == accountId
                && transaction.AnalyticsTreatment == TransactionAnalyticsTreatments.Ordinary
                && transaction.BookedAtUtc >= minimumUtc
                && transaction.BookedAtUtc < maximumUtc)
            .Select(transaction => new DuplicateCandidate(
                transaction.Id,
                transaction.Amount,
                transaction.Currency,
                transaction.Description,
                transaction.BookedAtUtc))
            .OrderBy(transaction => transaction.BookedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(row => Classify(row, candidates, timeZone)).ToList();
    }

    private static StatementImportClassifiedRow Classify(
        StatementImportMappedRow row,
        IReadOnlyList<DuplicateCandidate> candidates,
        TimeZoneInfo timeZone)
    {
        if (row.ValidationStatus != StatementImportValidationStatuses.Valid)
        {
            return StatementImportClassifiedRow.None(row);
        }

        var sameValueCandidates = candidates
            .Where(candidate => candidate.Amount == row.Amount
                && string.Equals(candidate.Currency, row.Currency, StringComparison.OrdinalIgnoreCase)
                && IsSameEffectiveDate(row, candidate.BookedAtUtc, timeZone))
            .ToList();
        var exact = sameValueCandidates.FirstOrDefault(candidate =>
            string.Equals(
                NormalizeDescription(candidate.Description),
                NormalizeDescription(row.Description),
                StringComparison.Ordinal));
        if (exact is not null)
        {
            return new StatementImportClassifiedRow(
                row,
                StatementImportDuplicateClassifications.Exact,
                StatementImportReviewDispositions.Excluded,
                exact.Id);
        }

        var likely = sameValueCandidates.FirstOrDefault();
        return likely is null
            ? StatementImportClassifiedRow.None(row)
            : new StatementImportClassifiedRow(
                row,
                StatementImportDuplicateClassifications.Likely,
                StatementImportReviewDispositions.Pending,
                likely.Id);
    }

    private static List<StatementImportMappedRow> MarkRepeatedSourceRows(
        IReadOnlyList<StatementImportMappedRow> rows)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        return rows.Select(row =>
        {
            if (row.ValidationStatus != StatementImportValidationStatuses.Valid
                || fingerprints.Add(row.RowFingerprint))
            {
                return row;
            }

            return row with
            {
                ValidationStatus = StatementImportValidationStatuses.Invalid,
                ValidationCode = "duplicate_within_source"
            };
        }).ToList();
    }

    private static StageStatementImportRowCommand ToStageCommand(
        StatementImportClassifiedRow classified) =>
        new(
            classified.Row.RowNumber,
            classified.Row.RowFingerprint,
            classified.Row.SourceReferenceFingerprint,
            classified.Row.ValidationStatus,
            classified.Row.ValidationCode,
            classified.DuplicateClassification,
            classified.ReviewDisposition,
            classified.DuplicateCandidateTransactionId,
            classified.Row.SourceEvidenceJson,
            classified.Row.EffectiveDate,
            classified.Row.EffectiveAtUtc,
            classified.Row.TimestampPrecision,
            classified.Row.Description,
            classified.Row.Amount,
            classified.Row.Currency);

    private static bool IsSameEffectiveDate(
        StatementImportMappedRow row,
        DateTime candidateUtc,
        TimeZoneInfo timeZone)
    {
        if (row.EffectiveDate.HasValue)
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(candidateUtc, timeZone))
                == row.EffectiveDate.Value;
        }

        return row.EffectiveAtUtc.HasValue
            && Math.Abs((candidateUtc - row.EffectiveAtUtc.Value).TotalMinutes) <= 1;
    }

    private static UtcBounds GetUtcBounds(
        StatementImportMappedRow row,
        TimeZoneInfo timeZone)
    {
        if (row.EffectiveAtUtc.HasValue)
        {
            return new UtcBounds(
                row.EffectiveAtUtc.Value.AddMinutes(-1),
                row.EffectiveAtUtc.Value.AddMinutes(1).AddTicks(1));
        }

        var date = row.EffectiveDate!.Value;
        var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new UtcBounds(
            TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
    }

    private static string NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    private static string NormalizeToken(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string NormalizeValue(string? value, string? preferred, string fallback) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : !string.IsNullOrWhiteSpace(preferred)
                ? preferred.Trim()
                : fallback;

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ServiceResult<T> Fail<T>(ServiceError error) =>
        ServiceResult<T>.Fail(error.Message, error.Code, error.StatusCode);

    private sealed record DuplicateCandidate(
        Guid Id,
        decimal Amount,
        string Currency,
        string Description,
        DateTime BookedAtUtc);

    private sealed record StatementImportClassifiedRow(
        StatementImportMappedRow Row,
        string DuplicateClassification,
        string ReviewDisposition,
        Guid? DuplicateCandidateTransactionId)
    {
        public static StatementImportClassifiedRow None(StatementImportMappedRow row) =>
            new(
                row,
                StatementImportDuplicateClassifications.None,
                row.ValidationStatus == StatementImportValidationStatuses.Valid
                    ? StatementImportReviewDispositions.Included
                    : StatementImportReviewDispositions.Excluded,
                null);
    }

    private sealed record UtcBounds(DateTime StartUtc, DateTime EndUtc);
}
