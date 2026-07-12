import { useCallback, useRef, useState } from "react";
import { buildDeviceContext } from "../../lib/device/deviceIdentity";
import type { AuthFlowResponse } from "../../types/api";
import { usePrivacyPolicyQuery, useTermsPolicyQuery } from "../policies/usePolicies";
import {
  isNativeMicrosoftSignInConfigured,
  requestNativeMicrosoftSignIn
} from "./microsoftNativeSignIn";
import { useMicrosoftLoginMutation } from "./useAuthMutations";
import { readMfaTrustedDeviceCredential } from "./mfaTrustedDevice";

type MicrosoftSignInResult = {
  succeeded: boolean;
  cancelled?: boolean;
  message?: string;
  flow?: AuthFlowResponse;
};

export function useMicrosoftSignIn() {
  const loginMutation = useMicrosoftLoginMutation();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const [isPromptInFlight, setIsPromptInFlight] = useState(false);
  const promptInFlightRef = useRef(false);
  const isConfigured = isNativeMicrosoftSignInConfigured();

  const signInWithMicrosoft = useCallback(async (): Promise<MicrosoftSignInResult> => {
    loginMutation.reset();

    const termsVersion = termsQuery.data?.version;
    const privacyVersion = privacyQuery.data?.version;
    if (!isConfigured || !termsVersion || !privacyVersion) {
      return {
        succeeded: false,
        message: isConfigured
          ? "Could not load the current Terms and Privacy Policy. Please try again."
          : "Microsoft sign-in is not configured for this app build."
      };
    }

    if (promptInFlightRef.current) {
      return { succeeded: false, message: "Microsoft sign-in is already in progress." };
    }

    promptInFlightRef.current = true;
    setIsPromptInFlight(true);
    try {
      const nativeResult = await requestNativeMicrosoftSignIn();
      if (nativeResult.status === "cancelled") {
        return { succeeded: false, cancelled: true };
      }
      if (nativeResult.status === "failure") {
        return { succeeded: false, message: nativeResult.message };
      }

      const deviceContext = buildDeviceContext();
      const trustedDevice = await readMfaTrustedDeviceCredential({
        deviceFingerprint: deviceContext.deviceFingerprint
      });
      const flow = await loginMutation.mutateAsync({
        accessToken: nativeResult.accessToken,
        deviceContext,
        mfaTrustedDeviceToken: trustedDevice?.token,
        acceptPolicies: true,
        termsVersion,
        privacyVersion
      });
      return { succeeded: true, flow };
    } catch (error) {
      return {
        succeeded: false,
        message: error instanceof Error ? error.message : "Microsoft sign-in failed."
      };
    } finally {
      promptInFlightRef.current = false;
      setIsPromptInFlight(false);
    }
  }, [isConfigured, loginMutation, privacyQuery.data?.version, termsQuery.data?.version]);

  return {
    signInWithMicrosoft,
    isConfigured,
    isPending:
      isPromptInFlight || loginMutation.isPending || termsQuery.isLoading || privacyQuery.isLoading,
    isReady: isConfigured && Boolean(termsQuery.data?.version) && Boolean(privacyQuery.data?.version)
  };
}
