using NSFinance.Api.Common.Contracts;

namespace NSFinance.Api.Modules.Imports.Services;

internal static class StatementImportUploadPolicy
{
    public const long MaximumMultipartBodyBytes =
        StatementImportStagingPolicy.MaximumFileSizeBytes + (128 * 1024);

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/csv",
        "application/octet-stream",
        "application/vnd.ms-excel",
        "text/comma-separated-values",
        "text/csv",
        "text/plain"
    };

    public static ServiceError? ValidateFile(IFormFile? file)
    {
        if (file is null)
        {
            return Invalid("A CSV file is required.", "statement_csv_file_required");
        }

        if (file.Length is <= 0 or > StatementImportStagingPolicy.MaximumFileSizeBytes)
        {
            return Invalid(
                $"CSV files must be between 1 byte and {StatementImportStagingPolicy.MaximumFileSizeBytes} bytes.",
                "statement_csv_file_size_invalid");
        }

        var fileName = StatementImportStagingPolicy.NormalizeFileName(file.FileName);
        if (fileName.Length is 0 or > 260
            || !string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("The selected file must use the .csv extension.", "statement_csv_file_type_invalid");
        }

        var contentType = file.ContentType?.Split(';', 2)[0].Trim();
        if (!string.IsNullOrEmpty(contentType) && !AllowedContentTypes.Contains(contentType))
        {
            return Invalid("The selected file is not a supported CSV document.", "statement_csv_file_type_invalid");
        }

        return null;
    }

    public static bool TryNormalizeDelimiter(
        string? value,
        out string delimiter,
        out ServiceError? error)
    {
        delimiter = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "," or "comma" => ",",
            ";" or "semicolon" => ";",
            "\\t" or "tab" => "\t",
            "|" or "pipe" => "|",
            _ => string.Empty
        };

        error = delimiter.Length == 0
            ? Invalid(
                "Delimiter must be comma, semicolon, tab, or pipe.",
                "statement_csv_delimiter_invalid")
            : null;
        return error is null;
    }

    private static ServiceError Invalid(string message, string code) =>
        new(message, code, StatusCodes.Status400BadRequest);
}
