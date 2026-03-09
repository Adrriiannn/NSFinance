import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type {
  UpdateUserPreferenceRequest,
  UpdateUserProfileRequest
} from "../../types/api";
import {
  getUserPreferences,
  getUserProfile,
  updateUserPreferences,
  updateUserProfile
} from "./usersApi";

const userKeys = {
  profile: ["user", "profile"] as const,
  preferences: ["user", "preferences"] as const
};

export function useUserProfileQuery() {
  return useQuery({
    queryKey: userKeys.profile,
    queryFn: getUserProfile
  });
}

export function useUpdateUserProfileMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateUserProfileRequest) => updateUserProfile(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userKeys.profile });
    }
  });
}

export function useUserPreferencesQuery() {
  return useQuery({
    queryKey: userKeys.preferences,
    queryFn: getUserPreferences
  });
}

export function useUpdateUserPreferencesMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateUserPreferenceRequest) => updateUserPreferences(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userKeys.preferences });
    }
  });
}
