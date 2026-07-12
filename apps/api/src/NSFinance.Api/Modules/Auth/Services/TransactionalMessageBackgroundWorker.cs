using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class TransactionalMessageBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TransactionalEmailOptions> options,
    ITransactionalEmailSender emailSender,
    ILogger<TransactionalMessageBackgroundWorker> logger) : BackgroundService
{
    private readonly TransactionalEmailOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Transactional identity message worker iteration failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var batchSize = Math.Max(1, _options.BatchSize);
        var candidateIds = await dbContext.TransactionalMessages
            .AsNoTracking()
            .Where(x =>
                ((x.Status == TransactionalMessageStatuses.Pending || x.Status == TransactionalMessageStatuses.Retry)
                    && x.NextAttemptUtc <= now)
                || (x.Status == TransactionalMessageStatuses.Processing && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.CreatedUtc)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var messageId in candidateIds)
        {
            await ProcessMessageAsync(messageId, cancellationToken);
        }
    }

    private async Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var claimDb = claimScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claimed = await claimDb.TransactionalMessages
                .Where(x => x.Id == messageId
                    && (((x.Status == TransactionalMessageStatuses.Pending || x.Status == TransactionalMessageStatuses.Retry)
                            && x.NextAttemptUtc <= now)
                        || (x.Status == TransactionalMessageStatuses.Processing && x.LeaseExpiresUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, TransactionalMessageStatuses.Processing)
                    .SetProperty(x => x.LeaseId, leaseId)
                    .SetProperty(x => x.LeaseExpiresUtc, now.AddMinutes(2))
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                    cancellationToken);

            if (claimed == 0)
            {
                return;
            }
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payloadProtector = scope.ServiceProvider.GetRequiredService<IdentityPayloadProtector>();
        var renderer = scope.ServiceProvider.GetRequiredService<IdentityEmailRenderer>();
        var emailSender = scope.ServiceProvider.GetRequiredService<ITransactionalEmailSender>();
        var message = await dbContext.TransactionalMessages
            .Include(x => x.IdentityChallenge)
            .SingleAsync(x => x.Id == messageId && x.LeaseId == leaseId, cancellationToken);

        if (message.IdentityChallenge is not null
            && (message.IdentityChallenge.SupersededUtc is not null
                || message.IdentityChallenge.ConsumedUtc is not null
                || message.IdentityChallenge.ExpiresUtc <= DateTime.UtcNow))
        {
            MarkTerminal(message, TransactionalMessageStatuses.Cancelled, "challenge_inactive");
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        TransactionalEmailSendResult result;
        try
        {
            var payloadJson = payloadProtector.Unprotect(message.EncryptedPayload);
            var rendered = renderer.Render(message.TemplateKey, payloadJson);
            result = await emailSender.SendAsync(message.Recipient, rendered, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Transactional identity message failed messageId={MessageId}", message.Id);
            result = TransactionalEmailSendResult.Failure("identity_message_processing_failed");
        }

        if (result.Accepted)
        {
            message.Status = TransactionalMessageStatuses.ProviderAccepted;
            message.ProviderMessageId = result.ProviderMessageId;
            message.ProviderAcceptedUtc = DateTime.UtcNow;
            message.EncryptedPayload = string.Empty;
            ClearLease(message);
        }
        else if (message.AttemptCount >= message.MaxAttempts
            || string.Equals(result.FailureCode, "email_recipient_not_allowed", StringComparison.Ordinal)
            || string.Equals(result.FailureCode, "email_provider_not_configured", StringComparison.Ordinal))
        {
            MarkTerminal(message, TransactionalMessageStatuses.Failed, result.FailureCode ?? "email_delivery_failed");
        }
        else
        {
            message.Status = TransactionalMessageStatuses.Retry;
            message.LastFailureCode = result.FailureCode;
            message.NextAttemptUtc = DateTime.UtcNow.Add(ComputeBackoff(message.AttemptCount));
            ClearLease(message);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static TimeSpan ComputeBackoff(int attemptCount)
    {
        var seconds = Math.Min(900, 15 * Math.Pow(2, Math.Max(0, attemptCount - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static void MarkTerminal(
        Persistence.Entities.TransactionalMessage message,
        string status,
        string failureCode)
    {
        message.Status = status;
        message.LastFailureCode = failureCode;
        message.FailedUtc = DateTime.UtcNow;
        message.EncryptedPayload = string.Empty;
        ClearLease(message);
    }

    private static void ClearLease(Persistence.Entities.TransactionalMessage message)
    {
        message.LeaseId = null;
        message.LeaseExpiresUtc = null;
    }
}
