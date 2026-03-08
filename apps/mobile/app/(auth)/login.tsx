import { router } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { SaveCredentialsPrompt } from "../../src/components/forms/SaveCredentialsPrompt";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useLoginMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import {
  clearSavedCredentials,
  getSavedCredentials,
  getSavedCredentialsDecision,
  setSavedCredentials,
  setSavedCredentialsDecision
} from "../../src/lib/auth/savedCredentials";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"email" | "password", string>>;

export default function LoginScreen() {
  const loginMutation = useLoginMutation();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [captchaVerified, setCaptchaVerified] = useState(false);
  const [saveDecision, setSaveDecision] = useState<boolean | null>(null);
  const [showSavePrompt, setShowSavePrompt] = useState(false);

  useEffect(() => {
    const load = async () => {
      const decision = await getSavedCredentialsDecision();
      setSaveDecision(decision);

      if (decision) {
        const saved = await getSavedCredentials();
        if (saved) {
          setEmail(saved.email);
          setPassword(saved.password);
        }
      }
    };

    void load();
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

  const finishLogin = async () => {
    playSuccess();
    router.replace("/(tabs)");
  };

  const handleLogin = async () => {
    if (!validate()) {
      return;
    }

    await loginMutation.mutateAsync({
      email: email.trim().toLowerCase(),
      password
    });

    if (saveDecision === null) {
      setShowSavePrompt(true);
      return;
    }

    if (saveDecision) {
      await setSavedCredentials({
        email: email.trim().toLowerCase(),
        password
      });
    }

    await finishLogin();
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
          <ErrorState
            title="Sign-in failed"
            message={formatUnknownError(loginMutation.error)}
            onRetry={handleLogin}
            retryLabel="Try again"
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
          />
          <TextField
            label="Password"
            value={password}
            onChangeText={setPassword}
            placeholder="Password"
            secureTextEntry
            error={errors.password}
          />
          <CaptchaGate
            isVerified={captchaVerified}
            onVerify={() => setCaptchaVerified((current) => !current)}
          />
        </View>

        <View style={styles.ctaGroup}>
          <PrimaryButton
            label="Sign in"
            onPress={() => void handleLogin()}
            isLoading={loginMutation.isPending}
            disabled={!canSubmit}
          />

          <View style={styles.googleWrap}>
            <SecondaryButton label="Sign in with Google" onPress={() => undefined} disabled />
          </View>

          <SecondaryButton label="Create account" onPress={() => router.push("/register" as never)} />
        </View>
      </View>

      <SaveCredentialsPrompt
        visible={showSavePrompt}
        onConfirm={() => {
          void (async () => {
            setShowSavePrompt(false);
            await setSavedCredentialsDecision(true);
            await setSavedCredentials({
              email: email.trim().toLowerCase(),
              password
            });
            setSaveDecision(true);
            await finishLogin();
          })();
        }}
        onDecline={() => {
          void (async () => {
            setShowSavePrompt(false);
            await setSavedCredentialsDecision(false);
            await clearSavedCredentials();
            setSaveDecision(false);
            await finishLogin();
          })();
        }}
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
    gap: spacing[16]
  },
  googleWrap: {
    marginTop: spacing[8],
    marginBottom: spacing[8]
  }
});
