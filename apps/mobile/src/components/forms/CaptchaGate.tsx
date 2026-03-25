import { Ionicons } from "@expo/vector-icons";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { WebView, type WebViewMessageEvent } from "react-native-webview";
import type { WebViewErrorEvent, WebViewHttpErrorEvent } from "react-native-webview/lib/WebViewTypes";
import { apiConfig } from "../../lib/api/config";
import { useThemeRuntime } from "../../theme/runtime/ThemeRuntimeProvider";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../theme/tokens";

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

type ChallengeState = "loading" | "ready" | "expired" | "error";

const TURNSTILE_PAGE_BASE_URL =
  process.env.EXPO_PUBLIC_TURNSTILE_PAGE_BASE_URL?.trim() ?? "https://api.finance.nsireland.ie";
const TURNSTILE_REGISTER_PATH = "/turnstile/register";
const TURNSTILE_WIDGET_WIDTH = 300;
const TURNSTILE_WIDGET_HEIGHT = 65;
const TURNSTILE_SEAM_MASK = 1;
const TURNSTILE_BOTTOM_MASK = 2;

function isTokenCaptchaProps(props: CaptchaGateProps): props is TokenCaptchaProps {
  return "token" in props && "onTokenChange" in props;
}

function buildTurnstileRegisterUrl(baseUrl: string, theme: "light" | "dark"): string | null {
  if (!baseUrl) {
    return null;
  }

  try {
    const normalizedBaseUrl = baseUrl.replace(/\/+$/, "");
    const url = new URL(`${normalizedBaseUrl}${TURNSTILE_REGISTER_PATH}`);
    url.searchParams.set("action", "register");
    url.searchParams.set("theme", theme);
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
  const { resolvedThemeName } = useThemeRuntime();
  const [isChallengeReady, setIsChallengeReady] = useState(false);
  const [challengeSeed, setChallengeSeed] = useState(0);
  const [challengeState, setChallengeState] = useState<ChallengeState>("loading");
  const [lastError, setLastError] = useState<string | null>(null);
  const widgetBackground = resolvedThemeName === "light" ? "#FFFFFF" : "#2f3136";
  const pendingOverlayBackground =
    resolvedThemeName === "light" ? "rgba(255,255,255,0.48)" : "rgba(11,26,45,0.22)";

  const challengeUrl = useMemo(
    () => buildTurnstileRegisterUrl(TURNSTILE_PAGE_BASE_URL, resolvedThemeName),
    [resolvedThemeName]
  );

  useEffect(() => {
    if (!challengeUrl) {
      setChallengeState("error");
      setIsChallengeReady(true);
      setLastError("Turnstile URL could not be built from Turnstile host configuration.");
      logTurnstileDebug("challenge_url_invalid", {
        turnstileBaseUrl: TURNSTILE_PAGE_BASE_URL,
        apiBaseUrl: apiConfig.baseUrl
      });
      return;
    }

    setChallengeState("loading");
    setIsChallengeReady(false);
    setLastError(null);
    logTurnstileDebug("challenge_load", { challengeUrl, seed: challengeSeed, theme: resolvedThemeName });
  }, [challengeSeed, challengeUrl]);

  const retryChallenge = useCallback(() => {
    onTokenChange(null);
    setChallengeSeed((current) => current + 1);
    logTurnstileDebug("challenge_retry");
  }, [onTokenChange]);

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
        setLastError(null);
        return;
      }

      if (message.type === "turnstile.success") {
        const nextToken = message.token?.trim() ?? "";

        if (!nextToken) {
          onTokenChange(null);
          setChallengeState("error");
          setLastError("Turnstile returned an empty token.");
          setIsChallengeReady(true);
          return;
        }

        onTokenChange(nextToken);
        setChallengeState("ready");
        setLastError(null);
        return;
      }

      if (message.type === "turnstile.expired") {
        onTokenChange(null);
        setChallengeState("expired");
        setLastError("Security check expired. Please verify again.");
        setIsChallengeReady(true);
        return;
      }

      onTokenChange(null);
      setChallengeState("error");
      setLastError(message.code ? `Turnstile error code: ${message.code}` : message.message ?? "Unknown Turnstile error.");
      setIsChallengeReady(true);
    },
    [onTokenChange]
  );

  const handleWebViewError = useCallback(
    (event: WebViewErrorEvent) => {
      onTokenChange(null);
      setChallengeState("error");
      setIsChallengeReady(true);
      setLastError(event.nativeEvent.description || "WebView loading error.");
      logTurnstileDebug("webview_error", event.nativeEvent);
    },
    [onTokenChange]
  );

  const handleWebViewHttpError = useCallback(
    (event: WebViewHttpErrorEvent) => {
      onTokenChange(null);
      setChallengeState("error");
      setIsChallengeReady(true);
      setLastError(`HTTP ${event.nativeEvent.statusCode}`);
      logTurnstileDebug("webview_http_error", event.nativeEvent);
    },
    [onTokenChange]
  );

  const showRetry = challengeState === "error" || challengeState === "expired";
  const showLoadingOverlay = !isChallengeReady && challengeState === "loading";

  return (
    <View style={styles.wrap}>
      {showLabel ? <Text style={styles.label}>Security check</Text> : null}

      <View style={styles.inlineWidgetShell}>
        <View style={[styles.inlineWidgetClip, { backgroundColor: widgetBackground }]}>
          {challengeUrl ? (
            <WebView
              key={`turnstile-inline-${challengeSeed}`}
              source={{ uri: challengeUrl }}
              style={[styles.inlineWebView, { backgroundColor: widgetBackground }]}
              containerStyle={[styles.inlineWebViewContainer, { backgroundColor: widgetBackground }]}
              originWhitelist={["https://*", "http://*", "about:blank", "about:srcdoc"]}
              javaScriptEnabled
              domStorageEnabled
              androidLayerType="software"
              setSupportMultipleWindows={false}
              scrollEnabled={false}
              bounces={false}
              cacheEnabled={false}
              showsHorizontalScrollIndicator={false}
              showsVerticalScrollIndicator={false}
              overScrollMode="never"
              scalesPageToFit={false}
              contentInset={{ top: 0, left: 0, bottom: 0, right: 0 }}
              automaticallyAdjustContentInsets={false}
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
            />
          ) : (
            <View style={styles.inlineFallback}>
              <Text style={styles.inlineFallbackText}>Security challenge URL is unavailable.</Text>
            </View>
          )}

          <View pointerEvents="none" style={[styles.seamMaskLeft, { backgroundColor: widgetBackground }]} />
          <View pointerEvents="none" style={[styles.seamMaskRight, { backgroundColor: widgetBackground }]} />
          <View pointerEvents="none" style={[styles.seamMaskBottom, { backgroundColor: widgetBackground }]} />

          {showLoadingOverlay ? (
            <View pointerEvents="none" style={[styles.webViewPendingOverlay, { backgroundColor: pendingOverlayBackground }]}>
              <ActivityIndicator color={palette.primaryGlow} />
            </View>
          ) : null}
        </View>
      </View>

      {lastError ? <Text style={styles.errorText}>{lastError}</Text> : null}

      {showRetry ? (
        <Pressable style={({ pressed }) => [styles.retryAction, pressed ? styles.pressed : null]} onPress={retryChallenge}>
          <Text style={styles.retryActionText}>Retry challenge</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
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
    backgroundColor: "rgba(21,21,21,0.74)",
    borderRadius: 6,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[12]
  },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 6,
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
  inlineWidgetShell: {
    alignSelf: "center",
    width: TURNSTILE_WIDGET_WIDTH + 4,
    height: TURNSTILE_WIDGET_HEIGHT + 4,
    borderRadius: 6,
    overflow: "hidden",
    backgroundColor: "transparent",
    alignItems: "center",
    justifyContent: "center"
  },
  inlineWidgetClip: {
    width: TURNSTILE_WIDGET_WIDTH,
    height: TURNSTILE_WIDGET_HEIGHT,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    overflow: "hidden",
    backgroundColor: "#2f3136"
  },
  inlineWebView: {
    flex: 0,
    width: TURNSTILE_WIDGET_WIDTH,
    height: TURNSTILE_WIDGET_HEIGHT + 1,
    marginTop: -1,
    backgroundColor: "#2f3136"
  },
  inlineWebViewContainer: {
    flex: 0,
    width: TURNSTILE_WIDGET_WIDTH,
    height: TURNSTILE_WIDGET_HEIGHT,
    backgroundColor: "#2f3136"
  },
  inlineFallback: {
    width: TURNSTILE_WIDGET_WIDTH,
    height: TURNSTILE_WIDGET_HEIGHT,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  inlineFallbackText: {
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
  seamMaskLeft: {
    position: "absolute",
    left: 0,
    top: 0,
    bottom: 0,
    width: TURNSTILE_SEAM_MASK,
    backgroundColor: "#2f3136"
  },
  seamMaskRight: {
    position: "absolute",
    right: 0,
    top: 0,
    bottom: 0,
    width: TURNSTILE_SEAM_MASK,
    backgroundColor: "#2f3136"
  },
  seamMaskBottom: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 0,
    height: TURNSTILE_BOTTOM_MASK,
    backgroundColor: "#2f3136",
    zIndex: 8
  },
  errorText: {
    color: palette.negative,
    ...typography.caption,
    alignSelf: "center",
    width: "88%",
    maxWidth: 360
  },
  retryAction: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360,
    minHeight: 42,
    borderRadius: 6,
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
  pressed: {
    opacity: 0.9
  }
}));

