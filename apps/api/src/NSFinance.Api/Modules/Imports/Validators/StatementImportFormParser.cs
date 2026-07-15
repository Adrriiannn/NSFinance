using System.Globalization;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.DTOs;

namespace NSFinance.Api.Modules.Imports.Validators;

internal static class StatementImportFormParser
{
    private static readonly HashSet<string> InspectionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "delimiter"
    };

    private static readonly HashSet<string> PreviewFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "accountId",
        "delimiter",
        "dateColumn",
        "descriptionColumn",
        "amountColumn",
        "debitColumn",
        "creditColumn",
        "currencyColumn",
        "referenceColumn",
        "dateFormat",
        "dateValueKind",
        "amountMode",
        "amountSign",
        "locale",
        "timeZoneId"
    };

    public static ServiceError? ValidateInspectionForm(IFormCollection form)
    {
        var shapeError = ValidateShape(form, InspectionFields);
        return shapeError ?? ValidateSingleValues(form);
    }

    public static ServiceError? TryCreatePreviewRequest(
        IFormCollection form,
        out StatementImportPreviewRequest? request)
    {
        request = null;
        var shapeError = ValidateShape(form, PreviewFields) ?? ValidateSingleValues(form);
        if (shapeError is not null)
        {
            return shapeError;
        }

        if (!Guid.TryParse(Single(form, "accountId"), out var accountId)
            || accountId == Guid.Empty)
        {
            return Invalid("Destination account is required.", "statement_import_account_required");
        }

        if (!TryRequiredColumn(form, "dateColumn", out var dateColumn)
            || !TryRequiredColumn(form, "descriptionColumn", out var descriptionColumn))
        {
            return Invalid(
                "Date and description columns are required non-negative indexes.",
                "statement_import_mapping_column_invalid");
        }

        if (!TryOptionalColumn(form, "amountColumn", out var amountColumn)
            || !TryOptionalColumn(form, "debitColumn", out var debitColumn)
            || !TryOptionalColumn(form, "creditColumn", out var creditColumn)
            || !TryOptionalColumn(form, "currencyColumn", out var currencyColumn)
            || !TryOptionalColumn(form, "referenceColumn", out var referenceColumn))
        {
            return Invalid(
                "Optional mapping columns must be non-negative indexes when supplied.",
                "statement_import_mapping_column_invalid");
        }

        request = new StatementImportPreviewRequest(
            accountId,
            Single(form, "delimiter"),
            dateColumn,
            descriptionColumn,
            amountColumn,
            debitColumn,
            creditColumn,
            currencyColumn,
            referenceColumn,
            Single(form, "dateFormat"),
            Single(form, "dateValueKind"),
            Single(form, "amountMode"),
            Single(form, "amountSign"),
            Single(form, "locale"),
            Single(form, "timeZoneId"));
        return null;
    }

    public static ServiceError? GetSingleFile(
        IFormCollection form,
        out IFormFile? file)
    {
        file = null;
        if (form.Files.Count != 1)
        {
            return Invalid("Exactly one CSV file is required.", "statement_csv_file_count_invalid");
        }

        file = form.Files[0];
        if (!string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase))
        {
            file = null;
            return Invalid("The CSV upload field must be named file.", "statement_csv_file_field_invalid");
        }

        return null;
    }

    private static ServiceError? ValidateShape(
        IFormCollection form,
        IReadOnlySet<string> allowedFields)
    {
        var unexpected = form.Keys.FirstOrDefault(key => !allowedFields.Contains(key));
        return unexpected is null
            ? null
            : Invalid(
                "The upload form contains an unexpected field.",
                "statement_import_form_field_invalid");
    }

    private static ServiceError? ValidateSingleValues(IFormCollection form)
    {
        var repeated = form.FirstOrDefault(pair => pair.Value.Count > 1);
        return repeated.Key is null
            ? null
            : Invalid(
                "Upload form fields may only be supplied once.",
                "statement_import_form_field_repeated");
    }

    private static bool TryRequiredColumn(
        IFormCollection form,
        string key,
        out int value) =>
        int.TryParse(
            Single(form, key),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value)
        && value >= 0;

    private static bool TryOptionalColumn(
        IFormCollection form,
        string key,
        out int? value)
    {
        value = null;
        var raw = Single(form, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? Single(IFormCollection form, string key) =>
        form.TryGetValue(key, out var values) && values.Count == 1
            ? values[0]
            : null;

    private static ServiceError Invalid(string message, string code) =>
        new(message, code, StatusCodes.Status400BadRequest);
}
