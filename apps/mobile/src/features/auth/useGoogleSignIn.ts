import { useCallback, useEffect, useMemo, useState } from "react";
import { Platform } from "react-native";
import { exchangeCodeAsync } from "expo-auth-session";
import * as Google from "expo-auth-session/providers/google";
import { formatUnknownError } from "../../lib/api/errors";
import {
  pushGoogleOAuthDebugStep,
  resetGoogleOAuthDebugState,
  updateGoogleOAuthDebugState
} from "./googleOAuthDebug";
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

type TokenSummary = {
  hasToken: boolean;
  tokenLength: number;
  tokenPrefix: string;
};

type AuthResultShape = {
  hasParamsIdToken: boolean;
  hasAuthenticationIdToken: boolean;
  hasCode: boolean;
  hasError: boolean;
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

function summarizeAuthResultShape(authResult: unknown): AuthResultShape {
  const authResultWithToken = authResult as {
    params?: { id_token?: string; code?: string; error?: string };
    authentication?: { idToken?: string | null };
    error?: unknown;
  };

  const params = authResultWithToken.params ?? {};

  return {
    hasParamsIdToken: Boolean(params.id_token?.trim()),
    hasAuthenticationIdToken: Boolean(authResultWithToken.authentication?.idToken?.trim()),
    hasCode: Boolean(params.code?.trim()),
    hasError: Boolean(params.error?.trim()) || Boolean(authResultWithToken.error)
  };
}

function extractAuthorizationCode(authResult: unknown): string | null {
  const authResultWithParams = authResult as {
    params?: { code?: string };
  };
  const code = authResultWithParams.params?.code?.trim();
  return code && code.length > 0 ? code : null;
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

function summarizeClientId(clientId: string | null | undefined): string {
  if (!clientId) {
    return "missing";
  }

  const trimmed = clientId.trim();
  if (!trimmed) {
    return "missing";
  }

  if (trimmed.length <= 14) {
    return trimmed;
  }

  return `${trimmed.slice(0, 8)}...${trimmed.slice(-6)}`;
}

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);
  const oauthRequestEpoch = useGoogleOAuthRequestEpoch();

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

  const [request, response, promptAsync] = Google.useAuthRequest({
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
    if (!request) {
      return;
    }

    logGoogleAuthDebug("request_ready", {
      redirectUri: request.redirectUri,
      hasClientId: Boolean(activeClientId),
      oauthState,
      requestClientId: summarizeClientId(request.clientId)
    });
  }, [activeClientId, oauthState, request]);

  useEffect(() => {
    if (!response) {
      return;
    }

    logGoogleAuthDebug("response_received", {
      type: response.type,
      hasIdToken: Boolean(extractIdToken(response))
    });
  }, [response]);

  useEffect(() => {
    setIsPromptInFlight(false);
    googleLoginMutation.reset();
  }, [googleLoginMutation, oauthRequestEpoch]);

  const completeGoogleSignIn = useCallback(
    async (authResult: unknown): Promise<GoogleSignInResult> => {
      const authResultShape = summarizeAuthResultShape(authResult);
      updateGoogleOAuthDebugState(authResultShape);
      pushGoogleOAuthDebugStep("auth_response_received", JSON.stringify(authResultShape));

      let idToken = extractIdToken(authResult);
      if (!idToken) {
        const code = extractAuthorizationCode(authResult);
        const codeVerifier = request?.codeVerifier ?? null;
        const requestClientId = request?.clientId?.trim() ?? null;

        if (!code) {
          pushGoogleOAuthDebugStep("id_token_missing", "No id_token and no authorization code in callback.");
          return {
            succeeded: false,
            message: "Google sign-in did not return an ID token."
          };
        }

        if (!requestClientId || !request?.redirectUri || !codeVerifier) {
          pushGoogleOAuthDebugStep("code_exchange_blocked", "Missing clientId, redirectUri, or codeVerifier.");
          return {
            succeeded: false,
            message: "Google sign-in callback is missing exchange parameters."
          };
        }

        pushGoogleOAuthDebugStep("code_exchange_started", "Exchanging authorization code for ID token.");

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
          const exchangeTokenSummary = summarizeToken(idToken);
          updateGoogleOAuthDebugState({
            idTokenPresent: exchangeTokenSummary.hasToken,
            idTokenLength: exchangeTokenSummary.tokenLength,
            idTokenPrefix: exchangeTokenSummary.tokenPrefix
          });
          pushGoogleOAuthDebugStep(
            "code_exchange_completed",
            `idTokenPresent=${exchangeTokenSummary.hasToken} length=${exchangeTokenSummary.tokenLength}`
          );
        } catch (error) {
          const message = formatUnknownError(error);
          pushGoogleOAuthDebugStep("code_exchange_failed", message);
          resetGoogleOAuthFlowState("code_exchange_failed");
          return {
            succeeded: false,
            message
          };
        }
      }

      const tokenSummary = summarizeToken(idToken);
      updateGoogleOAuthDebugState({
        idTokenPresent: tokenSummary.hasToken,
        idTokenLength: tokenSummary.tokenLength,
        idTokenPrefix: tokenSummary.tokenPrefix
      });

      logGoogleAuthDebug("api_google_login_request_prepared", {
        endpoint: "/api/auth/google",
        hasIdToken: tokenSummary.hasToken,
        idTokenLength: tokenSummary.tokenLength,
        idTokenPrefix: tokenSummary.tokenPrefix
      });

      if (!idToken) {
        pushGoogleOAuthDebugStep("id_token_missing", "Token still missing after exchange path.");
        return {
          succeeded: false,
          message: "Google sign-in did not return an ID token."
        };
      }

      try {
        updateGoogleOAuthDebugState({
          backendCalled: true,
          backendOutcome: "none",
          backendMessage: ""
        });
        pushGoogleOAuthDebugStep("calling_backend", "Calling /api/auth/google...");

        await googleLoginMutation.mutateAsync({
          idToken,
          deviceContext: {
            platform: Platform.OS
          }
        });

        updateGoogleOAuthDebugState({
          backendOutcome: "success",
          backendMessage: "Google backend login succeeded."
        });
        pushGoogleOAuthDebugStep("backend_success", "Google login succeeded.");
        logGoogleAuthDebug("api_google_login_success", {
          endpoint: "/api/auth/google"
        });
        return { succeeded: true };
      } catch (error) {
        const failureMessage = formatUnknownError(error);
        updateGoogleOAuthDebugState({
          backendOutcome: "failure",
          backendMessage: failureMessage
        });
        pushGoogleOAuthDebugStep("backend_failure", failureMessage);
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
    [googleLoginMutation, request?.clientId, request?.codeVerifier, request?.redirectUri]
  );

  const signInWithGoogle = useCallback(async (): Promise<GoogleSignInResult> => {
    resetGoogleOAuthDebugState();
    googleLoginMutation.reset();
    pushGoogleOAuthDebugStep("sign_in_started", "Google sign-in button pressed.");

    const isExpoLikeRedirect = request?.redirectUri?.startsWith("exp://") ?? false;
    if (isExpoLikeRedirect) {
      pushGoogleOAuthDebugStep("blocked_expo_go", "Redirect URI indicates Expo Go.");
      return {
        succeeded: false,
        message:
          "Google sign-in is not supported in Expo Go. Use an Android development build (or production build) for OAuth."
      };
    }

    if (!isConfigured) {
      pushGoogleOAuthDebugStep("not_configured", "Google client IDs missing for current platform.");
      return {
        succeeded: false,
        message:
          Platform.OS === "ios"
            ? "Google sign-in is currently unavailable on iOS in this build."
            : "Google sign-in is not configured on this app build."
      };
    }

    if (!request) {
      pushGoogleOAuthDebugStep("request_not_ready", "Auth request is not ready.");
      return {
        succeeded: false,
        message: "Google sign-in is still preparing. Please try again."
      };
    }

    if (isPromptInFlight) {
      pushGoogleOAuthDebugStep("already_in_progress", "Google sign-in already in progress.");
      return {
        succeeded: false,
        message: "Google sign-in is already in progress."
      };
    }

    setIsPromptInFlight(true);
    try {
      logGoogleAuthDebug("prompt_open", {
        redirectUri: request.redirectUri,
        clientId: summarizeClientId(request.clientId),
        oauthState
      });
      pushGoogleOAuthDebugStep(
        "prompt_opened",
        `${request.redirectUri} client=${summarizeClientId(request.clientId)} state=${oauthState}`
      );
      const authResult = await promptAsync();
      logGoogleAuthDebug("prompt_result", {
        type: authResult.type
      });
      pushGoogleOAuthDebugStep("prompt_result", `type=${authResult.type}`);

      if (authResult.type === "cancel" || authResult.type === "dismiss") {
        pushGoogleOAuthDebugStep("prompt_cancelled", authResult.type);
        resetGoogleOAuthFlowState("manual_retry");
        return {
          succeeded: false,
          cancelled: true,
          message: "Google sign-in was cancelled."
        };
      }

      if (authResult.type !== "success") {
        pushGoogleOAuthDebugStep("prompt_non_success", authResult.type);
        resetGoogleOAuthFlowState("manual_retry");
        return {
          succeeded: false,
          message: "Google sign-in could not be completed."
        };
      }

      const completionResult = await completeGoogleSignIn(authResult);
      if (!completionResult.succeeded) {
        resetGoogleOAuthFlowState("manual_retry");
      }

      return completionResult;
    } catch (error) {
      pushGoogleOAuthDebugStep("prompt_exception", formatUnknownError(error));
      resetGoogleOAuthFlowState("manual_retry");
      return {
        succeeded: false,
        message: formatUnknownError(error)
      };
    } finally {
      setIsPromptInFlight(false);
    }
  }, [completeGoogleSignIn, googleLoginMutation, isConfigured, isPromptInFlight, oauthState, promptAsync, request]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending || isPromptInFlight,
    isReady: Boolean(request)
  };
}
