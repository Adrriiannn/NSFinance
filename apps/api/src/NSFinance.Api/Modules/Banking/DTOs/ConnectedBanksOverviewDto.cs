namespace NSFinance.Api.Modules.Banking.DTOs;

public sealed record ConnectedBanksOverviewDto(
    IReadOnlyList<BankConnectionDto> ActiveConnections,
    IReadOnlyList<BankConnectionDto> AttentionConnections);
