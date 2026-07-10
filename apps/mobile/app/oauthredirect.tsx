import { router, useLocalSearchParams } from "expo-router";
import { useEffect } from "react";
import { ActivityIndicator, Pressable, Text, View } from "react-native";
import {
  setGoogleOAuthCompletionState,
  useGoogleOAuthCompletionState
} from "../src/features/auth/googleOAuthCompletionState";
import { resetGoogleOAuthFlowState } from "../src/features/auth/googleOAuthFlowState";
import { palette, surfaces, typography, createRuntimeStyleSheet } from "../src/theme/tokens";

const SUCCESS_REDIRECT_DELAY_MS = 600;

function getFirstParamValue(value: string | string[] | undefined): string | undefined {
  if (Array.isArray(value)) {
    const first = value[0]?.trim();
    return first && first.length > 0 ? first : undefined;
  }

  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : undefined;
}

export default function OAuthRedirectScreen() {
  const params = useLocalSearchParams();
  const completion = useGoogleOAuthCompletionState();

  useEffect(() => {
    const errorDescription = getFirstParamValue(params.error_description as string | string[] | undefined);
    const errorCode = getFirstParamValue(params.error as string | string[] | undefined);
    if (!errorCode && !errorDescription) {
      return;
    }

    const message =
      errorDescription ??
      (errorCode ? `Google sign-in failed: ${errorCode}.` : "Google sign-in did not complete. Please try again.");
    setGoogleOAuthCompletionState("failure", message);
  }, [params]);

  useEffect(() => {
    if (completion.status !== "success") {
      return;
    }

    const timeout = setTimeout(() => {
      router.replace("/(tabs)" as never);
    }, SUCCESS_REDIRECT_DELAY_MS);

    return () => clearTimeout(timeout);
  }, [completion.status]);

  const isFailure = completion.status === "failure";
  const statusMessage = isFailure
    ? completion.message || "Google sign-in did not complete. Please try again."
    : completion.status === "success"
      ? "Sign-in complete. Opening NSFinance..."
      : "Securely returning to NSFinance...";

  return (
    <View style={styles.container}>
      <View style={styles.card}>
        <Text style={styles.eyebrow}>NSFinance Google Sign-In</Text>
        <Text style={styles.title}>{isFailure ? "Sign-in needs another try" : "Completing sign-in"}</Text>
        <View style={styles.statusRow}>
          {!isFailure ? <ActivityIndicator color={palette.accent} size="small" /> : null}
          <Text style={isFailure ? styles.errorText : styles.statusText}>{statusMessage}</Text>
        </View>

        {isFailure ? (
          <Pressable
            style={({ pressed }) => [styles.backButton, pressed ? styles.backButtonPressed : null]}
            onPress={() => {
              resetGoogleOAuthFlowState("manual_retry");
              router.replace({
                pathname: "/login",
                params: { googleError: statusMessage }
              } as never);
            }}
          >
            <Text style={styles.backButtonText}>Return to login</Text>
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 16,
    backgroundColor: palette.appBackground
  },
  card: {
    width: "100%",
    maxWidth: 420,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    borderRadius: 6,
    backgroundColor: surfaces.card,
    paddingHorizontal: 16,
    paddingVertical: 16,
    gap: 10
  },
  eyebrow: {
    color: palette.textSecondary,
    ...typography.caption,
    letterSpacing: 0,
    fontWeight: "500",
    textTransform: "uppercase"
  },
  title: {
    color: palette.textPrimary,
    ...typography.title2,
    fontWeight: "600"
  },
  statusRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8
  },
  statusText: {
    flex: 1,
    color: palette.textSecondary,
    ...typography.body2
  },
  errorText: {
    flex: 1,
    color: palette.negative,
    ...typography.body2
  },
  backButton: {
    minHeight: 44,
    paddingHorizontal: 16,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.accent,
    backgroundColor: palette.accent,
    alignItems: "center",
    justifyContent: "center"
  },
  backButtonPressed: {
    opacity: 0.9
  },
  backButtonText: {
    color: palette.appBackground,
    ...typography.body2,
    fontWeight: "600"
  }
}));
