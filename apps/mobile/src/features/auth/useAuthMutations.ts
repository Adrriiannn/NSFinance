import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../lib/api/queryKeys";
import type {
  ChangePasswordRequest,
  ConfirmTotpEnrollmentRequest,
  DisableMfaRequest,
  ConfirmEmailVerificationRequest,
  ForgotPasswordRequest,
  GoogleLoginRequest,
  LoginRequest,
  MicrosoftLoginRequest,
  RegisterRequest,
  RequestEmailVerificationRequest,
  ResetPasswordRequest,
  VerifyMfaLoginRequest,
  VerifyPasswordRecoveryCodeRequest
} from "../../types/api";
import { useAuthSession } from "../../providers/AuthProvider";
import {
  beginTotpEnrollment,
  changePassword,
  confirmEmailVerification,
  confirmTotpEnrollment,
  disableMfa,
  forgotPassword,
  getCurrentUser,
  getMfaStatus,
  login,
  loginWithGoogle,
  loginWithMicrosoft,
  requestEmailVerification,
  resetPassword,
  register,
  verifyMfaLogin,
  verifyPasswordRecoveryCode
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
  return useMutation({
    mutationFn: (payload: LoginRequest) => login(payload)
  });
}

export function useGoogleLoginMutation() {
  return useMutation({
    mutationFn: (payload: GoogleLoginRequest) => loginWithGoogle(payload)
  });
}

export function useMicrosoftLoginMutation() {
  return useMutation({
    mutationFn: (payload: MicrosoftLoginRequest) => loginWithMicrosoft(payload)
  });
}

export function useRegisterMutation() {
  return useMutation({
    mutationFn: (payload: RegisterRequest) => register(payload)
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

export function useVerifyPasswordRecoveryCodeMutation() {
  return useMutation({
    mutationFn: (payload: VerifyPasswordRecoveryCodeRequest) =>
      verifyPasswordRecoveryCode(payload)
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

export function useMfaStatusQuery() {
  const { isAuthenticated } = useAuthSession();

  return useQuery({
    queryKey: [...queryKeys.auth.me, "mfa"],
    queryFn: getMfaStatus,
    enabled: isAuthenticated
  });
}

export function useBeginTotpEnrollmentMutation() {
  return useMutation({ mutationFn: beginTotpEnrollment });
}

export function useConfirmTotpEnrollmentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: ConfirmTotpEnrollmentRequest) => confirmTotpEnrollment(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.auth.me, "mfa"] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.auth.me });
    }
  });
}

export function useVerifyMfaLoginMutation() {
  return useMutation({
    mutationFn: (payload: VerifyMfaLoginRequest) => verifyMfaLogin(payload)
  });
}

export function useDisableMfaMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: DisableMfaRequest) => disableMfa(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.auth.me, "mfa"] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.auth.me });
    }
  });
}

export function useChangePasswordMutation() {
  return useMutation({
    mutationFn: (payload: ChangePasswordRequest) => changePassword(payload)
  });
}
