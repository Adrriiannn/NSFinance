import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  ChangePasswordRequest,
  ConfirmEmailVerificationRequest,
  ForgotPasswordRequest,
  GoogleLoginRequest,
  LoginRequest,
  RegisterRequest,
  RequestEmailVerificationRequest,
  ResetPasswordRequest
} from "../../types/api";
import { useAuthSession } from "../../providers/AuthProvider";
import {
  changePassword,
  confirmEmailVerification,
  forgotPassword,
  getCurrentUser,
  login,
  loginWithGoogle,
  requestEmailVerification,
  resetPassword,
  register
} from "./authApi";

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

export function useGoogleLoginMutation() {
  const queryClient = useQueryClient();
  const { applyAuthTokenResponse, refreshSessionUser } = useAuthSession();

  return useMutation({
    mutationFn: (payload: GoogleLoginRequest) => loginWithGoogle(payload),
    onSuccess: async (response) => {
      await applyAuthTokenResponse(response);
      void refreshSessionUser();
      void queryClient.invalidateQueries();
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

export function useForgotPasswordMutation() {
  return useMutation({
    mutationFn: (payload: ForgotPasswordRequest) => forgotPassword(payload)
  });
}

export function useResetPasswordMutation() {
  return useMutation({
    mutationFn: (payload: ResetPasswordRequest) => resetPassword(payload)
  });
}

export function useRequestEmailVerificationMutation() {
  return useMutation({
    mutationFn: (payload: RequestEmailVerificationRequest) => requestEmailVerification(payload)
  });
}

export function useConfirmEmailVerificationMutation() {
  return useMutation({
    mutationFn: (payload: ConfirmEmailVerificationRequest) => confirmEmailVerification(payload)
  });
}

export function useChangePasswordMutation() {
  return useMutation({
    mutationFn: (payload: ChangePasswordRequest) => changePassword(payload)
  });
}
