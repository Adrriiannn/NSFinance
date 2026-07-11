import { useCallback, useRef, useState } from "react";
import { formatUnknownError } from "../../lib/api/errors";
import { buildDeviceContext } from "../../lib/device/deviceIdentity";
import {
  isNativeGoogleSignInConfigured,
  requestNativeGoogleSignIn
} from "./googleNativeSignIn";
import { useGoogleLoginMutation } from "./useAuthMutations";

type GoogleSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
};

export function useGoogleSignIn() {
  const googleLoginMutation = useGoogleLoginMutation();
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
        await googleLoginMutation.mutateAsync({
          idToken: nativeResult.idToken,
          deviceContext: buildDeviceContext()
        });

        return { succeeded: true };
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
  }, [googleLoginMutation, isConfigured]);

  return {
    signInWithGoogle,
    isConfigured,
    isPending: googleLoginMutation.isPending || isPromptInFlight,
    isReady: isConfigured
  };
}
