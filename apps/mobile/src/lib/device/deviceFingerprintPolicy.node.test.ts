import assert from "node:assert/strict";
import test from "node:test";
import { buildDeviceFingerprint } from "./deviceFingerprintPolicy";

test("platform-scoped device identity is stable and separates Android devices", () => {
  const first = buildDeviceFingerprint({
    platform: "android",
    platformScopedId: "android-id-one",
    fallbackParts: ["same-model"]
  });
  const same = buildDeviceFingerprint({
    platform: "android",
    platformScopedId: "android-id-one",
    fallbackParts: ["changed-model"]
  });
  const second = buildDeviceFingerprint({
    platform: "android",
    platformScopedId: "android-id-two",
    fallbackParts: ["same-model"]
  });

  assert.equal(first, same);
  assert.notEqual(first, second);
  assert.match(first, /^android:id:/);
});

test("device identity has a deterministic fallback without exposing raw values", () => {
  const first = buildDeviceFingerprint({
    platform: "android",
    fallbackParts: ["Samsung", "SM-G991B", "14"]
  });
  const same = buildDeviceFingerprint({
    platform: "android",
    fallbackParts: ["Samsung", "SM-G991B", "14"]
  });

  assert.equal(first, same);
  assert.match(first, /^android:fallback:/);
  assert.equal(first.includes("Samsung"), false);
  assert.equal(buildDeviceFingerprint({
    platform: "web",
    fallbackParts: []
  }), "web:unknown-device");
});
