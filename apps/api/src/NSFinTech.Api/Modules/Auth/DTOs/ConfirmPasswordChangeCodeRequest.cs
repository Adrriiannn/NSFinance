namespace NSFinTech.Api.Modules.Auth.DTOs;

public sealed record ConfirmPasswordChangeCodeRequest(
    string Code,
    string NewPassword);