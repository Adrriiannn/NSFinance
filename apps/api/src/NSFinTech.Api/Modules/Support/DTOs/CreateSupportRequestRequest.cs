namespace NSFinTech.Api.Modules.Support.DTOs;

public sealed record CreateSupportRequestRequest(
    string Category,
    string Message);
