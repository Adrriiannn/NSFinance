import assert from "node:assert/strict";
import test from "node:test";
import {
  getGoogleOAuthCompletionState,
  resetGoogleOAuthCompletionState,
  setGoogleOAuthCompletionState
} from "./googleOAuthCompletionState";

test("Google OAuth completion state records a bounded production status", () => {
  resetGoogleOAuthCompletionState();
  setGoogleOAuthCompletionState("pending", "Opening Google sign-in...");

  assert.deepEqual(getGoogleOAuthCompletionState(), {
    status: "pending",
    message: "Opening Google sign-in..."
  });

  setGoogleOAuthCompletionState("failure", "Google sign-in was cancelled.");
  assert.deepEqual(getGoogleOAuthCompletionState(), {
    status: "failure",
    message: "Google sign-in was cancelled."
  });
});

test("reset clears the Google OAuth completion state", () => {
  setGoogleOAuthCompletionState("success");
  resetGoogleOAuthCompletionState();

  assert.deepEqual(getGoogleOAuthCompletionState(), {
    status: "idle",
    message: ""
  });
});
