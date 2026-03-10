using NSFinTech.Api.Modules.Users.DTOs;
using NSFinTech.Api.Modules.Users;

namespace NSFinTech.Api.Modules.Users.Validators;

public static class UpdateUserProfileValidator
{
    public static Dictionary<string, string[]> Validate(UpdateUserProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.PrimaryEmail)
            || request.PrimaryEmail.Trim().Length > 256
            || !request.PrimaryEmail.Contains('@'))
        {
            errors["primaryEmail"] = ["Primary email must be a valid email address and must not exceed 256 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length is < 2 or > 160)
        {
            errors["fullName"] = ["Full name must be between 2 and 160 characters."];
        }

        var normalizedNsTag = NsTagPolicy.Normalize(request.DisplayName);
        if (!NsTagPolicy.IsValid(normalizedNsTag))
        {
            errors["displayName"] = [NsTagPolicy.ValidationMessage];
        }

        if (!string.IsNullOrWhiteSpace(request.Handle) && request.Handle.Trim().Length > 80)
        {
            errors["handle"] = ["Handle must not exceed 80 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileImageUrl) && request.ProfileImageUrl.Trim().Length > 512)
        {
            errors["profileImageUrl"] = ["Profile image URL must not exceed 512 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileSubtitle) && request.ProfileSubtitle.Trim().Length > 180)
        {
            errors["profileSubtitle"] = ["Profile subtitle must not exceed 180 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Timezone) || request.Timezone.Trim().Length > 64)
        {
            errors["timezone"] = ["Timezone is required and must not exceed 64 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Locale) || request.Locale.Trim().Length > 16)
        {
            errors["locale"] = ["Locale is required and must not exceed 16 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.PreferredCurrency) || request.PreferredCurrency.Trim().Length != 3)
        {
            errors["preferredCurrency"] = ["Preferred currency must be an ISO 3-letter code."];
        }

        if (string.IsNullOrWhiteSpace(request.OnboardingStatus) || request.OnboardingStatus.Trim().Length > 40)
        {
            errors["onboardingStatus"] = ["Onboarding status is required and must not exceed 40 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && request.PhoneNumber.Trim().Length > 40)
        {
            errors["phoneNumber"] = ["Phone number must not exceed 40 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.CountryRegion) && request.CountryRegion.Trim().Length > 80)
        {
            errors["countryRegion"] = ["Country/region must not exceed 80 characters."];
        }

        if (request.DateOfBirth.HasValue)
        {
            var dob = request.DateOfBirth.Value.Date;
            if (dob > DateTime.UtcNow.Date)
            {
                errors["dateOfBirth"] = ["Date of birth cannot be in the future."];
            }
            else if (dob > DateTime.UtcNow.Date.AddYears(-8))
            {
                errors["dateOfBirth"] = ["Date of birth must be at least 8 years ago."];
            }
            else if (dob < DateTime.UtcNow.Date.AddYears(-120))
            {
                errors["dateOfBirth"] = ["Date of birth must be within a reasonable range."];
            }
        }

        if (request.FinancialFocus is { Count: > 20 })
        {
            errors["financialFocus"] = ["You can store up to 20 financial focus tags."];
        }

        if (!string.IsNullOrWhiteSpace(request.EmploymentStatus) && request.EmploymentStatus.Trim().Length > 40)
        {
            errors["employmentStatus"] = ["Employment status must not exceed 40 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.IncomeStability) && request.IncomeStability.Trim().Length > 40)
        {
            errors["incomeStability"] = ["Income stability must not exceed 40 characters."];
        }

        if (!string.IsNullOrWhiteSpace(request.PrimaryFinancialConcern) && request.PrimaryFinancialConcern.Trim().Length > 60)
        {
            errors["primaryFinancialConcern"] = ["Primary financial concern must not exceed 60 characters."];
        }

        return errors;
    }
}
