using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed record BankConnectionAttemptSweepResult(int ExpiredCount, int SupersededCount);

internal enum AttemptTransitionResult
{
    Applied,
    AlreadyAtTarget,
    BlockedTerminal,
    Invalid,
    Conflict
}

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

    private static readonly HashSet<string> CallbackAwaitingStatuses = new(StringComparer.Ordinal)
    {
        BankConnectionAttemptStatuses.Created,
        BankConnectionAttemptStatuses.AuthLaunched,
        BankConnectionAttemptStatuses.AwaitingCallback
    };

    private static readonly HashSet<string> DefaultExpiryStatuses = new(StringComparer.Ordinal)
    {
        BankConnectionAttemptStatuses.Created,
        BankConnectionAttemptStatuses.AuthLaunched,
        BankConnectionAttemptStatuses.AwaitingCallback,
        BankConnectionAttemptStatuses.CallbackReceived,
        BankConnectionAttemptStatuses.AppReturnInitiated
    };

    private static readonly HashSet<string> StaleProcessingExpiryStatuses = new(StringComparer.Ordinal)
    {
        BankConnectionAttemptStatuses.AppReturnConfirmed,
        BankConnectionAttemptStatuses.ConnectionCreated,
        BankConnectionAttemptStatuses.Processing
    };

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AttemptLocks = new();

    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = BuildAllowedTransitions();

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
            AuthLaunchedUtc = now,
            TransitionVersion = 0
        };

        dbContext.BankConnectionAttempts.Add(attempt);
        await dbContext.SaveChangesAsync(cancellationToken);

        var supersededCount = await SupersedeCompetingAttemptsAsync(
            attempt,
            reconnectRequested,
            cancellationToken);

        logger.LogInformation(
            "Bank connection attempt created attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} provider={Provider} reconnectRequested={ReconnectRequested} launchOrigin={LaunchOrigin} supersededCount={SupersededCount}",
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId,
            attempt.ProviderName,
            reconnectRequested,
            attempt.LaunchOriginPath ?? "<none>",
            supersededCount);

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

        await ReconcileLifecycleTruthAsync(attempt, cancellationToken);
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

        await ReconcileLifecycleTruthAsync(attempt, cancellationToken);
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

        await ReconcileLifecycleTruthAsync(attempt, cancellationToken);
        return ToStatusDto(attempt);
    }

    public async Task MarkCallbackReceivedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var result = await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.CallbackReceived,
            transitionEvent: "attempt_callback_received",
            mutate: (state, now) =>
            {
                state.CallbackHandledUtc ??= now;
            },
            cancellationToken);

        if (result is AttemptTransitionResult.AlreadyAtTarget or AttemptTransitionResult.BlockedTerminal)
        {
            logger.LogInformation(
                "Duplicate callback ignored for attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} status={Status}",
                attempt.Id,
                attempt.ConnectionId,
                attempt.UserId,
                attempt.Status);
        }
    }

    public async Task MarkAppReturnInitiatedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.AppReturnInitiated,
            transitionEvent: "attempt_app_return_initiated",
            mutate: (state, now) =>
            {
                state.AppReturnInitiatedUtc ??= now;
            },
            cancellationToken);
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

        await ReconcileLifecycleTruthAsync(attempt, cancellationToken);
        if (IsTerminalStatus(attempt.Status))
        {
            logger.LogInformation(
                "Duplicate app return confirmation ignored for attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} status={Status}",
                attempt.Id,
                attempt.ConnectionId,
                attempt.UserId,
                attempt.Status);
            return ToStatusDto(attempt);
        }

        var connectionStatus = await dbContext.OpenBankingConnections
            .AsNoTracking()
            .Where(x => x.Id == attempt.ConnectionId && x.UserId == userId)
            .Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);

        var targetStatus = connectionStatus is BankConnectionStatuses.ConnectedPendingSync
            or BankConnectionStatuses.Connected
            or BankConnectionStatuses.Synced
            ? BankConnectionAttemptStatuses.Completed
            : BankConnectionAttemptStatuses.AppReturnConfirmed;

        await TransitionAttemptAsync(
            attempt,
            targetStatus,
            transitionEvent: "attempt_app_return_confirmed",
            mutate: (state, now) =>
            {
                state.AppReturnConfirmedUtc ??= now;
                if (targetStatus == BankConnectionAttemptStatuses.Completed)
                {
                    state.CompletedUtc ??= now;
                }
            },
            cancellationToken);

        return ToStatusDto(attempt);
    }

    public async Task MarkProcessingAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.Processing,
            transitionEvent: "attempt_processing_started",
            mutate: null,
            cancellationToken);
    }

    public async Task MarkCompletedAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.Completed,
            transitionEvent: "attempt_completed",
            mutate: (state, now) =>
            {
                state.CompletedUtc ??= now;
            },
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        BankConnectionAttempt attempt,
        string? failureCode,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.Failed,
            transitionEvent: "attempt_failed",
            mutate: (state, now) =>
            {
                state.FailureCode = string.IsNullOrWhiteSpace(failureCode) ? null : failureCode.Trim();
                state.FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
                state.FailedUtc ??= now;
            },
            cancellationToken);
    }

    public async Task<BankConnectionAttemptSweepResult> SweepLifecycleAsync(
        int batchSize,
        TimeSpan staleProcessingExpiryAge,
        CancellationToken cancellationToken)
    {
        var normalizedBatchSize = Math.Clamp(batchSize, 1, 512);
        var now = DateTime.UtcNow;
        var staleProcessingThresholdUtc = now - staleProcessingExpiryAge;
        var expiredCount = 0;
        var supersededCount = 0;

        var expirableAttempts = await dbContext.BankConnectionAttempts
            .Where(x =>
                !TerminalStatuses.Contains(x.Status)
                && (DefaultExpiryStatuses.Contains(x.Status) && x.ExpiresUtc <= now
                    || StaleProcessingExpiryStatuses.Contains(x.Status)
                    && x.ExpiresUtc <= now
                    && x.UpdatedUtc <= staleProcessingThresholdUtc))
            .OrderBy(x => x.ExpiresUtc)
            .ThenBy(x => x.UpdatedUtc)
            .Take(normalizedBatchSize)
            .ToListAsync(cancellationToken);

        if (expirableAttempts.Count == 0)
        {
            logger.LogDebug("Stale attempt cleanup skipped due active use or no stale attempts.");
        }

        foreach (var attempt in expirableAttempts)
        {
            var result = await TransitionAttemptAsync(
                attempt,
                BankConnectionAttemptStatuses.Expired,
                transitionEvent: "attempt_cleanup_sweep_expired",
                mutate: null,
                cancellationToken);
            if (result == AttemptTransitionResult.Applied)
            {
                expiredCount++;
            }
        }

        var activeCandidates = await dbContext.BankConnectionAttempts
            .Where(x => ActiveStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(normalizedBatchSize * 6, 16, 512))
            .ToListAsync(cancellationToken);

        var grouped = activeCandidates
            .GroupBy(BuildScopeKey)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();

        foreach (var group in grouped)
        {
            var ordered = group
                .OrderByDescending(x => x.CreatedUtc)
                .ThenByDescending(x => x.Id)
                .ToList();
            var current = ordered[0];
            foreach (var stale in ordered.Skip(1))
            {
                var result = await TransitionAttemptAsync(
                    stale,
                    BankConnectionAttemptStatuses.Superseded,
                    transitionEvent: "attempt_cleanup_sweep_superseded",
                    mutate: (state, transitionNow) =>
                    {
                        state.SupersededByAttemptId = current.Id;
                        state.CompletedUtc ??= transitionNow;
                    },
                    cancellationToken);
                if (result == AttemptTransitionResult.Applied)
                {
                    supersededCount++;
                }
            }
        }

        if (expiredCount > 0 || supersededCount > 0)
        {
            logger.LogInformation(
                "Bank connection attempt sweep completed expiredCount={ExpiredCount} supersededCount={SupersededCount}",
                expiredCount,
                supersededCount);
        }

        return new BankConnectionAttemptSweepResult(expiredCount, supersededCount);
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
        => CallbackAwaitingStatuses.Contains(status);

    private async Task ReconcileLifecycleTruthAsync(BankConnectionAttempt attempt, CancellationToken cancellationToken)
    {
        await ExpireIfNeededAsync(attempt, cancellationToken);
        if (IsTerminalStatus(attempt.Status))
        {
            return;
        }

        await SupersedeIfShadowedByNewerAttemptAsync(attempt, cancellationToken);
    }

    private async Task<int> SupersedeCompetingAttemptsAsync(
        BankConnectionAttempt attempt,
        bool reconnectRequested,
        CancellationToken cancellationToken)
    {
        var candidatesQuery = BuildSupersessionScopeQuery(
                userId: attempt.UserId,
                connectionId: attempt.ConnectionId,
                launchOriginPath: attempt.LaunchOriginPath,
                reconnectRequested: reconnectRequested)
            .Where(x =>
                x.Id != attempt.Id
                && ActiveStatuses.Contains(x.Status)
                && x.CreatedUtc < attempt.CreatedUtc)
            .OrderBy(x => x.CreatedUtc)
            .Take(16);

        var candidates = await candidatesQuery.ToListAsync(cancellationToken);
        var supersededCount = 0;
        foreach (var candidate in candidates)
        {
            var result = await TransitionAttemptAsync(
                candidate,
                BankConnectionAttemptStatuses.Superseded,
                transitionEvent: "attempt_superseded_by_new_launch",
                mutate: (state, now) =>
                {
                    state.SupersededByAttemptId = attempt.Id;
                    state.CompletedUtc ??= now;
                },
                cancellationToken);

            if (result == AttemptTransitionResult.Applied)
            {
                supersededCount++;
            }
        }

        return supersededCount;
    }

    private IQueryable<BankConnectionAttempt> BuildSupersessionScopeQuery(
        Guid userId,
        Guid connectionId,
        string? launchOriginPath,
        bool reconnectRequested)
    {
        var activeAttemptsQuery = dbContext.BankConnectionAttempts
            .Where(x => x.UserId == userId);

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

        return activeAttemptsQuery;
    }

    private async Task SupersedeIfShadowedByNewerAttemptAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!ActiveStatuses.Contains(attempt.Status))
        {
            return;
        }

        var byLaunchOrigin = !string.IsNullOrWhiteSpace(attempt.LaunchOriginPath);
        var newerAttempt = await BuildSupersessionScopeQuery(
                attempt.UserId,
                attempt.ConnectionId,
                attempt.LaunchOriginPath,
                reconnectRequested: !byLaunchOrigin)
            .AsNoTracking()
            .Where(x =>
                x.Id != attempt.Id
                && ActiveStatuses.Contains(x.Status)
                && x.CreatedUtc > attempt.CreatedUtc)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (newerAttempt is null)
        {
            return;
        }

        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.Superseded,
            transitionEvent: "attempt_superseded_by_shadowed_read",
            mutate: (state, now) =>
            {
                state.SupersededByAttemptId = newerAttempt.Id;
                state.CompletedUtc ??= now;
            },
            cancellationToken);
    }

    private async Task ExpireIfNeededAsync(
        BankConnectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!NeedsExpiry(attempt, now))
        {
            return;
        }

        await TransitionAttemptAsync(
            attempt,
            BankConnectionAttemptStatuses.Expired,
            transitionEvent: "attempt_expired_on_read",
            mutate: null,
            cancellationToken);
    }

    private async Task<AttemptTransitionResult> TransitionAttemptAsync(
        BankConnectionAttempt attempt,
        string targetStatus,
        string transitionEvent,
        Action<BankConnectionAttempt, DateTime>? mutate,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            var previousStatus = attempt.Status;
            if (previousStatus == targetStatus)
            {
                return AttemptTransitionResult.AlreadyAtTarget;
            }

            if (IsTerminalStatus(previousStatus))
            {
                logger.LogInformation(
                    "Attempt transition ignored because state is terminal event={TransitionEvent} attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} status={Status}",
                    transitionEvent,
                    attempt.Id,
                    attempt.ConnectionId,
                    attempt.UserId,
                    previousStatus);
                return AttemptTransitionResult.BlockedTerminal;
            }

            if (!IsTransitionAllowed(previousStatus, targetStatus))
            {
                logger.LogWarning(
                    "Invalid attempt transition blocked event={TransitionEvent} attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} from={FromStatus} to={ToStatus}",
                    transitionEvent,
                    attempt.Id,
                    attempt.ConnectionId,
                    attempt.UserId,
                    previousStatus,
                    targetStatus);
                return AttemptTransitionResult.Invalid;
            }

            var now = DateTime.UtcNow;
            mutate?.Invoke(attempt, now);
            attempt.Status = targetStatus;
            attempt.UpdatedUtc = now;
            attempt.TransitionVersion += 1;

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Attempt transition applied event={TransitionEvent} attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} from={FromStatus} to={ToStatus} transitionVersion={TransitionVersion}",
                    transitionEvent,
                    attempt.Id,
                    attempt.ConnectionId,
                    attempt.UserId,
                    previousStatus,
                    targetStatus,
                    attempt.TransitionVersion);
                return AttemptTransitionResult.Applied;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                logger.LogWarning(
                    exception,
                    "Attempt transition concurrency conflict event={TransitionEvent} attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} from={FromStatus} to={ToStatus} retry={Retry}",
                    transitionEvent,
                    attempt.Id,
                    attempt.ConnectionId,
                    attempt.UserId,
                    previousStatus,
                    targetStatus,
                    retry + 1);

                var reloaded = await ReloadAttemptAsync(attempt, cancellationToken);
                if (!reloaded)
                {
                    return AttemptTransitionResult.Conflict;
                }
            }
        }

        logger.LogWarning(
            "Attempt transition exhausted retries event={TransitionEvent} attemptId={AttemptId} connectionId={ConnectionId} userId={UserId} targetStatus={ToStatus}",
            transitionEvent,
            attempt.Id,
            attempt.ConnectionId,
            attempt.UserId,
            targetStatus);
        return AttemptTransitionResult.Conflict;
    }

    private async Task<bool> ReloadAttemptAsync(BankConnectionAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Entry(attempt).ReloadAsync(cancellationToken);
            return true;
        }
        catch
        {
            var refreshed = await dbContext.BankConnectionAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == attempt.Id, cancellationToken);
            if (refreshed is null)
            {
                return false;
            }

            CopyAttemptState(refreshed, attempt);
            return true;
        }
    }

    private static void CopyAttemptState(BankConnectionAttempt source, BankConnectionAttempt target)
    {
        target.UserId = source.UserId;
        target.ConnectionId = source.ConnectionId;
        target.ProviderName = source.ProviderName;
        target.ProviderEnvironment = source.ProviderEnvironment;
        target.Status = source.Status;
        target.LaunchOriginPath = source.LaunchOriginPath;
        target.AppReturnUri = source.AppReturnUri;
        target.CallbackState = source.CallbackState;
        target.PublicToken = source.PublicToken;
        target.CreatedUtc = source.CreatedUtc;
        target.UpdatedUtc = source.UpdatedUtc;
        target.ExpiresUtc = source.ExpiresUtc;
        target.AuthLaunchedUtc = source.AuthLaunchedUtc;
        target.CallbackHandledUtc = source.CallbackHandledUtc;
        target.AppReturnInitiatedUtc = source.AppReturnInitiatedUtc;
        target.AppReturnConfirmedUtc = source.AppReturnConfirmedUtc;
        target.CompletedUtc = source.CompletedUtc;
        target.FailedUtc = source.FailedUtc;
        target.FailureCode = source.FailureCode;
        target.FailureReason = source.FailureReason;
        target.SupersededByAttemptId = source.SupersededByAttemptId;
        target.TransitionVersion = source.TransitionVersion;
    }

    private static bool IsTransitionAllowed(string current, string target)
    {
        if (current == target)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(current, out var allowedTargets) && allowedTargets.Contains(target);
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

    private static string BuildScopeKey(BankConnectionAttempt attempt)
    {
        var originOrConnection = !string.IsNullOrWhiteSpace(attempt.LaunchOriginPath)
            ? $"origin:{attempt.LaunchOriginPath!.Trim()}"
            : $"connection:{attempt.ConnectionId:N}";
        return $"{attempt.UserId:N}:{originOrConnection}";
    }

    private static Dictionary<string, HashSet<string>> BuildAllowedTransitions()
    {
        HashSet<string> CommonNonTerminalTargets() =>
            [
                BankConnectionAttemptStatuses.Completed,
                BankConnectionAttemptStatuses.Failed,
                BankConnectionAttemptStatuses.Superseded,
                BankConnectionAttemptStatuses.Cancelled,
                BankConnectionAttemptStatuses.Expired
            ];

        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [BankConnectionAttemptStatuses.Created] =
            [
                BankConnectionAttemptStatuses.AuthLaunched,
                BankConnectionAttemptStatuses.AwaitingCallback,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.AuthLaunched] =
            [
                BankConnectionAttemptStatuses.AwaitingCallback,
                BankConnectionAttemptStatuses.CallbackReceived,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.AwaitingCallback] =
            [
                BankConnectionAttemptStatuses.CallbackReceived,
                BankConnectionAttemptStatuses.AppReturnInitiated,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.CallbackReceived] =
            [
                BankConnectionAttemptStatuses.AppReturnInitiated,
                BankConnectionAttemptStatuses.AppReturnConfirmed,
                BankConnectionAttemptStatuses.ConnectionCreated,
                BankConnectionAttemptStatuses.Processing,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.AppReturnInitiated] =
            [
                BankConnectionAttemptStatuses.AppReturnConfirmed,
                BankConnectionAttemptStatuses.ConnectionCreated,
                BankConnectionAttemptStatuses.Processing,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.AppReturnConfirmed] =
            [
                BankConnectionAttemptStatuses.ConnectionCreated,
                BankConnectionAttemptStatuses.Processing,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.ConnectionCreated] =
            [
                BankConnectionAttemptStatuses.Processing,
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.Processing] =
            [
                .. CommonNonTerminalTargets()
            ],
            [BankConnectionAttemptStatuses.Completed] = [],
            [BankConnectionAttemptStatuses.Failed] = [],
            [BankConnectionAttemptStatuses.Expired] = [],
            [BankConnectionAttemptStatuses.Superseded] = [],
            [BankConnectionAttemptStatuses.Cancelled] = []
        };

        return map;
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
