namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record CreateDeletionRequestRequest(
    Guid ChallengeId,
    string Code,
    string? Notes);
