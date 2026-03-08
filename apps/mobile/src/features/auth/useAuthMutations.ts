import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type { LoginRequest, RegisterRequest } from "../../types/api";
import { useAuthSession } from "../../providers/AuthProvider";
import { getCurrentUser, login, register } from "./authApi";

export function useCurrentUserQuery() {
  const { isAuthenticated } = useAuthSession();

  return useQuery({
    queryKey: queryKeys.auth.me,
    queryFn: getCurrentUser,
    enabled: isAuthenticated
  });
}

export function useLoginMutation() {
  const queryClient = useQueryClient();
  const { applyAuthTokenResponse } = useAuthSession();

  return useMutation({
    mutationFn: (payload: LoginRequest) => login(payload),
    onSuccess: async (response) => {
      await applyAuthTokenResponse(response);
      await queryClient.invalidateQueries();
    }
  });
}

export function useRegisterMutation() {
  const queryClient = useQueryClient();
  const { applyAuthTokenResponse } = useAuthSession();

  return useMutation({
    mutationFn: (payload: RegisterRequest) => register(payload),
    onSuccess: async (response) => {
      await applyAuthTokenResponse(response);
      await queryClient.invalidateQueries();
    }
  });
}
