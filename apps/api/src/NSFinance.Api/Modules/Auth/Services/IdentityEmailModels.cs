namespace NSFinance.Api.Modules.Auth.Services;

public sealed record IdentityEmailPayload(
    string DisplayName,
    string? Code,
    int? ExpiresInMinutes,
    DateTime OccurredUtc,
    string? SecurityUrl = null);

public sealed record RenderedIdentityEmail(
    string Subject,
    string PlainText,
    string Html);

public sealed record TransactionalEmailSendResult(
    bool Accepted,
    string? ProviderMessageId,
    string? FailureCode)
{
    public static TransactionalEmailSendResult Success(string providerMessageId) =>
        new(true, providerMessageId, null);

    public static TransactionalEmailSendResult Failure(string failureCode) =>
        new(false, null, failureCode);
}
