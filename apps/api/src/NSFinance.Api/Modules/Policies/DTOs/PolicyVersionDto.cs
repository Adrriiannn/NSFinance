namespace NSFinance.Api.Modules.Policies.DTOs;

public sealed record PolicyVersionDto(
    string PolicyType,
    string PolicyName,
    string Version,
    DateTime EffectiveUtc,
    string ContentReference,
    string ContentMarkdown,
    bool IsActive);
