using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NSFinance.Api.Persistence;

// EF Core's per-command Information logs are filtered out in configuration
// because background polling made them the dominant Application Insights cost.
// This keeps the diagnostically useful subset: commands that ran slowly.
public sealed class SlowDbCommandInterceptor(ILogger<SlowDbCommandInterceptor> logger) : DbCommandInterceptor
{
    internal static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(500);
    private const int MaxCommandTextLength = 2000;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    internal static bool IsSlow(TimeSpan duration) => duration >= Threshold;

    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        if (!IsSlow(eventData.Duration))
        {
            return;
        }

        // Command text carries parameter placeholders only; values are never logged.
        var commandText = command.CommandText.Length <= MaxCommandTextLength
            ? command.CommandText
            : command.CommandText[..MaxCommandTextLength];
        logger.LogWarning(
            "Slow database command elapsedMs={ElapsedMs} commandText={CommandText}",
            Math.Round(eventData.Duration.TotalMilliseconds, 1),
            commandText);
    }
}
