using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace NSFinance.Api.Modules.Imports.Parsing;

internal sealed class StatementCsvParser : IStatementCsvParser
{
    public const string Version = "statement-csv-v1";
    public const long MaximumSourceBytes = 5L * 1024L * 1024L;
    public const int MaximumDataRows = 5_000;
    public const int MaximumColumns = 50;
    public const int MaximumFieldCharacters = 1_024;

    private const int PrefixLength = 4;
    private const int ReaderBufferSize = 4_096;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly HashSet<string> SupportedDelimiters =
    [
        ",",
        ";",
        "\t",
        "|"
    ];

    public async Task<StatementCsvParseResult> ParseAsync(
        Stream source,
        StatementCsvParserOptions options,
        CancellationToken cancellationToken = default) =>
        await ParseCoreAsync(source, options, rowHandler: null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<StatementCsvParseResult> ParseRowsAsync(
        Stream source,
        StatementCsvParserOptions options,
        Func<StatementCsvDataRow, CancellationToken, ValueTask> rowHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rowHandler);
        return await ParseCoreAsync(source, options, rowHandler, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<StatementCsvParseResult> ParseCoreAsync(
        Stream source,
        StatementCsvParserOptions options,
        Func<StatementCsvDataRow, CancellationToken, ValueTask>? rowHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        if (!source.CanRead)
        {
            throw StatementCsvParserException.InvalidOptions();
        }

        ValidateOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        using var tracker = new StatementCsvSourceTracker(MaximumSourceBytes);
        var prefix = new byte[PrefixLength];
        var prefixCount = await ReadPrefixAsync(source, prefix, cancellationToken).ConfigureAwait(false);
        tracker.Record(prefix.AsSpan(0, prefixCount), rejectNul: false);

        var contentOffset = ValidateEncodingPrefix(prefix.AsSpan(0, prefixCount));
        var replayPrefix = prefix.AsMemory(contentOffset, prefixCount - contentOffset);

        using var trackedSource = new StatementCsvTrackedReadStream(
            source,
            tracker,
            cancellationToken);
        using var replaySource = new StatementCsvPrefixReadStream(
            replayPrefix,
            trackedSource,
            cancellationToken);
        using var textReader = new StreamReader(
            replaySource,
            StrictUtf8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: ReaderBufferSize,
            leaveOpen: true);
        using var csv = new CsvReader(textReader, CreateConfiguration(options.Delimiter));

        try
        {
            if (!await ReadAsync(csv, cancellationToken).ConfigureAwait(false))
            {
                throw StatementCsvParserException.EmptyFile();
            }

            var header = CopyCurrentRecord(csv);
            var columns = ValidateAndCreateColumns(header);
            var samples = ImmutableArray.CreateBuilder<StatementCsvSampleRow>(options.SampleRowLimit);
            var dataRowCount = 0;

            while (await ReadAsync(csv, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                dataRowCount++;

                if (dataRowCount > MaximumDataRows)
                {
                    throw StatementCsvParserException.TooManyRows();
                }

                var fields = CopyCurrentRecord(csv);
                if (fields.Length != columns.Length)
                {
                    throw StatementCsvParserException.ColumnCountMismatch();
                }

                ValidateFieldLengths(fields);

                ImmutableArray<string>? immutableFields = null;
                if (rowHandler is not null)
                {
                    immutableFields = fields.ToImmutableArray();
                    await rowHandler(
                            new StatementCsvDataRow(dataRowCount, immutableFields.Value),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (samples.Count < options.SampleRowLimit)
                {
                    samples.Add(new StatementCsvSampleRow(
                        dataRowCount,
                        immutableFields ?? fields.ToImmutableArray()));
                }
            }

            if (dataRowCount == 0)
            {
                throw StatementCsvParserException.EmptyData();
            }

            return new StatementCsvParseResult(
                Version,
                options.Delimiter,
                tracker.BytesRead,
                tracker.CompleteHash(),
                dataRowCount,
                columns,
                samples.ToImmutable());
        }
        catch (StatementCsvParserException)
        {
            throw;
        }
        catch (DecoderFallbackException)
        {
            throw StatementCsvParserException.InvalidUtf8();
        }
        catch (CsvHelperException exception) when (ContainsDecoderFailure(exception))
        {
            throw StatementCsvParserException.InvalidUtf8();
        }
        catch (CsvHelperException exception) when (IsFieldLimitFailure(exception))
        {
            throw StatementCsvParserException.FieldTooLong();
        }
        catch (CsvHelperException)
        {
            throw StatementCsvParserException.MalformedDocument();
        }
    }

    private static CsvConfiguration CreateConfiguration(string delimiter) =>
        new(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            IgnoreBlankLines = true,
            DetectColumnCountChanges = false,
            Mode = CsvMode.RFC4180,
            MaxFieldSize = MaximumFieldCharacters,
            ExceptionMessagesContainRawData = false,
            BadDataFound = _ => throw StatementCsvParserException.MalformedDocument(),
            MissingFieldFound = null,
            HeaderValidated = null
        };

    private static void ValidateOptions(StatementCsvParserOptions options)
    {
        if (options.SampleRowLimit < 0 ||
            options.SampleRowLimit > StatementCsvParserOptions.MaximumSampleRowLimit)
        {
            throw StatementCsvParserException.InvalidOptions();
        }

        if (!SupportedDelimiters.Contains(options.Delimiter))
        {
            throw StatementCsvParserException.UnsupportedDelimiter();
        }
    }

    private static int ValidateEncodingPrefix(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 3 &&
            prefix[0] == 0xEF &&
            prefix[1] == 0xBB &&
            prefix[2] == 0xBF)
        {
            RejectNul(prefix[3..]);
            return 3;
        }

        var hasUtf16Bom = prefix.Length >= 2 &&
            ((prefix[0] == 0xFF && prefix[1] == 0xFE) ||
             (prefix[0] == 0xFE && prefix[1] == 0xFF));
        var hasUtf32BigEndianBom = prefix.Length >= 4 &&
            prefix[0] == 0x00 &&
            prefix[1] == 0x00 &&
            prefix[2] == 0xFE &&
            prefix[3] == 0xFF;

        if (hasUtf16Bom || hasUtf32BigEndianBom)
        {
            throw StatementCsvParserException.UnsupportedEncoding();
        }

        RejectNul(prefix);
        return 0;
    }

    private static void RejectNul(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOf((byte)0) >= 0)
        {
            throw StatementCsvParserException.BinaryContent();
        }
    }

    private static ImmutableArray<StatementCsvColumn> ValidateAndCreateColumns(string[] header)
    {
        if (header.Length == 0)
        {
            throw StatementCsvParserException.BlankHeader();
        }

        if (header.Length > MaximumColumns)
        {
            throw StatementCsvParserException.TooManyColumns();
        }

        ValidateFieldLengths(header);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = ImmutableArray.CreateBuilder<StatementCsvColumn>(header.Length);

        for (var index = 0; index < header.Length; index++)
        {
            var name = header[index].Trim();
            if (name.Length == 0)
            {
                throw StatementCsvParserException.BlankHeader();
            }

            if (!names.Add(name))
            {
                throw StatementCsvParserException.DuplicateHeader();
            }

            columns.Add(new StatementCsvColumn(index, name));
        }

        return columns.MoveToImmutable();
    }

    private static void ValidateFieldLengths(IEnumerable<string> fields)
    {
        if (fields.Any(field => field.Length > MaximumFieldCharacters))
        {
            throw StatementCsvParserException.FieldTooLong();
        }
    }

    private static string[] CopyCurrentRecord(CsvReader csv)
    {
        var record = csv.Parser.Record;
        return record is null ? [] : (string[])record.Clone();
    }

    private static async Task<bool> ReadAsync(
        CsvReader csv,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await csv.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadPrefixAsync(
        Stream source,
        byte[] prefix,
        CancellationToken cancellationToken)
    {
        var count = 0;
        while (count < prefix.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source
                .ReadAsync(prefix.AsMemory(count, prefix.Length - count), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count;
    }

    private static bool ContainsDecoderFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DecoderFallbackException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFieldLimitFailure(CsvHelperException exception) =>
        exception.GetType().Name.Contains("MaxFieldSize", StringComparison.Ordinal);
}
