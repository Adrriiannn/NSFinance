import assert from "node:assert/strict";
import test from "node:test";
import {
  describeNativeGoogleSignInError,
  resolveNativeGoogleResponse
} from "./googleNativeSignInPolicy";

test("native Google success returns a trimmed ID token", () => {
  assert.deepEqual(
    resolveNativeGoogleResponse({
      type: "success",
      data: { idToken: "  signed-google-id-token  " }
    }),
    {
      status: "success",
      idToken: "signed-google-id-token"
    }
  );
});

test("native Google cancellation remains distinct from provider failure", () => {
  assert.deepEqual(resolveNativeGoogleResponse({ type: "cancelled" }), {
    status: "cancelled",
    message: "Google sign-in was cancelled."
  });
});

test("native Google rejects success without an ID token", () => {
  assert.deepEqual(resolveNativeGoogleResponse({ type: "success", data: { idToken: " " } }), {
    status: "failure",
    code: "google_id_token_missing",
    message: "Google did not return a valid identity token. Please try again."
  });
});

test("native Google maps missing Play services to a bounded user message", () => {
  assert.deepEqual(
    describeNativeGoogleSignInError({
      code: "PLAY_SERVICES_NOT_AVAILABLE",
      message: "internal provider detail"
    }),
    {
      code: "google_play_services_unavailable",
      message: "Google Play services is unavailable or needs an update on this device."
    }
  );
});

test("native Google maps app registration failures without exposing provider details", () => {
  assert.deepEqual(
    describeNativeGoogleSignInError({
      code: "ONE_TAP_START_FAILED",
      message: "DEVELOPER_ERROR: private provider detail"
    }),
    {
      code: "google_app_registration_invalid",
      message: "Google sign-in is not registered correctly for this app build."
    }
  );
});

test("native Google uses a neutral message for unknown failures", () => {
  assert.deepEqual(describeNativeGoogleSignInError(new Error("sensitive internal detail")), {
    code: "google_native_error",
    message: "Google sign-in could not be completed. Please try again."
  });
});
