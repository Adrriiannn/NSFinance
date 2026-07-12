import { Platform } from "react-native";
import runtimeConfig from "../../../runtime.config.json";
import {
  describeNativeMicrosoftSignInError,
  resolveNativeMicrosoftResponse,
  type NativeMicrosoftSignInResolution
} from "./microsoftNativeSignInPolicy";

type MicrosoftNativeModule = typeof import("../../../modules/nsfinance-microsoft-auth");

let nativeModulePromise: Promise<MicrosoftNativeModule> | null = null;

function loadNativeModule() {
  nativeModulePromise ??= import("../../../modules/nsfinance-microsoft-auth");
  return nativeModulePromise;
}

export function isNativeMicrosoftSignInConfigured() {
  return Platform.OS === "android" && Boolean(runtimeConfig.microsoftOAuth.scope.trim());
}

export async function requestNativeMicrosoftSignIn(): Promise<NativeMicrosoftSignInResolution> {
  if (!isNativeMicrosoftSignInConfigured()) {
    return {
      status: "failure",
      code: "microsoft_not_configured",
      message: "Microsoft sign-in is not configured for this app build."
    };
  }

  try {
    const nativeModule = await loadNativeModule();
    const result = await nativeModule.default.signIn(runtimeConfig.microsoftOAuth.scope);
    return resolveNativeMicrosoftResponse(result);
  } catch (error) {
    const failure = describeNativeMicrosoftSignInError(error);
    return {
      status: "failure",
      code: failure.code,
      message: failure.message
    };
  }
}
