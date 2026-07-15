using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Imports.Parsing;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Imports.Mapping;

internal sealed class StatementImportMappingEngine : IStatementImportMappingEngine
{
    private const int MaximumDescriptionLength = 512;
    private const int MaximumEvidenceValueRunes = 64;
    private const decimal MaximumStoredAmount = 9999999999999999.99m;
    private const int InvalidDefinitionStatusCode = 400;

    public ServiceError? ValidateDefinition(
        StatementImportMappingDefinition definition,
        IReadOnlyList<StatementCsvColumn> columns)
    {
        if (definition is null)
        {
            return InvalidDefinition(
                "A statement mapping definition is required.",
                StatementImportMappingErrorCodes.DefinitionRequired);
        }

        if (columns is null || columns.Count == 0)
        {
            return InvalidDefinition(
                "At least one statement column is required.",
                StatementImportMappingErrorCodes.ColumnsRequired);
        }

        if (!HasCanonicalColumnSchema(columns))
        {
            return InvalidDefinition(
                "Statement columns must have unique, zero-based indexes.",
                StatementImportMappingErrorCodes.ColumnSchemaInvalid);
        }

        if (!IsKnownDateValueKind(definition.DateValueKind))
        {
            return InvalidDefinition(
                "The statement date value kind is invalid.",
                StatementImportMappingErrorCodes.DateValueKindInvalid);
        }

        if (!IsKnownAmountMode(definition.AmountMode))
        {
            return InvalidDefinition(
                "The statement amount mode is invalid.",
                StatementImportMappingErrorCodes.AmountModeInvalid);
        }

        if (!IsKnownAmountSign(definition.AmountSign))
        {
            return InvalidDefinition(
                "The statement amount sign is invalid.",
                StatementImportMappingErrorCodes.AmountSignInvalid);
        }

        if (!HasValidAmountShape(definition))
        {
            return InvalidDefinition(
                "The selected amount columns do not match the statement amount mode.",
                StatementImportMappingErrorCodes.AmountShapeInvalid);
        }

        var selectedColumns = GetSelectedColumns(definition);
        if (selectedColumns.Any(index => index < 0 || index >= columns.Count))
        {
            return InvalidDefinition(
                "A selected statement column is outside the available column range.",
                StatementImportMappingErrorCodes.ColumnOutOfRange);
        }

        if (selectedColumns.Distinct().Count() != selectedColumns.Count)
        {
            return InvalidDefinition(
                "Each statement field must map to a unique source column.",
                StatementImportMappingErrorCodes.ColumnReused);
        }

        if (!TryGetSpecificCulture(definition.Locale, out var culture))
        {
            return InvalidDefinition(
                "The statement locale must be a valid specific culture.",
                StatementImportMappingErrorCodes.LocaleInvalid);
        }

        if (!TryGetTimeZone(definition.TimeZoneId, out _))
        {
            return InvalidDefinition(
                "The statement time zone is invalid.",
                StatementImportMappingErrorCodes.TimeZoneInvalid);
        }

        if (!IsValidDateFormat(
                definition.DateFormat,
                definition.DateValueKind,
                culture!))
        {
            return InvalidDefinition(
                "The statement date format is invalid for the selected date value kind.",
                StatementImportMappingErrorCodes.DateFormatInvalid);
        }

        return null;
    }

    public StatementImportMappedRow MapRow(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition,
        string accountCurrency)
    {
        var evidenceJson = BuildEvidenceJson(row, definition);
        var sourceReferenceFingerprint = CreateSourceReferenceFingerprint(row, definition);
        DateOnly? effectiveDate = null;
        DateTime? effectiveAtUtc = null;
        string? timestampPrecision = null;
        string? description = null;
        decimal? amount = null;
        var normalizedAccountCurrency = NormalizeCurrency(accountCurrency);

        StatementImportMappedRow Invalid(string code) => CreateMappedRow(
            row,
            definition,
            sourceReferenceFingerprint,
            StatementImportValidationStatuses.Invalid,
            code,
            evidenceJson,
            effectiveDate,
            effectiveAtUtc,
            timestampPrecision,
            description,
            amount,
            normalizedAccountCurrency);

        try
        {
            if (!TryCreateRuntime(definition, out var runtime))
            {
                return Invalid(StatementImportMappingErrorCodes.RowMappingInvalid);
            }

            if (GetSelectedColumns(definition).Any(index => index >= row.Fields.Length))
            {
                return Invalid(StatementImportMappingErrorCodes.RowColumnMissing);
            }

            if (normalizedAccountCurrency is null)
            {
                return Invalid(StatementImportMappingErrorCodes.RowAccountCurrencyInvalid);
            }

            var dateValue = NormalizeSourceScalar(row.Fields[definition.DateColumn]);
            if (dateValue.Length == 0)
            {
                return Invalid(StatementImportMappingErrorCodes.RowDateRequired);
            }

            if (TokenEquals(definition.DateValueKind, StatementImportDateValueKinds.Date))
            {
                if (!DateOnly.TryParseExact(
                        dateValue,
                        definition.DateFormat.Trim(),
                        runtime!.Culture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var parsedDate))
                {
                    return Invalid(StatementImportMappingErrorCodes.RowDateInvalid);
                }

                effectiveDate = parsedDate;
                timestampPrecision = StatementImportTimestampPrecisions.Date;
            }
            else
            {
                var instantResult = TryParseInstant(
                    dateValue,
                    definition.DateFormat.Trim(),
                    runtime!.Culture,
                    runtime.TimeZone,
                    runtime.FormatContainsOffset,
                    out var parsedInstantUtc);

                if (instantResult is not null)
                {
                    return Invalid(instantResult);
                }

                effectiveAtUtc = parsedInstantUtc;
                timestampPrecision = StatementImportTimestampPrecisions.Instant;
            }

            description = NormalizeDescription(row.Fields[definition.DescriptionColumn]);
            if (description.Length == 0)
            {
                description = null;
                return Invalid(StatementImportMappingErrorCodes.RowDescriptionRequired);
            }

            if (description.Length > MaximumDescriptionLength)
            {
                return Invalid(StatementImportMappingErrorCodes.RowDescriptionTooLong);
            }

            var amountResult = TryMapAmount(row, definition, runtime!.Culture, out var parsedAmount);
            if (amountResult is not null)
            {
                return Invalid(amountResult);
            }

            amount = parsedAmount;

            if (definition.CurrencyColumn is int currencyColumn)
            {
                var sourceCurrencyValue = NormalizeSourceScalar(row.Fields[currencyColumn]);
                if (sourceCurrencyValue.Length > 0)
                {
                    var sourceCurrency = NormalizeCurrency(sourceCurrencyValue);
                    if (sourceCurrency is null)
                    {
                        return Invalid(StatementImportMappingErrorCodes.RowSourceCurrencyInvalid);
                    }

                    if (!string.Equals(
                            sourceCurrency,
                            normalizedAccountCurrency,
                            StringComparison.Ordinal))
                    {
                        return Invalid(StatementImportMappingErrorCodes.RowCurrencyMismatch);
                    }
                }
            }

            return CreateMappedRow(
                row,
                definition,
                sourceReferenceFingerprint,
                StatementImportValidationStatuses.Valid,
                validationCode: null,
                evidenceJson,
                effectiveDate,
                effectiveAtUtc,
                timestampPrecision,
                description,
                amount,
                normalizedAccountCurrency);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or FormatException
                                          or InvalidTimeZoneException
                                          or OverflowException
                                          or TimeZoneNotFoundException)
        {
            return Invalid(StatementImportMappingErrorCodes.RowMappingInvalid);
        }
    }

    public string CreateCanonicalMappingJson(StatementImportMappingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteNumber("dateColumn", definition.DateColumn);
            writer.WriteNumber("descriptionColumn", definition.DescriptionColumn);
            WriteNullableNumber(writer, "amountColumn", definition.AmountColumn);
            WriteNullableNumber(writer, "debitColumn", definition.DebitColumn);
            WriteNullableNumber(writer, "creditColumn", definition.CreditColumn);
            WriteNullableNumber(writer, "currencyColumn", definition.CurrencyColumn);
            WriteNullableNumber(writer, "referenceColumn", definition.ReferenceColumn);
            writer.WriteString("dateFormat", definition.DateFormat?.Trim() ?? string.Empty);
            writer.WriteString("dateValueKind", NormalizeToken(definition.DateValueKind));
            writer.WriteString("amountMode", NormalizeToken(definition.AmountMode));
            writer.WriteString("amountSign", NormalizeToken(definition.AmountSign));
            writer.WriteString("locale", NormalizeLocaleForCanonicalJson(definition.Locale));
            writer.WriteString("timeZoneId", definition.TimeZoneId?.Trim() ?? string.Empty);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static StatementImportMappedRow CreateMappedRow(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition,
        string? sourceReferenceFingerprint,
        string validationStatus,
        string? validationCode,
        string evidenceJson,
        DateOnly? effectiveDate,
        DateTime? effectiveAtUtc,
        string? timestampPrecision,
        string? description,
        decimal? amount,
        string? currency)
    {
        var rowFingerprint = CreateRowFingerprint(
            row,
            definition,
            sourceReferenceFingerprint,
            validationStatus,
            validationCode,
            effectiveDate,
            effectiveAtUtc,
            timestampPrecision,
            description,
            amount,
            currency);

        return new StatementImportMappedRow(
            row.RowNumber,
            rowFingerprint,
            sourceReferenceFingerprint,
            validationStatus,
            validationCode,
            evidenceJson,
            effectiveDate,
            effectiveAtUtc,
            timestampPrecision,
            description,
            amount,
            currency);
    }

    private static string? TryMapAmount(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition,
        CultureInfo culture,
        out decimal amount)
    {
        amount = default;
        string sourceValue;
        var forceDebit = false;
        var forceCredit = false;

        if (TokenEquals(definition.AmountMode, StatementImportAmountModes.Signed))
        {
            sourceValue = row.Fields[definition.AmountColumn!.Value];
            if (string.IsNullOrWhiteSpace(sourceValue))
            {
                return StatementImportMappingErrorCodes.RowAmountRequired;
            }
        }
        else
        {
            var debitValue = row.Fields[definition.DebitColumn!.Value];
            var creditValue = row.Fields[definition.CreditColumn!.Value];
            var hasDebit = !string.IsNullOrWhiteSpace(debitValue);
            var hasCredit = !string.IsNullOrWhiteSpace(creditValue);

            if (hasDebit == hasCredit)
            {
                return StatementImportMappingErrorCodes.RowDebitCreditShapeInvalid;
            }

            sourceValue = hasDebit ? debitValue : creditValue;
            forceDebit = hasDebit;
            forceCredit = hasCredit;
        }

        if (!TryParseStrictDecimal(sourceValue, culture, out var parsedAmount))
        {
            return StatementImportMappingErrorCodes.RowAmountInvalid;
        }

        if (decimal.Round(parsedAmount, 2, MidpointRounding.ToEven) != parsedAmount)
        {
            return StatementImportMappingErrorCodes.RowAmountPrecisionInvalid;
        }

        if (parsedAmount > MaximumStoredAmount || parsedAmount < -MaximumStoredAmount)
        {
            return StatementImportMappingErrorCodes.RowAmountOutOfRange;
        }

        if (forceDebit)
        {
            amount = -decimal.Abs(parsedAmount);
        }
        else if (forceCredit)
        {
            amount = decimal.Abs(parsedAmount);
        }
        else
        {
            amount = parsedAmount;
        }

        if (TokenEquals(definition.AmountSign, StatementImportAmountSigns.Invert))
        {
            amount = -amount;
        }

        return null;
    }

    private static bool TryParseStrictDecimal(
        string source,
        CultureInfo culture,
        out decimal amount)
    {
        amount = default;
        var value = source.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        var numberFormat = culture.NumberFormat;
        value = RemoveLeadingSign(value, numberFormat, out var signIsValid);
        if (!signIsValid || value.Length == 0)
        {
            return false;
        }

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var decimalParts = SplitExact(value, decimalSeparator);
        if (decimalParts is null || decimalParts.Count > 2)
        {
            return false;
        }

        var integerPart = decimalParts[0];
        var fractionalPart = decimalParts.Count == 2 ? decimalParts[1] : null;
        if (!HasValidIntegerGrouping(
                integerPart,
                numberFormat.NumberGroupSeparator,
                numberFormat.NumberGroupSizes))
        {
            return false;
        }

        if (fractionalPart is not null
            && (fractionalPart.Length == 0 || !fractionalPart.All(IsAsciiDigit)))
        {
            return false;
        }

        return decimal.TryParse(
            source.Trim(),
            NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowThousands,
            culture,
            out amount);
    }

    private static string RemoveLeadingSign(
        string value,
        NumberFormatInfo numberFormat,
        out bool isValid)
    {
        isValid = true;
        var negativeSign = numberFormat.NegativeSign;
        var positiveSign = numberFormat.PositiveSign;

        if (negativeSign.Length > 0
            && value.StartsWith(negativeSign, StringComparison.Ordinal))
        {
            value = value[negativeSign.Length..];
        }
        else if (positiveSign.Length > 0
                 && value.StartsWith(positiveSign, StringComparison.Ordinal))
        {
            value = value[positiveSign.Length..];
        }

        if ((negativeSign.Length > 0 && value.Contains(negativeSign, StringComparison.Ordinal))
            || (positiveSign.Length > 0 && value.Contains(positiveSign, StringComparison.Ordinal)))
        {
            isValid = false;
        }

        return value;
    }

    private static IReadOnlyList<string>? SplitExact(string value, string separator)
    {
        if (separator.Length == 0)
        {
            return [value];
        }

        return value.Split(separator, StringSplitOptions.None);
    }

    private static bool HasValidIntegerGrouping(
        string integerPart,
        string groupSeparator,
        int[] groupSizes)
    {
        if (integerPart.Length == 0)
        {
            return false;
        }

        if (groupSeparator.Length == 0
            || !integerPart.Contains(groupSeparator, StringComparison.Ordinal))
        {
            return integerPart.All(IsAsciiDigit);
        }

        var groups = integerPart.Split(groupSeparator, StringSplitOptions.None);
        if (groups.Any(group => group.Length == 0 || !group.All(IsAsciiDigit)))
        {
            return false;
        }

        var usableSizes = groupSizes.TakeWhile(size => size > 0).ToArray();
        if (usableSizes.Length == 0)
        {
            return false;
        }

        var sizeIndex = 0;
        for (var groupIndex = groups.Length - 1; groupIndex > 0; groupIndex--)
        {
            var expectedSize = usableSizes[Math.Min(sizeIndex, usableSizes.Length - 1)];
            if (groups[groupIndex].Length != expectedSize)
            {
                return false;
            }

            if (sizeIndex < usableSizes.Length - 1)
            {
                sizeIndex++;
            }
        }

        var leftmostMaximum = usableSizes[Math.Min(sizeIndex, usableSizes.Length - 1)];
        return groups[0].Length >= 1 && groups[0].Length <= leftmostMaximum;
    }

    private static string? TryParseInstant(
        string value,
        string format,
        CultureInfo culture,
        TimeZoneInfo timeZone,
        bool formatContainsOffset,
        out DateTime instantUtc)
    {
        instantUtc = default;

        if (formatContainsOffset)
        {
            if (!HasExplicitOffsetValue(value))
            {
                return StatementImportMappingErrorCodes.RowInstantOffsetRequired;
            }

            if (!DateTimeOffset.TryParseExact(
                    value,
                    format,
                    culture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var offsetValue))
            {
                return StatementImportMappingErrorCodes.RowInstantInvalid;
            }

            instantUtc = offsetValue.UtcDateTime;
            return null;
        }

        if (!DateTime.TryParseExact(
                value,
                format,
                culture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localValue))
        {
            return StatementImportMappingErrorCodes.RowInstantInvalid;
        }

        localValue = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localValue))
        {
            return StatementImportMappingErrorCodes.RowInstantInvalidLocalTime;
        }

        if (timeZone.IsAmbiguousTime(localValue))
        {
            return StatementImportMappingErrorCodes.RowInstantAmbiguousLocalTime;
        }

        instantUtc = TimeZoneInfo.ConvertTimeToUtc(localValue, timeZone);
        return null;
    }

    private static bool TryCreateRuntime(
        StatementImportMappingDefinition definition,
        out MappingRuntime? runtime)
    {
        runtime = null;
        if (definition is null
            || !IsKnownDateValueKind(definition.DateValueKind)
            || !IsKnownAmountMode(definition.AmountMode)
            || !IsKnownAmountSign(definition.AmountSign)
            || !HasValidAmountShape(definition)
            || GetSelectedColumns(definition).Any(index => index < 0)
            || GetSelectedColumns(definition).Distinct().Count() != GetSelectedColumns(definition).Count
            || !TryGetSpecificCulture(definition.Locale, out var culture)
            || !TryGetTimeZone(definition.TimeZoneId, out var timeZone)
            || !IsValidDateFormat(definition.DateFormat, definition.DateValueKind, culture!))
        {
            return false;
        }

        runtime = new MappingRuntime(
            culture!,
            timeZone!,
            FormatContainsOffset(definition.DateFormat.Trim()));
        return true;
    }

    private static bool HasCanonicalColumnSchema(IReadOnlyList<StatementCsvColumn> columns)
    {
        var indexes = columns.Select(column => column.Index).ToArray();
        return indexes.Distinct().Count() == columns.Count
               && indexes.Order().SequenceEqual(Enumerable.Range(0, columns.Count));
    }

    private static List<int> GetSelectedColumns(StatementImportMappingDefinition definition)
    {
        var columns = new List<int>
        {
            definition.DateColumn,
            definition.DescriptionColumn
        };

        AddIfPresent(columns, definition.AmountColumn);
        AddIfPresent(columns, definition.DebitColumn);
        AddIfPresent(columns, definition.CreditColumn);
        AddIfPresent(columns, definition.CurrencyColumn);
        AddIfPresent(columns, definition.ReferenceColumn);
        return columns;
    }

    private static void AddIfPresent(List<int> columns, int? column)
    {
        if (column is int value)
        {
            columns.Add(value);
        }
    }

    private static bool HasValidAmountShape(StatementImportMappingDefinition definition) =>
        TokenEquals(definition.AmountMode, StatementImportAmountModes.Signed)
            ? definition.AmountColumn is not null
              && definition.DebitColumn is null
              && definition.CreditColumn is null
            : TokenEquals(definition.AmountMode, StatementImportAmountModes.DebitCredit)
              && definition.AmountColumn is null
              && definition.DebitColumn is not null
              && definition.CreditColumn is not null;

    private static bool IsKnownDateValueKind(string value) =>
        TokenEquals(value, StatementImportDateValueKinds.Date)
        || TokenEquals(value, StatementImportDateValueKinds.Instant);

    private static bool IsKnownAmountMode(string value) =>
        TokenEquals(value, StatementImportAmountModes.Signed)
        || TokenEquals(value, StatementImportAmountModes.DebitCredit);

    private static bool IsKnownAmountSign(string value) =>
        TokenEquals(value, StatementImportAmountSigns.AsIs)
        || TokenEquals(value, StatementImportAmountSigns.Invert);

    private static bool TokenEquals(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSpecificCulture(string? locale, out CultureInfo? culture)
    {
        culture = null;
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        try
        {
            culture = CultureInfo.GetCultureInfo(locale.Trim());
            return !culture.Equals(CultureInfo.InvariantCulture) && !culture.IsNeutralCulture;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetTimeZone(string? timeZoneId, out TimeZoneInfo? timeZone)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
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

    private static bool IsValidDateFormat(
        string? format,
        string dateValueKind,
        CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        try
        {
            var normalizedFormat = format.Trim();
            if (TokenEquals(dateValueKind, StatementImportDateValueKinds.Date))
            {
                var sample = new DateOnly(2004, 11, 23);
                var formatted = sample.ToString(normalizedFormat, culture);
                return DateOnly.TryParseExact(
                           formatted,
                           normalizedFormat,
                           culture,
                           DateTimeStyles.AllowWhiteSpaces,
                           out var parsed)
                       && parsed == sample;
            }

            var instantSample = new DateTime(2004, 11, 23, 14, 37, 41, DateTimeKind.Unspecified);
            var instantText = instantSample.ToString(normalizedFormat, culture);
            return DateTime.TryParseExact(
                       instantText,
                       normalizedFormat,
                       culture,
                       DateTimeStyles.AllowWhiteSpaces,
                       out var parsedInstant)
                   && parsedInstant.Year == instantSample.Year
                   && parsedInstant.Month == instantSample.Month
                   && parsedInstant.Day == instantSample.Day
                   && parsedInstant.Hour == instantSample.Hour
                   && parsedInstant.Minute == instantSample.Minute;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FormatContainsOffset(string format)
    {
        if (format.Length == 1
            && format[0] is 'O' or 'o' or 'R' or 'r' or 'u')
        {
            return true;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;
        foreach (var character in format)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (character == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && character is 'z' or 'K')
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeDescription(string source)
    {
        var normalized = source.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var pendingWhitespace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingWhitespace = builder.Length > 0;
                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append(' ');
                pendingWhitespace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string NormalizeReference(string source) =>
        NormalizeDescription(source).ToUpperInvariant();

    private static string NormalizeSourceScalar(string source) =>
        source.Trim().Normalize(NormalizationForm.FormKC);

    private static string? NormalizeCurrency(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var currency = source.Trim().ToUpperInvariant();
        return currency.Length == 3 && currency.All(character => character is >= 'A' and <= 'Z')
            ? currency
            : null;
    }

    private static string? CreateSourceReferenceFingerprint(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition)
    {
        if (definition?.ReferenceColumn is not int referenceColumn
            || referenceColumn < 0
            || referenceColumn >= row.Fields.Length)
        {
            return null;
        }

        var reference = NormalizeReference(row.Fields[referenceColumn]);
        return reference.Length == 0
            ? null
            : Sha256Hex(CreateLengthPrefixedPayload(
                "statement-import-reference-v1",
                reference));
    }

    private static string CreateRowFingerprint(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition,
        string? sourceReferenceFingerprint,
        string validationStatus,
        string? validationCode,
        DateOnly? effectiveDate,
        DateTime? effectiveAtUtc,
        string? timestampPrecision,
        string? description,
        decimal? amount,
        string? currency)
    {
        var dateValue = effectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        ?? effectiveAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var amountValue = amount?.ToString("G29", CultureInfo.InvariantCulture);
        var fallbackSource = validationStatus == StatementImportValidationStatuses.Invalid
            ? CreateCanonicalSelectedSourcePayload(row, definition)
            : null;

        return Sha256Hex(CreateLengthPrefixedPayload(
            "statement-import-row-v1",
            NormalizeToken(definition?.DateValueKind),
            timestampPrecision,
            dateValue,
            description,
            amountValue,
            currency,
            sourceReferenceFingerprint,
            validationStatus,
            validationCode,
            fallbackSource));
    }

    private static string CreateCanonicalSelectedSourcePayload(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition)
    {
        var values = new List<string?> { "statement-import-invalid-source-v1" };
        foreach (var column in GetSelectedColumns(definition))
        {
            values.Add(column >= 0 && column < row.Fields.Length
                ? row.Fields[column]
                : null);
        }

        return CreateLengthPrefixedPayload(values.ToArray());
    }

    private static string CreateLengthPrefixedPayload(params string?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (value is null)
            {
                builder.Append("-1:");
                continue;
            }

            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }

        return builder.ToString();
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string BuildEvidenceJson(
        StatementCsvDataRow row,
        StatementImportMappingDefinition definition)
    {
        if (row is null || definition is null)
        {
            return "{\"version\":1}";
        }

        var evidenceFields = new List<EvidenceField>();
        AddEvidenceField(evidenceFields, row, "date", definition.DateColumn);
        AddEvidenceField(evidenceFields, row, "description", definition.DescriptionColumn);

        if (definition.AmountColumn is int amountColumn)
        {
            AddEvidenceField(evidenceFields, row, "amount", amountColumn);
        }

        if (definition.DebitColumn is int debitColumn)
        {
            AddEvidenceField(evidenceFields, row, "debit", debitColumn);
        }

        if (definition.CreditColumn is int creditColumn)
        {
            AddEvidenceField(evidenceFields, row, "credit", creditColumn);
        }

        if (definition.CurrencyColumn is int currencyColumn)
        {
            AddEvidenceField(evidenceFields, row, "currency", currencyColumn);
        }

        if (definition.ReferenceColumn is int referenceColumn)
        {
            AddEvidenceField(evidenceFields, row, "reference", referenceColumn);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            foreach (var field in evidenceFields)
            {
                writer.WriteString(field.Name, field.Value);
            }

            var truncated = evidenceFields.Where(field => field.WasTruncated).ToArray();
            if (truncated.Length > 0)
            {
                writer.WriteStartArray("truncatedFields");
                foreach (var field in truncated)
                {
                    writer.WriteStringValue(field.Name);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool HasExplicitOffsetValue(string source)
    {
        var value = source.Trim();
        if (value.EndsWith('Z')
            || value.EndsWith("GMT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var plusIndex = value.LastIndexOf('+');
        var minusIndex = value.LastIndexOf('-');
        var signIndex = Math.Max(plusIndex, minusIndex);
        if (signIndex < 0 || signIndex == value.Length - 1)
        {
            return false;
        }

        var offset = value[(signIndex + 1)..];
        var offsetParts = offset.Split(':', StringSplitOptions.None);
        if (offsetParts.Length is < 1 or > 2
            || offsetParts[0].Length is < 1 or > 2
            || !offsetParts[0].All(IsAsciiDigit)
            || (offsetParts.Length == 2
                && (offsetParts[1].Length != 2 || !offsetParts[1].All(IsAsciiDigit))))
        {
            return false;
        }

        if (!int.TryParse(offsetParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || hours > 14)
        {
            return false;
        }

        var minutes = 0;
        if (offsetParts.Length == 2
            && !int.TryParse(offsetParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return false;
        }

        return minutes < 60 && (hours < 14 || minutes == 0);
    }

    private static void AddEvidenceField(
        List<EvidenceField> fields,
        StatementCsvDataRow row,
        string name,
        int column)
    {
        if (column < 0 || column >= row.Fields.Length)
        {
            return;
        }

        var bounded = BoundEvidenceValue(row.Fields[column], out var wasTruncated);
        fields.Add(new EvidenceField(name, bounded, wasTruncated));
    }

    private static string BoundEvidenceValue(string source, out bool wasTruncated)
    {
        var builder = new StringBuilder();
        var runeCount = 0;
        wasTruncated = false;

        foreach (var rune in source.EnumerateRunes())
        {
            if (runeCount == MaximumEvidenceValueRunes)
            {
                wasTruncated = true;
                break;
            }

            builder.Append(rune.ToString());
            runeCount++;
        }

        return builder.ToString();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is int number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string NormalizeToken(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeLocaleForCanonicalJson(string? locale)
    {
        if (!TryGetSpecificCulture(locale, out var culture))
        {
            return locale?.Trim() ?? string.Empty;
        }

        return culture!.Name;
    }

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';

    private static ServiceError InvalidDefinition(string message, string code) =>
        new(message, code, InvalidDefinitionStatusCode);

    private sealed record MappingRuntime(
        CultureInfo Culture,
        TimeZoneInfo TimeZone,
        bool FormatContainsOffset);

    private sealed record EvidenceField(
        string Name,
        string Value,
        bool WasTruncated);
}

internal static class StatementImportMappingErrorCodes
{
    public const string DefinitionRequired = "statement_import_mapping_required";
    public const string ColumnsRequired = "statement_import_mapping_columns_required";
    public const string ColumnSchemaInvalid = "statement_import_mapping_column_schema_invalid";
    public const string ColumnOutOfRange = "statement_import_mapping_column_out_of_range";
    public const string ColumnReused = "statement_import_mapping_column_reused";
    public const string DateValueKindInvalid = "statement_import_mapping_date_kind_invalid";
    public const string AmountModeInvalid = "statement_import_mapping_amount_mode_invalid";
    public const string AmountSignInvalid = "statement_import_mapping_amount_sign_invalid";
    public const string AmountShapeInvalid = "statement_import_mapping_amount_shape_invalid";
    public const string LocaleInvalid = "statement_import_mapping_locale_invalid";
    public const string TimeZoneInvalid = "statement_import_mapping_timezone_invalid";
    public const string DateFormatInvalid = "statement_import_mapping_date_format_invalid";

    public const string RowMappingInvalid = "statement_import_row_mapping_invalid";
    public const string RowColumnMissing = "statement_import_row_column_missing";
    public const string RowAccountCurrencyInvalid = "statement_import_row_account_currency_invalid";
    public const string RowDateRequired = "statement_import_row_date_required";
    public const string RowDateInvalid = "statement_import_row_date_invalid";
    public const string RowInstantInvalid = "statement_import_row_instant_invalid";
    public const string RowInstantOffsetRequired = "statement_import_row_instant_offset_required";
    public const string RowInstantInvalidLocalTime = "statement_import_row_instant_invalid_local_time";
    public const string RowInstantAmbiguousLocalTime = "statement_import_row_instant_ambiguous_local_time";
    public const string RowDescriptionRequired = "statement_import_row_description_required";
    public const string RowDescriptionTooLong = "statement_import_row_description_too_long";
    public const string RowAmountRequired = "statement_import_row_amount_required";
    public const string RowDebitCreditShapeInvalid = "statement_import_row_debit_credit_shape_invalid";
    public const string RowAmountInvalid = "statement_import_row_amount_invalid";
    public const string RowAmountPrecisionInvalid = "statement_import_row_amount_precision_invalid";
    public const string RowAmountOutOfRange = "statement_import_row_amount_out_of_range";
    public const string RowSourceCurrencyInvalid = "statement_import_row_source_currency_invalid";
    public const string RowCurrencyMismatch = "statement_import_row_currency_mismatch";
}
