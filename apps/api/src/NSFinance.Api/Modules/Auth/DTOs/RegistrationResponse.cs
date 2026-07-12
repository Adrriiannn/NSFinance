namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record RegistrationResponse(
    string Status,
    Guid ChallengeId,
    DateTime ExpiresUtc,
    int ResendAfterSeconds,
    string Message);
