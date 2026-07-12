import Ionicons from "@expo/vector-icons/Ionicons";
import { router } from "expo-router";
import { useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { OtpCodeField } from "../../src/components/ui/OtpCodeField";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { TextField } from "../../src/components/ui/TextField";
import { Button } from "../../src/components/ui/buttons/Button";
import { checkPasswordPolicy } from "../../src/features/auth/authApi";
import { clearMfaTrustedDeviceCredential } from "../../src/features/auth/mfaTrustedDevice";
import {
  hasNumberOrSymbol,
  isLengthWithinPolicy,
  PASSWORD_MAX_LENGTH
} from "../../src/features/auth/passwordPolicy";
import {
  useForgotPasswordMutation,
  useResetPasswordMutation,
  useVerifyPasswordRecoveryCodeMutation
} from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import type { CodeDeliveryResponse, PasswordRecoveryGrantResponse } from "../../src/types/api";
import { palette, spacing, typography } from "../../src/theme/tokens";

type RecoveryStage = "request" | "code" | "reset" | "done";

export default function ForgotPasswordScreen() {
  const [stage, setStage] = useState<RecoveryStage>("request");
  const [identity, setIdentity] = useState("");
  const [delivery, setDelivery] = useState<CodeDeliveryResponse | null>(null);
  const [grant, setGrant] = useState<PasswordRecoveryGrantResponse | null>(null);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const forgotMutation = useForgotPasswordMutation();
  const verifyMutation = useVerifyPasswordRecoveryCodeMutation();
  const resetMutation = useResetPasswordMutation();

  const isPasswordValid = useMemo(
    () => isLengthWithinPolicy(password) && hasNumberOrSymbol(password),
    [password]
  );

  const requestCode = async () => {
    if (!identity.trim()) {
      setError("Enter your email or verified phone number.");
      return;
    }

    setError(null);
    try {
      const response = await forgotMutation.mutateAsync({ identity: identity.trim() });
      setDelivery(response);
      setCode("");
      setStage("code");
    } catch (nextError) {
      setError(formatUnknownError(nextError));
    }
  };

  const verifyCode = async () => {
    if (!delivery || code.length !== 6) {
      setError("Enter the six-digit code.");
      return;
    }

    setError(null);
    try {
      const response = await verifyMutation.mutateAsync({
        challengeId: delivery.challengeId,
        code
      });
      setGrant(response);
      setStage("reset");
    } catch (nextError) {
      setCode("");
      setError(formatUnknownError(nextError));
    }
  };

  const resetPassword = async () => {
    if (!grant || !isPasswordValid) {
      setError("Use 12 to 128 characters with at least one number or symbol.");
      return;
    }

    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    setError(null);
    try {
      const policy = await checkPasswordPolicy({ password });
      if (policy.breachStatus !== "safe") {
        setError(
          policy.breachStatus === "compromised"
            ? "Choose a password that has not appeared in known data breaches."
            : "Password safety could not be checked. Please try again."
        );
        return;
      }

      await resetMutation.mutateAsync({
        challengeId: grant.challengeId,
        recoveryToken: grant.recoveryToken,
        newPassword: password
      });
      await clearMfaTrustedDeviceCredential();
      setPassword("");
      setConfirmPassword("");
      setGrant(null);
      setStage("done");
    } catch (nextError) {
      setError(formatUnknownError(nextError));
    }
  };

  const title = stage === "request"
    ? "Reset your password"
    : stage === "code"
      ? "Enter your code"
      : stage === "reset"
        ? "Choose a new password"
        : "Password updated";
  const body = stage === "request"
    ? "Use the email or verified phone number on your account."
    : stage === "code"
      ? (delivery?.message ?? "Enter the six-digit code we sent you.")
      : stage === "reset"
        ? "Use a unique password you do not use elsewhere."
        : "You can now sign in with your new password.";

  return (
    <AuthScreen>
      <View style={styles.content}>
        <View style={styles.copy}>
          <Text style={styles.title}>{title}</Text>
          <Text style={styles.body}>{body}</Text>
        </View>

        {stage === "request" ? (
          <TextField
            label="Email or phone"
            value={identity}
            onChangeText={(value) => {
              setIdentity(value);
              setError(null);
            }}
            autoCapitalize="none"
            autoCorrect={false}
            placeholder="Email or verified phone number"
            error={error ?? undefined}
          />
        ) : null}

        {stage === "code" ? (
          <OtpCodeField
            value={code}
            onChange={(value) => {
              setCode(value);
              setError(null);
            }}
            disabled={verifyMutation.isPending}
            error={error}
          />
        ) : null}

        {stage === "reset" ? (
          <View style={styles.fields}>
            <PasswordField
              label="New password"
              value={password}
              onChangeText={(value) => {
                setPassword(value.slice(0, PASSWORD_MAX_LENGTH));
                setError(null);
              }}
              autoComplete="new-password"
              textContentType="newPassword"
            />
            <PasswordField
              label="Confirm password"
              value={confirmPassword}
              onChangeText={(value) => {
                setConfirmPassword(value.slice(0, PASSWORD_MAX_LENGTH));
                setError(null);
              }}
              autoComplete="new-password"
              textContentType="newPassword"
              error={error ?? undefined}
            />
          </View>
        ) : null}

        {stage === "done" ? (
          <View style={styles.successIcon}>
            <Ionicons name="checkmark-circle" size={48} color={palette.success} />
          </View>
        ) : null}

        <View style={styles.actions}>
          {stage === "request" ? (
            <Button
              label="Send Code"
              onPress={() => void requestCode()}
              disabled={!identity.trim()}
              isLoading={forgotMutation.isPending}
            />
          ) : null}
          {stage === "code" ? (
            <>
              <Button
                label="Continue"
                onPress={() => void verifyCode()}
                disabled={code.length !== 6}
                isLoading={verifyMutation.isPending}
              />
              <Button label="Send another code" variant="ghost" onPress={() => void requestCode()} />
            </>
          ) : null}
          {stage === "reset" ? (
            <Button
              label="Update password"
              onPress={() => void resetPassword()}
              disabled={!password || !confirmPassword}
              isLoading={resetMutation.isPending}
            />
          ) : null}
          {stage === "done" ? (
            <Button label="Return to sign in" onPress={() => router.replace("/(auth)/login")} />
          ) : (
            <Button label="Back to sign in" variant="ghost" onPress={() => router.replace("/(auth)/login")} />
          )}
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
    gap: spacing[28],
    paddingHorizontal: spacing[20],
    paddingVertical: spacing[32]
  },
  copy: {
    alignItems: "center",
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title,
    textAlign: "center"
  },
  body: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  fields: {
    gap: spacing[14]
  },
  actions: {
    gap: spacing[12]
  },
  successIcon: {
    alignItems: "center",
    minHeight: 56,
    justifyContent: "center"
  }
});
