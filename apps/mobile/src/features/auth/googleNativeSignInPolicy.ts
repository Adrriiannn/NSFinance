export type NativeGoogleResponseLike = {
  type: string;
  data?: {
    idToken?: string | null;
  } | null;
};

export type NativeGoogleSignInResolution =
  | { status: "success"; idToken: string }
  | { status: "cancelled"; message: string }
  | { status: "failure"; code: string; message: string };

export type NativeGoogleSignInErrorDescription = {
  code: string;
  message: string;
};

export function resolveNativeGoogleResponse(
  response: NativeGoogleResponseLike
): NativeGoogleSignInResolution {
  if (response.type === "cancelled") {
    return {
      status: "cancelled",
      message: "Google sign-in was cancelled."
    };
  }

  if (response.type === "noSavedCredentialFound") {
    return {
      status: "failure",
      code: "google_account_unavailable",
      message: "No Google account was selected. Choose or add an account and try again."
    };
  }

  if (response.type !== "success") {
    return {
      status: "failure",
      code: "google_response_invalid",
      message: "Google sign-in could not be completed. Please try again."
    };
  }

  const idToken = response.data?.idToken?.trim();
  if (!idToken) {
    return {
      status: "failure",
      code: "google_id_token_missing",
      message: "Google did not return a valid identity token. Please try again."
    };
  }

  return {
    status: "success",
    idToken
  };
}

export function describeNativeGoogleSignInError(
  error: unknown
): NativeGoogleSignInErrorDescription {
  const candidate = error as { code?: unknown; message?: unknown } | null;
  const code = typeof candidate?.code === "string" ? candidate.code : "google_native_error";
  const rawMessage = typeof candidate?.message === "string" ? candidate.message : "";

  if (code === "PLAY_SERVICES_NOT_AVAILABLE") {
    return {
      code: "google_play_services_unavailable",
      message: "Google Play services is unavailable or needs an update on this device."
    };
  }

  if (code === "IN_PROGRESS") {
    return {
      code: "google_sign_in_in_progress",
      message: "Google sign-in is already in progress."
    };
  }

  if (code === "SIGN_IN_CANCELLED") {
    return {
      code: "google_sign_in_cancelled",
      message: "Google sign-in was cancelled."
    };
  }

  if (code === "SIGN_IN_REQUIRED") {
    return {
      code: "google_sign_in_required",
      message: "Choose a Google account to continue."
    };
  }

  if (code === "ONE_TAP_START_FAILED" || /developer_error|configuration|oauth client/i.test(rawMessage)) {
    return {
      code: "google_app_registration_invalid",
      message: "Google sign-in is not registered correctly for this app build."
    };
  }

  return {
    code: "google_native_error",
    message: "Google sign-in could not be completed. Please try again."
  };
}
