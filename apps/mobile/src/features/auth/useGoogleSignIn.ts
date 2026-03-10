import { useCallback } from "react";
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

const googleClientId = readEnv("EXPO_PUBLIC_GOOGLE_CLIENT_ID");
const fallbackGoogleClientId = "missing-google-client-id";

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();

  const [request, , promptAsync] = Google.useAuthRequest({
    clientId: googleClientId ?? fallbackGoogleClientId,
    selectAccount: true,
    scopes: ["openid", "profile", "email"]
  });

  const isConfigured = Boolean(googleClientId);

  const signInWithGoogle = useCallback(async (): Promise<GoogleSignInResult> => {
    if (!isConfigured) {
      return {
        succeeded: false,
        message: "Google sign-in is not configured on this app build."
      };
    }

    if (!request) {
      return {
        succeeded: false,
        message: "Google sign-in is still preparing. Please try again."
      };
    }

    try {
      const authResult = await promptAsync();
      if (authResult.type === "cancel" || authResult.type === "dismiss") {
        return { succeeded: false, cancelled: true, message: "Google sign-in was cancelled." };
      }

      if (authResult.type !== "success") {
        return { succeeded: false, message: "Google sign-in could not be completed." };
      }

      const authResultWithToken = authResult as {
        params?: { id_token?: string };
        authentication?: { idToken?: string | null };
      };
      const idToken =
        authResultWithToken.params?.id_token ?? authResultWithToken.authentication?.idToken ?? null;

      if (!idToken) {
        return {
          succeeded: false,
          message: "Google sign-in did not return an ID token."
        };
      }

      await googleLoginMutation.mutateAsync({
        idToken,
        deviceContext: {
          platform: Platform.OS
        }
      });

      return { succeeded: true };
    } catch (error) {
      return {
        succeeded: false,
        message: formatUnknownError(error)
      };
    }
  }, [googleLoginMutation, isConfigured, promptAsync, request]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending,
    isReady: Boolean(request)
  };
}
