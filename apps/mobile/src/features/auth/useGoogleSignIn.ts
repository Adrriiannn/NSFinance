import { useCallback, useEffect, useMemo, useState } from "react";
import { Platform } from "react-native";
import * as Google from "expo-auth-session/providers/google";
import { formatUnknownError } from "../../lib/api/errors";
import { useGoogleLoginMutation } from "./useAuthMutations";

type GoogleSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
};

type TokenSummary = {
  hasToken: boolean;
  tokenLength: number;
  tokenPrefix: string;
};

const GOOGLE_CLIENT_ID_FALLBACK = "missing-google-client-id";

function normalizeEnvValue(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : undefined;
}

function readGoogleWebClientId(): string | undefined {
  const value = process.env.EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID;
  return normalizeEnvValue(value);
}

function readGoogleAndroidClientIdDebug(): string | undefined {
  const value = process.env.EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_DEBUG;
  return normalizeEnvValue(value);
}

function readGoogleAndroidClientIdProd(): string | undefined {
  const value = process.env.EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD;
  return normalizeEnvValue(value);
}

function readActiveGoogleAndroidClientId(): string | undefined {
  const value = __DEV__ ? readGoogleAndroidClientIdDebug() : readGoogleAndroidClientIdProd();
  return value ? value : undefined;
}

function extractIdToken(authResult: unknown): string | null {
  const authResultWithToken = authResult as {
    params?: { id_token?: string };
    authentication?: { idToken?: string | null };
  };
  const idToken =
    authResultWithToken.params?.id_token ?? authResultWithToken.authentication?.idToken ?? null;

  if (!idToken) {
    return null;
  }

  const trimmed = idToken.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function summarizeToken(token: string | null): TokenSummary {
  if (!token) {
    return {
      hasToken: false,
      tokenLength: 0,
      tokenPrefix: ""
    };
  }

  return {
    hasToken: true,
    tokenLength: token.length,
    tokenPrefix: token.slice(0, 10)
  };
}

function logGoogleAuthDebug(event: string, details?: Record<string, unknown>) {
  if (!__DEV__) {
    return;
  }

  if (!details) {
    console.info(`[GoogleAuth] ${event}`);
    return;
  }

  console.info(`[GoogleAuth] ${event}`, details);
}

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);

  const googleWebClientId = readGoogleWebClientId();
  const googleAndroidClientId = readActiveGoogleAndroidClientId();
  const safeGoogleWebClientId = googleWebClientId ?? GOOGLE_CLIENT_ID_FALLBACK;
  const safeGoogleAndroidClientId = googleAndroidClientId ?? GOOGLE_CLIENT_ID_FALLBACK;
  const activeClientId = useMemo(
    () =>
      Platform.select({
        android: googleAndroidClientId,
        web: googleWebClientId,
        default: undefined
      }),
    [googleAndroidClientId, googleWebClientId]
  );

  const [request, response, promptAsync] = Google.useAuthRequest({
    webClientId: safeGoogleWebClientId,
    androidClientId: safeGoogleAndroidClientId,
    clientId: safeGoogleWebClientId,
    selectAccount: true,
    scopes: ["openid", "profile", "email"]
  });

  const isConfigured = Boolean(activeClientId);

  useEffect(() => {
    if (!request) {
      return;
    }

    logGoogleAuthDebug("request_ready", {
      redirectUri: request.redirectUri,
      hasClientId: Boolean(activeClientId)
    });
  }, [activeClientId, request]);

  useEffect(() => {
    if (!response) {
      return;
    }

    logGoogleAuthDebug("response_received", {
      type: response.type,
      hasIdToken: Boolean(extractIdToken(response))
    });
  }, [response]);

  const completeGoogleSignIn = useCallback(
    async (authResult: unknown): Promise<GoogleSignInResult> => {
      const idToken = extractIdToken(authResult);
      const tokenSummary = summarizeToken(idToken);

      logGoogleAuthDebug("api_google_login_request_prepared", {
        endpoint: "/api/auth/google",
        hasIdToken: tokenSummary.hasToken,
        idTokenLength: tokenSummary.tokenLength,
        idTokenPrefix: tokenSummary.tokenPrefix
      });

      if (!idToken) {
        return {
          succeeded: false,
          message: "Google sign-in did not return an ID token."
        };
      }

      try {
        await googleLoginMutation.mutateAsync({
          idToken,
          deviceContext: {
            platform: Platform.OS
          }
        });

        logGoogleAuthDebug("api_google_login_success", {
          endpoint: "/api/auth/google"
        });
        return { succeeded: true };
      } catch (error) {
        const failureMessage = formatUnknownError(error);
        logGoogleAuthDebug("api_google_login_failure", {
          endpoint: "/api/auth/google",
          reason: failureMessage
        });
        return {
          succeeded: false,
          message: failureMessage
        };
      }
    },
    [googleLoginMutation]
  );

  const signInWithGoogle = useCallback(async (): Promise<GoogleSignInResult> => {
    const isExpoLikeRedirect = request?.redirectUri?.startsWith("exp://") ?? false;
    if (isExpoLikeRedirect) {
      return {
        succeeded: false,
        message:
          "Google sign-in is not supported in Expo Go. Use an Android development build (or production build) for OAuth."
      };
    }

    if (!isConfigured) {
      return {
        succeeded: false,
        message:
          Platform.OS === "ios"
            ? "Google sign-in is currently unavailable on iOS in this build."
            : "Google sign-in is not configured on this app build."
      };
    }

    if (!request) {
      return {
        succeeded: false,
        message: "Google sign-in is still preparing. Please try again."
      };
    }

    if (isPromptInFlight) {
      return {
        succeeded: false,
        message: "Google sign-in is already in progress."
      };
    }

    setIsPromptInFlight(true);
    try {
      logGoogleAuthDebug("prompt_open", {
        redirectUri: request.redirectUri
      });
      const authResult = await promptAsync();
      logGoogleAuthDebug("prompt_result", {
        type: authResult.type
      });

      if (authResult.type === "cancel" || authResult.type === "dismiss") {
        return {
          succeeded: false,
          cancelled: true,
          message: "Google sign-in was cancelled."
        };
      }

      if (authResult.type !== "success") {
        return {
          succeeded: false,
          message: "Google sign-in could not be completed."
        };
      }

      return await completeGoogleSignIn(authResult);
    } catch (error) {
      return {
        succeeded: false,
        message: formatUnknownError(error)
      };
    } finally {
      setIsPromptInFlight(false);
    }
  }, [completeGoogleSignIn, isConfigured, isPromptInFlight, promptAsync, request]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending || isPromptInFlight,
    isReady: Boolean(request)
  };
}
