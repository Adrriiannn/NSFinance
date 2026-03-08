import { router } from "expo-router";
import { useMemo, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useRegisterMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<
  Record<"email" | "password" | "confirmPassword" | "firstName" | "lastName", string>
>;

export default function RegisterScreen() {
  const registerMutation = useRegisterMutation();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState("");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [captchaVerified, setCaptchaVerified] = useState(false);
  const [errors, setErrors] = useState<FormErrors>({});

  const canSubmit = useMemo(
    () =>
      email.trim().length > 0 &&
      password.length >= 8 &&
      confirmPassword.length >= 8 &&
      captchaVerified,
    [captchaVerified, confirmPassword, email, password]
  );

  const validate = () => {
    const nextErrors: FormErrors = {};

    if (!email.trim()) {
      nextErrors.email = "Email is required.";
    }

    if (!firstName.trim()) {
      nextErrors.firstName = "First name is required.";
    }

    if (!lastName.trim()) {
      nextErrors.lastName = "Last name is required.";
    }

    if (password.length < 8) {
      nextErrors.password = "Password must be at least 8 characters.";
    }

    if (password !== confirmPassword) {
      nextErrors.confirmPassword = "Passwords do not match.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleRegister = async () => {
    if (!validate()) {
      return;
    }

    await registerMutation.mutateAsync({
      email: email.trim().toLowerCase(),
      password,
      firstName: firstName.trim(),
      lastName: lastName.trim()
    });

    playSuccess();
    router.replace("/(tabs)");
  };

  return (
    <AuthScreen>
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Create account</Text>
          <Text style={styles.subtitle}>Set up your NSFinTech profile.</Text>
        </View>
        <NsfLogo size={52} />
      </View>

      <View style={styles.centerWrap}>
        {registerMutation.isError ? (
          <ErrorState
            title="Registration failed"
            message={formatUnknownError(registerMutation.error)}
            onRetry={handleRegister}
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
            label="First name"
            value={firstName}
            onChangeText={setFirstName}
            placeholder="Marius"
            error={errors.firstName}
          />
          <TextField
            label="Last name"
            value={lastName}
            onChangeText={setLastName}
            placeholder="Albu"
            error={errors.lastName}
          />
          <TextField
            label="Password"
            value={password}
            onChangeText={setPassword}
            placeholder="At least 8 characters"
            secureTextEntry
            error={errors.password}
          />
          <TextField
            label="Confirm password"
            value={confirmPassword}
            onChangeText={setConfirmPassword}
            placeholder="Repeat password"
            secureTextEntry
            error={errors.confirmPassword}
          />
          <CaptchaGate
            isVerified={captchaVerified}
            onVerify={() => setCaptchaVerified((current) => !current)}
          />
        </View>

        <View style={styles.ctaGroup}>
          <PrimaryButton
            label="Create account"
            onPress={() => void handleRegister()}
            isLoading={registerMutation.isPending}
            disabled={!canSubmit}
          />
          <View style={styles.backButtonWrap}>
            <SecondaryButton label="Back to sign in" onPress={() => router.push("/login" as never)} />
          </View>
        </View>
      </View>
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
    gap: spacing[20]
  },
  backButtonWrap: {
    marginTop: spacing[8]
  }
});
