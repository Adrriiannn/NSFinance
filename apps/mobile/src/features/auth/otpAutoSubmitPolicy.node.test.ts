import assert from "node:assert/strict";
import test from "node:test";
import {
  buildOtpAttemptKey,
  normalizeOtpCode,
  shouldAutoSubmitOtp
} from "./otpAutoSubmitPolicy";

test("OTP normalization accepts pasted formatting and keeps six digits", () => {
  assert.equal(normalizeOtpCode(" 12 34-56 "), "123456");
  assert.equal(normalizeOtpCode("1234567"), "123456");
});

test("OTP attempt key requires a challenge and a complete code", () => {
  assert.equal(buildOtpAttemptKey("challenge-1", "12345"), null);
  assert.equal(buildOtpAttemptKey("", "123456"), null);
  assert.equal(buildOtpAttemptKey("challenge-1", "123456"), "challenge-1:123456");
});

test("OTP auto-submit runs once when the sixth digit arrives", () => {
  assert.equal(
    shouldAutoSubmitOtp({
      challengeId: "challenge-1",
      code: "123456",
      isPending: false,
      lastAttemptKey: null
    }),
    true
  );
  assert.equal(
    shouldAutoSubmitOtp({
      challengeId: "challenge-1",
      code: "123456",
      isPending: false,
      lastAttemptKey: "challenge-1:123456"
    }),
    false
  );
});

test("OTP auto-submit waits while a request is in flight and accepts a changed code", () => {
  assert.equal(
    shouldAutoSubmitOtp({
      challengeId: "challenge-1",
      code: "654321",
      isPending: true,
      lastAttemptKey: null
    }),
    false
  );
  assert.equal(
    shouldAutoSubmitOtp({
      challengeId: "challenge-1",
      code: "654321",
      isPending: false,
      lastAttemptKey: "challenge-1:123456"
    }),
    true
  );
});
