import assert from "node:assert/strict";
import test from "node:test";
import {
  getMfaChallengeRemainingMs,
  isMfaChallengeExpired
} from "./mfaChallengePolicy";

test("MFA challenge policy distinguishes active and expired server deadlines", () => {
  const now = Date.parse("2026-07-12T20:00:00.000Z");

  assert.equal(
    getMfaChallengeRemainingMs("2026-07-12T20:10:00.000Z", now),
    10 * 60 * 1000
  );
  assert.equal(isMfaChallengeExpired("2026-07-12T20:10:00.000Z", now), false);
  assert.equal(isMfaChallengeExpired("2026-07-12T20:00:00.000Z", now), true);
  assert.equal(isMfaChallengeExpired("not-a-date", now), true);
});
