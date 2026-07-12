namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record ConfirmPasswordChangeCodeRequest(
    Guid ChallengeId,
    string GrantToken,
    string NewPassword);
