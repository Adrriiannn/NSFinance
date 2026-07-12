namespace NSFinance.Api.Modules.Auth.Services;

public sealed record MicrosoftIdentityPayload(
    string ProviderSubject,
    string TenantId,
    string ObjectId,
    string Email,
    string? Name,
    string? GivenName,
    string? FamilyName);
