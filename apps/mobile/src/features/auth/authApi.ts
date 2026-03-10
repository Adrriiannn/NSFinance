import { apiRequest } from "../../lib/api/client";
import type {
  AuthActionResponse,
  AuthTokenResponse,
  ChangePasswordRequest,
  ConfirmPasswordChangeCodeRequest,
  ConfirmEmailVerificationRequest,
  ForgotPasswordRequest,
  GoogleLoginRequest,
  GoogleAuthOptionsDto,
  LoginRequest,
  RefreshTokenRequest,
  RegisterRequest,
  RequestEmailVerificationRequest,
  ResetPasswordRequest,
  SessionDto,
  UserProfileDto,
  VerifyPasswordChangeCodeRequest
} from "../../types/api";

export function register(payload: RegisterRequest): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/register", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function login(payload: LoginRequest): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function loginWithGoogle(payload: GoogleLoginRequest): Promise<AuthTokenResponse> {
  return apiRequest<AuthTokenResponse>("/api/auth/google", {
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

export function logoutAll(): Promise<{ revokedSessions: number }> {
  return apiRequest<{ revokedSessions: number }>("/api/auth/logout-all", {
    method: "POST"
  });
}

export function forgotPassword(payload: ForgotPasswordRequest): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/forgot-password", {
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
): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/verify-email/request", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

export function confirmEmailVerification(
  payload: ConfirmEmailVerificationRequest
): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/verify-email/confirm", {
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

export function requestPasswordChangeCode(): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/change-password/request-code", {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function verifyPasswordChangeCode(
  payload: VerifyPasswordChangeCodeRequest
): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/change-password/verify-code", {
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

export function requestAccountDeletionCode(): Promise<AuthActionResponse> {
  return apiRequest<AuthActionResponse>("/api/auth/deletion/request-code", {
    method: "POST",
    body: JSON.stringify({})
  });
}

export function getGoogleAuthOptions(): Promise<GoogleAuthOptionsDto> {
  return apiRequest<GoogleAuthOptionsDto>("/api/auth/providers/google");
}
