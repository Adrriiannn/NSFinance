namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record VerifyPasswordRecoveryCodeRequest(
    Guid ChallengeId,
    string Code);
