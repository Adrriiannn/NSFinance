namespace NSFinance.Api.Modules.Auth.Services;

public sealed record GoogleIdentityPayload(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    string? GivenName,
    string? FamilyName,
    string? PictureUrl);
