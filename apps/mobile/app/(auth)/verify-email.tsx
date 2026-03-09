import { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import {
  useConfirmEmailVerificationMutation,
  useRequestEmailVerificationMutation
} from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { palette, spacing, typography } from "../../src/theme/tokens";

export default function VerifyEmailScreen() {
  const requestMutation = useRequestEmailVerificationMutation();
  const confirmMutation = useConfirmEmailVerificationMutation();
  const [email, setEmail] = useState("");
  const [token, setToken] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [debugToken, setDebugToken] = useState<string | null>(null);

  const handleRequest = async () => {
    const response = await requestMutation.mutateAsync({
      email: email.trim().toLowerCase()
    });
    setMessage(response.message);
    setDebugToken(response.debugToken ?? null);
  };

  const handleConfirm = async () => {
    const response = await confirmMutation.mutateAsync({
      token: token.trim()
    });
    setMessage(response.message);
  };

  return (
    <AuthScreen>
      <View style={styles.header}>
        <Text style={styles.title}>Verify email</Text>
        <Text style={styles.subtitle}>Request and confirm email verification tokens.</Text>
      </View>

      {requestMutation.isError ? (
        <ErrorState
          title="Verification request failed"
          message={formatUnknownError(requestMutation.error)}
          onRetry={handleRequest}
          retryLabel="Try again"
        />
      ) : null}

      {confirmMutation.isError ? (
        <ErrorState
          title="Verification failed"
          message={formatUnknownError(confirmMutation.error)}
          onRetry={handleConfirm}
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
        <PrimaryButton
          label="Request verification"
          onPress={() => void handleRequest()}
          isLoading={requestMutation.isPending}
          disabled={!email.trim()}
        />

        <TextField label="Verification token" value={token} onChangeText={setToken} placeholder="Paste token" />
        <SecondaryButton
          label="Confirm token"
          onPress={() => void handleConfirm()}
          disabled={!token.trim() || confirmMutation.isPending}
        />
      </View>

      {message ? <Text style={styles.message}>{message}</Text> : null}
      {debugToken ? <Text style={styles.debugToken}>Dev verification token: {debugToken}</Text> : null}
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
  }
});
