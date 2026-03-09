import { apiRequest } from "../../lib/api/client";
import type {
  UpdateUserPreferenceRequest,
  UpdateUserProfileRequest,
  UserPreferenceDto,
  UserProfileDetailsDto
} from "../../types/api";

export function getUserProfile(): Promise<UserProfileDetailsDto> {
  return apiRequest<UserProfileDetailsDto>("/api/users/profile");
}

export function updateUserProfile(payload: UpdateUserProfileRequest): Promise<UserProfileDetailsDto> {
  return apiRequest<UserProfileDetailsDto>("/api/users/profile", {
    method: "PATCH",
    body: JSON.stringify(payload)
  });
}

export function getUserPreferences(): Promise<UserPreferenceDto> {
  return apiRequest<UserPreferenceDto>("/api/users/preferences");
}

export function updateUserPreferences(
  payload: UpdateUserPreferenceRequest
): Promise<UserPreferenceDto> {
  return apiRequest<UserPreferenceDto>("/api/users/preferences", {
    method: "PATCH",
    body: JSON.stringify(payload)
  });
}
