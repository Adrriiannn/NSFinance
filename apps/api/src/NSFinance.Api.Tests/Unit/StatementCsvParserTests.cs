using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NSFinance.Api.Modules.Imports.Parsing;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementCsvParserTests
{
    private readonly StatementCsvParser parser = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParseAsync_ValidUtf8WithOptionalBom_ReturnsIndexedBoundedResult(bool includeBom)
    {
        var bytes = Encode("Date,Description,Amount\n2026-07-01,Coffee,-4.50\n2026-07-02,Salary,1000.00\n", includeBom);

        var result = await ParseAsync(bytes, new StatementCsvParserOptions(SampleRowLimit: 1));

        Assert.Equal(StatementCsvParser.Version, result.ParserVersion);
        Assert.Equal(",", result.Delimiter);
        Assert.Equal(bytes.Length, result.SourceByteCount);
        Assert.Equal(2, result.DataRowCount);
        Assert.Equal(["Date", "Description", "Amount"], result.Columns.Select(column => column.Name));
        Assert.Equal([0, 1, 2], result.Columns.Select(column => column.Index));
        var sample = Assert.Single(result.SampleRows);
        Assert.Equal(1, sample.RowNumber);
        Assert.Equal(["2026-07-01", "Coffee", "-4.50"], sample.Fields.ToArray());
    }

    [Fact]
    public async Task ParseAsync_HashesExactSourceBytes_ProducesStableReplayFingerprint()
    {
        var bytes = Encode("Date,Amount\r\n2026-07-01,12.34\r\n", includeBom: true);
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var first = await ParseAsync(bytes);
        var replay = await ParseAsync(bytes);
        var changed = await ParseAsync(Encode("Date,Amount\n2026-07-01,12.34\n", includeBom: true));

        Assert.Equal(expected, first.SourceSha256);
        Assert.Equal(first.SourceSha256, replay.SourceSha256);
        Assert.NotEqual(first.SourceSha256, changed.SourceSha256);
    }

    [Fact]
    public async Task ParseRowsAsync_StreamsEveryValidatedRowWithoutExpandingSamples()
    {
        var streamedRows = new List<StatementCsvDataRow>();
        await using var source = new MemoryStream(Encode(
            "Date,Amount\n2026-07-01,1.00\n2026-07-02,2.00\n2026-07-03,3.00\n",
            includeBom: false));

        var result = await parser.ParseRowsAsync(
            source,
            new StatementCsvParserOptions(SampleRowLimit: 1),
            (row, _) =>
            {
                streamedRows.Add(row);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(3, result.DataRowCount);
        Assert.Single(result.SampleRows);
        Assert.Equal([1, 2, 3], streamedRows.Select(row => row.RowNumber));
    }

    [Theory]
    [InlineData("Date,Amount\n\"2026-07-01,12.34\n", StatementCsvParserErrorCodes.MalformedDocument, 400)]
    [InlineData("Date,Amount\n2026-07-01\n", StatementCsvParserErrorCodes.ColumnCountMismatch, 400)]
    [InlineData("Date, date\n2026-07-01,2026-07-01\n", StatementCsvParserErrorCodes.DuplicateHeader, 400)]
    [InlineData("Date,   \n2026-07-01,12.34\n", StatementCsvParserErrorCodes.BlankHeader, 400)]
    [InlineData("Date,Amount\n", StatementCsvParserErrorCodes.EmptyData, 400)]
    public async Task ParseAsync_MalformedStructure_ReturnsStableClientError(
        string csv,
        string expectedCode,
        int expectedStatusCode)
    {
        var exception = await ParseFailureAsync(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(StatementCsvParserFailureKind.InvalidDocument, exception.FailureKind);
        Assert.Equal(expectedStatusCode, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_EmptySource_ReturnsStableClientError()
    {
        var exception = await ParseFailureAsync([]);

        Assert.Equal(StatementCsvParserErrorCodes.EmptyFile, exception.Code);
        Assert.Equal(400, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_InvalidUtf8_ReturnsUnsupportedContentWithoutEchoingSource()
    {
        var bytes = Encoding.UTF8.GetBytes("Date,Description\n2026-07-01,")
            .Concat(new byte[] { 0xC3, 0x28 })
            .ToArray();

        var exception = await ParseFailureAsync(bytes);

        Assert.Equal(StatementCsvParserErrorCodes.InvalidUtf8, exception.Code);
        Assert.Equal(StatementCsvParserFailureKind.UnsupportedContent, exception.FailureKind);
        Assert.Equal(415, exception.RecommendedStatusCode);
        Assert.DoesNotContain("2026-07-01", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_Utf16Bom_ReturnsUnsupportedEncoding()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("Date,Amount\r\n2026-07-01,1.00\r\n"))
            .ToArray();

        var exception = await ParseFailureAsync(bytes);

        Assert.Equal(StatementCsvParserErrorCodes.UnsupportedEncoding, exception.Code);
        Assert.Equal(415, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_NulContent_ReturnsBinaryContent()
    {
        var bytes = Encoding.UTF8.GetBytes("Date,Description\n2026-07-01,abc\0def\n");

        var exception = await ParseFailureAsync(bytes);

        Assert.Equal(StatementCsvParserErrorCodes.BinaryContent, exception.Code);
        Assert.Equal(415, exception.RecommendedStatusCode);
    }

    [Theory]
    [InlineData("::")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task ParseAsync_UnsupportedDelimiter_ReturnsStableClientError(string delimiter)
    {
        var exception = await ParseFailureAsync(
            Encoding.UTF8.GetBytes("Date,Amount\n2026-07-01,1.00\n"),
            new StatementCsvParserOptions(delimiter));

        Assert.Equal(StatementCsvParserErrorCodes.UnsupportedDelimiter, exception.Code);
        Assert.Equal(400, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_ColumnLimit_AcceptsFiftyAndRejectsFiftyOne()
    {
        var accepted = BuildColumnCsv(StatementCsvParser.MaximumColumns);
        var rejected = BuildColumnCsv(StatementCsvParser.MaximumColumns + 1);

        var result = await ParseAsync(accepted);
        var exception = await ParseFailureAsync(rejected);

        Assert.Equal(StatementCsvParser.MaximumColumns, result.Columns.Length);
        Assert.Equal(StatementCsvParserErrorCodes.TooManyColumns, exception.Code);
        Assert.Equal(413, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_FieldLimit_AcceptsBoundaryAndRejectsDataAndHeaderOverflow()
    {
        var boundary = new string('x', StatementCsvParser.MaximumFieldCharacters);
        var overflow = boundary + "x";

        var accepted = await ParseAsync(Encoding.UTF8.GetBytes($"Value\n{boundary}\n"));
        var dataException = await ParseFailureAsync(Encoding.UTF8.GetBytes($"Value\n{overflow}\n"));
        var headerException = await ParseFailureAsync(Encoding.UTF8.GetBytes($"{overflow}\nvalue\n"));

        Assert.Equal(boundary, Assert.Single(accepted.SampleRows).Fields[0]);
        Assert.Equal(StatementCsvParserErrorCodes.FieldTooLong, dataException.Code);
        Assert.Equal(StatementCsvParserErrorCodes.FieldTooLong, headerException.Code);
        Assert.Equal(413, dataException.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_RowLimit_AcceptsFiveThousandAndRejectsNextRow()
    {
        var accepted = BuildRowCsv(StatementCsvParser.MaximumDataRows);
        var rejected = BuildRowCsv(StatementCsvParser.MaximumDataRows + 1);

        var result = await ParseAsync(accepted, new StatementCsvParserOptions(SampleRowLimit: 0));
        var exception = await ParseFailureAsync(rejected);

        Assert.Equal(StatementCsvParser.MaximumDataRows, result.DataRowCount);
        Assert.Empty(result.SampleRows);
        Assert.Equal(StatementCsvParserErrorCodes.TooManyRows, exception.Code);
        Assert.Equal(413, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_ByteLimit_AcceptsExactFiveMiBAndRejectsOneMoreByte()
    {
        var exact = BuildCsvAtExactByteLimit();
        var overflow = new byte[exact.Length + 1];
        exact.CopyTo(overflow, 0);
        overflow[^1] = (byte)'\n';

        var result = await ParseAsync(exact, new StatementCsvParserOptions(SampleRowLimit: 0));
        var exception = await ParseFailureAsync(
            overflow,
            new StatementCsvParserOptions(SampleRowLimit: 0));

        Assert.Equal(StatementCsvParser.MaximumSourceBytes, result.SourceByteCount);
        Assert.Equal(StatementCsvParserErrorCodes.FileTooLarge, exception.Code);
        Assert.Equal(413, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_SampleRowsRemainStrictlyBoundedAndDoNotRetainLaterValues()
    {
        const string valueOutsideSample = "must-not-survive-parser-boundary";
        var bytes = Encoding.UTF8.GetBytes(
            $"Date,Description\n2026-07-01,first\n2026-07-02,{valueOutsideSample}\n2026-07-03,last\n");

        var result = await ParseAsync(bytes, new StatementCsvParserOptions(SampleRowLimit: 1));
        var retainedFields = result.SampleRows.SelectMany(row => row.Fields).ToArray();
        var instanceFields = typeof(StatementCsvParser).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Equal(3, result.DataRowCount);
        Assert.Single(result.SampleRows);
        Assert.DoesNotContain(retainedFields, value => value.Contains(valueOutsideSample, StringComparison.Ordinal));
        Assert.Empty(instanceFields);
    }

    [Fact]
    public async Task ParseAsync_SampleLimitAboveBound_ReturnsInvalidOptions()
    {
        var options = new StatementCsvParserOptions(
            SampleRowLimit: StatementCsvParserOptions.MaximumSampleRowLimit + 1);

        var exception = await ParseFailureAsync(
            Encoding.UTF8.GetBytes("Date\n2026-07-01\n"),
            options);

        Assert.Equal(StatementCsvParserErrorCodes.InvalidOptions, exception.Code);
        Assert.Equal(400, exception.RecommendedStatusCode);
    }

    [Fact]
    public async Task ParseAsync_CancellationDuringRead_StopsTheActiveStream()
    {
        await using var source = new PrefixThenBlockingStream(Encoding.UTF8.GetBytes("A\n1\n"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            parser.ParseAsync(source, new StatementCsvParserOptions(), cancellation.Token));

        Assert.True(source.ObservedCancelableRead);
    }

    private async Task<StatementCsvParseResult> ParseAsync(
        byte[] bytes,
        StatementCsvParserOptions? options = null)
    {
        await using var source = new MemoryStream(bytes, writable: false);
        return await parser.ParseAsync(source, options ?? new StatementCsvParserOptions());
    }

    private async Task<StatementCsvParserException> ParseFailureAsync(
        byte[] bytes,
        StatementCsvParserOptions? options = null)
    {
        await using var source = new MemoryStream(bytes, writable: false);
        return await Assert.ThrowsAsync<StatementCsvParserException>(() =>
            parser.ParseAsync(source, options ?? new StatementCsvParserOptions()));
    }

    private static byte[] Encode(string value, bool includeBom)
    {
        var body = Encoding.UTF8.GetBytes(value);
        return includeBom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
    }

    private static byte[] BuildColumnCsv(int columnCount)
    {
        var header = string.Join(',', Enumerable.Range(0, columnCount).Select(index => $"C{index}"));
        var data = string.Join(',', Enumerable.Repeat("1", columnCount));
        return Encoding.UTF8.GetBytes($"{header}\n{data}\n");
    }

    private static byte[] BuildRowCsv(int rowCount)
    {
        var builder = new StringBuilder("Value\n");
        for (var index = 0; index < rowCount; index++)
        {
            builder.Append(index).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildCsvAtExactByteLimit()
    {
        const int rowCount = StatementCsvParser.MaximumDataRows;
        const string header = "A,B\n";
        var remainingFieldCharacters = checked(
            (int)StatementCsvParser.MaximumSourceBytes -
            Encoding.UTF8.GetByteCount(header) -
            (rowCount * 2));
        var builder = new StringBuilder((int)StatementCsvParser.MaximumSourceBytes);
        builder.Append(header);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowsRemaining = rowCount - rowIndex;
            var rowFieldCharacters = (remainingFieldCharacters + rowsRemaining - 1) / rowsRemaining;
            var firstFieldCharacters = Math.Min(
                StatementCsvParser.MaximumFieldCharacters,
                rowFieldCharacters);
            var secondFieldCharacters = rowFieldCharacters - firstFieldCharacters;

            builder
                .Append('a', firstFieldCharacters)
                .Append(',')
                .Append('b', secondFieldCharacters)
                .Append('\n');

            remainingFieldCharacters -= rowFieldCharacters;
        }

        Assert.Equal(0, remainingFieldCharacters);
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        Assert.Equal(StatementCsvParser.MaximumSourceBytes, bytes.LongLength);
        return bytes;
    }

    private sealed class PrefixThenBlockingStream(byte[] prefix) : Stream
    {
        private bool prefixReturned;

        public bool ObservedCancelableRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!prefixReturned)
            {
                prefixReturned = true;
                prefix.CopyTo(buffer);
                return prefix.Length;
            }

            ObservedCancelableRead = cancellationToken.CanBeCanceled;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
