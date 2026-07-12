import { router } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { OtpCodeField } from "../../src/components/ui/OtpCodeField";
import { Button } from "../../src/components/ui/buttons/Button";
import {
  useConfirmEmailVerificationMutation,
  useRequestEmailVerificationMutation
} from "../../src/features/auth/useAuthMutations";
import {
  clearPendingEmailVerification,
  getPendingEmailVerification,
  stageEmailVerification
} from "../../src/features/auth/pendingAuthFlow";
import { formatUnknownError } from "../../src/lib/api/errors";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function VerifyEmailScreen() {
  const [pending, setPending] = useState(getPendingEmailVerification);
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(Date.now());
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

  const handleConfirm = async () => {
    if (!pending || code.length !== 6) {
      setError("Enter the six-digit code.");
      return;
    }

    setError(null);
    try {
      const session = await confirmMutation.mutateAsync({
        challengeId: pending.challengeId,
        code,
        deviceContext: buildDeviceContext()
      });
      await applyAuthTokenResponse(session, pending.rememberMe);
      clearPendingEmailVerification();
      playSuccess();
      router.replace("/(tabs)");
    } catch (nextError) {
      setCode("");
      setError(formatUnknownError(nextError));
    }
  };

  const handleResend = async () => {
    if (!pending?.email || resendWaitSeconds > 0) {
      return;
    }

    setError(null);
    try {
      const delivery = await resendMutation.mutateAsync({ email: pending.email });
      const nextPending = { ...delivery, email: pending.email, rememberMe: pending.rememberMe };
      stageEmailVerification(nextPending);
      setPending(nextPending);
      setCode("");
      setNow(Date.now());
    } catch (nextError) {
      setError(formatUnknownError(nextError));
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
          value={code}
          onChange={(value) => {
            setCode(value);
            setError(null);
          }}
          disabled={confirmMutation.isPending}
          error={error}
        />

        <View style={styles.actions}>
          <Button
            label="Confirm email"
            onPress={() => void handleConfirm()}
            disabled={code.length !== 6}
            isLoading={confirmMutation.isPending}
          />
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
  }
});
