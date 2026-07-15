using System.Collections.Immutable;

namespace NSFinance.Api.Modules.Imports.Parsing;

internal interface IStatementCsvParser
{
    Task<StatementCsvParseResult> ParseAsync(
        Stream source,
        StatementCsvParserOptions options,
        CancellationToken cancellationToken = default);

    Task<StatementCsvParseResult> ParseRowsAsync(
        Stream source,
        StatementCsvParserOptions options,
        Func<StatementCsvDataRow, CancellationToken, ValueTask> rowHandler,
        CancellationToken cancellationToken = default);
}

internal sealed record StatementCsvParserOptions(
    string Delimiter = StatementCsvParserOptions.DefaultDelimiter,
    int SampleRowLimit = StatementCsvParserOptions.DefaultSampleRowLimit)
{
    public const string DefaultDelimiter = ",";
    public const int DefaultSampleRowLimit = 10;
    public const int MaximumSampleRowLimit = 25;
}

internal sealed record StatementCsvColumn(int Index, string Name);

internal sealed record StatementCsvSampleRow(
    int RowNumber,
    ImmutableArray<string> Fields);

internal sealed record StatementCsvDataRow(
    int RowNumber,
    ImmutableArray<string> Fields);

internal sealed record StatementCsvParseResult(
    string ParserVersion,
    string Delimiter,
    long SourceByteCount,
    string SourceSha256,
    int DataRowCount,
    ImmutableArray<StatementCsvColumn> Columns,
    ImmutableArray<StatementCsvSampleRow> SampleRows);

internal enum StatementCsvParserFailureKind
{
    InvalidDocument,
    PayloadTooLarge,
    UnsupportedContent
}

internal static class StatementCsvParserErrorCodes
{
    public const string InvalidOptions = "statement_csv_invalid_options";
    public const string UnsupportedDelimiter = "statement_csv_unsupported_delimiter";
    public const string EmptyFile = "statement_csv_empty_file";
    public const string BlankHeader = "statement_csv_blank_header";
    public const string DuplicateHeader = "statement_csv_duplicate_header";
    public const string EmptyData = "statement_csv_empty_data";
    public const string ColumnCountMismatch = "statement_csv_column_count_mismatch";
    public const string MalformedDocument = "statement_csv_malformed";
    public const string FileTooLarge = "statement_csv_file_too_large";
    public const string TooManyRows = "statement_csv_too_many_rows";
    public const string TooManyColumns = "statement_csv_too_many_columns";
    public const string FieldTooLong = "statement_csv_field_too_long";
    public const string InvalidUtf8 = "statement_csv_invalid_utf8";
    public const string UnsupportedEncoding = "statement_csv_unsupported_encoding";
    public const string BinaryContent = "statement_csv_binary_content";
}
