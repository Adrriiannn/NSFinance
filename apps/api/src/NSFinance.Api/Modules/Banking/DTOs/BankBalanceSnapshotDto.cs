namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankBalanceSnapshotDto(
    Guid Id,
    Guid LinkedBankAccountId,
    decimal? Available,
    decimal? Current,
    decimal? Overdraft,
    string Currency,
    DateTime CapturedUtc);
