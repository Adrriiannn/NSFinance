using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSFinance.Api.Modules.Imports.Mapping;
using NSFinance.Api.Modules.Imports.Parsing;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportMappingEngineTests
{
    private static readonly ImmutableArray<StatementCsvColumn> Columns =
    [
        new(0, "Date"),
        new(1, "Description"),
        new(2, "Amount"),
        new(3, "Debit"),
        new(4, "Credit"),
        new(5, "Currency"),
        new(6, "Reference"),
        new(7, "Ignored")
    ];

    private readonly StatementImportMappingEngine _engine = new();

    [Fact]
    public void ValidateDefinition_AcceptsExplicitSignedDateMapping()
    {
        var error = _engine.ValidateDefinition(SignedDateDefinition(), Columns);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateDefinition_RejectsColumnBoundsReuseAndNoncanonicalSchema()
    {
        var outOfRange = _engine.ValidateDefinition(
            SignedDateDefinition() with { DescriptionColumn = Columns.Length },
            Columns);
        var reused = _engine.ValidateDefinition(
            SignedDateDefinition() with { ReferenceColumn = 1 },
            Columns);
        var malformedSchema = _engine.ValidateDefinition(
            SignedDateDefinition(),
            [new StatementCsvColumn(0, "Date"), new StatementCsvColumn(0, "Amount")]);

        Assert.Equal(StatementImportMappingErrorCodes.ColumnOutOfRange, outOfRange?.Code);
        Assert.Equal(StatementImportMappingErrorCodes.ColumnReused, reused?.Code);
        Assert.Equal(StatementImportMappingErrorCodes.ColumnSchemaInvalid, malformedSchema?.Code);
    }

    [Fact]
    public void ValidateDefinition_RejectsInvalidAmountModesAndColumnShapes()
    {
        var invalidMode = _engine.ValidateDefinition(
            SignedDateDefinition() with { AmountMode = "combined" },
            Columns);
        var signedWithSplitColumns = _engine.ValidateDefinition(
            SignedDateDefinition() with { DebitColumn = 3, CreditColumn = 4 },
            Columns);
        var splitMissingCredit = _engine.ValidateDefinition(
            DebitCreditDefinition() with { CreditColumn = null },
            Columns);

        Assert.Equal(StatementImportMappingErrorCodes.AmountModeInvalid, invalidMode?.Code);
        Assert.Equal(StatementImportMappingErrorCodes.AmountShapeInvalid, signedWithSplitColumns?.Code);
        Assert.Equal(StatementImportMappingErrorCodes.AmountShapeInvalid, splitMissingCredit?.Code);
    }

    [Fact]
    public void ValidateDefinition_RejectsInvalidKindsSignLocaleTimeZoneAndDateFormat()
    {
        var cases = new (StatementImportMappingDefinition Definition, string Code)[]
        {
            (SignedDateDefinition() with { DateValueKind = "floating" }, StatementImportMappingErrorCodes.DateValueKindInvalid),
            (SignedDateDefinition() with { AmountSign = "negative" }, StatementImportMappingErrorCodes.AmountSignInvalid),
            (SignedDateDefinition() with { Locale = "en" }, StatementImportMappingErrorCodes.LocaleInvalid),
            (SignedDateDefinition() with { Locale = "not-a-locale" }, StatementImportMappingErrorCodes.LocaleInvalid),
            (SignedDateDefinition() with { TimeZoneId = "Mars/Olympus" }, StatementImportMappingErrorCodes.TimeZoneInvalid),
            (SignedDateDefinition() with { DateFormat = "yyyy" }, StatementImportMappingErrorCodes.DateFormatInvalid),
            (InstantDefinition() with { DateFormat = "yyyy-MM-dd" }, StatementImportMappingErrorCodes.DateFormatInvalid)
        };

        foreach (var testCase in cases)
        {
            var error = _engine.ValidateDefinition(testCase.Definition, Columns);

            Assert.Equal(testCase.Code, error?.Code);
            Assert.Equal(400, error?.StatusCode);
        }
    }

    [Fact]
    public void MapRow_MapsSignedDateValuesWithoutConvertingDateOnlyToAnInstant()
    {
        var mapped = _engine.MapRow(
            Row("15/07/2026", "  Coffee\t\nShop  ", "-12.30", "", "", "eur", "  abc  123 ", "private-extra"),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Valid, mapped.ValidationStatus);
        Assert.Null(mapped.ValidationCode);
        Assert.Equal(new DateOnly(2026, 7, 15), mapped.EffectiveDate);
        Assert.Null(mapped.EffectiveAtUtc);
        Assert.Equal(StatementImportTimestampPrecisions.Date, mapped.TimestampPrecision);
        Assert.Equal("Coffee Shop", mapped.Description);
        Assert.Equal(-12.30m, mapped.Amount);
        Assert.Equal("EUR", mapped.Currency);
        Assert.Matches("^[0-9a-f]{64}$", mapped.RowFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", mapped.SourceReferenceFingerprint!);
    }

    [Fact]
    public void MapRow_UsesExplicitLocaleGroupingAndOptionalSignInversion()
    {
        var definition = SignedDateDefinition() with
        {
            DateFormat = "dd.MM.yyyy",
            Locale = "de-DE",
            AmountSign = StatementImportAmountSigns.Invert
        };

        var mapped = _engine.MapRow(
            Row("15.07.2026", "Gehalt", "1.234,50", "", "", "EUR", "DE-1", ""),
            definition,
            "eur");

        Assert.Equal(StatementImportValidationStatuses.Valid, mapped.ValidationStatus);
        Assert.Equal(-1234.50m, mapped.Amount);
        Assert.Equal("EUR", mapped.Currency);
    }

    [Fact]
    public void MapRow_RejectsMalformedLocaleGroupingInsteadOfInventingAnAmount()
    {
        var mapped = _engine.MapRow(
            Row("15/07/2026", "Malformed grouping", "12,34", "", "", "EUR", "A-1", ""),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(StatementImportMappingErrorCodes.RowAmountInvalid, mapped.ValidationCode);
        Assert.Null(mapped.Amount);
    }

    [Theory]
    [InlineData("25.10", "", -25.10)]
    [InlineData("-25.10", "", -25.10)]
    [InlineData("", "25.10", 25.10)]
    [InlineData("", "-25.10", 25.10)]
    public void MapRow_DebitCreditModeDerivesSignFromThePopulatedColumn(
        string debit,
        string credit,
        decimal expected)
    {
        var mapped = _engine.MapRow(
            Row("15/07/2026", "Split amount", "", debit, credit, "EUR", "SPLIT-1", ""),
            DebitCreditDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Valid, mapped.ValidationStatus);
        Assert.Equal(expected, mapped.Amount);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("10.00", "12.00")]
    public void MapRow_DebitCreditModeRequiresExactlyOneNonblankValue(
        string debit,
        string credit)
    {
        var mapped = _engine.MapRow(
            Row("15/07/2026", "Invalid split", "", debit, credit, "EUR", "SPLIT-2", ""),
            DebitCreditDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(StatementImportMappingErrorCodes.RowDebitCreditShapeInvalid, mapped.ValidationCode);
    }

    [Theory]
    [InlineData("15/01/2026 12:00", "2026-01-15T12:00:00Z")]
    [InlineData("15/07/2026 12:00", "2026-07-15T11:00:00Z")]
    public void MapRow_ConvertsUnambiguousLocalInstantsToUtcUsingTheExplicitTimeZone(
        string source,
        string expectedUtc)
    {
        var mapped = _engine.MapRow(
            Row(source, "Timed transaction", "8.00", "", "", "EUR", "TIME-1", ""),
            InstantDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Valid, mapped.ValidationStatus);
        Assert.Null(mapped.EffectiveDate);
        Assert.Equal(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), mapped.EffectiveAtUtc);
        Assert.Equal(DateTimeKind.Utc, mapped.EffectiveAtUtc?.Kind);
        Assert.Equal(StatementImportTimestampPrecisions.Instant, mapped.TimestampPrecision);
    }

    [Theory]
    [InlineData("29/03/2026 01:30", StatementImportMappingErrorCodes.RowInstantInvalidLocalTime)]
    [InlineData("25/10/2026 01:30", StatementImportMappingErrorCodes.RowInstantAmbiguousLocalTime)]
    public void MapRow_RejectsInvalidOrAmbiguousDublinWallTimes(
        string source,
        string expectedCode)
    {
        var mapped = _engine.MapRow(
            Row(source, "DST transaction", "8.00", "", "", "EUR", "TIME-2", ""),
            InstantDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(expectedCode, mapped.ValidationCode);
        Assert.Null(mapped.EffectiveAtUtc);
    }

    [Fact]
    public void MapRow_UsesAnExplicitSourceOffsetWithoutApplyingTheAccountTimeZoneTwice()
    {
        var definition = InstantDefinition() with
        {
            DateFormat = "yyyy-MM-dd'T'HH:mm:sszzz",
            TimeZoneId = "UTC"
        };

        var mapped = _engine.MapRow(
            Row("2026-07-15T12:30:00+02:00", "Offset transaction", "8.00", "", "", "EUR", "TIME-3", ""),
            definition,
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Valid, mapped.ValidationStatus);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc), mapped.EffectiveAtUtc);
    }

    [Fact]
    public void MapRow_RequiresAnExplicitOffsetWhenTheSelectedFormatContainsK()
    {
        var definition = InstantDefinition() with
        {
            DateFormat = "yyyy-MM-dd'T'HH:mm:ssK",
            TimeZoneId = "UTC"
        };

        var mapped = _engine.MapRow(
            Row("2026-07-15T12:30:00", "Missing offset", "8.00", "", "", "EUR", "TIME-4", ""),
            definition,
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(StatementImportMappingErrorCodes.RowInstantOffsetRequired, mapped.ValidationCode);
        Assert.Null(mapped.EffectiveAtUtc);
    }

    [Theory]
    [InlineData("", StatementImportMappingErrorCodes.RowDateRequired)]
    [InlineData("07/15/2026", StatementImportMappingErrorCodes.RowDateInvalid)]
    public void MapRow_ReturnsDateValidationStatesRatherThanThrowing(
        string source,
        string expectedCode)
    {
        var mapped = _engine.MapRow(
            Row(source, "Date validation", "1.00", "", "", "EUR", "DATE-1", ""),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(expectedCode, mapped.ValidationCode);
    }

    [Fact]
    public void MapRow_RejectsBlankAndStorageOversizedDescriptions()
    {
        var blank = _engine.MapRow(
            Row("15/07/2026", " \t ", "1.00", "", "", "EUR", "DESC-1", ""),
            SignedDateDefinition(),
            "EUR");
        var longDescription = _engine.MapRow(
            Row("15/07/2026", new string('x', 513), "1.00", "", "", "EUR", "DESC-2", ""),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(StatementImportMappingErrorCodes.RowDescriptionRequired, blank.ValidationCode);
        Assert.Equal(StatementImportMappingErrorCodes.RowDescriptionTooLong, longDescription.ValidationCode);
    }

    [Theory]
    [InlineData("0.00", StatementImportMappingErrorCodes.RowAmountZero)]
    [InlineData("1.234", StatementImportMappingErrorCodes.RowAmountPrecisionInvalid)]
    [InlineData("10000000000000000.00", StatementImportMappingErrorCodes.RowAmountOutOfRange)]
    [InlineData("EUR 12.00", StatementImportMappingErrorCodes.RowAmountInvalid)]
    public void MapRow_RejectsAmountsThatCannotBeStoredExactly(
        string source,
        string expectedCode)
    {
        var mapped = _engine.MapRow(
            Row("15/07/2026", "Amount validation", source, "", "", "EUR", "AMOUNT-1", ""),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(StatementImportValidationStatuses.Invalid, mapped.ValidationStatus);
        Assert.Equal(expectedCode, mapped.ValidationCode);
    }

    [Fact]
    public void MapRow_UsesAccountCurrencyAndRejectsConflictingSourceCurrency()
    {
        var blankSource = _engine.MapRow(
            Row("15/07/2026", "No source currency", "1.00", "", "", "", "CUR-1", ""),
            SignedDateDefinition(),
            "eur");
        var conflictingSource = _engine.MapRow(
            Row("15/07/2026", "Wrong source currency", "1.00", "", "", "USD", "CUR-2", ""),
            SignedDateDefinition(),
            "EUR");
        var malformedAccountCurrency = _engine.MapRow(
            Row("15/07/2026", "Bad account currency", "1.00", "", "", "EUR", "CUR-3", ""),
            SignedDateDefinition(),
            "EURO");

        Assert.Equal(StatementImportValidationStatuses.Valid, blankSource.ValidationStatus);
        Assert.Equal("EUR", blankSource.Currency);
        Assert.Equal(StatementImportMappingErrorCodes.RowCurrencyMismatch, conflictingSource.ValidationCode);
        Assert.Equal("EUR", conflictingSource.Currency);
        Assert.Equal(StatementImportMappingErrorCodes.RowAccountCurrencyInvalid, malformedAccountCurrency.ValidationCode);
        Assert.Null(malformedAccountCurrency.Currency);
    }

    [Fact]
    public void MapRow_CreatesDeterministicNormalizedRowAndReferenceFingerprints()
    {
        var first = _engine.MapRow(
            Row("15/07/2026", "Coffee   Shop", "-12.30", "", "", "EUR", "abc  123", "ignored-a"),
            SignedDateDefinition(),
            "EUR");
        var semanticallyEquivalent = _engine.MapRow(
            Row("15/07/2026", " Coffee\tShop ", "-12.30", "", "", "eur", " ABC 123 ", "ignored-b"),
            SignedDateDefinition(),
            "eur");
        var changedAmount = _engine.MapRow(
            Row("15/07/2026", "Coffee Shop", "-12.31", "", "", "EUR", "ABC 123", "ignored-a"),
            SignedDateDefinition(),
            "EUR");

        Assert.Equal(first.RowFingerprint, semanticallyEquivalent.RowFingerprint);
        Assert.Equal(first.SourceReferenceFingerprint, semanticallyEquivalent.SourceReferenceFingerprint);
        Assert.NotEqual(first.RowFingerprint, changedAmount.RowFingerprint);
        Assert.Equal(
            HashLengthPrefixed("statement-import-reference-v1", "ABC 123"),
            first.SourceReferenceFingerprint);
    }

    [Fact]
    public void MapRow_EmitsOnlyBoundedAllowlistedEvidence()
    {
        var mapped = _engine.MapRow(
            Row(
                new string('d', 1024),
                new string('x', 1024),
                new string('a', 1024),
                new string('b', 1024),
                new string('c', 1024),
                new string('u', 1024),
                new string('r', 1024),
                "must-not-appear"),
            DebitCreditDefinition(),
            "EUR");

        using var document = JsonDocument.Parse(mapped.SourceEvidenceJson);
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        var allowlist = new HashSet<string>(StringComparer.Ordinal)
        {
            "version", "date", "description", "debit", "credit", "currency", "reference", "truncatedFields"
        };

        Assert.All(properties, property => Assert.Contains(property, allowlist));
        Assert.DoesNotContain("must-not-appear", mapped.SourceEvidenceJson, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(mapped.SourceEvidenceJson) <= 4096);
        Assert.Equal(64, document.RootElement.GetProperty("description").GetString()!.Length);
        Assert.Contains("description", document.RootElement.GetProperty("truncatedFields")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void MapRow_ReturnsSafeInvalidStatesForMissingColumnsAndInvalidMappings()
    {
        var missingColumn = _engine.MapRow(
            new StatementCsvDataRow(4, ["15/07/2026", "Short row"]),
            SignedDateDefinition(),
            "EUR");
        var invalidMapping = _engine.MapRow(
            Row("15/07/2026", "Invalid mapping", "1.00", "", "", "EUR", "MAP-1", ""),
            SignedDateDefinition() with { AmountMode = "mystery" },
            "EUR");

        Assert.Equal(StatementImportMappingErrorCodes.RowColumnMissing, missingColumn.ValidationCode);
        Assert.Equal(StatementImportMappingErrorCodes.RowMappingInvalid, invalidMapping.ValidationCode);
        Assert.Matches("^[0-9a-f]{64}$", missingColumn.RowFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", invalidMapping.RowFingerprint);
    }

    [Fact]
    public void MapRow_InvalidFingerprintUsesFullSelectedValuesBeyondEvidenceTruncation()
    {
        var sharedPrefix = new string('d', 64);
        var first = _engine.MapRow(
            Row(sharedPrefix + "a", "Invalid date", "1.00", "", "", "EUR", "DATE-A", ""),
            SignedDateDefinition(),
            "EUR");
        var second = _engine.MapRow(
            Row(sharedPrefix + "b", "Invalid date", "1.00", "", "", "EUR", "DATE-A", ""),
            SignedDateDefinition(),
            "EUR");

        using var firstEvidence = JsonDocument.Parse(first.SourceEvidenceJson);
        using var secondEvidence = JsonDocument.Parse(second.SourceEvidenceJson);
        Assert.Equal(
            firstEvidence.RootElement.GetProperty("date").GetString(),
            secondEvidence.RootElement.GetProperty("date").GetString());
        Assert.NotEqual(first.RowFingerprint, second.RowFingerprint);
    }

    [Fact]
    public void CreateCanonicalMappingJson_WritesStableNormalizedPropertyOrderAndNulls()
    {
        var json = _engine.CreateCanonicalMappingJson(SignedDateDefinition() with
        {
            DateValueKind = " DATE ",
            AmountMode = " SIGNED ",
            AmountSign = " AS_IS ",
            Locale = "en-ie",
            TimeZoneId = " UTC "
        });

        Assert.Equal(
            "{\"version\":1,\"dateColumn\":0,\"descriptionColumn\":1,\"amountColumn\":2,\"debitColumn\":null,\"creditColumn\":null,\"currencyColumn\":5,\"referenceColumn\":6,\"dateFormat\":\"dd/MM/yyyy\",\"dateValueKind\":\"date\",\"amountMode\":\"signed\",\"amountSign\":\"as_is\",\"locale\":\"en-IE\",\"timeZoneId\":\"UTC\"}",
            json);
    }

    private static StatementImportMappingDefinition SignedDateDefinition() => new(
        DateColumn: 0,
        DescriptionColumn: 1,
        AmountColumn: 2,
        DebitColumn: null,
        CreditColumn: null,
        CurrencyColumn: 5,
        ReferenceColumn: 6,
        DateFormat: "dd/MM/yyyy",
        DateValueKind: StatementImportDateValueKinds.Date,
        AmountMode: StatementImportAmountModes.Signed,
        AmountSign: StatementImportAmountSigns.AsIs,
        Locale: "en-IE",
        TimeZoneId: "Europe/Dublin");

    private static StatementImportMappingDefinition DebitCreditDefinition() =>
        SignedDateDefinition() with
        {
            AmountColumn = null,
            DebitColumn = 3,
            CreditColumn = 4,
            AmountMode = StatementImportAmountModes.DebitCredit
        };

    private static StatementImportMappingDefinition InstantDefinition() =>
        SignedDateDefinition() with
        {
            DateFormat = "dd/MM/yyyy HH:mm",
            DateValueKind = StatementImportDateValueKinds.Instant
        };

    private static StatementCsvDataRow Row(params string[] fields) =>
        new(2, fields.ToImmutableArray());

    private static string HashLengthPrefixed(params string[] values)
    {
        var payload = string.Concat(values.Select(value => $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
