namespace NSFinTech.Api.Modules.Policies.DTOs;

public sealed record PolicyVersionDto(
    string PolicyType,
    string PolicyName,
    string Version,
    DateTime EffectiveUtc,
    string ContentReference,
    bool IsActive);
