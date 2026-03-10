namespace NSFinance.Api.Modules.Support.DTOs;

public sealed record CreateDeletionRequestRequest(
    string VerificationCode,
    string? Notes);
