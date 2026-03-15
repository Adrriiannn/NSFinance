import { router } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Platform, StyleSheet, Text, View } from "react-native";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { NsfLogo } from "../../src/components/branding/NsfLogo";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { SecondaryButton } from "../../src/components/ui/SecondaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useRegisterMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { authApiRouteDiagnostics, getAuthApiDebugDetail } from "../../src/lib/api/diagnostics";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"email" | "firstName" | "lastName" | "password" | "confirmPassword", string>>;
type FocusField = "email" | "firstName" | "lastName" | "password" | "confirmPassword" | null;

type PasswordRule = {
  key: "minLength" | "lower" | "upper" | "symbolAndDigit";
  label: string;
  isMet: boolean;
};

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
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [confirmPasswordVisible, setConfirmPasswordVisible] = useState(false);
  const authApiDebugDetail = getAuthApiDebugDetail();

  useEffect(() => {
    if (focusedField !== "password") {
      setPasswordVisible(false);
    }

    if (focusedField !== "confirmPassword") {
      setConfirmPasswordVisible(false);
    }
  }, [focusedField]);

  const keyboardMirrorField = useMemo(() => {
    switch (focusedField) {
      case "email":
        return {
          key: "email",
          label: "Email",
          value: email,
          onChangeText: setEmail,
          placeholder: "you@example.com",
          keyboardType: "email-address" as const,
          autoCapitalize: "none" as const
        };
      case "firstName":
        return {
          key: "firstName",
          label: "First Name",
          value: firstName,
          onChangeText: setFirstName,
          placeholder: "John",
          autoCapitalize: "words" as const
        };
      case "lastName":
        return {
          key: "lastName",
          label: "Last Name",
          value: lastName,
          onChangeText: setLastName,
          placeholder: "Doe",
          autoCapitalize: "words" as const
        };
      case "password":
        return {
          key: "password",
          label: "Password",
          value: password,
          onChangeText: setPassword,
          placeholder: "Choose a password",
          secureTextEntry: true,
          passwordVisible,
          onPasswordVisibilityChange: setPasswordVisible
        };
      case "confirmPassword":
        return {
          key: "confirmPassword",
          label: "Confirm Password",
          value: confirmPassword,
          onChangeText: setConfirmPassword,
          placeholder: "Repeat password",
          secureTextEntry: true,
          passwordVisible: confirmPasswordVisible,
          onPasswordVisibilityChange: setConfirmPasswordVisible
        };
      default:
        return null;
    }
  }, [
    confirmPassword,
    confirmPasswordVisible,
    email,
    firstName,
    focusedField,
    lastName,
    password,
    passwordVisible
  ]);

  const passwordRules = useMemo<PasswordRule[]>(
    () => [
      { key: "minLength", label: "Minimum 10 characters", isMet: password.length >= 10 },
      { key: "lower", label: "At least a lowercase character", isMet: /[a-z]/.test(password) },
      { key: "upper", label: "At least an uppercase character", isMet: /[A-Z]/.test(password) },
      {
        key: "symbolAndDigit",
        label: "At least a symbol and a digit",
        isMet: /[^A-Za-z0-9]/.test(password) && /\d/.test(password)
      }
    ],
    [password]
  );
  const allPasswordRulesMet = passwordRules.every((rule) => rule.isMet);
  const hasConfirmInput = confirmPassword.trim().length > 0;
  const passwordsMatch = hasConfirmInput && password === confirmPassword;
  const shouldShowMirrorPasswordRules = focusedField === "password";
  const mirrorOrderedPasswordRules = useMemo<PasswordRule[]>(() => {
    const byKey = new Map(passwordRules.map((rule) => [rule.key, rule]));
    return [
      byKey.get("lower"),
      byKey.get("upper"),
      byKey.get("symbolAndDigit"),
      byKey.get("minLength")
    ].filter((rule): rule is PasswordRule => Boolean(rule));
  }, [passwordRules]);
  const keyboardMirrorRequirements = useMemo(
    () => {
      if (shouldShowMirrorPasswordRules) {
        return {
          items: mirrorOrderedPasswordRules,
          showSuccessWhenAllMet: true,
          successText: "Your password meets out requirements."
        };
      }

      if (focusedField === "confirmPassword" && passwordsMatch) {
        return {
          items: [{ key: "confirm-password-match", label: "", isMet: true }],
          showSuccessWhenAllMet: true,
          successText: "The confirmed password matches."
        };
      }

      return null;
    },
    [focusedField, mirrorOrderedPasswordRules, passwordsMatch, shouldShowMirrorPasswordRules]
  );

  const canSubmit = useMemo(
    () =>
      email.trim().length > 0 &&
      firstName.trim().length > 0 &&
      lastName.trim().length > 0 &&
      allPasswordRulesMet &&
      password === confirmPassword &&
      captchaVerified,
    [allPasswordRulesMet, captchaVerified, confirmPassword, email, firstName, lastName, password]
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

    if (!allPasswordRulesMet) {
      nextErrors.password = "Password does not meet requirements.";
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

    const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    const locale = Intl.DateTimeFormat().resolvedOptions().locale || "en-US";
    const displayName = `${firstName.trim()} ${lastName.trim()}`.trim();

    await registerMutation.mutateAsync({
      email: email.trim().toLowerCase(),
      password,
      displayName,
      timezone,
      locale,
      preferredCurrency: "EUR",
      deviceContext: {
        platform: Platform.OS
      }
    });

    playSuccess();
    router.replace("/(tabs)");
  };

  return (
    <AuthScreen
      keyboardMirrorField={keyboardMirrorField}
      keyboardMirrorRequirements={keyboardMirrorRequirements}
    >
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Create account</Text>
          <Text style={styles.subtitle}>Set up your NSFinance profile.</Text>
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

          <View style={styles.nameRow}>
            <View style={styles.nameField}>
              <TextField
                label="First Name"
                value={firstName}
                onChangeText={setFirstName}
                placeholder="John"
                error={errors.firstName}
                onFocus={() => setFocusedField("firstName")}
                forceFocused={focusedField === "firstName"}
              />
            </View>
            <View style={styles.nameField}>
              <TextField
                label="Last Name"
                value={lastName}
                onChangeText={setLastName}
                placeholder="Doe"
                error={errors.lastName}
                onFocus={() => setFocusedField("lastName")}
                forceFocused={focusedField === "lastName"}
              />
            </View>
          </View>

          <PasswordField
            label="Password"
            value={password}
            onChangeText={setPassword}
            placeholder="Choose a password"
            error={errors.password}
            onFocus={() => setFocusedField("password")}
            forceFocused={focusedField === "password"}
            isPasswordVisible={passwordVisible}
            onPasswordVisibilityChange={setPasswordVisible}
            autoHideOnBlur={false}
          />

          {allPasswordRulesMet ? (
            <Text style={styles.ruleSuccess}>Your password meets our requirements.</Text>
          ) : (
            <View style={styles.ruleList}>
              {passwordRules.map((rule, index) => (
                <Text
                  key={rule.key}
                  numberOfLines={1}
                  adjustsFontSizeToFit
                  minimumFontScale={0.82}
                  style={[
                    styles.ruleText,
                    index % 2 === 0 ? styles.ruleTextLeft : styles.ruleTextRight,
                    rule.isMet ? styles.ruleMet : styles.ruleMissing
                  ]}
                >
                  {rule.label}
                </Text>
              ))}
            </View>
          )}

          <PasswordField
            label="Confirm Password"
            value={confirmPassword}
            onChangeText={setConfirmPassword}
            placeholder="Repeat password"
            error={errors.confirmPassword}
            onFocus={() => setFocusedField("confirmPassword")}
            forceFocused={focusedField === "confirmPassword"}
            isPasswordVisible={confirmPasswordVisible}
            onPasswordVisibilityChange={setConfirmPasswordVisible}
            autoHideOnBlur={false}
          />

          {passwordsMatch ? <Text style={styles.matchText}>The confirmed password matches.</Text> : null}

          <CaptchaGate
            isVerified={captchaVerified}
            onVerify={() => setCaptchaVerified((current) => !current)}
            showLabel={false}
          />
        </View>

        <View style={styles.ctaGroup}>
          <PrimaryButton
            label="Create account"
            onPress={() => void handleRegister()}
            isLoading={registerMutation.isPending}
            disabled={!canSubmit}
          />
          <SecondaryButton label="Back to sign in" onPress={() => router.push("/login" as never)} />
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
  nameRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  nameField: {
    flex: 1
  },
  ctaGroup: {
    gap: spacing[12]
  },
  ruleList: {
    flexDirection: "row",
    flexWrap: "wrap",
    justifyContent: "space-between",
    rowGap: spacing[8]
  },
  ruleText: {
    width: "48.5%",
    flexShrink: 1,
    ...typography.caption
  },
  ruleTextLeft: {
    textAlign: "left"
  },
  ruleTextRight: {
    textAlign: "right"
  },
  ruleMet: {
    color: palette.success
  },
  ruleMissing: {
    color: palette.negative
  },
  ruleSuccess: {
    color: palette.success,
    ...typography.caption
  },
  matchText: {
    color: palette.success,
    ...typography.caption
  }
});





