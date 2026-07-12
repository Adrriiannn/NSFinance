using System.Text.Json;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class TransactionalMessageService(
    AppDbContext dbContext,
    IdentityPayloadProtector payloadProtector,
    ITransactionalEmailSender emailSender,
    IOptions<TransactionalEmailOptions> options)
{
    private readonly TransactionalEmailOptions _options = options.Value;

    public bool IsEmailConfigured => emailSender.IsConfigured;

    public TransactionalMessage QueueEmail(
        Guid? userId,
        Guid? challengeId,
        string recipient,
        string templateKey,
        IdentityEmailPayload payload)
    {
        if (!emailSender.IsConfigured)
        {
            throw new InvalidOperationException("Transactional email delivery is not configured.");
        }

        var now = DateTime.UtcNow;
        var message = new TransactionalMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IdentityChallengeId = challengeId,
            Channel = IdentityChannels.Email,
            TemplateKey = templateKey,
            TemplateVersion = IdentityEmailRenderer.CurrentTemplateVersion,
            Recipient = recipient.Trim().ToLowerInvariant(),
            EncryptedPayload = payloadProtector.Protect(JsonSerializer.Serialize(payload)),
            Status = TransactionalMessageStatuses.Pending,
            AttemptCount = 0,
            MaxAttempts = Math.Max(1, _options.MaxAttempts),
            CreatedUtc = now,
            NextAttemptUtc = now
        };

        dbContext.TransactionalMessages.Add(message);
        return message;
    }
}

public static class TransactionalMessageStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retry = "retry";
    public const string ProviderAccepted = "provider_accepted";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
