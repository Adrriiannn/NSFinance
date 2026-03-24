import { Ionicons } from "@expo/vector-icons";
import { useCallback, useMemo, useState } from "react";
import { ActivityIndicator, Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { WebView, type WebViewMessageEvent } from "react-native-webview";
import type { WebViewErrorEvent, WebViewHttpErrorEvent } from "react-native-webview/lib/WebViewTypes";
import { apiConfig } from "../../lib/api/config";
import { palette, spacing, typography } from "../../theme/tokens";

type TokenCaptchaProps = {
  token: string | null;
  onTokenChange: (token: string | null) => void;
  showLabel?: boolean;
};

type LegacyCaptchaProps = {
  isVerified: boolean;
  onVerify: () => void;
  showLabel?: boolean;
};

type CaptchaGateProps = TokenCaptchaProps | LegacyCaptchaProps;

type TurnstileMessage =
  | { type: "turnstile.ready" }
  | { type: "turnstile.success"; token?: string }
  | { type: "turnstile.expired" }
  | { type: "turnstile.error"; code?: string; message?: string };

type ChallengeState = "idle" | "loading" | "ready" | "expired" | "error";

const TURNSTILE_SITE_KEY = process.env.EXPO_PUBLIC_TURNSTILE_SITE_KEY?.trim() ?? "";
const TURNSTILE_REGISTER_PATH = "/turnstile/register";

function isTokenCaptchaProps(props: CaptchaGateProps): props is TokenCaptchaProps {
  return "token" in props && "onTokenChange" in props;
}

function buildTurnstileRegisterUrl(baseUrl: string, siteKey: string): string | null {
  if (!baseUrl || !siteKey) {
    return null;
  }

  try {
    const normalizedBaseUrl = baseUrl.replace(/\/+$/, "");
    const url = new URL(`${normalizedBaseUrl}${TURNSTILE_REGISTER_PATH}`);
    url.searchParams.set("siteKey", siteKey);
    url.searchParams.set("action", "register");
    url.searchParams.set("theme", "dark");
    return url.toString();
  } catch {
    return null;
  }
}

function logTurnstileDebug(event: string, details?: unknown) {
  if (!__DEV__) {
    return;
  }

  if (details === undefined) {
    console.info(`[Turnstile][register] ${event}`);
    return;
  }

  console.info(`[Turnstile][register] ${event}`, details);
}

function LegacyCaptchaGate({ isVerified, onVerify, showLabel = true }: LegacyCaptchaProps) {
  return (
    <View style={styles.wrap}>
      {showLabel ? <Text style={styles.label}>Security check</Text> : null}
      <Pressable style={({ pressed }) => [styles.card, pressed ? styles.pressed : null]} onPress={onVerify}>
        <View style={[styles.checkbox, isVerified ? styles.checkboxVerified : null]}>
          {isVerified ? <Ionicons name="checkmark" size={14} color={palette.appBackground} /> : null}
        </View>
        <View style={styles.body}>
          <Text style={styles.title}>Verify you are human</Text>
          <Text style={styles.meta}>Complete the security check to continue.</Text>
        </View>
      </Pressable>
    </View>
  );
}

export function CaptchaGate(props: CaptchaGateProps) {
  return isTokenCaptchaProps(props) ? <TokenCaptchaGate {...props} /> : <LegacyCaptchaGate {...props} />;
}

function TokenCaptchaGate({ token, onTokenChange, showLabel = true }: TokenCaptchaProps) {
  const [isChallengeVisible, setIsChallengeVisible] = useState(false);
  const [isChallengeReady, setIsChallengeReady] = useState(false);
  const [challengeSeed, setChallengeSeed] = useState(0);
  const [challengeState, setChallengeState] = useState<ChallengeState>("idle");
  const [statusText, setStatusText] = useState<string | null>(null);
  const [lastError, setLastError] = useState<string | null>(null);

  const isVerified = Boolean(token?.trim());
  const challengeUrl = useMemo(
    () => buildTurnstileRegisterUrl(apiConfig.baseUrl, TURNSTILE_SITE_KEY),
    []
  );

  const openChallenge = useCallback(() => {
    if (!TURNSTILE_SITE_KEY) {
      onTokenChange(null);
      setChallengeState("error");
      setLastError("Turnstile site key is missing.");
      setStatusText("Security check is unavailable. Configure EXPO_PUBLIC_TURNSTILE_SITE_KEY.");
      logTurnstileDebug("site_key_missing");
      return;
    }

    if (!challengeUrl) {
      onTokenChange(null);
      setChallengeState("error");
      setLastError("Turnstile URL could not be built from API configuration.");
      setStatusText("Security check is unavailable. Verify API base URL configuration.");
      logTurnstileDebug("challenge_url_invalid", { baseUrl: apiConfig.baseUrl });
      return;
    }

    onTokenChange(null);
    setChallengeSeed((current) => current + 1);
    setIsChallengeReady(false);
    setChallengeState("loading");
    setStatusText("Loading security challenge...");
    setLastError(null);
    setIsChallengeVisible(true);
    logTurnstileDebug("challenge_open", { challengeUrl });
  }, [challengeUrl, onTokenChange]);

  const closeChallenge = useCallback(
    (reason: "cancel" | "success") => {
      setIsChallengeVisible(false);
      setIsChallengeReady(false);

      if (reason === "cancel") {
        onTokenChange(null);
        setStatusText("Security check cancelled.");
        setChallengeState("idle");
        logTurnstileDebug("challenge_closed_cancel");
      }

      if (reason === "success") {
        logTurnstileDebug("challenge_closed_success");
      }
    },
    [onTokenChange]
  );

  const retryChallenge = useCallback(() => {
    setChallengeSeed((current) => current + 1);
    setIsChallengeReady(false);
    setChallengeState("loading");
    setStatusText("Reloading security challenge...");
    setLastError(null);
    logTurnstileDebug("challenge_retry");
  }, []);

  const onTurnstileMessage = useCallback(
    (event: WebViewMessageEvent) => {
      let message: TurnstileMessage | null = null;

      try {
        message = JSON.parse(event.nativeEvent.data) as TurnstileMessage;
      } catch {
        logTurnstileDebug("message_parse_failed", event.nativeEvent.data);
      }

      if (!message) {
        return;
      }

      logTurnstileDebug("message_received", message);

      if (message.type === "turnstile.ready") {
        setIsChallengeReady(true);
        setChallengeState("ready");
        setStatusText("Complete the challenge to continue.");
        setLastError(null);
        return;
      }

      if (message.type === "turnstile.success") {
        const nextToken = message.token?.trim() ?? "";

        if (!nextToken) {
          onTokenChange(null);
          setChallengeState("error");
          setStatusText("Security check returned an empty token. Please retry.");
          setLastError("Turnstile returned an empty token.");
          setIsChallengeReady(true);
          return;
        }

        onTokenChange(nextToken);
        setChallengeState("idle");
        setStatusText("Security check completed.");
        setLastError(null);
        closeChallenge("success");
        return;
      }

      if (message.type === "turnstile.expired") {
        onTokenChange(null);
        setChallengeState("expired");
        setStatusText("Security check expired. Retry to continue.");
        setLastError(null);
        setIsChallengeReady(true);
        return;
      }

      onTokenChange(null);
      setChallengeState("error");
      setStatusText("Security challenge failed. Retry to continue.");
      setLastError(message.code ? `Turnstile error code: ${message.code}` : message.message ?? "Unknown Turnstile error.");
      setIsChallengeReady(true);
    },
    [closeChallenge, onTokenChange]
  );

  const handleWebViewError = useCallback((event: WebViewErrorEvent) => {
    setChallengeState("error");
    setIsChallengeReady(true);
    setStatusText("Security challenge failed to load. Retry to continue.");
    setLastError(event.nativeEvent.description || "WebView loading error.");
    logTurnstileDebug("webview_error", event.nativeEvent);
  }, []);

  const handleWebViewHttpError = useCallback((event: WebViewHttpErrorEvent) => {
    setChallengeState("error");
    setIsChallengeReady(true);
    setStatusText("Security challenge endpoint returned an error. Retry to continue.");
    setLastError(`HTTP ${event.nativeEvent.statusCode}`);
    logTurnstileDebug("webview_http_error", event.nativeEvent);
  }, []);

  const helperText = isVerified
    ? "Security check completed."
    : statusText ?? "Complete the security check to continue.";

  const showRetry = challengeState === "error" || challengeState === "expired";

  return (
    <View style={styles.wrap}>
      {showLabel ? <Text style={styles.label}>Security check</Text> : null}

      <Pressable
        style={({ pressed }) => [styles.card, pressed ? styles.pressed : null]}
        onPress={openChallenge}
      >
        <View style={[styles.checkbox, isVerified ? styles.checkboxVerified : null]}>
          {isVerified ? <Ionicons name="checkmark" size={14} color={palette.appBackground} /> : null}
        </View>
        <View style={styles.body}>
          <Text style={styles.title}>{isVerified ? "Verification complete" : "Verify you are human"}</Text>
          <Text style={styles.meta}>{helperText}</Text>
        </View>
      </Pressable>

      <Modal
        visible={isChallengeVisible}
        transparent
        animationType="fade"
        onRequestClose={() => closeChallenge("cancel")}
      >
        <View style={styles.modalOverlay}>
          <View style={styles.modalCard}>
            <View style={styles.modalHeader}>
              <Text style={styles.modalTitle}>Security verification</Text>
              <Pressable
                onPress={() => closeChallenge("cancel")}
                style={({ pressed }) => [styles.closeButton, pressed ? styles.pressed : null]}
              >
                <Ionicons name="close" size={18} color={palette.textPrimary} />
              </Pressable>
            </View>

            <Text style={styles.modalSubtitle}>Complete the Turnstile challenge to continue registration.</Text>

            <View style={styles.webViewShell}>
              {challengeUrl ? (
                <WebView
                  key={`turnstile-${challengeSeed}`}
                  source={{ uri: challengeUrl }}
                  originWhitelist={["https://*", "http://*", "about:blank", "about:srcdoc"]}
                  javaScriptEnabled
                  domStorageEnabled
                  setSupportMultipleWindows={false}
                  startInLoadingState
                  onMessage={onTurnstileMessage}
                  onError={handleWebViewError}
                  onHttpError={handleWebViewHttpError}
                  onShouldStartLoadWithRequest={(request) => {
                    const nextUrl = (request.url || "").toLowerCase();
                    const isAllowed =
                      nextUrl.startsWith("https://") ||
                      nextUrl.startsWith("http://") ||
                      nextUrl.startsWith("about:blank") ||
                      nextUrl.startsWith("about:srcdoc");

                    if (!isAllowed) {
                      logTurnstileDebug("navigation_blocked", request.url);
                    }

                    return isAllowed;
                  }}
                  renderLoading={() => (
                    <View style={styles.webViewLoading}>
                      <ActivityIndicator color={palette.primaryGlow} />
                      <Text style={styles.webViewLoadingText}>Loading security challenge...</Text>
                    </View>
                  )}
                />
              ) : (
                <View style={styles.webViewLoading}>
                  <Ionicons name="warning-outline" size={18} color={palette.negative} />
                  <Text style={styles.webViewLoadingText}>Security challenge URL is unavailable.</Text>
                </View>
              )}

              {!isChallengeReady && challengeState === "loading" ? (
                <View pointerEvents="none" style={styles.webViewPendingOverlay}>
                  <ActivityIndicator color={palette.primaryGlow} />
                </View>
              ) : null}
            </View>

            {lastError ? <Text style={styles.errorText}>{lastError}</Text> : null}

            <View style={styles.modalActionRow}>
              {showRetry ? (
                <Pressable
                  style={({ pressed }) => [styles.retryAction, pressed ? styles.pressed : null]}
                  onPress={retryChallenge}
                >
                  <Text style={styles.retryActionText}>Retry challenge</Text>
                </Pressable>
              ) : null}

              <Pressable
                style={({ pressed }) => [styles.secondaryAction, pressed ? styles.pressed : null]}
                onPress={() => closeChallenge("cancel")}
              >
                <Text style={styles.secondaryActionText}>Cancel</Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing[8]
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption
  },
  card: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360,
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    borderRadius: 14,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[12]
  },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(255,255,255,0.04)",
    alignItems: "center",
    justifyContent: "center"
  },
  checkboxVerified: {
    borderColor: palette.success,
    backgroundColor: palette.success
  },
  body: {
    flex: 1,
    gap: spacing[4]
  },
  title: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  meta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.72)",
    justifyContent: "center",
    paddingHorizontal: spacing[16]
  },
  modalCard: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[14],
    gap: spacing[10]
  },
  modalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  modalTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  closeButton: {
    width: 30,
    height: 30,
    borderRadius: 15,
    alignItems: "center",
    justifyContent: "center",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)"
  },
  modalSubtitle: {
    color: palette.textSecondary,
    ...typography.caption
  },
  webViewShell: {
    minHeight: 220,
    borderRadius: 12,
    overflow: "hidden",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "#0b1a2d"
  },
  webViewLoading: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8],
    backgroundColor: "#0b1a2d",
    paddingHorizontal: spacing[12]
  },
  webViewLoadingText: {
    color: palette.textSecondary,
    ...typography.caption,
    textAlign: "center"
  },
  webViewPendingOverlay: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(11,26,45,0.22)"
  },
  errorText: {
    color: palette.negative,
    ...typography.caption
  },
  modalActionRow: {
    flexDirection: "row",
    gap: spacing[8],
    justifyContent: "flex-end"
  },
  retryAction: {
    minHeight: 42,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.caution,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(129,93,34,0.26)",
    paddingHorizontal: spacing[12]
  },
  retryActionText: {
    color: palette.caution,
    ...typography.body2,
    fontWeight: "600"
  },
  secondaryAction: {
    minHeight: 42,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "rgba(18,36,58,0.74)",
    paddingHorizontal: spacing[12]
  },
  secondaryActionText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  pressed: {
    opacity: 0.9
  }
});
