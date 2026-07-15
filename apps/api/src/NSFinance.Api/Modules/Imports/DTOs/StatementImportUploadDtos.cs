namespace NSFinance.Api.Modules.Imports.DTOs;

public sealed record StatementCsvColumnDto(int Index, string Name);

public sealed record StatementCsvSampleRowDto(
    int RowNumber,
    IReadOnlyList<string> Fields);

public sealed record StatementCsvInspectionDto(
    string FileName,
    string ParserVersion,
    string Delimiter,
    long SourceByteCount,
    int DataRowCount,
    IReadOnlyList<StatementCsvColumnDto> Columns,
    IReadOnlyList<StatementCsvSampleRowDto> SampleRows);

public sealed record StatementImportPreviewDto(
    StatementImportBatchDto Batch,
    StatementImportRowPageDto Rows);

public sealed record StatementImportPreviewRequest(
    Guid AccountId,
    string? Delimiter,
    int DateColumn,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    int? CurrencyColumn,
    int? ReferenceColumn,
    string? DateFormat,
    string? DateValueKind,
    string? AmountMode,
    string? AmountSign,
    string? Locale,
    string? TimeZoneId);
