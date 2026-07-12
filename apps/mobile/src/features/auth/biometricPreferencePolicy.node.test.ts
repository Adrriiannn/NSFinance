import assert from "node:assert/strict";
import test from "node:test";
import {
  parseBiometricPreferenceStore,
  setBiometricPreference
} from "./biometricPreferencePolicy";

test("biometric preference storage migrates the former single-account shape", () => {
  const store = parseBiometricPreferenceStore(JSON.stringify({
    userId: "user-1",
    decision: "enabled"
  }));

  assert.equal(store.preferences["user-1"]?.decision, "enabled");
});

test("biometric preferences remain independently scoped to each account", () => {
  const first = setBiometricPreference(parseBiometricPreferenceStore(null), {
    userId: "user-1",
    decision: "enabled"
  });
  const second = setBiometricPreference(first, {
    userId: "user-2",
    decision: "declined"
  });

  assert.equal(second.preferences["user-1"]?.decision, "enabled");
  assert.equal(second.preferences["user-2"]?.decision, "declined");
});

test("invalid biometric preference storage fails closed", () => {
  assert.deepEqual(parseBiometricPreferenceStore("not-json").preferences, {});
  assert.deepEqual(parseBiometricPreferenceStore(JSON.stringify({
    version: 1,
    preferences: {
      invalid: { userId: "", decision: "enabled" }
    }
  })).preferences, {});
});
