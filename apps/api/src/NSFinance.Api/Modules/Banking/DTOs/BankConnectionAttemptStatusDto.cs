namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankConnectionAttemptStatusDto(
    Guid AttemptId,
    Guid ConnectionId,
    string Status,
    bool SafeToClose,
    bool ShouldAutoClose,
    bool ShouldAutoReturn,
    bool ManualActionRequired,
    string Headline,
    string Message,
    DateTime UpdatedUtc,
    DateTime ExpiresUtc,
    DateTime? CallbackHandledUtc,
    DateTime? AppReturnInitiatedUtc,
    DateTime? AppReturnConfirmedUtc,
    DateTime? CompletedUtc);
