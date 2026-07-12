namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record ResetPasswordRequest(
    Guid ChallengeId,
    string RecoveryToken,
    string NewPassword);
