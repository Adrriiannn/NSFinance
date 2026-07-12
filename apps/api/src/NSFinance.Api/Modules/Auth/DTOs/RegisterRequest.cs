namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? Timezone,
    string? Locale,
    string? PreferredCurrency,
    DeviceContextDto? DeviceContext,
    string? CaptchaToken = null,
    bool AcceptPolicies = false,
    string? TermsVersion = null,
    string? PrivacyVersion = null);
