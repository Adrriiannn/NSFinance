export type NativeMicrosoftResponseLike = {
  status: string;
  accessToken?: string | null;
};

export type NativeMicrosoftSignInResolution =
  | { status: "success"; accessToken: string }
  | { status: "cancelled" }
  | { status: "failure"; code: string; message: string };

export type NativeMicrosoftSignInErrorDescription = {
  code: string;
  message: string;
};

export function resolveNativeMicrosoftResponse(
  response: NativeMicrosoftResponseLike
): NativeMicrosoftSignInResolution {
  if (response.status === "cancelled") {
    return { status: "cancelled" };
  }

  if (response.status !== "success") {
    return {
      status: "failure",
      code: "microsoft_response_invalid",
      message: "Microsoft sign-in could not be completed. Please try again."
    };
  }

  const accessToken = response.accessToken?.trim();
  if (!accessToken) {
    return {
      status: "failure",
      code: "microsoft_access_token_missing",
      message: "Microsoft did not return a valid sign-in token. Please try again."
    };
  }

  return { status: "success", accessToken };
}

export function describeNativeMicrosoftSignInError(
  error: unknown
): NativeMicrosoftSignInErrorDescription {
  const candidate = error as { code?: unknown } | null;
  const code = typeof candidate?.code === "string" ? candidate.code : "microsoft_native_error";

  if (code === "microsoft_activity_unavailable") {
    return {
      code,
      message: "Microsoft sign-in needs the active NSFinance screen. Please try again."
    };
  }

  if (code === "microsoft_scope_missing") {
    return {
      code: "microsoft_not_configured",
      message: "Microsoft sign-in is not configured for this app build."
    };
  }

  return {
    code: "microsoft_native_error",
    message: "Microsoft sign-in could not be completed. Please try again."
  };
}
