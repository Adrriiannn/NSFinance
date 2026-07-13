import assert from "node:assert/strict";
import test from "node:test";
import {
  clearPendingAuthFlows,
  getPendingEmailVerification,
  getPendingMfaLogin,
  stageEmailVerification,
  stageMfaLogin
} from "./pendingAuthFlow";

test("Remember me survives a fresh-login MFA continuation", () => {
  clearPendingAuthFlows();
  stageMfaLogin({
    challengeId: "challenge-1",
    challengeToken: "token-1",
    expiresUtc: "2026-07-12T22:00:00.000Z",
    methods: ["totp", "recovery_code"],
    accountHint: "tes****@test.local",
    context: "fresh_login",
    rememberSession: true
  });

  assert.equal(getPendingMfaLogin()?.rememberSession, true);
  assert.equal(getPendingMfaLogin()?.context, "fresh_login");
});

test("remembered-session MFA stays distinct from fresh-login MFA", () => {
  clearPendingAuthFlows();
  stageMfaLogin({
    challengeId: "challenge-2",
    challengeToken: "token-2",
    expiresUtc: "2026-07-12T22:00:00.000Z",
    methods: ["totp"],
    accountHint: "tes****@test.local",
    context: "remembered_session",
    rememberSession: true
  });

  assert.equal(getPendingMfaLogin()?.context, "remembered_session");
});

test("email verification preserves the same Remember me decision", () => {
  clearPendingAuthFlows();
  stageEmailVerification({
    challengeId: "email-1",
    expiresUtc: "2026-07-12T22:00:00.000Z",
    resendAfterSeconds: 60,
    message: "Check your email.",
    email: "redacted@test.local",
    rememberSession: true
  });

  assert.equal(getPendingEmailVerification()?.rememberSession, true);
  assert.equal(getPendingMfaLogin(), null);
  clearPendingAuthFlows();
});
