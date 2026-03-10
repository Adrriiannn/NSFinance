namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record RefreshTokenRequest(
    string RefreshToken,
    DeviceContextDto? DeviceContext);
