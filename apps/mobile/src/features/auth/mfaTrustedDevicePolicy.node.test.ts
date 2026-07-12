import assert from "node:assert/strict";
import test from "node:test";
import {
  isMfaTrustedDeviceCredentialUsable,
  parseMfaTrustedDeviceCredential
} from "./mfaTrustedDevicePolicy";

const VALID = {
  userId: "user-1",
  deviceFingerprint: "device-a",
  token: "a".repeat(64),
  expiresUtc: "2030-01-31T00:00:00.000Z"
};

test("trusted MFA device credential requires a valid complete shape", () => {
  assert.deepEqual(parseMfaTrustedDeviceCredential(JSON.stringify(VALID)), VALID);
  assert.equal(parseMfaTrustedDeviceCredential("not-json"), null);
  assert.equal(parseMfaTrustedDeviceCredential(JSON.stringify({ ...VALID, token: "short" })), null);
});

test("trusted MFA device credential is scoped to account, app installation, and expiry", () => {
  const credential = parseMfaTrustedDeviceCredential(JSON.stringify(VALID));
  const beforeExpiry = Date.parse("2030-01-30T00:00:00.000Z");

  assert.equal(isMfaTrustedDeviceCredentialUsable({
    credential,
    deviceFingerprint: "device-a",
    expectedUserId: "user-1",
    nowMs: beforeExpiry
  }), true);
  assert.equal(isMfaTrustedDeviceCredentialUsable({
    credential,
    deviceFingerprint: "device-b",
    expectedUserId: "user-1",
    nowMs: beforeExpiry
  }), false);
  assert.equal(isMfaTrustedDeviceCredentialUsable({
    credential,
    deviceFingerprint: "device-a",
    expectedUserId: "user-2",
    nowMs: beforeExpiry
  }), false);
  assert.equal(isMfaTrustedDeviceCredentialUsable({
    credential,
    deviceFingerprint: "device-a",
    expectedUserId: "user-1",
    nowMs: Date.parse(VALID.expiresUtc)
  }), false);
});
