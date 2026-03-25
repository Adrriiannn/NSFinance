import { router, useLocalSearchParams, usePathname } from "expo-router";
import { useEffect } from "react";
import { ActivityIndicator, Pressable, Text, View } from "react-native";
import { useGoogleOAuthDebugState, pushGoogleOAuthDebugStep, updateGoogleOAuthDebugState } from "../src/features/auth/googleOAuthDebug";
import { palette, surfaces, typography, createRuntimeStyleSheet } from "../src/theme/tokens";

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
      <View style={styles.card}>
        <Text style={styles.eyebrow}>NSFinance Google Sign-In</Text>
        <Text style={styles.title}>Completing sign-in</Text>
        <View style={styles.statusRow}>
          <ActivityIndicator color={palette.accent} size="small" />
          <Text style={styles.statusText}>Current step: {debugState.currentStep}</Text>
        </View>

        <View style={styles.fieldsBlock}>
          <Text style={styles.sectionTitle}>Callback status</Text>
          <Text style={styles.text}>idToken present: {debugState.idTokenPresent ? "yes" : "no"}</Text>
          <Text style={styles.text}>idToken length: {debugState.idTokenLength}</Text>
          <Text style={styles.text}>idToken prefix: {debugState.idTokenPrefix || "-"}</Text>
          <Text style={styles.text}>Backend called: {debugState.backendCalled ? "yes" : "no"}</Text>
          <Text style={styles.text}>Backend outcome: {debugState.backendOutcome}</Text>
          {debugState.backendMessage ? (
            <Text style={styles.errorText}>Backend message: {debugState.backendMessage}</Text>
          ) : null}
        </View>

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
    letterSpacing: 1.1,
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
    color: palette.textSecondary,
    ...typography.body2
  },
  text: {
    color: palette.textSecondary,
    ...typography.body2
  },
  errorText: {
    marginTop: 4,
    color: palette.negative,
    ...typography.caption
  },
  fieldsBlock: {
    width: "100%",
    padding: 10,
    borderWidth: 1,
    borderColor: palette.border,
    borderRadius: 6,
    backgroundColor: surfaces.field
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "500",
    marginBottom: 4
  },
  timelineText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  backButton: {
    marginTop: 2,
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

