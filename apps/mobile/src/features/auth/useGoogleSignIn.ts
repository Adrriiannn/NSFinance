import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Platform } from "react-native";
import * as Google from "expo-auth-session/providers/google";
import { formatUnknownError } from "../../lib/api/errors";
import { useGoogleLoginMutation } from "./useAuthMutations";

type GoogleSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
};

function readEnv(name: string): string | undefined {
  const value = process.env[name]?.trim();
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

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
  const pendingResultResolverRef = useRef<((result: GoogleSignInResult) => void) | null>(null);
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);

  const googleWebClientId = readEnv("EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID");
  const googleAndroidClientId = __DEV__
    ? readEnv("EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_DEBUG")
    : readEnv("EXPO_PUBLIC_GOOGLE_ANDROID_CLIENT_ID_PROD");
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
    webClientId: googleWebClientId,
    androidClientId: googleAndroidClientId,
    clientId: googleWebClientId,
    selectAccount: true,
    scopes: ["openid", "profile", "email"]
  });

  const isConfigured = Boolean(activeClientId);

  const resolvePendingResult = useCallback((result: GoogleSignInResult) => {
    const resolver = pendingResultResolverRef.current;
    pendingResultResolverRef.current = null;
    setIsPromptInFlight(false);
    resolver?.(result);
  }, []);

  useEffect(() => {
    if (!response || !pendingResultResolverRef.current) {
      return;
    }

    if (response.type === "cancel" || response.type === "dismiss") {
      resolvePendingResult({
        succeeded: false,
        cancelled: true,
        message: "Google sign-in was cancelled."
      });
      return;
    }

    if (response.type !== "success") {
      resolvePendingResult({
        succeeded: false,
        message: "Google sign-in could not be completed."
      });
      return;
    }

    const idToken = extractIdToken(response);
    if (!idToken) {
      resolvePendingResult({
        succeeded: false,
        message: "Google sign-in did not return an ID token."
      });
      return;
    }

    let isCancelled = false;

    void (async () => {
      try {
        await googleLoginMutation.mutateAsync({
          idToken,
          deviceContext: {
            platform: Platform.OS
          }
        });

        if (!isCancelled) {
          resolvePendingResult({ succeeded: true });
        }
      } catch (error) {
        if (!isCancelled) {
          resolvePendingResult({
            succeeded: false,
            message: formatUnknownError(error)
          });
        }
      }
    })();

    return () => {
      isCancelled = true;
    };
  }, [googleLoginMutation, resolvePendingResult, response]);

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

    if (pendingResultResolverRef.current || isPromptInFlight) {
      return {
        succeeded: false,
        message: "Google sign-in is already in progress."
      };
    }

    const resultPromise = new Promise<GoogleSignInResult>((resolve) => {
      pendingResultResolverRef.current = resolve;
      setIsPromptInFlight(true);
    });

    try {
      const authResult = await promptAsync();
      if (authResult.type === "cancel" || authResult.type === "dismiss") {
        resolvePendingResult({
          succeeded: false,
          cancelled: true,
          message: "Google sign-in was cancelled."
        });
        return resultPromise;
      }

      if (authResult.type !== "success") {
        resolvePendingResult({
          succeeded: false,
          message: "Google sign-in could not be completed."
        });
        return resultPromise;
      }
    } catch (error) {
      resolvePendingResult({
        succeeded: false,
        message: formatUnknownError(error)
      });
    }

    return resultPromise;
  }, [isConfigured, isPromptInFlight, promptAsync, request, resolvePendingResult]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending || isPromptInFlight,
    isReady: Boolean(request)
  };
}
