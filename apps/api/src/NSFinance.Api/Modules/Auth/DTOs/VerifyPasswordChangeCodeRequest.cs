namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record VerifyPasswordChangeCodeRequest(
    Guid ChallengeId,
    string Code);
