namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record GlobalBankSyncRequest(
    string? Trigger,
    string? Source);
