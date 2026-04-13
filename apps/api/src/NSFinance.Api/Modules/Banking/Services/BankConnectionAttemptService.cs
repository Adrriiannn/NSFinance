using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class BankConnectionAttemptService(
    AppDbContext dbContext,
    ILogger<BankConnectionAttemptService> logger)
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.Ordinal)
    {
        BankConnectionAttemptStatuses.Completed,
        BankConnectionAttemptStatuses.Failed,
        BankConnectionAttemptStatuses.Expired,
        BankConnectionAttemptStatuses.Superseded,
        BankConnectionAttemptStatuses.Cancelled
    };

    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.Ordinal)
    {
        BankConnectionAttemptStatuses.Created,
        BankConnectionAttemptStatuses.AuthLaunched,
        BankConnectionAttemptStatuses.AwaitingCallback,
        BankConnectionAttemptStatuses.CallbackReceived,
        BankConnectionAttemptStatuses.AppReturnInitiated,
        BankConnectionAttemptStatuses.AppReturnConfirmed,
        BankConnectionAttemptStatuses.ConnectionCreated,
        BankConnectionAttemptStatuses.Processing
    };

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AttemptLocks = new();

    public async Task<BankConnectionAttempt> CreateAttemptAsync(
        Guid userId,
        Guid connectionId,
        string providerName,
        string providerEnvironment,
        string callbackState,
        string? appReturnUri,
        DateTime? expiresUtc,
        bool reconnectRequested,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var attemptId = Guid.NewGuid();
        var launchOriginPath = ResolveLaunchOriginPath(appReturnUri);
        var attempt = new BankConnectionAttempt
        {
            Id = attemptId,
            UserId = userId,
            ConnectionId = connectionId,
            ProviderName = providerName,
            ProviderEnvironment = providerEnvironment,
            Status = BankConnectionAttemptStatuses.AwaitingCallback,
            LaunchOriginPath = launchOriginPath,
            AppReturnUri = appReturnUri,
            CallbackState = callbackState,
            PublicToken = CreatePublicToken(),
            CreatedUtc = now,
            UpdatedUtc = now,
            ExpiresUtc = expiresUtc ?? now.AddMinutes(15),
            AuthLaunchedUtc = now
        };

        var activeAttemptsQuery = dbContext.BankConnectionAttempts
            .Where(x =>
                x.UserId == userId
                && ActiveStatuses.Contains(x.Status));

        if (reconnectRequested)
        {
            activeAttemptsQuery = activeAttemptsQuery.Where(x => x.ConnectionId == connectionId);
        }
        else if (!string.IsNullOrWhiteSpace(launchOriginPath))
        {
            activeAttemptsQuery = activeAttemptsQuery.Where(x => x.LaunchOriginPath == launchOriginPath);
        }
        else
        {
            activeAttemptsQuery = activeAttemptsQuery.Where(x => x.ConnectionId == connectionId);
        }

        var supersededAttempts = await activeAttemptsQuery.ToListAsync(cancellationToken);
        foreach (var superseded in supersededAttempts)
        {
            superseded.Status = BankConnectionAttemptStatuses.Superseded;
            superseded.SupersededByAttemptId = attemptId;
            superseded.UpdatedUtc = now;
            superseded.CompletedUtc ??= now;
        }

        dbContext.BankConnectionAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt created attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} provider={Provider} reconnectRequested={ReconnectRequested} launchOrigin={LaunchOrigin} supersededCount={SupersededCount}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId,
            attempt.ProviderName,
            reconnectRequested,
            attempt.LaunchOriginPath ?? "<none>",
            supersededAttempts.Count);

        return attempt;
    }

    public async Task<BankConnectionAttempt?> FindByCallbackStateAsync(
        string? callbackState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callbackState))
        {
            return null;
        }

        var attempt = await dbContext.BankConnectionAttempts
            .SingleOrDefaultAsync(x => x.CallbackState == callbackState, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        await ExpireIfNeededAsync(attempt, cancellationToken);
        return attempt;
    }

    public async Task<BankConnectionAttempt?> FindForUserAsync(
        Guid userId,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.BankConnectionAttempts
            .SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        await ExpireIfNeededAsync(attempt, cancellationToken);
        return attempt;
    }

    public async Task<BankConnectionAttemptStatusDto?> GetPublicStatusAsync(
        Guid attemptId,
        string token,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.BankConnectionAttempts
            .SingleOrDefaultAsync(
                x => x.Id == attemptId && x.PublicToken == token,
                cancellationToken);

        if (attempt is null)
        {
            return null;
        }

        await ExpireIfNeededAsync(attempt, cancellationToken);

        return ToStatusDto(attempt);
    }

    public async Task MarkCallbackReceivedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (IsTerminalStatus(attempt.Status))
        {
            return;
        }

        var now = DateTime.UtcNow;
        attempt.CallbackHandledUtc ??= now;
        if (attempt.Status is BankConnectionAttemptStatuses.Created
            or BankConnectionAttemptStatuses.AuthLaunched
            or BankConnectionAttemptStatuses.AwaitingCallback)
        {
            attempt.Status = BankConnectionAttemptStatuses.CallbackReceived;
        }

        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt callback received attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);
    }

    public async Task MarkAppReturnInitiatedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (IsTerminalStatus(attempt.Status))
        {
            return;
        }

        var now = DateTime.UtcNow;
        attempt.AppReturnInitiatedUtc ??= now;
        attempt.Status = BankConnectionAttemptStatuses.AppReturnInitiated;
        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt app return initiated attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);
    }

    public async Task<BankConnectionAttemptStatusDto?> ConfirmAppReturnHandledAsync(
        Guid userId,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.BankConnectionAttempts
            .SingleOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        await ExpireIfNeededAsync(attempt, cancellationToken);
        if (attempt.Status == BankConnectionAttemptStatuses.Expired)
        {
            return ToStatusDto(attempt);
        }

        var now = DateTime.UtcNow;
        var connectionStatus = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.Id == attempt.ConnectionId && x.UserId == userId)
            .Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);
        attempt.AppReturnConfirmedUtc ??= now;
        if (connectionStatus is BankConnectionStatuses.ConnectedPendingSync
            or BankConnectionStatuses.Connected
            or BankConnectionStatuses.Synced)
        {
            attempt.Status = BankConnectionAttemptStatuses.Completed;
            attempt.CompletedUtc ??= now;
        }
        else if (attempt.Status is BankConnectionAttemptStatuses.CallbackReceived
            or BankConnectionAttemptStatuses.AppReturnInitiated
            or BankConnectionAttemptStatuses.AwaitingCallback
            or BankConnectionAttemptStatuses.AuthLaunched
            or BankConnectionAttemptStatuses.Created)
        {
            attempt.Status = BankConnectionAttemptStatuses.AppReturnConfirmed;
        }

        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt app return confirmed attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);

        return ToStatusDto(attempt);
    }

    public async Task MarkProcessingAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (IsTerminalStatus(attempt.Status))
        {
            return;
        }

        var now = DateTime.UtcNow;
        attempt.Status = BankConnectionAttemptStatuses.Processing;
        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt processing started attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);
    }

    public async Task MarkCompletedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.Status == BankConnectionAttemptStatuses.Completed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        attempt.Status = BankConnectionAttemptStatuses.Completed;
        attempt.CompletedUtc ??= now;
        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt completed attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);
    }

    public async Task MarkFailedAsync(
        BankConnectionAttempt attempt,
        string? failureCode,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        if (attempt.Status == BankConnectionAttemptStatuses.Failed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        attempt.Status = BankConnectionAttemptStatuses.Failed;
        attempt.FailureCode = string.IsNullOrWhiteSpace(failureCode) ? null : failureCode.Trim();
        attempt.FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
        attempt.FailedUtc ??= now;
        attempt.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Bank connection attempt failed attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} failureCode={FailureCode}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId,
            attempt.FailureCode ?? "<none>");
    }

    public async Task<T> WithAttemptLockAsync<T>(
        Guid attemptId,
        Func<Task<T>> action)
    {
        var gate = AttemptLocks.GetOrAdd(attemptId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    public static bool IsTerminalStatus(string status)
        => TerminalStatuses.Contains(status);

    public static bool IsAwaitingCallbackStatus(string status)
        => status is BankConnectionAttemptStatuses.Created
            or BankConnectionAttemptStatuses.AuthLaunched
            or BankConnectionAttemptStatuses.AwaitingCallback;

    private async Task ExpireIfNeededAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!NeedsExpiry(attempt, DateTime.UtcNow))
        {
            return;
        }

        attempt.Status = BankConnectionAttemptStatuses.Expired;
        attempt.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bank connection attempt expired attemptId={AttemptId} connectionId={ConnectionId} userId={UserId}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId);
    }

    private static bool NeedsExpiry(BankConnectionAttempt attempt, DateTime now)
        => !IsTerminalStatus(attempt.Status) && now >= attempt.ExpiresUtc;

    private static string CreatePublicToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private static string? ResolveLaunchOriginPath(string? appReturnUri)
    {
        if (string.IsNullOrWhiteSpace(appReturnUri))
        {
            return null;
        }

        if (!Uri.TryCreate(appReturnUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue("returnTo", out var returnToValues))
        {
            var candidate = returnToValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.StartsWith("/", StringComparison.Ordinal))
            {
                return candidate.Trim();
            }
        }

        var host = uri.Host?.Trim('/') ?? string.Empty;
        var path = uri.AbsolutePath?.Trim('/') ?? string.Empty;
        var combined = $"{host}/{path}".Trim('/');
        return string.IsNullOrWhiteSpace(combined) ? null : $"/{combined}";
    }

    private static BankConnectionAttemptStatusDto ToStatusDto(BankConnectionAttempt attempt)
    {
        var safeToClose = attempt.Status switch
        {
            BankConnectionAttemptStatuses.AppReturnConfirmed => true,
            BankConnectionAttemptStatuses.ConnectionCreated => true,
            BankConnectionAttemptStatuses.Processing => true,
            BankConnectionAttemptStatuses.Completed => true,
            BankConnectionAttemptStatuses.Superseded => true,
            BankConnectionAttemptStatuses.Expired => true,
            BankConnectionAttemptStatuses.Cancelled => true,
            BankConnectionAttemptStatuses.Failed => true,
            _ => false
        };

        var shouldAutoClose = attempt.Status switch
        {
            BankConnectionAttemptStatuses.AppReturnConfirmed => true,
            BankConnectionAttemptStatuses.ConnectionCreated => true,
            BankConnectionAttemptStatuses.Processing => true,
            BankConnectionAttemptStatuses.Completed => true,
            BankConnectionAttemptStatuses.Superseded => true,
            BankConnectionAttemptStatuses.Expired => true,
            BankConnectionAttemptStatuses.Cancelled => true,
            _ => false
        };

        var shouldAutoReturn = attempt.Status switch
        {
            BankConnectionAttemptStatuses.CallbackReceived => true,
            BankConnectionAttemptStatuses.AppReturnInitiated => true,
            _ => false
        };

        var manualActionRequired = attempt.Status switch
        {
            BankConnectionAttemptStatuses.Failed => true,
            BankConnectionAttemptStatuses.Expired => true,
            BankConnectionAttemptStatuses.Cancelled => true,
            _ => false
        };

        var (headline, message) = attempt.Status switch
        {
            BankConnectionAttemptStatuses.Created
                or BankConnectionAttemptStatuses.AuthLaunched
                or BankConnectionAttemptStatuses.AwaitingCallback
                => ("Waiting for bank authorization", "Complete your bank consent flow and return to NSFinance."),
            BankConnectionAttemptStatuses.CallbackReceived
                or BankConnectionAttemptStatuses.AppReturnInitiated
                => ("Returning to NSFinance...", "Your connection is being completed in the app."),
            BankConnectionAttemptStatuses.AppReturnConfirmed
                or BankConnectionAttemptStatuses.ConnectionCreated
                or BankConnectionAttemptStatuses.Processing
                => ("Finishing setup in NSFinance", "You can leave this tab. NSFinance will continue in the app."),
            BankConnectionAttemptStatuses.Completed
                => ("This connection is already complete", "Your bank is connected. You can close this tab."),
            BankConnectionAttemptStatuses.Superseded
                => ("This attempt was replaced", "A newer connection attempt is active. You can close this tab."),
            BankConnectionAttemptStatuses.Expired
                => ("This attempt expired", "Open NSFinance to start a new bank connection attempt."),
            BankConnectionAttemptStatuses.Cancelled
                => ("This attempt was cancelled", "Open NSFinance to start again."),
            _ => ("Connection needs attention", "Open NSFinance to continue.")
        };

        return new BankConnectionAttemptStatusDto(
            attempt.Id,
            attempt.ConnectionId,
            attempt.Status,
            safeToClose,
            shouldAutoClose,
            shouldAutoReturn,
            manualActionRequired,
            headline,
            message,
            attempt.UpdatedUtc,
            attempt.ExpiresUtc,
            attempt.CallbackHandledUtc,
            attempt.AppReturnInitiatedUtc,
            attempt.AppReturnConfirmedUtc,
            attempt.CompletedUtc);
    }
}
