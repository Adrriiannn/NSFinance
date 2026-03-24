import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { AuthDivider } from "../../src/components/ui/AuthDivider";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { Banner } from "../../src/components/ui/feedback/Banner";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { persistRememberedEmail, readRememberedEmail } from "../../src/features/auth/rememberedEmail";
import { useLoginMutation } from "../../src/features/auth/useAuthMutations";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { ApiClientError, formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"email" | "password", string>>;
type FocusField = "email" | "password" | null;
type ErrorFieldTarget = "none" | "password" | "both";

const LOGIN_ATTEMPTS_BEFORE_CAPTCHA = 3;
const LOGIN_ERROR_BANNER_DURATION_MS = 5000;
const LOGIN_ERROR_SHAKE_DURATION_MS = 60;
const LOCKOUT_READY_NOTICE_DURATION_MS = 5000;

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

export default function LoginScreen() {
  const loginMutation = useLoginMutation();
  const googleSignIn = useGoogleSignIn();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [captchaToken, setCaptchaToken] = useState<string | null>(null);
  const [failedLoginAttempts, setFailedLoginAttempts] = useState(0);
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [googleError, setGoogleError] = useState<string | null>(null);
  const [rememberEmail, setRememberEmail] = useState(false);
  const [loginErrorBanner, setLoginErrorBanner] = useState<LoginErrorBannerState | null>(null);
  const [countdownNowMs, setCountdownNowMs] = useState(Date.now());

  const shouldShowCaptcha = failedLoginAttempts >= LOGIN_ATTEMPTS_BEFORE_CAPTCHA;
  const emailShakeX = useRef(new Animated.Value(0)).current;
  const passwordShakeX = useRef(new Animated.Value(0)).current;
  const loginBannerOpacity = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    let cancelled = false;

    const hydrateRememberedEmail = async () => {
      const remembered = await readRememberedEmail();
      if (cancelled || !remembered.enabled) {
        return;
      }

      setRememberEmail(true);
      setEmail(remembered.email);
    };

    void hydrateRememberedEmail();

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

  const lockoutRemainingSeconds =
    loginErrorBanner?.kind === "lockout_countdown"
      ? Math.max(0, Math.ceil((loginErrorBanner.unlockAtMs - countdownNowMs) / 1000))
      : 0;

  const isLockoutActive = loginErrorBanner?.kind === "lockout_countdown" && lockoutRemainingSeconds > 0;

  const bannerCopy =
    loginErrorBanner?.kind === "lockout_countdown"
      ? {
          title: "Sign-in failed. Too many failed attempts.",
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

  const handleLogin = async () => {
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

    setGoogleError(null);
    try {
      await loginMutation.mutateAsync({
        email: normalizedEmail,
        password,
        captchaToken: shouldShowCaptcha ? captchaToken : null,
        deviceContext: {
          platform: Platform.OS
        }
      });

      setLoginErrorBanner(null);
      setFailedLoginAttempts(0);
      await persistRememberedEmail(rememberEmail, email);
      playSuccess();
      router.replace("/(tabs)");
    } catch (error) {
      if (error instanceof ApiClientError) {
        const lockoutRetryAfterMs = tryParseLockoutRetryAfterMs(error);

        if (lockoutRetryAfterMs && lockoutRetryAfterMs > Date.now()) {
          setLoginErrorBanner({
            kind: "lockout_countdown",
            id: Date.now(),
            unlockAtMs: lockoutRetryAfterMs
          });
        } else if (error.code === "account_not_found") {
          setLoginErrorBanner(null);
          setCaptchaToken(null);
          setFailedLoginAttempts(0);
          router.push((`/register?email=${encodeURIComponent(normalizedEmail)}`) as never);
          return;
        } else if (error.code === "password_login_unavailable") {
          setLoginErrorBanner({
            kind: "temporary_error",
            id: Date.now(),
            title: "Use Google to sign in",
            message: "This account uses Google sign-in. Continue with Google to log in.",
            highlightTarget: "none"
          });
        } else {
          let highlightTarget: ErrorFieldTarget = "none";
          if (error.code === "invalid_password") {
            highlightTarget = "password";
          } else if ([400, 401, 403].includes(error.status)) {
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
    setGoogleError(null);
    const result = await googleSignIn.signInWithGoogle();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setGoogleError(result.message ?? "Google sign-in failed.");
      }
      return;
    }

    playSuccess();
    router.replace("/(tabs)");
  };

  return (
    <AuthScreen>
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Welcome back</Text>
          <Text style={styles.subtitle}>Sign in to continue managing your finances.</Text>
        </View>
        <NsfLogo size={52} />
      </View>

      <View style={styles.centerWrap}>
        <View style={styles.formAndActions}>
          <View style={styles.loginCoreFrame}>
            {loginErrorBanner && bannerCopy ? (
              <View pointerEvents="box-none" style={styles.errorBannerAboveForm}>
                <Animated.View style={[styles.narrowBlock, { opacity: loginBannerOpacity }]}>
                  <Banner title={bannerCopy.title} message={bannerCopy.message} tone="error" />
                </Animated.View>
              </View>
            ) : null}

            <View style={styles.loginCoreLifted}>
              <View style={styles.form}>
                <Animated.View style={[styles.narrowBlock, { transform: [{ translateX: emailShakeX }] }]}>
                  <TextField
                    label="Email"
                    value={email}
                    onChangeText={setEmail}
                    autoCapitalize="none"
                    keyboardType="email-address"
                    placeholder="you@example.com"
                    dense
                    containerStyle={[styles.authFieldContainer, showEmailFieldError ? styles.authFieldTransientError : null]}
                    style={styles.authFieldInput}
                    error={errors.email}
                    onFocus={() => setFocusedField("email")}
                    forceFocused={focusedField === "email"}
                  />
                </Animated.View>

                <Animated.View style={[styles.narrowBlock, { transform: [{ translateX: passwordShakeX }] }]}>
                  <PasswordField
                    label="Password"
                    value={password}
                    onChangeText={setPassword}
                    placeholder="Password"
                    dense
                    containerStyle={[styles.authFieldContainer, showPasswordFieldError ? styles.authFieldTransientError : null]}
                    style={styles.authFieldInput}
                    error={errors.password}
                    onFocus={() => setFocusedField("password")}
                    forceFocused={focusedField === "password"}
                    isPasswordVisible={passwordVisible}
                    onPasswordVisibilityChange={setPasswordVisible}
                    autoHideOnBlur={false}
                  />
                </Animated.View>

                {shouldShowCaptcha ? <CaptchaGate token={captchaToken} onTokenChange={setCaptchaToken} showLabel={false} /> : null}

                <View style={styles.narrowBlock}>
                  <View style={styles.rememberEmailRow}>
                    <Pressable
                      accessibilityRole="checkbox"
                      accessibilityState={{ checked: rememberEmail }}
                      onPress={() => setRememberEmail((current) => !current)}
                      style={({ pressed }) => [
                        styles.rememberEmailCheckbox,
                        rememberEmail ? styles.rememberEmailCheckboxChecked : null,
                        pressed ? styles.rememberEmailCheckboxPressed : null
                      ]}
                    >
                      {rememberEmail ? <Ionicons name="checkmark" size={14} color={palette.appBackground} /> : null}
                    </Pressable>
                    <Pressable
                      accessibilityRole="button"
                      accessibilityLabel="Remember my email"
                      onPress={() => setRememberEmail((current) => !current)}
                      style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                    >
                      <Text style={styles.rememberEmailLabel}>Remember my email</Text>
                    </Pressable>
                  </View>
                </View>
              </View>

              <View style={[styles.ctaGroup, styles.narrowBlock]}>
                <View style={styles.primaryAuthRow}>
                  <PrimaryButton
                    label="Log in"
                    onPress={() => void handleLogin()}
                    isLoading={loginMutation.isPending}
                    disabled={!canSubmit}
                    style={styles.primaryAuthButton}
                  />

                  <SecondaryButton
                    label={googleSignIn.isPending ? "Signing in with Google..." : "Sign in with Google"}
                    onPress={() => void handleGoogleSignIn()}
                    disabled={!googleSignIn.isConfigured || googleSignIn.isPending}
                    style={styles.primaryAuthButton}
                  />
                </View>
                {googleError ? <Text style={styles.googleError}>{googleError}</Text> : null}

                <View style={styles.forgotWrap}>
                  <Pressable
                    onPress={() => router.push("/forgot-password" as never)}
                    style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                  >
                    <Text style={styles.forgotLink}>Forgot your password?</Text>
                  </Pressable>
                </View>
              </View>
            </View>
          </View>

          <View style={[styles.createAccountSection, styles.narrowBlock]}>
            <AuthDivider widthPercent={70} />
            <SecondaryButton label="Create account" onPress={() => router.push("/register" as never)} />
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

const styles = StyleSheet.create({
  topRow: {
    marginTop: spacing[16],
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: spacing[12]
  },
  headerTextWrap: {
    flex: 1,
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
  authFieldContainer: {
    minHeight: 36,
    borderRadius: 12,
    paddingHorizontal: 12
  },
  authFieldInput: {
    paddingVertical: 8
  },
  authFieldTransientError: {
    borderColor: palette.negative
  },
  rememberEmailRow: {
    minHeight: 28,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
  },
  rememberEmailCheckbox: {
    width: 20,
    height: 20,
    borderRadius: 4,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(18,36,58,0.74)",
    alignItems: "center",
    justifyContent: "center"
  },
  rememberEmailCheckboxPressed: {
    opacity: 0.86
  },
  rememberEmailCheckboxChecked: {
    borderColor: palette.success,
    backgroundColor: palette.success
  },
  rememberEmailLabel: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  ctaGroup: {
    gap: spacing[12]
  },
  primaryAuthRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[10]
  },
  primaryAuthButton: {
    flex: 1
  },
  forgotWrap: {
    alignItems: "center"
  },
  forgotLink: {
    color: palette.primaryGlow,
    ...typography.body2
  },
  linkPressed: {
    opacity: 0.75
  },
  googleError: {
    color: palette.negative,
    ...typography.caption
  },
  createAccountSection: {
    marginTop: spacing[16],
    gap: spacing[28]
  }
});
