namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record StartTrueLayerLinkResponse(
    Guid ConnectionId,
    Guid AttemptId,
    string Provider,
    string Environment,
    string AuthorizationUrl,
    IReadOnlyList<string> Scopes,
    DateTime ExpiresAtUtc);
