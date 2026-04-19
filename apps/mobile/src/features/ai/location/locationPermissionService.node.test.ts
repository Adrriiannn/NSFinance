import assert from "node:assert/strict";
import test from "node:test";
import { normalizeForegroundPermissionState } from "./locationPermissionLogic";

type PermissionStub = {
  status: "granted" | "denied" | "undetermined";
  canAskAgain: boolean;
};

function permissionStub(status: PermissionStub["status"], canAskAgain: boolean): PermissionStub {
  return {
    status,
    canAskAgain
  };
}

test("permission normalization maps granted state", () => {
  const state = normalizeForegroundPermissionState(
    permissionStub("granted", true),
    true
  );
  assert.equal(state, "granted");
});

test("permission normalization maps askable denial", () => {
  const state = normalizeForegroundPermissionState(
    permissionStub("denied", true),
    true
  );
  assert.equal(state, "denied_can_ask_again");
});

test("permission normalization maps non-askable denial", () => {
  const state = normalizeForegroundPermissionState(
    permissionStub("denied", false),
    true
  );
  assert.equal(state, "denied_open_settings");
});

test("permission normalization maps disabled services to unavailable", () => {
  const state = normalizeForegroundPermissionState(
    permissionStub("granted", true),
    false
  );
  assert.equal(state, "unavailable");
});
