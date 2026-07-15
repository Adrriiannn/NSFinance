using System.Text;
using System.Text.Json;
using System.Globalization;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Services;

public static class StatementImportStagingPolicy
{
    public const long MaximumFileSizeBytes = 5 * 1024 * 1024;
    public const int MaximumRows = 5_000;
    public const int MaximumMappingJsonBytes = 16 * 1024;
    public const int MaximumSourceEvidenceJsonBytes = 4 * 1024;

    private static readonly HashSet<string> AllowedEvidenceValueProperties = new(StringComparer.Ordinal)
    {
        "date",
        "description",
        "amount",
        "debit",
        "credit",
        "currency",
        "reference"
    };

    public static ServiceError? Validate(StageStatementImportBatchCommand command)
    {
        if (command.AccountId == Guid.Empty)
        {
            return Invalid("Account is required.", "statement_import_account_required");
        }

        var fileName = NormalizeFileName(command.FileName);
        if (fileName.Length is 0 or > 260)
        {
            return Invalid("File name is invalid.", "statement_import_file_name_invalid");
        }

        if (command.FileSizeBytes is <= 0 or > MaximumFileSizeBytes)
        {
            return Invalid(
                $"Statement files must be between 1 byte and {MaximumFileSizeBytes} bytes.",
                "statement_import_file_size_invalid");
        }

        if (!IsSha256Fingerprint(command.SourceFingerprint))
        {
            return Invalid("Source fingerprint is invalid.", "statement_import_source_fingerprint_invalid");
        }

        if (!IsSha256Fingerprint(command.MappingFingerprint))
        {
            return Invalid("Mapping fingerprint is invalid.", "statement_import_mapping_fingerprint_invalid");
        }

        if (!IsVersionToken(command.ParserVersion))
        {
            return Invalid("Parser version is invalid.", "statement_import_parser_version_invalid");
        }

        if (!IsVersionToken(command.MappingVersion))
        {
            return Invalid("Mapping version is invalid.", "statement_import_mapping_version_invalid");
        }

        if (!IsLocale(command.Locale))
        {
            return Invalid("Statement locale is invalid.", "statement_import_locale_invalid");
        }

        if (!IsTimeZone(command.TimeZoneId))
        {
            return Invalid("Statement time zone is invalid.", "statement_import_time_zone_invalid");
        }

        if (!IsBoundedJsonObject(command.MappingJson, MaximumMappingJsonBytes, out var mappingDocument))
        {
            return Invalid("Mapping definition is invalid.", "statement_import_mapping_invalid");
        }
        mappingDocument!.Dispose();

        if (command.Rows is null || command.Rows.Count is 0 or > MaximumRows)
        {
            return Invalid(
                $"A statement import must contain between 1 and {MaximumRows} rows.",
                "statement_import_row_count_invalid");
        }

        var rowNumbers = new HashSet<int>();
        foreach (var row in command.Rows)
        {
            var rowError = ValidateRow(row, rowNumbers);
            if (rowError is not null)
            {
                return rowError;
            }
        }

        return null;
    }

    public static string NormalizeFileName(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? normalized : normalized[(separatorIndex + 1)..];
    }

    public static string NormalizeFingerprint(string value) => value.Trim().ToLowerInvariant();

    public static string NormalizeVersion(string value) => value.Trim();

    public static string NormalizeLocale(string value) => CultureInfo.GetCultureInfo(value.Trim()).Name;

    public static string NormalizeTimeZone(string value) => value.Trim();

    public static string? NormalizeCurrency(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static ServiceError? ValidateAccountCurrency(
        StageStatementImportBatchCommand command,
        string accountCurrency)
    {
        var normalizedAccountCurrency = NormalizeCurrency(accountCurrency);
        if (normalizedAccountCurrency is null || !IsCurrency(normalizedAccountCurrency))
        {
            return Invalid(
                "The destination account currency is invalid.",
                "statement_import_account_currency_invalid");
        }

        return command.Rows.Any(row =>
                row.ValidationStatus == StatementImportValidationStatuses.Valid
                && NormalizeCurrency(row.Currency) != normalizedAccountCurrency)
            ? Invalid(
                "Every valid statement row must match the destination account currency.",
                "statement_import_currency_mismatch")
            : null;
    }

    private static ServiceError? ValidateRow(
        StageStatementImportRowCommand row,
        ISet<int> rowNumbers)
    {
        if (row.RowNumber <= 0 || !rowNumbers.Add(row.RowNumber))
        {
            return Invalid(
                "Statement row numbers must be positive and unique within the batch.",
                "statement_import_row_number_invalid");
        }

        if (!IsSha256Fingerprint(row.RowFingerprint))
        {
            return Invalid("Statement row fingerprint is invalid.", "statement_import_row_fingerprint_invalid");
        }

        if (row.SourceReferenceFingerprint is not null
            && !IsSha256Fingerprint(row.SourceReferenceFingerprint))
        {
            return Invalid(
                "Statement row source-reference fingerprint is invalid.",
                "statement_import_row_reference_fingerprint_invalid");
        }

        if (!IsValidRowState(
                row.ValidationStatus,
                row.DuplicateClassification,
                row.ReviewDisposition,
                row.ValidationCode,
                row.DuplicateCandidateTransactionId))
        {
            return Invalid("Statement row state is invalid.", "statement_import_row_state_invalid");
        }

        if (!IsMappedSourceEvidence(row.SourceEvidenceJson))
        {
            return Invalid(
                "Statement row evidence must contain only bounded mapped source fields.",
                "statement_import_row_evidence_invalid");
        }

        if (!HasConsistentTimestamp(row))
        {
            return Invalid(
                "Statement row date precision is inconsistent.",
                "statement_import_row_date_invalid");
        }

        if (row.Description?.Length > 512)
        {
            return Invalid("Statement row description is too long.", "statement_import_row_description_invalid");
        }

        var currency = NormalizeCurrency(row.Currency);
        if (currency is not null && !IsCurrency(currency))
        {
            return Invalid("Statement row currency is invalid.", "statement_import_row_currency_invalid");
        }

        var requiresNormalizedFinancialFields =
            row.ValidationStatus == StatementImportValidationStatuses.Valid;
        if (requiresNormalizedFinancialFields
            && (row.TimestampPrecision is null
                || string.IsNullOrWhiteSpace(row.Description)
                || !row.Amount.HasValue
                || currency is null))
        {
            return Invalid(
                "Reviewable statement rows require a date, description, amount, and currency.",
                "statement_import_row_normalized_fields_required");
        }

        return null;
    }

    private static bool IsValidRowState(
        string validationStatus,
        string duplicateClassification,
        string reviewDisposition,
        string? validationCode,
        Guid? duplicateCandidateTransactionId)
    {
        if (validationCode?.Length > 64 || (validationCode is not null && !IsToken(validationCode)))
        {
            return false;
        }

        return (
            validationStatus,
            duplicateClassification,
            reviewDisposition,
            validationCode,
            duplicateCandidateTransactionId.HasValue) switch
        {
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.None,
                StatementImportReviewDispositions.Included or StatementImportReviewDispositions.Excluded,
                null,
                false) => true,
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.Exact,
                StatementImportReviewDispositions.Excluded,
                null,
                true) => true,
            (StatementImportValidationStatuses.Valid,
                StatementImportDuplicateClassifications.Likely,
                StatementImportReviewDispositions.Pending
                    or StatementImportReviewDispositions.Included
                    or StatementImportReviewDispositions.Excluded,
                null,
                true) => true,
            (StatementImportValidationStatuses.Invalid,
                StatementImportDuplicateClassifications.None,
                StatementImportReviewDispositions.Excluded,
                not null,
                false) => true,
            _ => false
        };
    }

    private static bool HasConsistentTimestamp(StageStatementImportRowCommand row) =>
        row.TimestampPrecision switch
        {
            StatementImportTimestampPrecisions.Date =>
                row.EffectiveDate.HasValue && !row.EffectiveAtUtc.HasValue,
            StatementImportTimestampPrecisions.Instant =>
                !row.EffectiveDate.HasValue
                && row.EffectiveAtUtc is { Kind: DateTimeKind.Utc },
            null => !row.EffectiveDate.HasValue && !row.EffectiveAtUtc.HasValue,
            _ => false
        };

    private static bool IsMappedSourceEvidence(string value)
    {
        if (!IsBoundedJsonObject(value, MaximumSourceEvidenceJsonBytes, out var document))
        {
            return false;
        }

        using (document)
        {
            var properties = document!.RootElement.EnumerateObject().ToList();
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            var truncatedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                if (!propertyNames.Add(property.Name))
                {
                    return false;
                }

                if (AllowedEvidenceValueProperties.Contains(property.Name))
                {
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                        || (property.Value.ValueKind == JsonValueKind.String
                            && property.Value.GetString()!.Length > 1_024))
                    {
                        return false;
                    }

                    continue;
                }

                if (property.Name == "version")
                {
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt32(out var version)
                        || version != 1)
                    {
                        return false;
                    }

                    continue;
                }

                if (property.Name != "truncatedFields"
                    || property.Value.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String
                        || item.GetString() is not { } fieldName
                        || !AllowedEvidenceValueProperties.Contains(fieldName)
                        || !truncatedFields.Add(fieldName))
                    {
                        return false;
                    }
                }
            }

            if (truncatedFields.Any(field => !propertyNames.Contains(field)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBoundedJsonObject(string value, int maximumBytes, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(value, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSha256Fingerprint(string value) =>
        value?.Length == 64 && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    private static bool IsVersionToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 32
        && IsToken(value);

    private static bool IsLocale(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            return false;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(value.Trim());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsTimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || (value != "UTC" && !value.Contains('/')))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim());
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

    private static bool IsToken(string value) => value.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static bool IsCurrency(string value) =>
        value.Length == 3 && value.All(character => character is >= 'A' and <= 'Z');

    private static ServiceError Invalid(string message, string code) =>
        new(message, code, StatusCodes.Status400BadRequest);
}
