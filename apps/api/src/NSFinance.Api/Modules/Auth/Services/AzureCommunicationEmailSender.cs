using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Options;
using NSFinance.Api.Modules.Auth.Configuration;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class AzureCommunicationEmailSender : ITransactionalEmailSender
{
    private readonly TransactionalEmailOptions _options;
    private readonly EmailClient? _client;
    private readonly HashSet<string> _recipientAllowList;

    public AzureCommunicationEmailSender(IOptions<TransactionalEmailOptions> options)
    {
        _options = options.Value;
        var hasEndpoint = Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint);
        IsConfigured = _options.Enabled
            && hasEndpoint
            && !string.IsNullOrWhiteSpace(_options.SenderAddress);

        _recipientAllowList = _options.RecipientAllowList
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (IsConfigured)
        {
            _client = new EmailClient(endpoint!, new DefaultAzureCredential());
        }
    }

    public bool IsConfigured { get; }

    public async Task<TransactionalEmailSendResult> SendAsync(
        string recipient,
        RenderedIdentityEmail message,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || _client is null)
        {
            return TransactionalEmailSendResult.Failure("email_provider_not_configured");
        }

        var normalizedRecipient = recipient.Trim().ToLowerInvariant();
        if (_recipientAllowList.Count > 0 && !_recipientAllowList.Contains(normalizedRecipient))
        {
            return TransactionalEmailSendResult.Failure("email_recipient_not_allowed");
        }

        try
        {
            var content = new EmailContent(message.Subject)
            {
                PlainText = message.PlainText,
                Html = message.Html
            };
            var email = new EmailMessage(_options.SenderAddress, recipient, content);
            if (!string.IsNullOrWhiteSpace(_options.ReplyToAddress))
            {
                email.ReplyTo.Add(new EmailAddress(_options.ReplyToAddress));
            }

            var operation = await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
            return TransactionalEmailSendResult.Success(operation.Id);
        }
        catch (RequestFailedException exception)
        {
            return TransactionalEmailSendResult.Failure(
                string.IsNullOrWhiteSpace(exception.ErrorCode)
                    ? "email_provider_rejected"
                    : $"email_provider_{NormalizeFailureCode(exception.ErrorCode)}");
        }
    }

    private static string NormalizeFailureCode(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
    }
}
