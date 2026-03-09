namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
