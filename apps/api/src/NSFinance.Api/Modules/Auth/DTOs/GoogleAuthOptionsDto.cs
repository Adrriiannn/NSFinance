namespace NSFinance.Api.Modules.Auth.DTOs;

public sealed record GoogleAuthOptionsDto(
    bool IsConfigured,
    string ProviderType,
    string? AuthorizationUrl,
    string? CallbackPath,
    string Message);
