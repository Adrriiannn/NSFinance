namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record TrueLayerCallbackOutcome(
    bool Succeeded,
    string Code,
    string Message,
    int HttpStatusCode,
    Guid? ConnectionId,
    string? AppReturnUri);
