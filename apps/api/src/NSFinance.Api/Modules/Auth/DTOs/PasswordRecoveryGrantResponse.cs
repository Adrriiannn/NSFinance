namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record PasswordRecoveryGrantResponse(
    Guid ChallengeId,
    string RecoveryToken,
    DateTime ExpiresUtc);
