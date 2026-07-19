import { Ionicons } from "@expo/vector-icons";
import * as SecureStore from "expo-secure-store";
import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Animated, Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Path } from "react-native-svg";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/fields/PasswordField";
import { Button } from "../../src/components/ui/buttons/Button";
import { Banner } from "../../src/components/ui/feedback/Banner";
import { TextField } from "../../src/components/ui/fields/TextField";
import { useLoginMutation } from "../../src/features/auth/useAuthMutations";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { useMicrosoftSignIn } from "../../src/features/auth/useMicrosoftSignIn";
import { stageEmailVerification, stageMfaLogin } from "../../src/features/auth/pendingAuthFlow";
import { readMfaTrustedDeviceCredential } from "../../src/features/auth/mfaTrustedDevice";
import { ApiClientError, formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import type { AuthFlowResponse } from "../../src/types/api";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"email" | "password", string>>;
type FocusField = "email" | "password" | null;
type ErrorFieldTarget = "none" | "password" | "both";

const LOGIN_ATTEMPTS_BEFORE_CAPTCHA = 3;
const LOGIN_ERROR_BANNER_DURATION_MS = 5000;
const LOGIN_ERROR_SHAKE_DURATION_MS = 60;
const LOCKOUT_READY_NOTICE_DURATION_MS = 5000;
const LOGIN_LOCKOUT_UNTIL_KEY = "nsfinance.auth.login.lockout_until_utc_ms";
const INSET_OUTLINE_RADIUS = 6;
const INSET_OUTLINE_WIDTH = 1;
const INSET_LABEL_LEFT = 20;
const INSET_NOTCH_OFFSET_X = -3;
const INSET_LABEL_NOTCH_PADDING = 5;
const INSET_LABEL_NOTCH_SAFETY_BUFFER = 0;
const INSET_LABEL_TOP = -8;
const INSET_LABEL_CHAR_WIDTH_ESTIMATE = 7.6;

type LoginErrorBannerState =
  | { kind: "temporary_error"; id: number; title: string; message: string; highlightTarget: ErrorFieldTarget }
  | { kind: "lockout_countdown"; id: number; unlockAtMs: number }
  | { kind: "lockout_ready"; id: number };

function tryParseLockoutRetryAfterMs(error: ApiClientError): number | null {
  if (error.code !== "auth_locked" && error.status !== 429) {
    return null;
  }

  const match = error.message.match(/try again after\s+([0-9T:+.\-Z]+)/i);
  if (!match?.[1]) {
    return null;
  }

  const parsed = Date.parse(match[1].trim().replace(/\.$/, ""));
  if (Number.isNaN(parsed)) {
    return null;
  }

  return parsed;
}

function formatMmSs(totalSeconds: number): string {
  const normalized = Math.max(0, totalSeconds);
  const minutes = Math.floor(normalized / 60);
  const seconds = normalized % 60;
  return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

async function readPersistedLockoutUntilMs(): Promise<number | null> {
  try {
    const raw = await SecureStore.getItemAsync(LOGIN_LOCKOUT_UNTIL_KEY);
    if (!raw) {
      return null;
    }

    const parsed = Number.parseInt(raw, 10);
    if (!Number.isFinite(parsed)) {
      return null;
    }

    return parsed;
  } catch {
    return null;
  }
}

async function persistLockoutUntilMs(unlockAtMs: number) {
  try {
    await SecureStore.setItemAsync(LOGIN_LOCKOUT_UNTIL_KEY, String(unlockAtMs));
  } catch {
    // Keep auth UX running even if local persistence fails.
  }
}

async function clearPersistedLockoutUntil() {
  try {
    await SecureStore.deleteItemAsync(LOGIN_LOCKOUT_UNTIL_KEY);
  } catch {
    // Keep auth UX running even if local persistence fails.
  }
}

function runShake(animationValue: Animated.Value) {
  Animated.sequence([
    Animated.timing(animationValue, {
      toValue: -10,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: 10,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: -8,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: 8,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: -4,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: 4,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    }),
    Animated.timing(animationValue, {
      toValue: 0,
      duration: LOGIN_ERROR_SHAKE_DURATION_MS,
      useNativeDriver: true
    })
  ]).start();
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function toOpaqueColor(color: string): string {
  const rgbaMatch = color.match(
    /^rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(0|0?\.\d+|1(?:\.0+)?)\s*\)$/i
  );

  if (!rgbaMatch) {
    return color;
  }

  const [, red, green, blue] = rgbaMatch;
  return `rgb(${red}, ${green}, ${blue})`;
}

function removeWhitespace(value: string): string {
  return value.replace(/\s+/g, "");
}

type InsetFieldShellProps = {
  label: string;
  color: string;
  children: ReactNode;
};

function InsetFieldShell({ label, color, children }: InsetFieldShellProps) {
  const [shellWidth, setShellWidth] = useState(0);
  const [shellHeight, setShellHeight] = useState(0);
  const estimatedLabelWidth = useMemo(
    () => Math.ceil(Math.max(label.trim().length, 1) * INSET_LABEL_CHAR_WIDTH_ESTIMATE),
    [label]
  );
  const [labelWidth, setLabelWidth] = useState(estimatedLabelWidth);
  const resolvedLabelWidth = Math.max(
    labelWidth + INSET_LABEL_NOTCH_SAFETY_BUFFER,
    estimatedLabelWidth + INSET_LABEL_NOTCH_SAFETY_BUFFER,
    24
  );

  const outlinePath = useMemo(() => {
    if (shellWidth <= 0 || shellHeight <= 0) {
      return "";
    }

    const stroke = INSET_OUTLINE_WIDTH;
    const x0 = stroke / 2;
    const y0 = stroke / 2;
    const x1 = shellWidth - stroke / 2;
    const y1 = shellHeight - stroke / 2;
    const radius = clamp(
      INSET_OUTLINE_RADIUS,
      0,
      Math.min((x1 - x0) / 2, (y1 - y0) / 2)
    );

    const minGapStart = x0 + radius + 2;
    const maxGapEnd = x1 - radius - 2;
    const notchLabelLeft = INSET_LABEL_LEFT + INSET_NOTCH_OFFSET_X;
    const preferredGapStart = notchLabelLeft - INSET_LABEL_NOTCH_PADDING;
    const preferredGapEnd =
      notchLabelLeft + resolvedLabelWidth + INSET_LABEL_NOTCH_PADDING;

    const notchStart = clamp(preferredGapStart, minGapStart, maxGapEnd - 10);
    const notchEnd = clamp(preferredGapEnd, notchStart + 10, maxGapEnd);

    return [
      `M ${notchEnd} ${y0}`,
      `H ${x1 - radius}`,
      `A ${radius} ${radius} 0 0 1 ${x1} ${y0 + radius}`,
      `V ${y1 - radius}`,
      `A ${radius} ${radius} 0 0 1 ${x1 - radius} ${y1}`,
      `H ${x0 + radius}`,
      `A ${radius} ${radius} 0 0 1 ${x0} ${y1 - radius}`,
      `V ${y0 + radius}`,
      `A ${radius} ${radius} 0 0 1 ${x0 + radius} ${y0}`,
      `H ${notchStart}`
    ].join(" ");
  }, [resolvedLabelWidth, shellHeight, shellWidth]);

  return (
    <View
      style={styles.insetFieldWrap}
      onLayout={(event) => {
        const { width, height } = event.nativeEvent.layout;
        setShellWidth(width);
        setShellHeight(height);
      }}
    >
      {children}
      {outlinePath ? (
        <Svg pointerEvents="none" style={styles.insetOutlineSvg}>
          <Path d={outlinePath} stroke={color} strokeWidth={INSET_OUTLINE_WIDTH} fill="none" />
        </Svg>
      ) : null}
      <View
        pointerEvents="none"
        style={[styles.insetFieldLabelChip, { minWidth: resolvedLabelWidth }]}
      >
        <Text
          onLayout={(event) => {
            const nextWidth = Math.ceil(event.nativeEvent.layout.width);
            setLabelWidth((current) => Math.max(current, nextWidth));
          }}
          style={[styles.insetFieldLabelText, { color: toOpaqueColor(color) }]}
        >
          {label}
        </Text>
      </View>
    </View>
  );
}

export default function LoginScreen() {
  const searchParams = useLocalSearchParams<{
    googleError?: string | string[];
    mfaExpired?: string | string[];
    mfaUnavailable?: string | string[];
  }>();
  const loginMutation = useLoginMutation();
  const googleSignIn = useGoogleSignIn();
  const microsoftSignIn = useMicrosoftSignIn();
  const {
    applyAuthTokenResponse,
    isAuthTransitioning,
    sessionMessage,
    clearSessionMessage
  } = useAuthSession();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [captchaToken, setCaptchaToken] = useState<string | null>(null);
  const [failedLoginAttempts, setFailedLoginAttempts] = useState(0);
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [rememberMe, setRememberMe] = useState(false);
  const [googleError, setGoogleError] = useState<string | null>(null);
  const [loginErrorBanner, setLoginErrorBanner] = useState<LoginErrorBannerState | null>(null);
  const [countdownNowMs, setCountdownNowMs] = useState(Date.now());

  const shouldShowCaptcha = failedLoginAttempts >= LOGIN_ATTEMPTS_BEFORE_CAPTCHA;
  const emailShakeX = useRef(new Animated.Value(0)).current;
  const passwordShakeX = useRef(new Animated.Value(0)).current;
  const loginBannerOpacity = useRef(new Animated.Value(1)).current;
  const rawMfaExpired = searchParams.mfaExpired;
  const mfaExpired = Array.isArray(rawMfaExpired) ? rawMfaExpired[0] : rawMfaExpired;
  const isMfaExpired = mfaExpired === "1";
  const rawMfaUnavailable = searchParams.mfaUnavailable;
  const mfaUnavailable = Array.isArray(rawMfaUnavailable)
    ? rawMfaUnavailable[0]
    : rawMfaUnavailable;
  const isMfaUnavailable = mfaUnavailable === "1";

  useEffect(() => {
    const rawGoogleError = searchParams.googleError;
    const nextGoogleError = Array.isArray(rawGoogleError) ? rawGoogleError[0] : rawGoogleError;
    const normalizedGoogleError = nextGoogleError?.trim();
    if (!normalizedGoogleError) {
      return;
    }

    setGoogleError(normalizedGoogleError);
  }, [searchParams.googleError]);

  useEffect(() => {
    let cancelled = false;

    const hydrateLockout = async () => {
      const persistedUnlockAt = await readPersistedLockoutUntilMs();
      if (cancelled || !persistedUnlockAt) {
        return;
      }

      if (persistedUnlockAt <= Date.now()) {
        await clearPersistedLockoutUntil();
        return;
      }

      setLoginErrorBanner({
        kind: "lockout_countdown",
        id: Date.now(),
        unlockAtMs: persistedUnlockAt
      });
    };

    void hydrateLockout();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!shouldShowCaptcha && captchaToken) {
      setCaptchaToken(null);
    }
  }, [captchaToken, shouldShowCaptcha]);

  useEffect(() => {
    loginBannerOpacity.setValue(1);
  }, [loginBannerOpacity, loginErrorBanner?.id, loginErrorBanner?.kind]);

  useEffect(() => {
    if (loginErrorBanner?.kind !== "temporary_error") {
      return;
    }

    const timeout = setTimeout(() => {
      setLoginErrorBanner(null);
    }, LOGIN_ERROR_BANNER_DURATION_MS);

    return () => clearTimeout(timeout);
  }, [loginErrorBanner]);

  useEffect(() => {
    if (loginErrorBanner?.kind !== "lockout_countdown") {
      return;
    }

    setCountdownNowMs(Date.now());
    const interval = setInterval(() => {
      setCountdownNowMs(Date.now());
    }, 1000);

    return () => clearInterval(interval);
  }, [loginErrorBanner]);

  useEffect(() => {
    if (loginErrorBanner?.kind !== "lockout_countdown") {
      return;
    }

    if (countdownNowMs < loginErrorBanner.unlockAtMs) {
      return;
    }

    void clearPersistedLockoutUntil();
    setLoginErrorBanner({ kind: "lockout_ready", id: Date.now() });
  }, [countdownNowMs, loginErrorBanner]);

  useEffect(() => {
    if (loginErrorBanner?.kind !== "lockout_ready") {
      return;
    }

    const hideTimeout = setTimeout(() => {
      Animated.timing(loginBannerOpacity, {
        toValue: 0,
        duration: 320,
        useNativeDriver: true
      }).start(() => {
        setLoginErrorBanner((current) => (current?.kind === "lockout_ready" ? null : current));
      });
    }, LOCKOUT_READY_NOTICE_DURATION_MS);

    return () => {
      clearTimeout(hideTimeout);
      loginBannerOpacity.stopAnimation();
    };
  }, [loginBannerOpacity, loginErrorBanner]);

  useEffect(() => {
    if (loginErrorBanner?.kind !== "temporary_error") {
      emailShakeX.setValue(0);
      passwordShakeX.setValue(0);
      return;
    }

    if (loginErrorBanner.highlightTarget === "none") {
      return;
    }

    if (loginErrorBanner.highlightTarget === "both") {
      emailShakeX.setValue(0);
      passwordShakeX.setValue(0);
      runShake(emailShakeX);
      runShake(passwordShakeX);
      return;
    }

    passwordShakeX.setValue(0);
    runShake(passwordShakeX);
  }, [emailShakeX, loginErrorBanner, passwordShakeX]);

  const showEmailFieldError = loginErrorBanner?.kind === "temporary_error" && loginErrorBanner.highlightTarget === "both";
  const showPasswordFieldError =
    loginErrorBanner?.kind === "temporary_error" &&
    (loginErrorBanner.highlightTarget === "password" || loginErrorBanner.highlightTarget === "both");
  const emailBorderColor =
    errors.email || showEmailFieldError ? palette.negative : focusedField === "email" ? palette.primaryGlow : palette.borderStrong;
  const passwordBorderColor =
    errors.password || showPasswordFieldError
      ? palette.negative
      : focusedField === "password"
        ? palette.primaryGlow
        : palette.borderStrong;

  const lockoutRemainingSeconds =
    loginErrorBanner?.kind === "lockout_countdown"
      ? Math.max(0, Math.ceil((loginErrorBanner.unlockAtMs - countdownNowMs) / 1000))
      : 0;

  const isLockoutActive = loginErrorBanner?.kind === "lockout_countdown" && lockoutRemainingSeconds > 0;

  const bannerCopy =
    loginErrorBanner?.kind === "lockout_countdown"
      ? {
          title: "Sign-in blocked. Too many failed attempts.",
          message: `Try again in ${formatMmSs(lockoutRemainingSeconds)} minutes.`
        }
      : loginErrorBanner?.kind === "lockout_ready"
        ? {
            title: "You may try logging in now.",
            message: "Please double check your credentials before logging in."
          }
        : loginErrorBanner?.kind === "temporary_error"
          ? {
              title: loginErrorBanner.title,
              message: loginErrorBanner.message
            }
          : null;

  const canSubmit = useMemo(
    () => email.trim().length > 0 && password.length > 0 && (!shouldShowCaptcha || Boolean(captchaToken)) && !isLockoutActive,
    [captchaToken, email, isLockoutActive, password, shouldShowCaptcha]
  );

  const validate = () => {
    const nextErrors: FormErrors = {};

    if (!email.trim()) {
      nextErrors.email = "Email is required.";
    }

    if (!password) {
      nextErrors.password = "Password is required.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const completeAuthFlow = async (
    flow: AuthFlowResponse,
    verificationEmail: string | undefined,
    rememberSession: boolean
  ) => {
    if (flow.status === "authenticated" && flow.session) {
      await applyAuthTokenResponse(flow.session, { rememberSession });
      playSuccess();
      router.replace("/(tabs)");
      return;
    }

    if (flow.status === "email_verification_required" && flow.emailVerification) {
      stageEmailVerification({
        ...flow.emailVerification,
        email: verificationEmail,
        rememberSession
      });
      router.push("/(auth)/verify-email" as never);
      return;
    }

    if (flow.status === "mfa_required" && flow.mfaChallenge) {
      stageMfaLogin({
        ...flow.mfaChallenge,
        context: "fresh_login",
        rememberSession
      });
      router.push("/(auth)/mfa" as never);
      return;
    }

    throw new Error("The sign-in response was incomplete. Please try again.");
  };

  const handleLogin = async () => {
    if (isAuthTransitioning) {
      return;
    }

    if (!validate()) {
      return;
    }

    if (shouldShowCaptcha && !captchaToken) {
      return;
    }

    if (isLockoutActive) {
      return;
    }

    const normalizedEmail = email.trim().toLowerCase();

    clearSessionMessage();
    setGoogleError(null);
    try {
      const deviceContext = buildDeviceContext();
      const trustedDevice = await readMfaTrustedDeviceCredential({
        deviceFingerprint: deviceContext.deviceFingerprint
      });
      const flow = await loginMutation.mutateAsync({
        email: normalizedEmail,
        password,
        captchaToken: shouldShowCaptcha ? captchaToken : null,
        deviceContext,
        mfaTrustedDeviceToken: trustedDevice?.token
      });

      void clearPersistedLockoutUntil();
      setLoginErrorBanner(null);
      setFailedLoginAttempts(0);
      await completeAuthFlow(flow, normalizedEmail, rememberMe);
    } catch (error) {
      if (error instanceof ApiClientError) {
        const lockoutRetryAfterMs = tryParseLockoutRetryAfterMs(error);

        if (lockoutRetryAfterMs && lockoutRetryAfterMs > Date.now()) {
          void persistLockoutUntilMs(lockoutRetryAfterMs);
          setLoginErrorBanner({
            kind: "lockout_countdown",
            id: Date.now(),
            unlockAtMs: lockoutRetryAfterMs
          });
        } else {
          let highlightTarget: ErrorFieldTarget = "none";
          if ([400, 401, 403].includes(error.status)) {
            highlightTarget = "both";
          }

          setLoginErrorBanner({
            kind: "temporary_error",
            id: Date.now(),
            title: "Sign-in failed",
            message: formatUnknownError(error),
            highlightTarget
          });
        }

        if (error.status > 0) {
          setFailedLoginAttempts((current) => current + 1);
          if (shouldShowCaptcha) {
            setCaptchaToken(null);
          }
        }
      } else {
        setLoginErrorBanner({
          kind: "temporary_error",
          id: Date.now(),
          title: "Sign-in failed",
          message: formatUnknownError(error),
          highlightTarget: "none"
        });
      }
    }
  };

  const handleGoogleSignIn = async () => {
    if (isAuthTransitioning) {
      setGoogleError("Finishing sign-out. Please try again in a moment.");
      return;
    }

    setGoogleError(null);
    clearSessionMessage();
    const result = await googleSignIn.signInWithGoogle();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setGoogleError(result.message ?? "Google sign-in failed.");
      }
      return;
    }

    if (result.flow) {
      try {
        await completeAuthFlow(result.flow, undefined, rememberMe);
      } catch (error) {
        setGoogleError(formatUnknownError(error));
      }
    }
  };

  const handleMicrosoftSignIn = async () => {
    if (isAuthTransitioning) {
      setGoogleError("Finishing sign-out. Please try again in a moment.");
      return;
    }

    setGoogleError(null);
    clearSessionMessage();
    const result = await microsoftSignIn.signInWithMicrosoft();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setGoogleError(result.message ?? "Microsoft sign-in failed.");
      }
      return;
    }

    if (result.flow) {
      try {
        await completeAuthFlow(result.flow, undefined, rememberMe);
      } catch (error) {
        setGoogleError(formatUnknownError(error));
      }
    }
  };

  return (
    <AuthScreen>
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Welcome back!</Text>
          <Text style={styles.subtitle}>Sign in to continue managing your finances.</Text>
        </View>
      </View>

      <View style={styles.centerWrap}>
        <View style={styles.formAndActions}>
          <View style={styles.loginCoreFrame}>
            {loginErrorBanner && bannerCopy ? (
              <View pointerEvents="box-none" style={styles.errorBannerAboveForm}>
                <Animated.View style={[styles.narrowBlock, { opacity: loginBannerOpacity }]}>
                  <Banner
                    title={bannerCopy.title}
                    message={
                      bannerCopy.message
                    }
                    tone="error"
                  />
                </Animated.View>
              </View>
            ) : isMfaExpired || isMfaUnavailable ? (
              <View pointerEvents="none" style={styles.errorBannerAboveForm}>
                <View style={styles.narrowBlock}>
                  <Banner
                    title={isMfaExpired ? "Security check expired" : "Security check unavailable"}
                    message="Sign in again to request a new Authenticator check."
                  />
                </View>
              </View>
            ) : sessionMessage ? (
              <View pointerEvents="none" style={styles.errorBannerAboveForm}>
                <View style={styles.narrowBlock}>
                  <Banner title="Sign in again" message={sessionMessage} />
                </View>
              </View>
            ) : null}

            <View style={styles.loginCoreLifted}>
              <View style={styles.form}>
                <Animated.View style={[styles.narrowBlock, { transform: [{ translateX: emailShakeX }] }]}>
                  <InsetFieldShell label="Email" color={emailBorderColor}>
                    <TextField
                      label="Email"
                      value={email}
                      onChangeText={(value) => setEmail(removeWhitespace(value))}
                      autoCapitalize="none"
                      keyboardType="email-address"
                      placeholder="you@example.com"
                      showLabel={false}
                      dense
                      containerStyle={styles.insetFieldContainer}
                      inputStyle={styles.authFieldInput}
                      error={errors.email}
                      onFocus={() => setFocusedField("email")}
                      forceFocused={focusedField === "email"}
                    />
                  </InsetFieldShell>
                </Animated.View>

                <Animated.View style={[styles.narrowBlock, { transform: [{ translateX: passwordShakeX }] }]}>
                  <InsetFieldShell label="Password" color={passwordBorderColor}>
                    <PasswordField
                      label="Password"
                      value={password}
                      onChangeText={(value) => setPassword(removeWhitespace(value))}
                      placeholder="Password"
                      showLabel={false}
                      dense
                      containerStyle={styles.insetFieldContainer}
                      style={styles.authFieldInput}
                      error={errors.password}
                      onFocus={() => setFocusedField("password")}
                      forceFocused={focusedField === "password"}
                      isPasswordVisible={passwordVisible}
                      onPasswordVisibilityChange={setPasswordVisible}
                      autoHideOnBlur={false}
                    />
                  </InsetFieldShell>
                </Animated.View>

                {shouldShowCaptcha ? <CaptchaGate token={captchaToken} onTokenChange={setCaptchaToken} showLabel={false} /> : null}

                <View style={styles.narrowBlock}>
                  <View style={styles.accountHelpRow}>
                    <Pressable
                      accessibilityRole="checkbox"
                      accessibilityState={{ checked: rememberMe }}
                      accessibilityLabel="Remember me"
                      onPress={() => setRememberMe((current) => !current)}
                      style={({ pressed }) => [
                        styles.rememberMeControl,
                        pressed ? styles.linkPressed : null
                      ]}
                    >
                      <View style={[
                        styles.rememberMeCheckbox,
                        rememberMe ? styles.rememberMeCheckboxChecked : null
                      ]}>
                        {rememberMe ? (
                          <Ionicons name="checkmark" size={14} color={palette.appBackground} />
                        ) : null}
                      </View>
                      <Text style={styles.rememberMeLabel}>Remember me</Text>
                    </Pressable>
                    <Pressable
                      onPress={() => router.push("/forgot-password" as never)}
                      style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                    >
                      <Text style={styles.forgotInlineLink}>Forgot password?</Text>
                    </Pressable>
                  </View>
                </View>
              </View>

              <View style={[styles.ctaGroup, styles.narrowBlock]}>
                <Button
                  label="Log in"
                  onPress={() => void handleLogin()}
                  isLoading={loginMutation.isPending}
                  disabled={!canSubmit || isAuthTransitioning}
                  style={[styles.authButton, styles.loginButton]}
                />

                <View style={styles.socialDividerRow}>
                  <View style={styles.socialDividerLine} />
                  <Text style={styles.socialDividerText}>Or continue with</Text>
                  <View style={styles.socialDividerLine} />
                </View>

                <View style={styles.socialAuthRow}>
                  <Button
                    label="Google"
                    variant="secondary"
                    icon={<Ionicons name="logo-google" size={16} color={palette.textPrimary} />}
                    onPress={() => void handleGoogleSignIn()}
                    isLoading={googleSignIn.isPending}
                    disabled={!googleSignIn.isConfigured || googleSignIn.isPending || isAuthTransitioning}
                    style={styles.authButton}
                  />

                  <Button
                    label="Microsoft"
                    variant="secondary"
                    icon={<Ionicons name="logo-microsoft" size={16} color={palette.textPrimary} />}
                    onPress={() => void handleMicrosoftSignIn()}
                    isLoading={microsoftSignIn.isPending}
                    disabled={!microsoftSignIn.isReady || microsoftSignIn.isPending || isAuthTransitioning}
                    style={styles.authButton}
                  />
                </View>
                {googleError ? <Text style={styles.googleError}>{googleError}</Text> : null}

                <View style={styles.signUpRow}>
                  <Text style={styles.signUpPrompt}>Don&apos;t have an account yet? </Text>
                  <Pressable
                    onPress={() => router.push("/register" as never)}
                    style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                  >
                    <Text style={styles.signUpLink}>Sign up</Text>
                  </Pressable>
                </View>
              </View>
            </View>
          </View>
        </View>
      </View>

      <AuthLegalLinks
        onPressTerms={() => router.push("/legal/terms" as never)}
        onPressPrivacy={() => router.push("/legal/privacy-policy" as never)}
      />
    </AuthScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  topRow: {
    marginTop: spacing[16],
    alignItems: "center",
    gap: spacing[8]
  },
  headerTextWrap: {
    width: "100%",
    alignItems: "center",
    gap: spacing[8]
  },
  title: {
    color: palette.textPrimary,
    ...typography.title1,
    textAlign: "center"
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.body2,
    textAlign: "center"
  },
  centerWrap: {
    flex: 1,
    justifyContent: "center",
    gap: spacing[40]
  },
  narrowBlock: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360
  },
  form: {
    gap: spacing[16]
  },
  formAndActions: {
    gap: spacing[16]
  },
  loginCoreFrame: {
    position: "relative"
  },
  errorBannerAboveForm: {
    position: "absolute",
    left: 0,
    right: 0,
    top: -118,
    zIndex: 20
  },
  loginCoreLifted: {
    transform: [{ translateY: -18 }]
  },
  insetFieldWrap: {
    position: "relative"
  },
  insetOutlineSvg: {
    ...StyleSheet.absoluteFillObject,
    zIndex: 2
  },
  insetFieldLabelChip: {
    position: "absolute",
    top: INSET_LABEL_TOP,
    left: INSET_LABEL_LEFT,
    zIndex: 4,
    alignItems: "flex-start"
  },
  insetFieldLabelText: {
    ...typography.fieldLabel,
    includeFontPadding: false,
    flexShrink: 0,
    paddingRight: 2
  },
  insetFieldContainer: {
    minHeight: 44,
    borderRadius: 6,
    paddingHorizontal: 12,
    borderWidth: 0,
    shadowColor: "transparent",
    shadowOpacity: 0,
    shadowRadius: 0,
    shadowOffset: { width: 0, height: 0 },
    elevation: 0
  },
  authFieldInput: {
    paddingVertical: 8
  },
  accountHelpRow: {
    minHeight: 28,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  rememberMeControl: {
    minHeight: 32,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  rememberMeCheckbox: {
    width: 20,
    height: 20,
    borderRadius: 4,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "transparent"
  },
  rememberMeCheckboxChecked: {
    borderColor: palette.primaryGlow,
    backgroundColor: palette.primaryGlow
  },
  rememberMeLabel: {
    color: palette.textSecondary,
    ...typography.body2
  },
  ctaGroup: {
    marginTop: spacing[16],
    gap: spacing[14]
  },
  authButton: {
    flex: 1,
    borderRadius: 6,
    minHeight: 44
  },
  loginButton: {
    marginTop: spacing[6]
  },
  socialAuthRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[10]
  },
  socialDividerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10],
    marginTop: spacing[4]
  },
  socialDividerLine: {
    flex: 1,
    height: StyleSheet.hairlineWidth,
    backgroundColor: "rgba(242,140,40,0.22)"
  },
  socialDividerText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  forgotInlineLink: {
    color: palette.primaryGlow,
    ...typography.body2,
    fontWeight: "600"
  },
  signUpRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    marginTop: spacing[4]
  },
  signUpPrompt: {
    color: palette.textSecondary,
    ...typography.body2
  },
  signUpLink: {
    color: palette.primaryGlow,
    ...typography.body2,
    fontWeight: "600"
  },
  linkPressed: {
    opacity: 0.75
  },
  googleError: {
    color: palette.negative,
    ...typography.caption
  }
}));

