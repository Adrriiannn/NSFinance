namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record TrueLayerCallbackOutcome(
    bool Succeeded,
    string Code,
    string Message,
    int HttpStatusCode,
    Guid? ConnectionId,
    string? AppReturnUri,
    bool SafeToClose = true,
    bool ShouldAutoReturn = false,
    string? CallbackLifecycleStage = null,
    string? CallbackLifecycleReason = null);
