import { Ionicons } from "@expo/vector-icons";
import * as SecureStore from "expo-secure-store";
import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import { Animated, Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { Button } from "../../src/components/ui/buttons/Button";
import { Banner } from "../../src/components/ui/feedback/Banner";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
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
const LOGIN_LOCKOUT_UNTIL_KEY = "nsfinance.auth.login.lockout_until_utc_ms";

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

export default function LoginScreen() {
  const searchParams = useLocalSearchParams<{ googleError?: string | string[] }>();
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

  const isGoogleOnlyLoginBanner =
    loginErrorBanner?.kind === "temporary_error" && loginErrorBanner.title === "This account uses Google sign-in.";

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

      void clearPersistedLockoutUntil();
      setLoginErrorBanner(null);
      setFailedLoginAttempts(0);
      await persistRememberedEmail(rememberEmail, email);
      playSuccess();
      router.replace("/(tabs)");
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
            title: "This account uses Google sign-in.",
            message: "Please try logging in via the Sign in with Google option.",
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

  const handleMicrosoftSignIn = () => {
    setGoogleError("Microsoft sign-in is coming soon.");
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
                  <Banner
                    title={bannerCopy.title}
                    message={
                      isGoogleOnlyLoginBanner ? (
                        <>
                          Please try logging in via the{" "}
                          <Text style={styles.googleSignInMessageAccent}>Sign in with Google</Text> option.
                        </>
                      ) : (
                        bannerCopy.message
                      )
                    }
                    tone="error"
                  />
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
                    <View style={styles.rememberEmailLeft}>
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
                <PrimaryButton
                  label="Log in"
                  onPress={() => void handleLogin()}
                  isLoading={loginMutation.isPending}
                  disabled={!canSubmit}
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
                    disabled={!googleSignIn.isConfigured || googleSignIn.isPending}
                    style={styles.authButton}
                  />

                  <Button
                    label="Microsoft"
                    variant="secondary"
                    icon={<Ionicons name="logo-microsoft" size={16} color={palette.textPrimary} />}
                    onPress={handleMicrosoftSignIn}
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
    justifyContent: "space-between",
    gap: spacing[10]
  },
  rememberEmailLeft: {
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
    marginTop: spacing[16],
    gap: spacing[14]
  },
  authButton: {
    flex: 1,
    borderRadius: 18,
    minHeight: 50
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
    backgroundColor: "rgba(220,232,255,0.22)"
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
    fontWeight: "700"
  },
  linkPressed: {
    opacity: 0.75
  },
  googleError: {
    color: palette.negative,
    ...typography.caption
  },
  googleSignInMessageAccent: {
    color: palette.primaryGlow,
    fontWeight: "700"
  }
});
