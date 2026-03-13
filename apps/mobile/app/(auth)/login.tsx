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
  const authApiDebugDetail = getAuthApiDebugDetail();

  useEffect(() => {
    if (focusedField !== "password") {
      setPasswordVisible(false);
    }
  }, [focusedField]);

  const keyboardMirrorField = useMemo(() => {
    if (focusedField === "email") {
      return {
        key: "email",
        label: "Email",
        value: email,
        onChangeText: setEmail,
        placeholder: "you@example.com",
        keyboardType: "email-address" as const,
        autoCapitalize: "none" as const
      };
    }

    if (focusedField === "password") {
      return {
        key: "password",
        label: "Password",
        value: password,
        onChangeText: setPassword,
        placeholder: "Password",
        secureTextEntry: true,
        passwordVisible,
        onPasswordVisibilityChange: setPasswordVisible
      };
    }

    return null;
  }, [email, focusedField, password, passwordVisible]);

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
    <AuthScreen keyboardMirrorField={keyboardMirrorField}>
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Welcome back</Text>
          <Text style={styles.subtitle}>Sign in to continue managing your finances.</Text>
        </View>
        <NsfLogo size={52} />
      </View>

      <View style={styles.centerWrap}>
        {loginMutation.isError ? (
          <ErrorState
            title="Sign-in failed"
            message={formatUnknownError(loginMutation.error)}
            onRetry={handleLogin}
            retryLabel="Try again"
            debugDetail={authApiDebugDetail}
            showDebugDetail={authApiRouteDiagnostics.enabled}
          />
        ) : null}

        <View style={styles.form}>
          <TextField
            label="Email"
            value={email}
            onChangeText={setEmail}
            autoCapitalize="none"
            keyboardType="email-address"
            placeholder="you@example.com"
            error={errors.email}
            onFocus={() => setFocusedField("email")}
            forceFocused={focusedField === "email"}
          />
          <PasswordField
            label="Password"
            value={password}
            onChangeText={setPassword}
            placeholder="Password"
            error={errors.password}
            onFocus={() => setFocusedField("password")}
            forceFocused={focusedField === "password"}
            isPasswordVisible={passwordVisible}
            onPasswordVisibilityChange={setPasswordVisible}
            autoHideOnBlur={false}
          />
          <CaptchaGate
            isVerified={captchaVerified}
            onVerify={() => setCaptchaVerified((current) => !current)}
            showLabel={false}
          />
        </View>

        <View style={styles.ctaGroup}>
          <PrimaryButton
            label="Sign in"
            onPress={() => void handleLogin()}
            isLoading={loginMutation.isPending}
            disabled={!canSubmit}
          />

          <SecondaryButton
            label={googleSignIn.isPending ? "Signing in with Google..." : "Sign in with Google"}
            onPress={() => void handleGoogleSignIn()}
            disabled={!googleSignIn.isConfigured || googleSignIn.isPending}
          />
          {googleError ? <Text style={styles.googleError}>{googleError}</Text> : null}

          <View style={styles.forgotWrap}>
            <Pressable
              onPress={() => router.push("/forgot-password" as never)}
              style={({ pressed }) => [pressed ? styles.linkPressed : null]}
            >
              <Text style={styles.forgotLink}>Forgot Password</Text>
            </Pressable>
          </View>

          <AuthDivider widthPercent={70} />

          <SecondaryButton label="Create account" onPress={() => router.push("/register" as never)} />
        </View>
      </View>

      <AuthLegalLinks
        onPressTerms={() => router.push("/legal/terms" as never)}
        onPressPrivacy={() => router.push("/legal/privacy" as never)}
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
    gap: spacing[24]
  },
  form: {
    gap: spacing[16]
  },
  ctaGroup: {
    gap: spacing[12]
  },
  forgotWrap: {
    alignItems: "flex-end"
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
  }
});

