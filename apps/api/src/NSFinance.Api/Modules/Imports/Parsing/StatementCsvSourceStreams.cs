using System.Security.Cryptography;

namespace NSFinance.Api.Modules.Imports.Parsing;

internal sealed class StatementCsvSourceTracker : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly long maximumBytes;
    private bool completed;

    public StatementCsvSourceTracker(long maximumBytes)
    {
        this.maximumBytes = maximumBytes;
    }

    public long BytesRead { get; private set; }

    public long RemainingBytes => maximumBytes - BytesRead;

    public void Record(ReadOnlySpan<byte> bytes, bool rejectNul)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        if (bytes.Length > RemainingBytes)
        {
            throw StatementCsvParserException.FileTooLarge();
        }

        if (rejectNul && bytes.IndexOf((byte)0) >= 0)
        {
            throw StatementCsvParserException.BinaryContent();
        }

        hash.AppendData(bytes);
        BytesRead += bytes.Length;
    }

    public string CompleteHash()
    {
        if (completed)
        {
            throw new InvalidOperationException("The source hash has already been completed.");
        }

        completed = true;
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose()
    {
        hash.Dispose();
    }
}

internal sealed class StatementCsvTrackedReadStream : Stream
{
    private readonly Stream inner;
    private readonly StatementCsvSourceTracker tracker;
    private readonly CancellationToken operationCancellationToken;

    public StatementCsvTrackedReadStream(
        Stream inner,
        StatementCsvSourceTracker tracker,
        CancellationToken operationCancellationToken)
    {
        this.inner = inner;
        this.tracker = tracker;
        this.operationCancellationToken = operationCancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var boundedCount = GetBoundedReadCount(count);
        if (boundedCount == 0)
        {
            return 0;
        }

        var read = inner.Read(buffer, offset, boundedCount);
        tracker.Record(buffer.AsSpan(offset, read), rejectNul: true);
        operationCancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var boundedCount = GetBoundedReadCount(buffer.Length);
        if (boundedCount == 0)
        {
            return 0;
        }

        var read = inner.Read(buffer[..boundedCount]);
        tracker.Record(buffer[..read], rejectNul: true);
        operationCancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(buffer, cancellationToken);

    private async ValueTask<int> ReadCoreAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var boundedCount = GetBoundedReadCount(buffer.Length);
        if (boundedCount == 0)
        {
            return 0;
        }

        using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
        var effectiveCancellationToken = linkedCancellation?.Token
            ?? (operationCancellationToken.CanBeCanceled
                ? operationCancellationToken
                : cancellationToken);

        var read = await inner
            .ReadAsync(buffer[..boundedCount], effectiveCancellationToken)
            .ConfigureAwait(false);

        tracker.Record(buffer.Span[..read], rejectNul: true);
        operationCancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    private CancellationTokenSource? CreateLinkedCancellation(CancellationToken cancellationToken)
    {
        if (!operationCancellationToken.CanBeCanceled || !cancellationToken.CanBeCanceled)
        {
            return null;
        }

        if (operationCancellationToken == cancellationToken)
        {
            return null;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(
            operationCancellationToken,
            cancellationToken);
    }

    private int GetBoundedReadCount(int requestedCount)
    {
        if (requestedCount <= 0)
        {
            return 0;
        }

        var remainingWithOverflowProbe = tracker.RemainingBytes + 1;
        return (int)Math.Min(requestedCount, remainingWithOverflowProbe);
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class StatementCsvPrefixReadStream : Stream
{
    private readonly ReadOnlyMemory<byte> prefix;
    private readonly Stream remainder;
    private readonly CancellationToken operationCancellationToken;
    private int prefixOffset;

    public StatementCsvPrefixReadStream(
        ReadOnlyMemory<byte> prefix,
        Stream remainder,
        CancellationToken operationCancellationToken)
    {
        this.prefix = prefix;
        this.remainder = remainder;
        this.operationCancellationToken = operationCancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var prefixRead = ReadPrefix(buffer.AsSpan(offset, count));
        return prefixRead > 0 ? prefixRead : remainder.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var prefixRead = ReadPrefix(buffer);
        return prefixRead > 0 ? prefixRead : remainder.Read(buffer);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var prefixRead = ReadPrefix(buffer.AsSpan(offset, count));
        return prefixRead > 0
            ? Task.FromResult(prefixRead)
            : remainder.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        operationCancellationToken.ThrowIfCancellationRequested();
        var prefixRead = ReadPrefix(buffer.Span);
        return prefixRead > 0
            ? ValueTask.FromResult(prefixRead)
            : remainder.ReadAsync(buffer, cancellationToken);
    }

    private int ReadPrefix(Span<byte> destination)
    {
        if (destination.Length == 0 || prefixOffset >= prefix.Length)
        {
            return 0;
        }

        var count = Math.Min(destination.Length, prefix.Length - prefixOffset);
        prefix.Span.Slice(prefixOffset, count).CopyTo(destination);
        prefixOffset += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
