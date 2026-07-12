import assert from "node:assert/strict";
import test from "node:test";
import {
  canRenderProtectedRoutes,
  shouldAutoPromptBiometric,
  shouldOfferSessionProtection,
  shouldRememberSession,
  shouldReviewBiometricFallback,
  shouldLockSessionForAppExit
} from "./sessionProtectionPolicy";

test("an existing fingerprint preference remembers the session without another prompt", () => {
  assert.equal(shouldRememberSession({
    biometricPreference: { userId: "user-1", decision: "enabled" }
  }), true);
  assert.equal(shouldOfferSessionProtection({
    biometricAvailable: true,
    biometricPreference: { userId: "user-1", decision: "enabled" }
  }), false);
});

test("a declined device decision keeps the session in memory only", () => {
  assert.equal(shouldRememberSession({
    biometricPreference: { userId: "user-1", decision: "declined" }
  }), false);
  assert.equal(shouldOfferSessionProtection({
    biometricAvailable: true,
    biometricPreference: { userId: "user-1", decision: "declined" }
  }), false);
});

test("a first device login offers protection without persisting first", () => {
  assert.equal(shouldRememberSession({ biometricPreference: null }), false);
  assert.equal(shouldOfferSessionProtection({
    biometricAvailable: true,
    biometricPreference: null
  }), true);
});

test("a device without enrolled biometrics never receives a fingerprint offer", () => {
  assert.equal(shouldOfferSessionProtection({
    biometricAvailable: false,
    biometricPreference: null
  }), false);
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
});

test("Android Back locks only remembered sessions protected by biometrics", () => {
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: true,
    biometricEnabled: true
  }), true);
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: false,
    biometricEnabled: true
  }), false);
  assert.equal(shouldLockSessionForAppExit({
    rememberedSession: true,
    biometricEnabled: false
  }), false);
});
