import assert from "node:assert/strict";
import test from "node:test";
import {
  describeNativeMicrosoftSignInError,
  resolveNativeMicrosoftResponse
} from "./microsoftNativeSignInPolicy";

test("native Microsoft success returns only a trimmed access token", () => {
  assert.deepEqual(
    resolveNativeMicrosoftResponse({
      status: "success",
      accessToken: "  signed-microsoft-access-token  "
    }),
    {
      status: "success",
      accessToken: "signed-microsoft-access-token"
    }
  );
});

test("native Microsoft cancellation remains distinct from provider failure", () => {
  assert.deepEqual(resolveNativeMicrosoftResponse({ status: "cancelled" }), {
    status: "cancelled"
  });
});

test("native Microsoft rejects success without an access token", () => {
  assert.deepEqual(
    resolveNativeMicrosoftResponse({ status: "success", accessToken: " " }),
    {
      status: "failure",
      code: "microsoft_access_token_missing",
      message: "Microsoft did not return a valid sign-in token. Please try again."
    }
  );
});

test("native Microsoft rejects unknown response states", () => {
  assert.deepEqual(resolveNativeMicrosoftResponse({ status: "unexpected" }), {
    status: "failure",
    code: "microsoft_response_invalid",
    message: "Microsoft sign-in could not be completed. Please try again."
  });
});

test("native Microsoft maps inactive screen errors to bounded guidance", () => {
  assert.deepEqual(
    describeNativeMicrosoftSignInError({
      code: "microsoft_activity_unavailable",
      message: "private Android activity detail"
    }),
    {
      code: "microsoft_activity_unavailable",
      message: "Microsoft sign-in needs the active NSFinance screen. Please try again."
    }
  );
});

test("native Microsoft does not expose provider exception details", () => {
  assert.deepEqual(
    describeNativeMicrosoftSignInError({
      code: "microsoft_sign_in_failed",
      message: "sensitive tenant and broker detail"
    }),
    {
      code: "microsoft_native_error",
      message: "Microsoft sign-in could not be completed. Please try again."
    }
  );
});
