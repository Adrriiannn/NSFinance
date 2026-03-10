namespace NSFinance.Api.Modules.Policies.DTOs;

public sealed record PolicyAcceptanceDto(
    string PolicyType,
    string PolicyVersion,
    DateTime AcceptedUtc,
    string AcceptanceContext,
    string? Platform,
    string? AppVersion);
