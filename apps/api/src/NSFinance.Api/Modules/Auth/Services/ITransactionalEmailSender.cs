namespace NSFinance.Api.Modules.Auth.Services;

public interface ITransactionalEmailSender
{
    bool IsConfigured { get; }

    Task<TransactionalEmailSendResult> SendAsync(
        string recipient,
        RenderedIdentityEmail message,
        CancellationToken cancellationToken);
}
