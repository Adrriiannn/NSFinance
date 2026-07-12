import { apiRequest } from "../../lib/api/client";
import type {
  AuthActionResponse,
  AuthFlowResponse,
  AuthTokenResponse,
  BeginTotpEnrollmentResponse,
  ChangePasswordRequest,
  CodeDeliveryResponse,
  ConfirmPasswordChangeCodeRequest,
  ConfirmEmailVerificationRequest,
  ConfirmTotpEnrollmentRequest,
  ConfirmTotpEnrollmentResponse,
  DisableMfaRequest,
  ForgotPasswordRequest,
  GoogleLoginRequest,
  GoogleAuthOptionsDto,
  LoginRequest,
  MfaStatusResponse,
  MicrosoftAuthOptionsDto,
  MicrosoftLoginRequest,
  PasswordRecoveryGrantResponse,
  PasswordPolicyCheckRequest,
  PasswordPolicyCheckResponse,
  RefreshTokenRequest,
  RegisterRequest,
  RegistrationResponse,
  RequestEmailVerificationRequest,
  ResetPasswordRequest,
  SessionDto,
  UserProfileDto,
  VerifyMfaLoginRequest,
  VerifyPasswordRecoveryCodeRequest,
  VerifyPasswordChangeCodeRequest
} from "../../types/api";

export function register(payload: RegisterRequest): Promise<RegistrationResponse> {
  return apiRequest<RegistrationResponse>("/api/auth/register", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function login(payload: LoginRequest): Promise<AuthFlowResponse> {
  return apiRequest<AuthFlowResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function checkPasswordPolicy(payload: PasswordPolicyCheckRequest): Promise<PasswordPolicyCheckResponse> {
  return apiRequest<PasswordPolicyCheckResponse>("/api/auth/password-policy/check", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function loginWithGoogle(payload: GoogleLoginRequest): Promise<AuthFlowResponse> {
  return apiRequest<AuthFlowResponse>("/api/auth/google", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function loginWithMicrosoft(payload: MicrosoftLoginRequest): Promise<AuthFlowResponse> {
  return apiRequest<AuthFlowResponse>("/api/auth/microsoft", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function refreshToken(payload: RefreshTokenRequest): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/refresh", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function getCurrentUser(): Promise<UserProfileDto> {
  return apiRequest<UserProfileDto>("/api/auth/me");
}

export function getSessions(): Promise<SessionDto[]> {
  return apiRequest<SessionDto[]>("/api/auth/sessions");
}

export function revokeSession(sessionId: string): Promise<void> {
  return apiRequest<void>(`/api/auth/sessions/${sessionId}`, {
    method: "DELETE"
  });
}

export function logout(): Promise<void> {
  return apiRequest<void>("/api/auth/logout", {
    method: "POST"
  });
}

export function logoutWithAccessToken(accessToken: string): Promise<void> {
  return apiRequest<void>("/api/auth/logout", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`
    }
  });
}

export function logoutAll(): Promise<{ revokedSessions: number }> {
  return apiRequest<{ revokedSessions: number }>("/api/auth/logout-all", {
    method: "POST"
  });
}

export function forgotPassword(payload: ForgotPasswordRequest): Promise<CodeDeliveryResponse> {
  return apiRequest<CodeDeliveryResponse>("/api/auth/forgot-password", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function verifyPasswordRecoveryCode(
  payload: VerifyPasswordRecoveryCodeRequest
): Promise<PasswordRecoveryGrantResponse> {
  return apiRequest<PasswordRecoveryGrantResponse>("/api/auth/password-recovery/verify", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function resetPassword(payload: ResetPasswordRequest): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/reset-password", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function requestEmailVerification(
  payload: RequestEmailVerificationRequest
): Promise<CodeDeliveryResponse> {
  return apiRequest<CodeDeliveryResponse>("/api/auth/verify-email/request", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function confirmEmailVerification(
  payload: ConfirmEmailVerificationRequest
): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/verify-email/confirm", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function changePassword(payload: ChangePasswordRequest): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/change-password", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function requestPasswordChangeCode(): Promise<CodeDeliveryResponse> {
  return apiRequest<CodeDeliveryResponse>("/api/auth/change-password/request-code", {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function verifyPasswordChangeCode(
  payload: VerifyPasswordChangeCodeRequest
): Promise<PasswordRecoveryGrantResponse> {
  return apiRequest<PasswordRecoveryGrantResponse>("/api/auth/change-password/verify-code", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function confirmPasswordChangeWithCode(
  payload: ConfirmPasswordChangeCodeRequest
): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/change-password/confirm", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function requestAccountDeletionCode(): Promise<CodeDeliveryResponse> {
  return apiRequest<CodeDeliveryResponse>("/api/auth/deletion/request-code", {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function getGoogleAuthOptions(): Promise<GoogleAuthOptionsDto> {
  return apiRequest<GoogleAuthOptionsDto>("/api/auth/providers/google");
}

export function getMicrosoftAuthOptions(): Promise<MicrosoftAuthOptionsDto> {
  return apiRequest<MicrosoftAuthOptionsDto>("/api/auth/providers/microsoft");
}

export function getMfaStatus(): Promise<MfaStatusResponse> {
  return apiRequest<MfaStatusResponse>("/api/auth/mfa/status");
}

export function beginTotpEnrollment(): Promise<BeginTotpEnrollmentResponse> {
  return apiRequest<BeginTotpEnrollmentResponse>("/api/auth/mfa/totp/enroll", {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function confirmTotpEnrollment(
  payload: ConfirmTotpEnrollmentRequest
): Promise<ConfirmTotpEnrollmentResponse> {
  return apiRequest<ConfirmTotpEnrollmentResponse>("/api/auth/mfa/totp/confirm", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function verifyMfaLogin(payload: VerifyMfaLoginRequest): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/mfa/challenge/verify", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function disableMfa(payload: DisableMfaRequest): Promise<void> {
  return apiRequest<void>("/api/auth/mfa/totp/disable", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}
