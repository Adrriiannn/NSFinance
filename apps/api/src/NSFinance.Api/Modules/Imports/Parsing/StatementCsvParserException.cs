namespace NSFinance.Api.Modules.Imports.Parsing;

internal sealed class StatementCsvParserException : Exception
{
    private StatementCsvParserException(
        string code,
        StatementCsvParserFailureKind failureKind,
        string message)
        : base(message)
    {
        Code = code;
        FailureKind = failureKind;
    }

    public string Code { get; }

    public StatementCsvParserFailureKind FailureKind { get; }

    public int RecommendedStatusCode => FailureKind switch
    {
        StatementCsvParserFailureKind.PayloadTooLarge => 413,
        StatementCsvParserFailureKind.UnsupportedContent => 415,
        _ => 400
    };

    internal static StatementCsvParserException InvalidOptions() => Invalid(
        StatementCsvParserErrorCodes.InvalidOptions,
        "The CSV parser options are invalid.");

    internal static StatementCsvParserException UnsupportedDelimiter() => Invalid(
        StatementCsvParserErrorCodes.UnsupportedDelimiter,
        "The requested CSV delimiter is not supported.");

    internal static StatementCsvParserException EmptyFile() => Invalid(
        StatementCsvParserErrorCodes.EmptyFile,
        "The CSV file is empty.");

    internal static StatementCsvParserException BlankHeader() => Invalid(
        StatementCsvParserErrorCodes.BlankHeader,
        "Every CSV column must have a header.");

    internal static StatementCsvParserException DuplicateHeader() => Invalid(
        StatementCsvParserErrorCodes.DuplicateHeader,
        "CSV column headers must be unique.");

    internal static StatementCsvParserException EmptyData() => Invalid(
        StatementCsvParserErrorCodes.EmptyData,
        "The CSV file does not contain any data rows.");

    internal static StatementCsvParserException ColumnCountMismatch() => Invalid(
        StatementCsvParserErrorCodes.ColumnCountMismatch,
        "Every CSV data row must have the same number of columns as the header.");

    internal static StatementCsvParserException MalformedDocument() => Invalid(
        StatementCsvParserErrorCodes.MalformedDocument,
        "The CSV file is malformed.");

    internal static StatementCsvParserException FileTooLarge() => TooLarge(
        StatementCsvParserErrorCodes.FileTooLarge,
        "The CSV file exceeds the allowed size.");

    internal static StatementCsvParserException TooManyRows() => TooLarge(
        StatementCsvParserErrorCodes.TooManyRows,
        "The CSV file contains too many data rows.");

    internal static StatementCsvParserException TooManyColumns() => TooLarge(
        StatementCsvParserErrorCodes.TooManyColumns,
        "The CSV file contains too many columns.");

    internal static StatementCsvParserException FieldTooLong() => TooLarge(
        StatementCsvParserErrorCodes.FieldTooLong,
        "A CSV field exceeds the allowed length.");

    internal static StatementCsvParserException InvalidUtf8() => Unsupported(
        StatementCsvParserErrorCodes.InvalidUtf8,
        "The CSV file is not valid UTF-8.");

    internal static StatementCsvParserException UnsupportedEncoding() => Unsupported(
        StatementCsvParserErrorCodes.UnsupportedEncoding,
        "The CSV file encoding is not supported.");

    internal static StatementCsvParserException BinaryContent() => Unsupported(
        StatementCsvParserErrorCodes.BinaryContent,
        "The uploaded content is not a text CSV file.");

    private static StatementCsvParserException Invalid(string code, string message) =>
        new(code, StatementCsvParserFailureKind.InvalidDocument, message);

    private static StatementCsvParserException TooLarge(string code, string message) =>
        new(code, StatementCsvParserFailureKind.PayloadTooLarge, message);

    private static StatementCsvParserException Unsupported(string code, string message) =>
        new(code, StatementCsvParserFailureKind.UnsupportedContent, message);
}
