namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record CodeDeliveryResponse(
    Guid ChallengeId,
    DateTime ExpiresUtc,
    int ResendAfterSeconds,
    string Message);
