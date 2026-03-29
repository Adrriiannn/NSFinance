namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record BankRecurringPaymentsDto(
    IReadOnlyList<BankDirectDebitDto> DirectDebits,
    IReadOnlyList<BankStandingOrderDto> StandingOrders);
