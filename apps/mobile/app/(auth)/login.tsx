import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { AuthDivider } from "../../src/components/ui/AuthDivider";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { persistRememberedEmail, readRememberedEmail } from "../../src/features/auth/rememberedEmail";
import { useLoginMutation } from "../../src/features/auth/useAuthMutations";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { formatUnknownError } from "../../src/lib/api/errors";
import { authApiRouteDiagnostics, getAuthApiDebugDetail } from "../../src/lib/api/diagnostics";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"email" | "password", string>>;
type FocusField = "email" | "password" | null;

export default function LoginScreen() {
  const loginMutation = useLoginMutation();
  const googleSignIn = useGoogleSignIn();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [captchaVerified, setCaptchaVerified] = useState(false);
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [googleError, setGoogleError] = useState<string | null>(null);
  const [rememberEmail, setRememberEmail] = useState(false);
  const authApiDebugDetail = getAuthApiDebugDetail();

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

  const canSubmit = useMemo(
    () => email.trim().length > 0 && password.length > 0 && captchaVerified,
    [captchaVerified, email, password]
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

    setGoogleError(null);
    await loginMutation.mutateAsync({
      email: email.trim().toLowerCase(),
      password,
      deviceContext: {
        platform: Platform.OS
      }
    });

    await persistRememberedEmail(rememberEmail, email);
    playSuccess();
    router.replace("/(tabs)");
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
        {loginMutation.isError ? (
          <View style={styles.narrowBlock}>
            <ErrorState
              title="Sign-in failed"
              message={formatUnknownError(loginMutation.error)}
              onRetry={handleLogin}
              retryLabel="Try again"
              debugDetail={authApiDebugDetail}
              showDebugDetail={authApiRouteDiagnostics.enabled}
            />
          </View>
        ) : null}

        <View style={styles.formAndActions}>
          <View style={styles.form}>
            <View style={styles.narrowBlock}>
              <TextField
                label="Email"
                value={email}
                onChangeText={setEmail}
                autoCapitalize="none"
                keyboardType="email-address"
              placeholder="you@example.com"
              dense
              containerStyle={styles.authFieldContainer}
              style={styles.authFieldInput}
              error={errors.email}
              onFocus={() => setFocusedField("email")}
              forceFocused={focusedField === "email"}
              />
            </View>
            <View style={styles.narrowBlock}>
            <PasswordField
              label="Password"
              value={password}
              onChangeText={setPassword}
              placeholder="Password"
              dense
              containerStyle={styles.authFieldContainer}
              style={styles.authFieldInput}
              error={errors.password}
              onFocus={() => setFocusedField("password")}
              forceFocused={focusedField === "password"}
                isPasswordVisible={passwordVisible}
                onPasswordVisibilityChange={setPasswordVisible}
                autoHideOnBlur={false}
              />
            </View>
            <CaptchaGate
              isVerified={captchaVerified}
              onVerify={() => setCaptchaVerified((current) => !current)}
              showLabel={false}
            />

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

            <View style={styles.createAccountSection}>
              <AuthDivider widthPercent={70} />
              <SecondaryButton label="Create account" onPress={() => router.push("/register" as never)} />
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
  authFieldContainer: {
    minHeight: 36,
    borderRadius: 12,
    paddingHorizontal: 12
  },
  authFieldInput: {
    paddingVertical: 8
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





