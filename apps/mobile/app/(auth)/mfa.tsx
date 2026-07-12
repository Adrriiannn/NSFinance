import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, AppState, BackHandler, Pressable, StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import {
  OtpCodeField,
  type OtpCodeFieldHandle
} from "../../src/components/ui/OtpCodeField";
import { TextField } from "../../src/components/ui/TextField";
import { Button } from "../../src/components/ui/buttons/Button";
import { useVerifyMfaLoginMutation } from "../../src/features/auth/useAuthMutations";
import {
  clearPendingMfaLogin,
  getPendingMfaLogin
} from "../../src/features/auth/pendingAuthFlow";
import {
  getMfaChallengeRemainingMs,
  isMfaChallengeExpired
} from "../../src/features/auth/mfaChallengePolicy";
import {
  buildOtpAttemptKey,
  normalizeOtpCode,
  shouldAutoSubmitOtp
} from "../../src/features/auth/otpAutoSubmitPolicy";
import { ApiClientError, formatUnknownError } from "../../src/lib/api/errors";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import { showFlashMessage } from "../../src/lib/flashMessage";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

const INVALID_TOTP_MESSAGE =
  "That code is incorrect or no longer active. Check your authenticator and try again.";
const INVALID_RECOVERY_MESSAGE =
  "That recovery code is incorrect or has already been used.";
type ChallengeUnavailableReason = "expired" | "invalid";

export default function MfaScreen() {
  const [pending] = useState(getPendingMfaLogin);
  const [challengeUnavailable, setChallengeUnavailable] = useState<ChallengeUnavailableReason | null>(
    () => !pending ? "invalid" : isMfaChallengeExpired(pending.expiresUtc) ? "expired" : null
  );
  const [method, setMethod] = useState<"totp" | "recovery_code">("totp");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [canRetry, setCanRetry] = useState(false);
  const [rememberDevice, setRememberDevice] = useState(false);
  const [isRememberedSessionPending, setIsRememberedSessionPending] = useState(false);
  const codeFieldRef = useRef<OtpCodeFieldHandle | null>(null);
  const lastAttemptKeyRef = useRef<string | null>(null);
  const verifyMutation = useVerifyMfaLoginMutation();
  const {
    applyAuthTokenResponse,
    completeRememberedSessionMfa,
    signInAnotherWay
  } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const markChallengeUnavailable = useCallback((reason: ChallengeUnavailableReason) => {
    clearPendingMfaLogin();
    lastAttemptKeyRef.current = null;
    setError(null);
    setCanRetry(false);
    setChallengeUnavailable(reason);
  }, []);

  const returnToSignIn = useCallback(async () => {
    clearPendingMfaLogin();
    if (pending?.context === "remembered_session") {
      await signInAnotherWay();
    }
    router.replace({
      pathname: "/(auth)/login",
      params: challengeUnavailable === "expired"
        ? { mfaExpired: "1" }
        : { mfaUnavailable: "1" }
    } as never);
  }, [challengeUnavailable, pending?.context, signInAnotherWay]);

  const isVerifying = verifyMutation.isPending || isRememberedSessionPending;

  useEffect(() => {
    if (pending?.context !== "remembered_session") {
      return;
    }

    const subscription = BackHandler.addEventListener("hardwareBackPress", () => {
      void returnToSignIn();
      return true;
    });
    return () => subscription.remove();
  }, [pending?.context, returnToSignIn]);

  const handleVerify = useCallback(async (force = false) => {
    if (!pending || challengeUnavailable) {
      return;
    }

    if (isMfaChallengeExpired(pending.expiresUtc)) {
      markChallengeUnavailable("expired");
      return;
    }

    if (!code.trim()) {
      setError(method === "totp" ? "Enter the six-digit code." : "Enter a recovery code.");
      return;
    }

    const attemptKey = method === "totp"
      ? buildOtpAttemptKey(pending.challengeId, code)
      : `${pending.challengeId}:recovery:${code.trim().toUpperCase()}`;
    if (!attemptKey || (!force && attemptKey === lastAttemptKeyRef.current)) {
      return;
    }

    lastAttemptKeyRef.current = attemptKey;
    setError(null);
    setCanRetry(false);
    try {
      const request = {
        challengeId: pending.challengeId,
        challengeToken: pending.challengeToken,
        code: code.trim(),
        method,
        deviceContext: buildDeviceContext(),
        rememberDevice: method === "totp" && rememberDevice
      };
      if (pending.context === "remembered_session") {
        setIsRememberedSessionPending(true);
        await completeRememberedSessionMfa(request);
      } else {
        const session = await verifyMutation.mutateAsync(request);
        await applyAuthTokenResponse(session, {
          rememberSession: pending.rememberSession,
          completedViaMfa: true
        });
      }
      clearPendingMfaLogin();
      playSuccess();
      router.replace("/(tabs)");
    } catch (nextError) {
      const apiErrorCode = nextError instanceof ApiClientError ? nextError.code : null;
      if (apiErrorCode === "mfa_challenge_expired" || apiErrorCode === "mfa_challenge_invalid") {
        const reason = apiErrorCode === "mfa_challenge_expired" ? "expired" : "invalid";
        markChallengeUnavailable(reason);
        return;
      }

      const isInvalidCode = apiErrorCode === "mfa_code_invalid";
      const message = isInvalidCode
        ? method === "totp" ? INVALID_TOTP_MESSAGE : INVALID_RECOVERY_MESSAGE
        : formatUnknownError(nextError);

      setError(message);
      setCanRetry(!isInvalidCode);
      showFlashMessage(message, { tone: "error", durationMs: 3200 });
    } finally {
      setIsRememberedSessionPending(false);
    }
  }, [
    applyAuthTokenResponse,
    challengeUnavailable,
    code,
    completeRememberedSessionMfa,
    markChallengeUnavailable,
    method,
    pending,
    playSuccess,
    rememberDevice,
    verifyMutation
  ]);

  useEffect(() => {
    if (!pending || challengeUnavailable) {
      return;
    }

    const expireIfNeeded = () => {
      if (isMfaChallengeExpired(pending.expiresUtc)) {
        markChallengeUnavailable("expired");
      }
    };
    const timeout = setTimeout(
      expireIfNeeded,
      getMfaChallengeRemainingMs(pending.expiresUtc) + 50
    );
    const appStateSubscription = AppState.addEventListener("change", (nextState) => {
      if (nextState === "active") {
        expireIfNeeded();
      }
    });

    return () => {
      clearTimeout(timeout);
      appStateSubscription.remove();
    };
  }, [challengeUnavailable, markChallengeUnavailable, pending]);

  useEffect(() => {
    if (
      method !== "totp"
      || !pending
      || challengeUnavailable
      || !shouldAutoSubmitOtp({
        challengeId: pending.challengeId,
        code,
        isPending: isVerifying,
        lastAttemptKey: lastAttemptKeyRef.current
      })
    ) {
      return;
    }

    void handleVerify();
  }, [challengeUnavailable, code, handleVerify, isVerifying, method, pending]);

  useEffect(() => {
    if (challengeUnavailable || method !== "totp" || !error || isVerifying) {
      return;
    }

    const focusTimer = setTimeout(() => codeFieldRef.current?.focus(), 50);
    return () => clearTimeout(focusTimer);
  }, [challengeUnavailable, error, isVerifying, method]);

  if (!pending || challengeUnavailable) {
    const expired = challengeUnavailable === "expired";
    return (
      <AuthScreen>
        <View style={styles.content}>
          <Text style={styles.title}>{expired ? "Security check expired" : "Sign in again"}</Text>
          <Text style={styles.body}>
            {expired
              ? "Sign in again to request a new Authenticator check."
              : "This security check is no longer available. Sign in again to request a new one."}
          </Text>
          <Button label="Return to sign in" onPress={() => void returnToSignIn()} />
        </View>
      </AuthScreen>
    );
  }

  const alternativeMethod = pending.methods.find(
    (availableMethod): availableMethod is "totp" | "recovery_code" =>
      availableMethod !== method
      && (availableMethod === "totp" || availableMethod === "recovery_code")
  );

  return (
    <AuthScreen>
      <View style={styles.content}>
        <View style={styles.copy}>
          <Text style={styles.eyebrow}>SECURITY CHECK</Text>
          <Text style={styles.title}>
            {method === "totp" ? "Open your authenticator" : "Use a recovery code"}
          </Text>
          <Text style={styles.body}>
            {method === "totp"
              ? "Enter the current six-digit code for NSFinance."
              : "Each recovery code works once."}
          </Text>
        </View>

        {method === "totp" ? (
          <Pressable
            accessibilityRole="checkbox"
            accessibilityState={{ checked: rememberDevice }}
            accessibilityLabel="Remember this device for 30 days"
            onPress={() => setRememberDevice((current) => !current)}
            style={({ pressed }) => [styles.rememberDevice, pressed ? styles.pressed : null]}
          >
            <View style={[
              styles.checkbox,
              rememberDevice ? styles.checkboxChecked : null
            ]}>
              {rememberDevice ? (
                <Ionicons name="checkmark" size={14} color={palette.appBackground} />
              ) : null}
            </View>
            <Text style={styles.rememberDeviceLabel}>Remember this device for 30 days</Text>
          </Pressable>
        ) : null}

        {method === "totp" ? (
          <OtpCodeField
            ref={codeFieldRef}
            value={code}
            onChange={(value) => {
              setCode(normalizeOtpCode(value));
              setError(null);
              setCanRetry(false);
            }}
            disabled={isVerifying}
            error={error}
            accessibilityLabel="Authenticator code"
            autoFocus
          />
        ) : (
          <TextField
            label="Recovery code"
            value={code}
            onChangeText={(value) => {
              setCode(value.toUpperCase());
              setError(null);
              setCanRetry(false);
            }}
            autoCapitalize="characters"
            autoCorrect={false}
            error={error ?? undefined}
          />
        )}

        <View style={styles.actions}>
          {method === "totp" ? (
            <View style={styles.verificationStatus} accessibilityLiveRegion="polite">
              {isVerifying ? (
                <View style={styles.checkingRow}>
                  <ActivityIndicator color={palette.primary} size="small" />
                  <Text style={styles.checkingText}>Checking code...</Text>
                </View>
              ) : canRetry ? (
                <Button label="Try again" onPress={() => void handleVerify(true)} />
              ) : null}
            </View>
          ) : (
            <Button
              label={canRetry ? "Try again" : "Continue"}
              onPress={() => void handleVerify(canRetry)}
              disabled={!code.trim()}
              isLoading={isVerifying}
            />
          )}
          {alternativeMethod ? (
            <Button
              label="Use another method"
              variant="ghost"
              onPress={() => {
                setMethod(alternativeMethod);
                setCode("");
                setError(null);
                setCanRetry(false);
                setRememberDevice(false);
                lastAttemptKeyRef.current = null;
              }}
            />
          ) : null}
        </View>
      </View>
    </AuthScreen>
  );
}

const styles = StyleSheet.create({
  content: {
    flex: 1,
    width: "100%",
    maxWidth: 440,
    alignSelf: "center",
    justifyContent: "center",
    gap: spacing[32],
    paddingHorizontal: spacing[20],
    paddingVertical: spacing[32]
  },
  copy: {
    gap: spacing[8]
  },
  eyebrow: {
    color: palette.primary,
    fontSize: typography.caption.fontSize,
    fontFamily: typography.label.fontFamily
  },
  title: {
    color: palette.textPrimary,
    fontSize: typography.title.fontSize,
    lineHeight: typography.title.lineHeight,
    fontFamily: typography.title.fontFamily
  },
  body: {
    color: palette.textMuted,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.body.fontFamily
  },
  rememberDevice: {
    minHeight: 44,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    alignSelf: "flex-start"
  },
  checkbox: {
    width: 24,
    height: 24,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    borderRadius: 6,
    alignItems: "center",
    justifyContent: "center"
  },
  checkboxChecked: {
    backgroundColor: palette.primary,
    borderColor: palette.primary
  },
  rememberDeviceLabel: {
    color: palette.textSecondary,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.body.fontFamily
  },
  pressed: {
    opacity: 0.7
  },
  actions: {
    gap: spacing[12]
  },
  verificationStatus: {
    minHeight: 48,
    justifyContent: "center"
  },
  checkingRow: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8]
  },
  checkingText: {
    color: palette.textMuted,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.body.fontFamily
  }
});
