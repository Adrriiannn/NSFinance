import { apiRequest } from "../../lib/api/client";
import type {
  AuthTokenResponse,
  LoginRequest,
  RegisterRequest,
  UserProfileDto
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

export function getCurrentUser(): Promise<UserProfileDto> {
  return apiRequest<UserProfileDto>("/api/auth/me");
}

export function logout(): Promise<void> {
  return apiRequest<void>("/api/auth/logout", {
    method: "POST"
  });
}
