import { router } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import {
  OtpCodeField,
  type OtpCodeFieldHandle
} from "../../src/components/ui/OtpCodeField";
import { Button } from "../../src/components/ui/buttons/Button";
import {
  useConfirmEmailVerificationMutation,
  useRequestEmailVerificationMutation
} from "../../src/features/auth/useAuthMutations";
import {
  buildOtpAttemptKey,
  normalizeOtpCode,
  shouldAutoSubmitOtp
} from "../../src/features/auth/otpAutoSubmitPolicy";
import {
  clearPendingEmailVerification,
  getPendingEmailVerification,
  stageEmailVerification
} from "../../src/features/auth/pendingAuthFlow";
import { ApiClientError, formatUnknownError } from "../../src/lib/api/errors";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import { showFlashMessage } from "../../src/lib/flashMessage";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

const INVALID_CODE_MESSAGE =
  "That code is incorrect or no longer active. Check your latest email and try again.";

export default function VerifyEmailScreen() {
  const [pending, setPending] = useState(getPendingEmailVerification);
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [canRetry, setCanRetry] = useState(false);
  const [now, setNow] = useState(Date.now());
  const codeFieldRef = useRef<OtpCodeFieldHandle | null>(null);
  const lastAttemptKeyRef = useRef<string | null>(null);
  const confirmMutation = useConfirmEmailVerificationMutation();
  const resendMutation = useRequestEmailVerificationMutation();
  const { applyAuthTokenResponse } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  const resendAvailableAt = useMemo(() => {
    if (!pending) {
      return 0;
    }
    return Date.parse(pending.expiresUtc) - 10 * 60 * 1000 + pending.resendAfterSeconds * 1000;
  }, [pending]);
  const resendWaitSeconds = Math.max(0, Math.ceil((resendAvailableAt - now) / 1000));

  const handleConfirm = useCallback(async (force = false) => {
    const attemptKey = pending ? buildOtpAttemptKey(pending.challengeId, code) : null;
    if (!pending || !attemptKey || (!force && attemptKey === lastAttemptKeyRef.current)) {
      return;
    }

    lastAttemptKeyRef.current = attemptKey;
    setError(null);
    setCanRetry(false);
    try {
      const session = await confirmMutation.mutateAsync({
        challengeId: pending.challengeId,
        code,
        deviceContext: buildDeviceContext()
      });
      await applyAuthTokenResponse(session, { rememberSession: pending.rememberSession });
      clearPendingEmailVerification();
      playSuccess();
      router.replace("/(tabs)");
    } catch (nextError) {
      const isInvalidCode =
        nextError instanceof ApiClientError && nextError.code === "identity_code_invalid";
      const message = isInvalidCode ? INVALID_CODE_MESSAGE : formatUnknownError(nextError);

      setError(message);
      setCanRetry(!isInvalidCode);
      showFlashMessage(message, { tone: "error", durationMs: 3200 });
    }
  }, [applyAuthTokenResponse, code, confirmMutation, pending, playSuccess]);

  useEffect(() => {
    if (
      !pending ||
      !shouldAutoSubmitOtp({
        challengeId: pending.challengeId,
        code,
        isPending: confirmMutation.isPending,
        lastAttemptKey: lastAttemptKeyRef.current
      })
    ) {
      return;
    }

    void handleConfirm();
  }, [code, confirmMutation.isPending, handleConfirm, pending]);

  useEffect(() => {
    if (!error || confirmMutation.isPending) {
      return;
    }

    const focusTimer = setTimeout(() => codeFieldRef.current?.focus(), 50);
    return () => clearTimeout(focusTimer);
  }, [confirmMutation.isPending, error]);

  const handleResend = async () => {
    if (!pending?.email || resendWaitSeconds > 0) {
      return;
    }

    setError(null);
    setCanRetry(false);
    try {
      const delivery = await resendMutation.mutateAsync({ email: pending.email });
      const nextPending = {
        ...delivery,
        email: pending.email,
        rememberSession: pending.rememberSession
      };
      stageEmailVerification(nextPending);
      setPending(nextPending);
      setCode("");
      lastAttemptKeyRef.current = null;
      setNow(Date.now());
    } catch (nextError) {
      const message = formatUnknownError(nextError);
      setError(message);
      showFlashMessage(message, { tone: "error", durationMs: 3200 });
    }
  };

  if (!pending) {
    return (
      <AuthScreen>
        <View style={styles.content}>
          <Text style={styles.title}>Start again</Text>
          <Text style={styles.body}>This verification attempt is no longer available.</Text>
          <Button label="Return to sign in" onPress={() => router.replace("/(auth)/login")} />
        </View>
      </AuthScreen>
    );
  }

  return (
    <AuthScreen>
      <View style={styles.content}>
        <View style={styles.copy}>
          <Text style={styles.eyebrow}>EMAIL VERIFICATION</Text>
          <Text style={styles.title}>Check your inbox</Text>
          <Text style={styles.body}>{pending.message}</Text>
        </View>

        <OtpCodeField
          ref={codeFieldRef}
          value={code}
          onChange={(value) => {
            setCode(normalizeOtpCode(value));
            setError(null);
            setCanRetry(false);
          }}
          disabled={confirmMutation.isPending}
          error={error}
          autoFocus
        />

        <View style={styles.actions}>
          <View style={styles.confirmationStatus} accessibilityLiveRegion="polite">
            {confirmMutation.isPending ? (
              <View style={styles.checkingRow}>
                <ActivityIndicator color={palette.primary} size="small" />
                <Text style={styles.checkingText}>Checking code...</Text>
              </View>
            ) : canRetry ? (
              <Button label="Try again" onPress={() => void handleConfirm(true)} />
            ) : null}
          </View>
          {pending.email ? (
            <Button
              label={resendWaitSeconds > 0 ? `Resend in ${resendWaitSeconds}s` : "Resend code"}
              variant="ghost"
              disabled={resendWaitSeconds > 0}
              isLoading={resendMutation.isPending}
              onPress={() => void handleResend()}
            />
          ) : null}
          <Button
            label="Use another account"
            variant="ghost"
            onPress={() => {
              clearPendingEmailVerification();
              router.replace("/(auth)/login");
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
  confirmationStatus: {
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
