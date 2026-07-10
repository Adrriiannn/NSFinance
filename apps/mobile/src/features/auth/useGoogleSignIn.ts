import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Platform } from "react-native";
import { exchangeCodeAsync } from "expo-auth-session";
import * as Google from "expo-auth-session/providers/google";
import runtimeConfig from "../../../runtime.config.json";
import { formatUnknownError } from "../../lib/api/errors";
import { buildDeviceContext } from "../../lib/device/deviceIdentity";
import {
  resetGoogleOAuthCompletionState,
  setGoogleOAuthCompletionState
} from "./googleOAuthCompletionState";
import {
  resetGoogleOAuthFlowState,
  useGoogleOAuthRequestEpoch
} from "./googleOAuthFlowState";
import { useGoogleLoginMutation } from "./useAuthMutations";

type GoogleSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
};

const GOOGLE_CLIENT_ID_FALLBACK = "missing-google-client-id";

function normalizeConfigValue(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : undefined;
}

function readGoogleWebClientId(): string | undefined {
  return normalizeConfigValue(runtimeConfig.googleOAuth.webClientId);
}

function readGoogleAndroidClientIdProd(): string | undefined {
  return normalizeConfigValue(runtimeConfig.googleOAuth.androidClientId);
}

function readActiveGoogleAndroidClientId(): string | undefined {
  const value = readGoogleAndroidClientIdProd();
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

function extractAuthorizationCode(authResult: unknown): string | null {
  const authResultWithParams = authResult as {
    params?: { code?: string };
  };
  const code = authResultWithParams.params?.code?.trim();
  return code && code.length > 0 ? code : null;
}

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);
  const oauthRequestEpoch = useGoogleOAuthRequestEpoch();
  const lastFlowResetEpochRef = useRef<number | null>(null);

  const googleWebClientId = readGoogleWebClientId();
  const googleAndroidClientId = readActiveGoogleAndroidClientId();
  const safeGoogleWebClientId = googleWebClientId ?? GOOGLE_CLIENT_ID_FALLBACK;
  const safeGoogleAndroidClientId = googleAndroidClientId ?? GOOGLE_CLIENT_ID_FALLBACK;
  const oauthState = useMemo(() => `nsfinance-google-${oauthRequestEpoch}`, [oauthRequestEpoch]);
  const activeClientId = useMemo(
    () =>
      Platform.select({
        android: googleAndroidClientId,
        web: googleWebClientId,
        default: undefined
      }),
    [googleAndroidClientId, googleWebClientId]
  );

  const [request, , promptAsync] = Google.useAuthRequest({
    webClientId: safeGoogleWebClientId,
    androidClientId: safeGoogleAndroidClientId,
    // We perform a single explicit exchange path below to avoid double-exchanging the same code.
    shouldAutoExchangeCode: false,
    state: oauthState,
    selectAccount: true,
    scopes: ["openid", "profile", "email"]
  });

  const isConfigured = Boolean(activeClientId);

  useEffect(() => {
    if (lastFlowResetEpochRef.current === oauthRequestEpoch) {
      return;
    }

    lastFlowResetEpochRef.current = oauthRequestEpoch;
    setIsPromptInFlight(false);
    googleLoginMutation.reset();
  }, [googleLoginMutation, oauthRequestEpoch]);

  const completeGoogleSignIn = useCallback(
    async (authResult: unknown): Promise<GoogleSignInResult> => {
      let idToken = extractIdToken(authResult);
      if (!idToken) {
        const code = extractAuthorizationCode(authResult);
        const codeVerifier = request?.codeVerifier ?? null;
        const requestClientId = request?.clientId?.trim() ?? null;

        if (!code) {
          const message = "Google sign-in did not return an ID token.";
          setGoogleOAuthCompletionState("failure", message);
          return {
            succeeded: false,
            message
          };
        }

        if (!requestClientId || !request?.redirectUri || !codeVerifier) {
          const message = "Google sign-in callback is missing exchange parameters.";
          setGoogleOAuthCompletionState("failure", message);
          return {
            succeeded: false,
            message
          };
        }

        try {
          const tokenResponse = await exchangeCodeAsync(
            {
              clientId: requestClientId,
              code,
              redirectUri: request.redirectUri,
              extraParams: {
                code_verifier: codeVerifier
              }
            },
            Google.discovery
          );

          idToken = tokenResponse.idToken?.trim() ?? null;
        } catch (error) {
          const message = formatUnknownError(error);
          setGoogleOAuthCompletionState("failure", message);
          return {
            succeeded: false,
            message
          };
        }
      }

      if (!idToken) {
        const message = "Google sign-in did not return an ID token.";
        setGoogleOAuthCompletionState("failure", message);
        return {
          succeeded: false,
          message
        };
      }

      try {
        setGoogleOAuthCompletionState("pending", "Verifying your Google account...");

        await googleLoginMutation.mutateAsync({
          idToken,
          deviceContext: buildDeviceContext()
        });

        setGoogleOAuthCompletionState("success");
        return { succeeded: true };
      } catch (error) {
        const failureMessage = formatUnknownError(error);
        setGoogleOAuthCompletionState("failure", failureMessage);
        return {
          succeeded: false,
          message: failureMessage
        };
      }
    },
    [googleLoginMutation, request?.clientId, request?.codeVerifier, request?.redirectUri]
  );

  const signInWithGoogle = useCallback(async (): Promise<GoogleSignInResult> => {
    resetGoogleOAuthCompletionState();
    googleLoginMutation.reset();
    setGoogleOAuthCompletionState("pending", "Opening Google sign-in...");

    if (!isConfigured) {
      const message =
        Platform.OS === "ios"
          ? "Google sign-in is currently unavailable on iOS in this build."
          : "Google sign-in is not configured on this app build.";
      setGoogleOAuthCompletionState("failure", message);
      return {
        succeeded: false,
        message
      };
    }

    if (!request) {
      const message = "Google sign-in is still preparing. Please try again.";
      setGoogleOAuthCompletionState("failure", message);
      return {
        succeeded: false,
        message
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
      const authResult = await promptAsync();

      if (authResult.type === "cancel" || authResult.type === "dismiss") {
        const message = "Google sign-in was cancelled.";
        resetGoogleOAuthFlowState("manual_retry");
        setGoogleOAuthCompletionState("failure", message);
        return {
          succeeded: false,
          cancelled: true,
          message
        };
      }

      if (authResult.type !== "success") {
        const message = "Google sign-in could not be completed.";
        resetGoogleOAuthFlowState("manual_retry");
        setGoogleOAuthCompletionState("failure", message);
        return {
          succeeded: false,
          message
        };
      }

      const completionResult = await completeGoogleSignIn(authResult);
      if (!completionResult.succeeded) {
        resetGoogleOAuthFlowState("manual_retry");
        setGoogleOAuthCompletionState(
          "failure",
          completionResult.message ?? "Google sign-in could not be completed."
        );
      }

      return completionResult;
    } catch (error) {
      const message = formatUnknownError(error);
      resetGoogleOAuthFlowState("manual_retry");
      setGoogleOAuthCompletionState("failure", message);
      return {
        succeeded: false,
        message
      };
    } finally {
      setIsPromptInFlight(false);
    }
  }, [completeGoogleSignIn, googleLoginMutation, isConfigured, isPromptInFlight, promptAsync, request]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending || isPromptInFlight,
    isReady: Boolean(request)
  };
}
