import { router } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
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

export default function MfaScreen() {
  const [pending] = useState(getPendingMfaLogin);
  const [method, setMethod] = useState<"totp" | "recovery_code">("totp");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [canRetry, setCanRetry] = useState(false);
  const codeFieldRef = useRef<OtpCodeFieldHandle | null>(null);
  const lastAttemptKeyRef = useRef<string | null>(null);
  const verifyMutation = useVerifyMfaLoginMutation();
  const { applyAuthTokenResponse } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const handleVerify = useCallback(async (force = false) => {
    if (!pending || !code.trim()) {
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
      const session = await verifyMutation.mutateAsync({
        challengeId: pending.challengeId,
        challengeToken: pending.challengeToken,
        code: code.trim(),
        method,
        deviceContext: buildDeviceContext()
      });
      await applyAuthTokenResponse(session);
      clearPendingMfaLogin();
      playSuccess();
      router.replace("/(tabs)");
    } catch (nextError) {
      const isInvalidCode =
        nextError instanceof ApiClientError && nextError.code === "mfa_code_invalid";
      const message = isInvalidCode
        ? method === "totp" ? INVALID_TOTP_MESSAGE : INVALID_RECOVERY_MESSAGE
        : formatUnknownError(nextError);

      setError(message);
      setCanRetry(!isInvalidCode);
      showFlashMessage(message, { tone: "error", durationMs: 3200 });
    }
  }, [applyAuthTokenResponse, code, method, pending, playSuccess, verifyMutation]);

  useEffect(() => {
    if (
      method !== "totp"
      || !pending
      || !shouldAutoSubmitOtp({
        challengeId: pending.challengeId,
        code,
        isPending: verifyMutation.isPending,
        lastAttemptKey: lastAttemptKeyRef.current
      })
    ) {
      return;
    }

    void handleVerify();
  }, [code, handleVerify, method, pending, verifyMutation.isPending]);

  useEffect(() => {
    if (method !== "totp" || !error || verifyMutation.isPending) {
      return;
    }

    const focusTimer = setTimeout(() => codeFieldRef.current?.focus(), 50);
    return () => clearTimeout(focusTimer);
  }, [error, method, verifyMutation.isPending]);

  if (!pending) {
    return (
      <AuthScreen>
        <View style={styles.content}>
          <Text style={styles.title}>Sign in again</Text>
          <Text style={styles.body}>This authentication challenge is no longer available.</Text>
          <Button label="Return to sign in" onPress={() => router.replace("/(auth)/login")} />
        </View>
      </AuthScreen>
    );
  }

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
          <OtpCodeField
            ref={codeFieldRef}
            value={code}
            onChange={(value) => {
              setCode(normalizeOtpCode(value));
              setError(null);
              setCanRetry(false);
            }}
            disabled={verifyMutation.isPending}
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
              {verifyMutation.isPending ? (
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
              isLoading={verifyMutation.isPending}
            />
          )}
          <Button
            label={method === "totp" ? "I don't have access to the authenticator app" : "Use authenticator app"}
            variant="ghost"
            onPress={() => {
              setMethod((current) => (current === "totp" ? "recovery_code" : "totp"));
              setCode("");
              setError(null);
              setCanRetry(false);
              lastAttemptKeyRef.current = null;
            }}
          />
          <Button
            label="Reset my password"
            variant="ghost"
            onPress={() => {
              clearPendingMfaLogin();
              router.replace("/(auth)/forgot-password");
            }}
          />
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
