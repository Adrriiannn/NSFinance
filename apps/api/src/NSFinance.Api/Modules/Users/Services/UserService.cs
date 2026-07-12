using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Audit.Services;
using NSFinance.Api.Modules.Users;
using NSFinance.Api.Modules.Users.DTOs;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Users.Services;

public sealed class UserService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    IAuditService auditService)
{
    public async Task<ServiceResult<UserProfileDetailsDto>> GetProfileAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<UserProfileDetailsDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return ServiceResult<UserProfileDetailsDto>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        return ServiceResult<UserProfileDetailsDto>.Ok(MapProfile(user));
    }

    public async Task<ServiceResult<UserProfileDetailsDto>> UpdateProfileAsync(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<UserProfileDetailsDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserProfileDetailsDto>.Fail("User not found.", "user_not_found", StatusCodes.Status404NotFound);
        }

        user.FullName = request.FullName.Trim();

        var normalizedNsTag = NsTagPolicy.Normalize(request.DisplayName);
        if (!NsTagPolicy.IsValid(normalizedNsTag))
        {
            return ServiceResult<UserProfileDetailsDto>.Fail(
                NsTagPolicy.ValidationMessage,
                "invalid_ns_tag",
                StatusCodes.Status400BadRequest);
        }

        var existingTagOwner = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != userId && x.DisplayName.ToLower() == normalizedNsTag,
                cancellationToken);

        if (existingTagOwner)
        {
            return ServiceResult<UserProfileDetailsDto>.Fail(
                "NS Tag is already in use.",
                "ns_tag_already_in_use",
                StatusCodes.Status409Conflict);
        }

        user.DisplayName = normalizedNsTag;
        user.Handle = normalizedNsTag;
        user.ProfileImageUrl = NormalizeNullable(request.ProfileImageUrl);
        user.ProfileSubtitle = NormalizeNullable(request.ProfileSubtitle);
        user.Timezone = request.Timezone.Trim();
        user.Locale = request.Locale.Trim();
        user.PreferredCurrency = request.PreferredCurrency.Trim().ToUpperInvariant();
        user.OnboardingStatus = request.OnboardingStatus.Trim();
        user.DateOfBirth = request.DateOfBirth?.Date;
        user.CountryRegion = NormalizeNullable(request.CountryRegion);
        user.FinancialFocusJson = SerializeFocus(request.FinancialFocus);
        user.EmploymentStatus = NormalizeNullable(request.EmploymentStatus);
        user.IncomeStability = NormalizeNullable(request.IncomeStability);
        user.PrimaryFinancialConcern = NormalizeNullable(request.PrimaryFinancialConcern);
        user.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "account",
            eventName: "profile_updated",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                user.PrimaryEmail,
                user.FullName,
                user.DisplayName,
                user.Handle,
                user.Timezone,
                user.Locale,
                user.PreferredCurrency,
                user.OnboardingStatus,
                user.TwoFactorEnabled
            },
            cancellationToken);

        return ServiceResult<UserProfileDetailsDto>.Ok(MapProfile(user));
    }

    public async Task<ServiceResult<UserPreferenceDto>> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<UserPreferenceDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var preferences = await EnsurePreferenceAsync(userId, cancellationToken);
        return ServiceResult<UserPreferenceDto>.Ok(MapPreferences(preferences));
    }

    public async Task<ServiceResult<UserPreferenceDto>> UpdatePreferencesAsync(
        UpdateUserPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUserProvider.TryGetUserId(out var userId))
        {
            return ServiceResult<UserPreferenceDto>.Fail("Unauthorized.", "unauthorized", StatusCodes.Status401Unauthorized);
        }

        var preferences = await EnsurePreferenceAsync(userId, cancellationToken);
        preferences.AdviceTonePreference = request.AdviceTonePreference.Trim();
        preferences.DigestFrequency = request.DigestFrequency.Trim();
        preferences.ReminderPreference = request.ReminderPreference.Trim();
        preferences.NotificationPreferencesJson = NormalizeJson(request.NotificationPreferencesJson);
        preferences.PrivacyPreferencesJson = NormalizeJson(request.PrivacyPreferencesJson);
        preferences.EssentialCategoryPreferencesJson = NormalizeJson(request.EssentialCategoryPreferencesJson);
        preferences.FutureGoalConfigurationJson = NormalizeJson(request.FutureGoalConfigurationJson);
        preferences.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteEventAsync(
            category: "account",
            eventName: "privacy_preferences_updated",
            targetEntityType: "user",
            targetEntityId: userId.ToString(),
            actorId: userId,
            actorType: "user",
            metadata: new
            {
                preferences.AdviceTonePreference,
                preferences.DigestFrequency,
                preferences.ReminderPreference
            },
            cancellationToken);

        return ServiceResult<UserPreferenceDto>.Ok(MapPreferences(preferences));
    }

    private async Task<UserPreference> EnsurePreferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var preferences = await dbContext.UserPreferences
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (preferences is not null)
        {
            return preferences;
        }

        preferences = new UserPreference
        {
            UserId = userId,
            UpdatedUtc = DateTime.UtcNow
        };

        dbContext.UserPreferences.Add(preferences);
        await dbContext.SaveChangesAsync(cancellationToken);
        return preferences;
    }

    private static UserProfileDetailsDto MapProfile(User user)
    {
        return new UserProfileDetailsDto(
            user.Id,
            user.PrimaryEmail,
            user.FullName,
            user.DisplayName,
            user.Handle,
            user.ProfileImageUrl,
            user.ProfileSubtitle,
            user.Timezone,
            user.Locale,
            user.PreferredCurrency,
            user.OnboardingStatus,
            user.TwoFactorEnabled,
            user.PhoneNumber,
            user.DateOfBirth,
            user.CountryRegion,
            DeserializeFocus(user.FinancialFocusJson),
            user.EmploymentStatus,
            user.IncomeStability,
            user.PrimaryFinancialConcern,
            user.EmailVerified,
            user.PlanTier,
            user.CreatedUtc,
            user.LastLoginUtc);
    }

    private static UserPreferenceDto MapPreferences(UserPreference preferences)
    {
        return new UserPreferenceDto(
            preferences.AdviceTonePreference,
            preferences.DigestFrequency,
            preferences.ReminderPreference,
            preferences.NotificationPreferencesJson,
            preferences.PrivacyPreferencesJson,
            preferences.EssentialCategoryPreferencesJson,
            preferences.FutureGoalConfigurationJson,
            preferences.UpdatedUtc);
    }

    private static string NormalizeJson(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? "{}" : raw.Trim();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string SerializeFocus(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "[]";
        }

        var normalized = values
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyList<string> DeserializeFocus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
