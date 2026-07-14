import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  AppState,
  BackHandler,
  Keyboard,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import {
  OtpCodeField,
  type OtpCodeFieldHandle
} from "../../src/components/ui/OtpCodeField";
import { TextField } from "../../src/components/ui/TextField";
import { Button } from "../../src/components/ui/buttons/Button";
import { SystemModal } from "../../src/components/ui/surfaces/SystemModal";
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
import { palette, spacing, surfaces, typography } from "../../src/theme/tokens";

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
  const [isBiometricUnlockPending, setIsBiometricUnlockPending] = useState(false);
  const [methodMenuVisible, setMethodMenuVisible] = useState(false);
  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);
  const codeFieldRef = useRef<OtpCodeFieldHandle | null>(null);
  const lastAttemptKeyRef = useRef<string | null>(null);
  const verifyMutation = useVerifyMfaLoginMutation();
  const {
    applyAuthTokenResponse,
    biometricAvailable,
    biometricEnabled,
    completeRememberedSessionMfa,
    signInAnotherWay,
    unlockWithBiometrics
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

  const chooseDifferentAccount = useCallback(async () => {
    clearPendingMfaLogin();
    if (pending?.context === "remembered_session") {
      await signInAnotherWay();
    }
    router.replace("/(auth)/login" as never);
  }, [pending?.context, signInAnotherWay]);

  const isVerifying = verifyMutation.isPending
    || isRememberedSessionPending
    || isBiometricUnlockPending;

  useEffect(() => {
    const showSubscription = Keyboard.addListener("keyboardDidShow", () => {
      setIsKeyboardVisible(true);
    });
    const hideSubscription = Keyboard.addListener("keyboardDidHide", () => {
      setIsKeyboardVisible(false);
    });

    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, []);

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

  const handleBiometricUnlock = useCallback(async () => {
    if (isVerifying) {
      return;
    }

    setMethodMenuVisible(false);
    Keyboard.dismiss();
    setError(null);
    setIsBiometricUnlockPending(true);
    try {
      const result = await unlockWithBiometrics();
      if (!result.succeeded) {
        if (result.message) {
          setError(result.message);
          showFlashMessage(result.message, { tone: "error", durationMs: 3200 });
        }
        return;
      }

      clearPendingMfaLogin();
      playSuccess();
      router.replace("/(tabs)");
    } finally {
      setIsBiometricUnlockPending(false);
    }
  }, [isVerifying, playSuccess, unlockWithBiometrics]);

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
  const canUnlockWithFingerprint = pending.context === "remembered_session"
    && biometricAvailable
    && biometricEnabled;

  const selectMethod = (nextMethod: "totp" | "recovery_code") => {
    setMethodMenuVisible(false);
    setMethod(nextMethod);
    setCode("");
    setError(null);
    setCanRetry(false);
    setRememberDevice(false);
    lastAttemptKeyRef.current = null;
  };

  return (
    <>
      <AuthScreen
        focusedInputExtraClearance={method === "totp" ? 64 : 0}
        resetScrollOnKeyboardHide
      >
        <View style={styles.content}>
          <View style={styles.formContent}>
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
              <Text style={styles.accountHint}>{pending.accountHint}</Text>
            </View>

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
              <View style={styles.verificationStatus} accessibilityLiveRegion="polite">
                {isVerifying ? (
                  <View style={styles.checkingRow}>
                    <ActivityIndicator color={palette.primary} size="small" />
                    <Text style={styles.checkingText}>
                      {isBiometricUnlockPending ? "Checking fingerprint..." : "Checking code..."}
                    </Text>
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
          </View>

          <View style={[styles.actions, isKeyboardVisible ? styles.actionsKeyboardVisible : null]}>
            <Button
              label="Use another method"
              variant="ghost"
              onPress={() => {
                Keyboard.dismiss();
                setMethodMenuVisible(true);
              }}
              disabled={isVerifying}
            />
          </View>
        </View>
      </AuthScreen>

      <SystemModal
        visible={methodMenuVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setMethodMenuVisible(false)}
      >
        <Pressable style={styles.methodOverlay} onPress={() => setMethodMenuVisible(false)}>
          <Pressable style={styles.methodSheet} onPress={() => undefined}>
            <View style={styles.methodHeader}>
              <Text style={styles.methodTitle}>Use another method</Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Close authentication methods"
                hitSlop={12}
                onPress={() => setMethodMenuVisible(false)}
                style={({ pressed }) => [styles.closeButton, pressed ? styles.pressed : null]}
              >
                <Ionicons name="close" size={24} color={palette.textPrimary} />
              </Pressable>
            </View>

            <View style={styles.methodOptions}>
              {canUnlockWithFingerprint ? (
                <MethodOption
                  icon="finger-print"
                  label="Unlock with fingerprint"
                  onPress={() => void handleBiometricUnlock()}
                />
              ) : null}
              {alternativeMethod ? (
                <MethodOption
                  icon={alternativeMethod === "totp" ? "keypad-outline" : "key-outline"}
                  label={alternativeMethod === "totp" ? "Use Authenticator" : "Use a recovery code"}
                  onPress={() => selectMethod(alternativeMethod)}
                />
              ) : null}
              <MethodOption
                icon="person-outline"
                label="Use a different account"
                onPress={() => void chooseDifferentAccount()}
              />
            </View>
          </Pressable>
        </Pressable>
      </SystemModal>
    </>
  );
}

type MethodOptionProps = {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  onPress: () => void;
};

function MethodOption({ icon, label, onPress }: MethodOptionProps) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.methodOption, pressed ? styles.methodOptionPressed : null]}
    >
      <View style={styles.methodIcon}>
        <Ionicons name={icon} size={22} color={palette.primary} />
      </View>
      <Text style={styles.methodLabel}>{label}</Text>
      <Ionicons name="chevron-forward" size={20} color={palette.textMuted} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  content: {
    flex: 1,
    width: "100%",
    maxWidth: 440,
    alignSelf: "center",
    paddingHorizontal: spacing[20],
    paddingTop: spacing[8],
    paddingBottom: spacing[8]
  },
  formContent: {
    gap: spacing[20]
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
  accountHint: {
    color: palette.textSecondary,
    fontSize: typography.helper.fontSize,
    lineHeight: typography.helper.lineHeight,
    fontFamily: typography.label.fontFamily,
    marginTop: spacing[4]
  },
  rememberDevice: {
    minHeight: 44,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    alignSelf: "center"
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
    marginTop: "auto",
    paddingTop: spacing[20]
  },
  actionsKeyboardVisible: {
    marginTop: spacing[8],
    paddingTop: 0
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
  },
  methodOverlay: {
    flex: 1,
    justifyContent: "flex-end",
    backgroundColor: palette.overlay
  },
  methodSheet: {
    borderTopLeftRadius: 8,
    borderTopRightRadius: 8,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    paddingHorizontal: spacing[20],
    paddingTop: spacing[20],
    paddingBottom: spacing[20]
  },
  methodHeader: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  methodTitle: {
    color: palette.textPrimary,
    fontSize: typography.title2.fontSize,
    lineHeight: typography.title2.lineHeight,
    fontFamily: typography.title2.fontFamily
  },
  closeButton: {
    width: 44,
    height: 44,
    alignItems: "center",
    justifyContent: "center"
  },
  methodOptions: {
    paddingTop: spacing[8]
  },
  methodOption: {
    minHeight: 60,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12],
    borderTopWidth: 1,
    borderTopColor: palette.border
  },
  methodOptionPressed: {
    opacity: 0.7
  },
  methodIcon: {
    width: 36,
    height: 36,
    alignItems: "center",
    justifyContent: "center"
  },
  methodLabel: {
    flex: 1,
    color: palette.textPrimary,
    fontSize: typography.body.fontSize,
    lineHeight: typography.body.lineHeight,
    fontFamily: typography.label.fontFamily
  }
});
