import { router } from "expo-router";
import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { OtpCodeField } from "../../src/components/ui/OtpCodeField";
import { TextField } from "../../src/components/ui/TextField";
import { Button } from "../../src/components/ui/buttons/Button";
import { useVerifyMfaLoginMutation } from "../../src/features/auth/useAuthMutations";
import {
  clearPendingMfaLogin,
  getPendingMfaLogin
} from "../../src/features/auth/pendingAuthFlow";
import { formatUnknownError } from "../../src/lib/api/errors";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function MfaScreen() {
  const [pending] = useState(getPendingMfaLogin);
  const [method, setMethod] = useState<"totp" | "recovery_code">("totp");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const verifyMutation = useVerifyMfaLoginMutation();
  const { applyAuthTokenResponse } = useAuthSession();
  const { playSuccess } = useFeedbackSound();

  const handleVerify = async () => {
    if (!pending || !code.trim()) {
      setError(method === "totp" ? "Enter the six-digit code." : "Enter a recovery code.");
      return;
    }

    setError(null);
    try {
      const session = await verifyMutation.mutateAsync({
        challengeId: pending.challengeId,
        challengeToken: pending.challengeToken,
        code: code.trim(),
        method,
        deviceContext: buildDeviceContext()
      });
      await applyAuthTokenResponse(session, pending.rememberMe);
      clearPendingMfaLogin();
      playSuccess();
      router.replace("/(tabs)");
    } catch (nextError) {
      setCode("");
      setError(formatUnknownError(nextError));
    }
  };

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
            value={code}
            onChange={(value) => {
              setCode(value);
              setError(null);
            }}
            disabled={verifyMutation.isPending}
            error={error}
            accessibilityLabel="Authenticator code"
          />
        ) : (
          <TextField
            label="Recovery code"
            value={code}
            onChangeText={(value) => {
              setCode(value.toUpperCase());
              setError(null);
            }}
            autoCapitalize="characters"
            autoCorrect={false}
            error={error ?? undefined}
          />
        )}

        <View style={styles.actions}>
          <Button
            label="Continue"
            onPress={() => void handleVerify()}
            disabled={method === "totp" ? code.length !== 6 : !code.trim()}
            isLoading={verifyMutation.isPending}
          />
          <Button
            label={method === "totp" ? "I don't have access to the authenticator app" : "Use authenticator app"}
            variant="ghost"
            onPress={() => {
              setMethod((current) => (current === "totp" ? "recovery_code" : "totp"));
              setCode("");
              setError(null);
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
  }
});
