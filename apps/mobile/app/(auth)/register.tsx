import { router, useLocalSearchParams } from "expo-router";
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
  const routeParams = useLocalSearchParams<{ email?: string | string[] }>();
  const prefilledEmail = useMemo(() => {
    const rawEmail = Array.isArray(routeParams.email) ? routeParams.email[0] : routeParams.email;
    return typeof rawEmail === "string" ? rawEmail.trim().toLowerCase() : "";
  }, [routeParams.email]);

  const registerMutation = useRegisterMutation();
  const { playSuccess } = useFeedbackSound();
  const [email, setEmail] = useState(prefilledEmail);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [captchaToken, setCaptchaToken] = useState<string | null>(null);
  const [errors, setErrors] = useState<FormErrors>({});
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [confirmPasswordVisible, setConfirmPasswordVisible] = useState(false);
  const authApiDebugDetail = getAuthApiDebugDetail();

  useEffect(() => {
    if (!prefilledEmail) {
      return;
    }

    setEmail((current) => current || prefilledEmail);
  }, [prefilledEmail]);

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

  const canSubmit = useMemo(
    () =>
      email.trim().length > 0 &&
      firstName.trim().length > 0 &&
      lastName.trim().length > 0 &&
      allPasswordRulesMet &&
      password === confirmPassword &&
      Boolean(captchaToken),
    [allPasswordRulesMet, captchaToken, confirmPassword, email, firstName, lastName, password]
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
      captchaToken,
      deviceContext: {
        platform: Platform.OS
      }
    });

    playSuccess();
    router.replace("/(tabs)");
  };

  return (
    <AuthScreen
      focusedInputExtraClearance={
        focusedField === "confirmPassword" && passwordsMatch ? spacing[24] : 0
      }
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
          <View style={styles.narrowBlock}>
            <ErrorState
              title="Registration failed"
              message={formatUnknownError(registerMutation.error)}
              onRetry={handleRegister}
              retryLabel="Try again"
              debugDetail={authApiDebugDetail}
              showDebugDetail={authApiRouteDiagnostics.enabled}
            />
          </View>
        ) : null}

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

          <View style={[styles.nameRow, styles.narrowBlock]}>
            <View style={styles.nameField}>
              <TextField
                label="First Name"
                value={firstName}
                onChangeText={setFirstName}
                placeholder="John"
                dense
                containerStyle={styles.authFieldContainer}
                style={styles.authFieldInput}
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
                dense
                containerStyle={styles.authFieldContainer}
                style={styles.authFieldInput}
                error={errors.lastName}
                onFocus={() => setFocusedField("lastName")}
                forceFocused={focusedField === "lastName"}
              />
            </View>
          </View>

          <View style={styles.narrowBlock}>
            <PasswordField
              label="Password"
              value={password}
              onChangeText={setPassword}
              placeholder="Choose a password"
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

          {allPasswordRulesMet ? (
            <View style={styles.narrowBlock}>
              <Text style={styles.ruleSuccess}>Your password meets our requirements.</Text>
            </View>
          ) : (
            <View style={[styles.ruleList, styles.narrowBlock]}>
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

          <View style={styles.narrowBlock}>
            <PasswordField
              label="Confirm Password"
              value={confirmPassword}
              onChangeText={setConfirmPassword}
              placeholder="Repeat your password"
              dense
              containerStyle={styles.authFieldContainer}
              style={styles.authFieldInput}
              error={errors.confirmPassword}
              onFocus={() => setFocusedField("confirmPassword")}
              forceFocused={focusedField === "confirmPassword"}
              isPasswordVisible={confirmPasswordVisible}
              onPasswordVisibilityChange={setConfirmPasswordVisible}
              autoHideOnBlur={false}
            />
          </View>

          {passwordsMatch ? (
            <View style={styles.narrowBlock}>
              <Text style={styles.matchText}>The confirmed password matches.</Text>
            </View>
          ) : null}

          <CaptchaGate
            token={captchaToken}
            onTokenChange={setCaptchaToken}
            showLabel={false}
          />
        </View>

        <View style={[styles.ctaGroup, styles.narrowBlock]}>
          <SecondaryButton
            label="Back to sign in"
            onPress={() => router.push("/login" as never)}
            style={styles.ctaButton}
          />
          <PrimaryButton
            label="Create account"
            onPress={() => void handleRegister()}
            isLoading={registerMutation.isPending}
            disabled={!canSubmit}
            style={styles.ctaButton}
          />
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
    marginTop: spacing[32],
    gap: spacing[24]
  },
  narrowBlock: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360
  },
  form: {
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
  nameRow: {
    flexDirection: "row",
    gap: spacing[12]
  },
  nameField: {
    flex: 1
  },
  ctaGroup: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  ctaButton: {
    flex: 1
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






