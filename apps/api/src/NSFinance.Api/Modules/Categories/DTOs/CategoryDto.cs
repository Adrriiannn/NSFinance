namespace NSFinance.Api.Modules.Categories.DTOs;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Type,
    DateTime CreatedUtc);
