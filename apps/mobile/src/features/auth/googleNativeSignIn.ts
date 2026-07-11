import { Platform } from "react-native";
import runtimeConfig from "../../../runtime.config.json";
import {
  describeNativeGoogleSignInError,
  resolveNativeGoogleResponse,
  type NativeGoogleSignInResolution
} from "./googleNativeSignInPolicy";

type NativeGoogleModule = typeof import("react-native-nitro-google-signin");

let nativeGoogleModulePromise: Promise<NativeGoogleModule> | null = null;
let configuredWebClientId: string | null = null;

function readGoogleWebClientId(): string | null {
  const value = runtimeConfig.googleOAuth.webClientId?.trim();
  return value && value.length > 0 ? value : null;
}

function loadNativeGoogleModule(): Promise<NativeGoogleModule> {
  nativeGoogleModulePromise ??= import("react-native-nitro-google-signin");
  return nativeGoogleModulePromise;
}

async function getConfiguredNativeGoogleModule(): Promise<NativeGoogleModule> {
  const webClientId = readGoogleWebClientId();
  if (!webClientId) {
    throw new Error("Google web client ID is not configured.");
  }

  const nativeModule = await loadNativeGoogleModule();
  if (configuredWebClientId !== webClientId) {
    nativeModule.GoogleOneTapSignIn.configure({
      webClientId,
      offlineAccess: false,
      autoSelectOnSignIn: false
    });
    configuredWebClientId = webClientId;
  }

  return nativeModule;
}

export function isNativeGoogleSignInConfigured(): boolean {
  return Platform.OS === "android" && Boolean(readGoogleWebClientId());
}

export async function requestNativeGoogleSignIn(): Promise<NativeGoogleSignInResolution> {
  if (!isNativeGoogleSignInConfigured()) {
    return {
      status: "failure",
      code: "google_not_configured",
      message: "Google sign-in is not configured for this app build."
    };
  }

  try {
    const nativeModule = await getConfiguredNativeGoogleModule();
    await nativeModule.GoogleOneTapSignIn.checkPlayServices();
    const response = await nativeModule.GoogleOneTapSignIn.presentExplicitSignIn();
    return resolveNativeGoogleResponse(response);
  } catch (error) {
    const failure = describeNativeGoogleSignInError(error);
    return {
      status: "failure",
      code: failure.code,
      message: failure.message
    };
  }
}

export async function clearNativeGoogleSignInState(): Promise<void> {
  if (!isNativeGoogleSignInConfigured()) {
    return;
  }

  try {
    const nativeModule = await getConfiguredNativeGoogleModule();
    await nativeModule.GoogleOneTapSignIn.signOut();
  } catch {
    // The NSFinance session is authoritative; native provider cleanup is best-effort.
  }
}
