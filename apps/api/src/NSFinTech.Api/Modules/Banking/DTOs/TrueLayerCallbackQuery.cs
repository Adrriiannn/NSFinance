namespace NSFinTech.Api.Modules.Banking.DTOs;

public sealed record TrueLayerCallbackQuery(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription);
