import { useCallback, useRef, useState } from "react";
import { formatUnknownError } from "../../lib/api/errors";
import { buildDeviceContext } from "../../lib/device/deviceIdentity";
import type { AuthFlowResponse } from "../../types/api";
import {
  usePrivacyPolicyQuery,
  useTermsPolicyQuery
} from "../policies/usePolicies";
import {
  isNativeGoogleSignInConfigured,
  requestNativeGoogleSignIn
} from "./googleNativeSignIn";
import { useGoogleLoginMutation } from "./useAuthMutations";

type GoogleSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
  flow?: AuthFlowResponse;
};

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);
  const promptInFlightRef = useRef(false);
  const isConfigured = isNativeGoogleSignInConfigured();

  const signInWithGoogle = useCallback(async (): Promise<GoogleSignInResult> => {
    googleLoginMutation.reset();

    if (!isConfigured) {
      return {
        succeeded: false,
        message: "Google sign-in is not configured for this app build."
      };
    }

    const termsVersion = termsQuery.data?.version;
    const privacyVersion = privacyQuery.data?.version;
    if (!termsVersion || !privacyVersion) {
      return {
        succeeded: false,
        message: "Could not load the current Terms and Privacy Policy. Please try again."
      };
    }

    if (promptInFlightRef.current) {
      return {
        succeeded: false,
        message: "Google sign-in is already in progress."
      };
    }

    promptInFlightRef.current = true;
    setIsPromptInFlight(true);

    try {
      const nativeResult = await requestNativeGoogleSignIn();
      if (nativeResult.status === "cancelled") {
        return {
          succeeded: false,
          cancelled: true,
          message: nativeResult.message
        };
      }

      if (nativeResult.status === "failure") {
        return {
          succeeded: false,
          message: nativeResult.message
        };
      }

      try {
        const flow = await googleLoginMutation.mutateAsync({
          idToken: nativeResult.idToken,
          deviceContext: buildDeviceContext(),
          acceptPolicies: true,
          termsVersion,
          privacyVersion
        });

        return { succeeded: true, flow };
      } catch (error) {
        return {
          succeeded: false,
          message: formatUnknownError(error)
        };
      }
    } finally {
      promptInFlightRef.current = false;
      setIsPromptInFlight(false);
    }
  }, [googleLoginMutation, isConfigured, privacyQuery.data?.version, termsQuery.data?.version]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending:
      googleLoginMutation.isPending ||
      isPromptInFlight ||
      termsQuery.isLoading ||
      privacyQuery.isLoading,
    isReady: isConfigured && Boolean(termsQuery.data?.version) && Boolean(privacyQuery.data?.version)
  };
}
