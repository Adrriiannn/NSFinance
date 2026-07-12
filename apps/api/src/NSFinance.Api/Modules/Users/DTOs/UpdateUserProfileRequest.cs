namespace NSFinance.Api.Modules.Users.DTOs;

public sealed record UpdateUserProfileRequest(
    string FullName,
    string DisplayName,
    string? Handle,
    string? ProfileImageUrl,
    string? ProfileSubtitle,
    string Timezone,
    string Locale,
    string PreferredCurrency,
    string OnboardingStatus,
    DateTime? DateOfBirth,
    string? CountryRegion,
    IReadOnlyList<string>? FinancialFocus,
    string? EmploymentStatus,
    string? IncomeStability,
    string? PrimaryFinancialConcern);
