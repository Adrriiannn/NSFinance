namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? Timezone,
    string? Locale,
    string? PreferredCurrency,
    DeviceContextDto? DeviceContext);
