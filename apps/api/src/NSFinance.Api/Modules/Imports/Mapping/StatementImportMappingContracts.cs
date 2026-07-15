using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Parsing;

namespace NSFinance.Api.Modules.Imports.Mapping;

internal static class StatementImportDateValueKinds
{
    public const string Date = "date";
    public const string Instant = "instant";
}

internal static class StatementImportAmountModes
{
    public const string Signed = "signed";
    public const string DebitCredit = "debit_credit";
}

internal static class StatementImportAmountSigns
{
    public const string AsIs = "as_is";
    public const string Invert = "invert";
}

internal sealed record StatementImportMappingDefinition(
    int DateColumn,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    int? CurrencyColumn,
    int? ReferenceColumn,
    string DateFormat,
    string DateValueKind,
    string AmountMode,
    string AmountSign,
    string Locale,
    string TimeZoneId);

internal sealed record StatementImportMappedRow(
    int RowNumber,
    string RowFingerprint,
    string? SourceReferenceFingerprint,
    string ValidationStatus,
    string? ValidationCode,
    string SourceEvidenceJson,
    DateOnly? EffectiveDate,
    DateTime? EffectiveAtUtc,
    string? TimestampPrecision,
    string? Description,
    decimal? Amount,
    string? Currency);

internal interface IStatementImportMappingEngine
{
    ServiceError? ValidateDefinition(
        StatementImportMappingDefinition definition,
        IReadOnlyList<StatementCsvColumn> columns);

    StatementImportMappedRow MapRow(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition,
        string accountCurrency);

    string CreateCanonicalMappingJson(StatementImportMappingDefinition definition);
}
