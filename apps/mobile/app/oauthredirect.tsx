import { router, useLocalSearchParams, usePathname } from "expo-router";
import { useEffect } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { palette, typography } from "../src/theme/tokens";

const CALLBACK_FALLBACK_DELAY_MS = 1200;

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
    const fallbackGoogleError =
      errorDescription ??
      (errorCode ? `Google sign-in failed: ${errorCode}.` : "Google sign-in did not complete. Please try again.");

    const timeout = setTimeout(() => {
      logOAuthRedirectDebug("fallback_redirect_login");
      router.replace({
        pathname: "/login",
        params: { googleError: fallbackGoogleError }
      } as never);
    }, CALLBACK_FALLBACK_DELAY_MS);

    return () => {
      clearTimeout(timeout);
    };
  }, [params, pathname]);

  return (
    <View style={styles.container}>
      <ActivityIndicator color={palette.primaryGlow} />
      <Text style={styles.text}>Completing Google sign-in...</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 12,
    backgroundColor: palette.appBackground
  },
  text: {
    color: palette.textSecondary,
    ...typography.body2
  }
});
