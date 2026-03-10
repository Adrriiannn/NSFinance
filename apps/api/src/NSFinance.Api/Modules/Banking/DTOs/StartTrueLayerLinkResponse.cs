namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record StartTrueLayerLinkResponse(
    Guid ConnectionId,
    string Provider,
    string Environment,
    string AuthorizationUrl,
    IReadOnlyList<string> Scopes,
    DateTime ExpiresAtUtc);
