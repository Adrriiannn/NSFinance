namespace NSFinTech.Api.Modules.Policies.DTOs;

public sealed record UpdateConsentRequest(
    string ConsentType,
    string Status,
    string Source,
    string? MetadataJson);
