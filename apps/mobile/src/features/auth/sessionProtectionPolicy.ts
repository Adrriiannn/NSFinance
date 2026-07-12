import type { BiometricPreference } from "./biometricSecurity";

export function shouldRememberSession({
  explicitDecision,
  biometricPreference
}: {
  explicitDecision?: boolean;
  biometricPreference: BiometricPreference | null;
}): boolean {
  return explicitDecision ?? biometricPreference?.decision === "enabled";
}

export function shouldOfferSessionProtection({
  explicitDecision,
  biometricAvailable,
  biometricPreference
}: {
  explicitDecision?: boolean;
  biometricAvailable: boolean;
  biometricPreference: BiometricPreference | null;
}): boolean {
  return explicitDecision === undefined
    && biometricAvailable
    && biometricPreference === null;
}

export function shouldReviewBiometricFallback({
  fallbackUserId,
  authenticatedUserId,
  biometricPreference
}: {
  fallbackUserId: string | null;
  authenticatedUserId: string;
  biometricPreference: BiometricPreference | null;
}): boolean {
  return fallbackUserId === authenticatedUserId
    && biometricPreference?.decision === "enabled"
    && biometricPreference.fallbackReviewDismissed !== true;
}

export function shouldAutoPromptBiometric({
  isLocked,
  biometricAvailable,
  isForeground,
  alreadyAttempted
}: {
  isLocked: boolean;
  biometricAvailable: boolean;
  isForeground: boolean;
  alreadyAttempted: boolean;
}): boolean {
  return isLocked && biometricAvailable && isForeground && !alreadyAttempted;
}

export function canRenderProtectedRoutes({
  isBootstrapping,
  isLocked,
  isAuthenticated
}: {
  isBootstrapping: boolean;
  isLocked: boolean;
  isAuthenticated: boolean;
}): boolean {
  return !isBootstrapping && !isLocked && isAuthenticated;
}

export function shouldLockSessionForAppExit({
  rememberedSession,
  biometricEnabled
}: {
  rememberedSession: boolean;
  biometricEnabled: boolean;
}): boolean {
  return rememberedSession && biometricEnabled;
}
