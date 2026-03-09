namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record ResetPasswordRequest(
    string Token,
    string NewPassword);
