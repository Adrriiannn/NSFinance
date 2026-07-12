namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record GoogleLoginRequest(
    string IdToken,
    DeviceContextDto? DeviceContext,
    bool AcceptPolicies = false,
    string? TermsVersion = null,
    string? PrivacyVersion = null);
