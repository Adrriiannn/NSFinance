namespace NSFinTech.Api.Modules.Policies.DTOs;

public sealed record ConsentRecordDto(
    string ConsentType,
    string Status,
    DateTime UpdatedUtc,
    DateTime? GrantedUtc,
    DateTime? RevokedUtc,
    string Source,
    string? MetadataJson);
