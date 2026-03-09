using Microsoft.EntityFrameworkCore;
using NSFinTech.Api.Common.Contracts;
using NSFinTech.Api.Modules.Audit.Services;
using NSFinTech.Api.Modules.Users.DTOs;
using NSFinTech.Api.Persistence;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Modules.Users.Services;

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

        user.DisplayName = request.DisplayName.Trim();
        user.Timezone = request.Timezone.Trim();
        user.Locale = request.Locale.Trim();
        user.PreferredCurrency = request.PreferredCurrency.Trim().ToUpperInvariant();
        user.OnboardingStatus = request.OnboardingStatus.Trim();
        user.BiometricUnlockEnabled = request.BiometricUnlockEnabled;
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
                user.DisplayName,
                user.Timezone,
                user.Locale,
                user.PreferredCurrency,
                user.OnboardingStatus,
                user.BiometricUnlockEnabled
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
            user.DisplayName,
            user.Timezone,
            user.Locale,
            user.PreferredCurrency,
            user.OnboardingStatus,
            user.BiometricUnlockEnabled,
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
}
