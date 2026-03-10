namespace NSFinance.Api.Modules.Policies.DTOs;

public sealed record AcceptPolicyRequest(
    string PolicyType,
    string PolicyVersion,
    string AcceptanceContext,
    string? Platform,
    string? AppVersion);
