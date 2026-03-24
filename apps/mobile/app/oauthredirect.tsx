import { router, useLocalSearchParams, usePathname } from "expo-router";
import { useEffect } from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { useGoogleOAuthDebugState, pushGoogleOAuthDebugStep, updateGoogleOAuthDebugState } from "../src/features/auth/googleOAuthDebug";
import { palette, typography } from "../src/theme/tokens";

const SUCCESS_REDIRECT_DELAY_MS = 600;

function logOAuthRedirectDebug(event: string, details?: Record<string, unknown>) {
  if (!__DEV__) {
    return;
  }

  if (!details) {
    console.info(`[GoogleAuth][callback] ${event}`);
    return;
  }

  console.info(`[GoogleAuth][callback] ${event}`, details);
}

function getFirstParamValue(value: string | string[] | undefined): string | undefined {
  if (Array.isArray(value)) {
    const first = value[0]?.trim();
    return first && first.length > 0 ? first : undefined;
  }

  const trimmed = value?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : undefined;
}

export default function OAuthRedirectScreen() {
  const pathname = usePathname();
  const params = useLocalSearchParams();
  const debugState = useGoogleOAuthDebugState();

  useEffect(() => {
    logOAuthRedirectDebug("route_mounted", {
      pathname,
      paramKeys: Object.keys(params),
      hasCode: Boolean(params.code),
      hasState: Boolean(params.state),
      hasError: Boolean(params.error),
      error: getFirstParamValue(params.error as string | string[] | undefined),
      errorDescription: getFirstParamValue(params.error_description as string | string[] | undefined)
    });

    const errorDescription = getFirstParamValue(params.error_description as string | string[] | undefined);
    const errorCode = getFirstParamValue(params.error as string | string[] | undefined);
    const callbackErrorMessage =
      errorDescription ??
      (errorCode ? `Google sign-in failed: ${errorCode}.` : "Google sign-in did not complete. Please try again.");

    if (errorCode || errorDescription) {
      updateGoogleOAuthDebugState({
        backendOutcome: "failure",
        backendMessage: callbackErrorMessage
      });
      pushGoogleOAuthDebugStep("callback_error", callbackErrorMessage);
    }
  }, [params, pathname]);

  useEffect(() => {
    if (debugState.backendOutcome !== "success") {
      return;
    }

    const timeout = setTimeout(() => {
      pushGoogleOAuthDebugStep("callback_redirect_tabs", "Navigating to app after backend success.");
      router.replace("/(tabs)" as never);
    }, SUCCESS_REDIRECT_DELAY_MS);

    return () => clearTimeout(timeout);
  }, [debugState.backendOutcome]);

  const canReturnToLogin = debugState.backendOutcome === "failure" || debugState.currentStep !== "idle";
  const responseFieldSummary = [
    `params.id_token: ${debugState.hasParamsIdToken ? "yes" : "no"}`,
    `authentication.idToken: ${debugState.hasAuthenticationIdToken ? "yes" : "no"}`,
    `code: ${debugState.hasCode ? "yes" : "no"}`,
    `error: ${debugState.hasError ? "yes" : "no"}`
  ];

  return (
    <View style={styles.container}>
      <ActivityIndicator color={palette.primaryGlow} />
      <Text style={styles.text}>Completing Google sign-in...</Text>
      <Text style={styles.text}>Current step: {debugState.currentStep}</Text>
      <Text style={styles.text}>idToken present: {debugState.idTokenPresent ? "yes" : "no"}</Text>
      <Text style={styles.text}>idToken length: {debugState.idTokenLength}</Text>
      <Text style={styles.text}>idToken prefix: {debugState.idTokenPrefix || "-"}</Text>
      <Text style={styles.text}>Backend called: {debugState.backendCalled ? "yes" : "no"}</Text>
      <Text style={styles.text}>Backend outcome: {debugState.backendOutcome}</Text>
      {debugState.backendMessage ? <Text style={styles.errorText}>Backend message: {debugState.backendMessage}</Text> : null}
      <View style={styles.fieldsBlock}>
        <Text style={styles.sectionTitle}>Auth response fields</Text>
        {responseFieldSummary.map((line) => (
          <Text key={line} style={styles.text}>
            {line}
          </Text>
        ))}
      </View>
      <View style={styles.fieldsBlock}>
        <Text style={styles.sectionTitle}>Debug timeline</Text>
        {debugState.lines.map((line) => (
          <Text key={line} style={styles.timelineText}>
            {line}
          </Text>
        ))}
      </View>
      {canReturnToLogin ? (
        <Pressable
          style={({ pressed }) => [styles.backButton, pressed ? styles.backButtonPressed : null]}
          onPress={() =>
            router.replace({
              pathname: "/login",
              params: { googleError: debugState.backendMessage || "Google sign-in did not complete." }
            } as never)
          }
        >
          <Text style={styles.backButtonText}>Return to login</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    paddingHorizontal: 20,
    backgroundColor: palette.appBackground
  },
  text: {
    color: palette.textSecondary,
    ...typography.body2
  },
  errorText: {
    color: palette.negative,
    ...typography.caption,
    textAlign: "center"
  },
  fieldsBlock: {
    width: "100%",
    maxWidth: 360,
    marginTop: 4,
    padding: 10,
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 10,
    backgroundColor: "rgba(18,36,58,0.38)"
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "700",
    marginBottom: 4
  },
  timelineText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  backButton: {
    marginTop: 8,
    minHeight: 42,
    paddingHorizontal: 16,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(18,36,58,0.78)",
    alignItems: "center",
    justifyContent: "center"
  },
  backButtonPressed: {
    opacity: 0.85
  },
  backButtonText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  }
});
