import assert from "node:assert/strict";
import test from "node:test";
import {
  canRenderProtectedRoutes,
  resolveSessionProtection,
  shouldAutoPromptBiometric,
  shouldAutoStartRememberedMfa,
  shouldReviewBiometricFallback,
  shouldLockSessionForAppExit
} from "./sessionProtectionPolicy";

test("Remember me persists through an existing fingerprint preference", () => {
  assert.deepEqual(resolveSessionProtection({
    rememberRequested: true,
    biometricAvailable: true,
    biometricPreference: { userId: "user-1", decision: "enabled" },
    mfaEnabled: false
  }), {
    persistSession: true,
    offerBiometricSetup: false,
    requiresProtectionSetup: false,
    unlockMethod: "biometric"
  });
});

test("an explicit Remember me choice can revisit a prior fingerprint decline", () => {
  const resolution = resolveSessionProtection({
    rememberRequested: true,
    biometricAvailable: true,
    biometricPreference: { userId: "user-1", decision: "declined" },
    mfaEnabled: false
  });

  assert.equal(resolution.persistSession, false);
  assert.equal(resolution.offerBiometricSetup, true);
  assert.equal(resolution.unlockMethod, "sign_in");
});

test("a first protected-device login offers fingerprint without persisting first", () => {
  const resolution = resolveSessionProtection({
    rememberRequested: true,
    biometricAvailable: true,
    biometricPreference: null,
    mfaEnabled: false
  });

  assert.equal(resolution.persistSession, false);
  assert.equal(resolution.offerBiometricSetup, true);
});

test("MFA protects a remembered session when fingerprint is unavailable or disabled", () => {
  assert.deepEqual(resolveSessionProtection({
    rememberRequested: true,
    biometricAvailable: false,
    biometricPreference: null,
    mfaEnabled: true
  }), {
    persistSession: true,
    offerBiometricSetup: false,
    requiresProtectionSetup: false,
    unlockMethod: "mfa"
  });
});

test("Remember me is not persisted without fingerprint or MFA protection", () => {
  const resolution = resolveSessionProtection({
    rememberRequested: true,
    biometricAvailable: false,
    biometricPreference: null,
    mfaEnabled: false
  });

  assert.equal(resolution.persistSession, false);
  assert.equal(resolution.requiresProtectionSetup, true);
});

test("an unchecked Remember me never persists or prompts for setup", () => {
  const resolution = resolveSessionProtection({
    rememberRequested: false,
    biometricAvailable: true,
    biometricPreference: { userId: "user-1", decision: "enabled" },
    mfaEnabled: true
  });

  assert.equal(resolution.persistSession, false);
  assert.equal(resolution.offerBiometricSetup, false);
  assert.equal(resolution.requiresProtectionSetup, false);
});

test("automatic biometric prompt requires a foreground cold-launch lock", () => {
  assert.equal(shouldAutoPromptBiometric({
    isLocked: true,
    biometricAvailable: true,
    isForeground: true,
    alreadyAttempted: false
  }), true);
  assert.equal(shouldAutoPromptBiometric({
    isLocked: true,
    biometricAvailable: true,
    isForeground: false,
    alreadyAttempted: false
  }), false);
  assert.equal(shouldAutoPromptBiometric({
    isLocked: true,
    biometricAvailable: true,
    isForeground: true,
    alreadyAttempted: true
  }), false);
});

test("remembered-session MFA starts only for an active MFA lock", () => {
  assert.equal(shouldAutoStartRememberedMfa({
    isLocked: true,
    unlockMethod: "mfa",
    isForeground: true,
    alreadyAttempted: false
  }), true);
  assert.equal(shouldAutoStartRememberedMfa({
    isLocked: true,
    unlockMethod: "biometric",
    isForeground: true,
    alreadyAttempted: false
  }), false);
  assert.equal(shouldAutoStartRememberedMfa({
    isLocked: true,
    unlockMethod: "mfa",
    isForeground: false,
    alreadyAttempted: false
  }), false);
});

test("protected routes remain unavailable throughout bootstrap and lock", () => {
  assert.equal(canRenderProtectedRoutes({
    isBootstrapping: true,
    isLocked: false,
    isAuthenticated: true
  }), false);
  assert.equal(canRenderProtectedRoutes({
    isBootstrapping: false,
    isLocked: true,
    isAuthenticated: false
  }), false);
  assert.equal(canRenderProtectedRoutes({
    isBootstrapping: false,
    isLocked: false,
    isAuthenticated: true
  }), true);
});

test("fallback review is one-time and scoped to the same account", () => {
  assert.equal(shouldReviewBiometricFallback({
    fallbackUserId: "user-1",
    authenticatedUserId: "user-1",
    biometricPreference: { userId: "user-1", decision: "enabled" }
  }), true);
  assert.equal(shouldReviewBiometricFallback({
    fallbackUserId: "user-1",
    authenticatedUserId: "user-1",
    biometricPreference: {
      userId: "user-1",
      decision: "enabled",
      fallbackReviewDismissed: true
    }
  }), false);
  assert.equal(shouldReviewBiometricFallback({
    fallbackUserId: "user-1",
    authenticatedUserId: "user-2",
    biometricPreference: { userId: "user-2", decision: "enabled" }
  }), false);
  assert.equal(shouldReviewBiometricFallback({
    fallbackUserId: "user-1",
    authenticatedUserId: "user-1",
    biometricPreference: { userId: "user-1", decision: "enabled" },
    completedViaMfa: true
  }), false);
});

test("Android Back locks remembered sessions protected by fingerprint or MFA", () => {
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: true,
    biometricEnabled: true,
    mfaEnabled: false
  }), true);
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: false,
    biometricEnabled: true,
    mfaEnabled: true
  }), false);
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: true,
    biometricEnabled: false,
    mfaEnabled: true
  }), true);
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: true,
    biometricEnabled: false,
    mfaEnabled: false
  }), false);
});
