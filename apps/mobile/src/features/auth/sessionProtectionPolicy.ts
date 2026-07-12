import type { BiometricPreference } from "./biometricSecurity";

export type RememberedSessionUnlockMethod = "biometric" | "mfa" | "sign_in";

export function resolveSessionProtection({
  rememberRequested,
  biometricAvailable,
  biometricPreference,
  mfaEnabled
}: {
  rememberRequested: boolean;
  biometricAvailable: boolean;
  biometricPreference: BiometricPreference | null;
  mfaEnabled: boolean;
}) {
  const biometricEnabled = biometricAvailable && biometricPreference?.decision === "enabled";
  const unlockMethod: RememberedSessionUnlockMethod = biometricEnabled
    ? "biometric"
    : mfaEnabled
      ? "mfa"
      : "sign_in";

  return {
    persistSession: rememberRequested && unlockMethod !== "sign_in",
    offerBiometricSetup:
      rememberRequested
      && biometricAvailable
      && biometricPreference?.decision !== "enabled",
    requiresProtectionSetup:
      rememberRequested
      && !biometricAvailable
      && !mfaEnabled,
    unlockMethod
  };
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

export function shouldAutoStartRememberedMfa({
  isLocked,
  unlockMethod,
  isForeground,
  alreadyAttempted
}: {
  isLocked: boolean;
  unlockMethod: RememberedSessionUnlockMethod;
  isForeground: boolean;
  alreadyAttempted: boolean;
}): boolean {
  return isLocked
    && unlockMethod === "mfa"
    && isForeground
    && !alreadyAttempted;
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
  biometricEnabled,
  mfaEnabled
}: {
  rememberedSession: boolean;
  biometricEnabled: boolean;
  mfaEnabled: boolean;
}): boolean {
  return rememberedSession && (biometricEnabled || mfaEnabled);
}
