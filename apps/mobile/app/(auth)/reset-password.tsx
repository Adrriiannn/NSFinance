import { router } from "expo-router";
import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useResetPasswordMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function ResetPasswordScreen() {
  const resetMutation = useResetPasswordMutation();
  const [token, setToken] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [message, setMessage] = useState<string | null>(null);

  const handleReset = async () => {
    const response = await resetMutation.mutateAsync({
      token: token.trim(),
      newPassword
    });
    setMessage(response.message);
  };

  return (
    <AuthScreen>
      <View style={styles.header}>
        <Text style={styles.title}>Reset password</Text>
        <Text style={styles.subtitle}>Use the token from email (or dev token) to set a new password.</Text>
      </View>

      {resetMutation.isError ? (
        <ErrorState
          title="Reset failed"
          message={formatUnknownError(resetMutation.error)}
          onRetry={handleReset}
          retryLabel="Try again"
        />
      ) : null}

      <View style={styles.form}>
        <TextField label="Reset token" value={token} onChangeText={setToken} placeholder="Paste token" />
        <TextField
          label="New password"
          value={newPassword}
          onChangeText={setNewPassword}
          placeholder="At least 10 characters"
          secureTextEntry
        />
      </View>

      {message ? <Text style={styles.message}>{message}</Text> : null}

      <View style={styles.actions}>
        <PrimaryButton
          label="Update password"
          onPress={() => void handleReset()}
          isLoading={resetMutation.isPending}
          disabled={!token.trim() || !newPassword}
        />
        <SecondaryButton label="Back to sign in" onPress={() => router.push("/login" as never)} />
      </View>
    </AuthScreen>
  );
}

const styles = StyleSheet.create({
  header: {
    marginTop: spacing[24],
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2
  },
  form: {
    marginTop: spacing[20],
    gap: spacing[12]
  },
  message: {
    marginTop: spacing[16],
    color: palette.textSecondary,
    ...typography.body2
  },
  actions: {
    marginTop: spacing[20],
    gap: spacing[12]
  }
});
