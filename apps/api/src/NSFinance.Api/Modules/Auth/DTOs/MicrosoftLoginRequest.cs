namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record MicrosoftLoginRequest(
    string AccessToken,
    DeviceContextDto? DeviceContext,
    bool AcceptPolicies = false,
    string? TermsVersion = null,
    string? PrivacyVersion = null,
    string? MfaTrustedDeviceToken = null);

public sealed record MicrosoftAuthOptionsResponse(
    bool IsConfigured,
    string? ClientId,
    string Authority,
    string? Scope);
