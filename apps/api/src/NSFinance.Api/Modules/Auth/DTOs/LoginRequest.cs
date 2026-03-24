namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record LoginRequest(
    string Email,
    string Password,
    DeviceContextDto? DeviceContext,
    string? CaptchaToken = null);
