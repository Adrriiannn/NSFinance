import { router } from "expo-router";
import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useForgotPasswordMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function ForgotPasswordScreen() {
  const forgotMutation = useForgotPasswordMutation();
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [debugToken, setDebugToken] = useState<string | null>(null);

  const handleRequest = async () => {
    const response = await forgotMutation.mutateAsync({
      email: email.trim().toLowerCase()
    });
    setMessage(response.message);
    setDebugToken(response.debugToken ?? null);
  };

  return (
    <AuthScreen>
      <View style={styles.header}>
        <Text style={styles.title}>Forgot password</Text>
        <Text style={styles.subtitle}>Request a reset token for your account email.</Text>
      </View>

      {forgotMutation.isError ? (
        <ErrorState
          title="Request failed"
          message={formatUnknownError(forgotMutation.error)}
          onRetry={handleRequest}
          retryLabel="Try again"
        />
      ) : null}

      <View style={styles.form}>
        <TextField
          label="Email"
          value={email}
          onChangeText={setEmail}
          autoCapitalize="none"
          keyboardType="email-address"
          placeholder="you@example.com"
        />
      </View>

      {message ? <Text style={styles.message}>{message}</Text> : null}
      {debugToken ? <Text style={styles.debugToken}>Dev reset token: {debugToken}</Text> : null}

      <View style={styles.actions}>
        <PrimaryButton
          label="Request reset"
          onPress={() => void handleRequest()}
          isLoading={forgotMutation.isPending}
          disabled={!email.trim()}
        />
        <SecondaryButton label="Reset with token" onPress={() => router.push("/reset-password" as never)} />
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
  debugToken: {
    marginTop: spacing[8],
    color: palette.accent,
    ...typography.caption
  },
  actions: {
    marginTop: spacing[20],
    gap: spacing[12]
  }
});
