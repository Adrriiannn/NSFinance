import { Ionicons } from "@expo/vector-icons";
import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Platform, Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Path } from "react-native-svg";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { Button } from "../../src/components/ui/buttons/Button";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useRegisterMutation } from "../../src/features/auth/useAuthMutations";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { formatUnknownError } from "../../src/lib/api/errors";
import { authApiRouteDiagnostics, getAuthApiDebugDetail } from "../../src/lib/api/diagnostics";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { palette, spacing, typography } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"fullName" | "email" | "password" | "confirmPassword", string>>;
type FocusField = "fullName" | "email" | "password" | "confirmPassword" | null;

type PasswordRequirement = {
  key: "personalInfo" | "minLength" | "numberOrSymbol";
  label: string;
  isMet: boolean;
};

type PasswordStrength = {
  score: number;
  label: "weak" | "fair" | "good" | "strong" | "very strong";
  color: string;
};

const COMMON_PASSWORDS = new Set([
  "password",
  "password1",
  "123456",
  "12345678",
  "qwerty",
  "letmein",
  "welcome",
  "admin",
  "abc123",
  "iloveyou",
  "secret",
  "football",
  "monkey"
]);

const SEQUENCE_PATTERNS = [
  "0123456789",
  "abcdefghijklmnopqrstuvwxyz",
  "qwertyuiopasdfghjklzxcvbnm"
];

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function normalizeFullName(value: string): string {
  return value.replace(/\s+/g, " ").trim();
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

function extractPersonalFragments(fullName: string, email: string): string[] {
  const nameFragments = normalizeFullName(fullName)
    .toLowerCase()
    .split(" ")
    .filter((part) => part.length >= 3);

  const emailLocalPart = email.trim().toLowerCase().split("@")[0] ?? "";
  const emailFragments = emailLocalPart
    .split(/[._\-+]/)
    .filter((part) => part.length >= 3);

  return Array.from(new Set([...nameFragments, ...emailFragments]));
}

function containsPersonalInfo(password: string, fullName: string, email: string): boolean {
  const normalizedPassword = password.toLowerCase();
  if (!normalizedPassword) {
    return false;
  }

  return extractPersonalFragments(fullName, email).some((fragment) => normalizedPassword.includes(fragment));
}

function evaluatePasswordStrength(password: string, fullName: string, email: string): PasswordStrength {
  if (!password) {
    return { score: 0, label: "weak", color: palette.negative };
  }

  const normalized = password.toLowerCase();
  const length = password.length;
  const digitMatches = password.match(/\d/g) ?? [];
  const symbolMatches = password.match(/[^A-Za-z0-9]/g) ?? [];
  const hasLower = /[a-z]/.test(password);
  const hasUpper = /[A-Z]/.test(password);
  const hasDigit = digitMatches.length > 0;
  const hasSymbol = symbolMatches.length > 0;
  const uniqueChars = new Set(password).size;
  const uniqueRatio = uniqueChars / length;
  const hasLongRepeat = /(.)\1{2,}/.test(password);
  const containsPersonal = containsPersonalInfo(password, fullName, email);
  const isCommon = COMMON_PASSWORDS.has(normalized);

  let score = 0;

  score += clamp(length * 2.7, 0, 30);

  const typeCount = [hasLower, hasUpper, hasDigit, hasSymbol].filter(Boolean).length;
  score += typeCount * 5;

  score += clamp(digitMatches.length * 2, 0, 8);
  score += clamp(symbolMatches.length * 3, 0, 12);

  const hasDigitInMiddle = /\D\d\D/.test(password);
  const hasSymbolInMiddle = /\w[^A-Za-z0-9]\w/.test(password);
  if (hasDigitInMiddle) {
    score += 6;
  }
  if (hasSymbolInMiddle) {
    score += 8;
  }

  score += clamp(uniqueRatio * 14, 0, 14);

  const hasSequentialRun = SEQUENCE_PATTERNS.some((sequence) => {
    for (let index = 0; index <= sequence.length - 4; index += 1) {
      const part = sequence.slice(index, index + 4);
      if (normalized.includes(part) || normalized.includes(part.split("").reverse().join(""))) {
        return true;
      }
    }
    return false;
  });

  if (hasLongRepeat) {
    score -= 12;
  }
  if (hasSequentialRun) {
    score -= 12;
  }
  if (/^[A-Za-z]+$/.test(password) || /^\d+$/.test(password)) {
    score -= 15;
  }
  if (containsPersonal) {
    score -= 22;
  }
  if (isCommon) {
    score -= 30;
  }

  const clampedScore = clamp(Math.round(score), 0, 100);

  if (clampedScore >= 90) {
    return { score: clampedScore, label: "very strong", color: palette.success };
  }
  if (clampedScore >= 75) {
    return { score: clampedScore, label: "strong", color: palette.success };
  }
  if (clampedScore >= 60) {
    return { score: clampedScore, label: "good", color: palette.primaryGlow };
  }
  if (clampedScore >= 40) {
    return { score: clampedScore, label: "fair", color: palette.caution };
  }
  return { score: clampedScore, label: "weak", color: palette.negative };
}

type InsetFieldShellProps = {
  label: string;
  color: string;
  children: ReactNode;
};

const INSET_OUTLINE_RADIUS = 6;
const INSET_OUTLINE_WIDTH = 1;
const INSET_LABEL_LEFT = 18;
const INSET_LABEL_NOTCH_PADDING = 6;
const INSET_LABEL_TOP = -8;
const INSET_BORDER_IDLE = "rgba(164, 191, 234, 0.72)";

function InsetFieldShell({ label, color, children }: InsetFieldShellProps) {
  const [shellWidth, setShellWidth] = useState(0);
  const [shellHeight, setShellHeight] = useState(0);
  const [labelWidth, setLabelWidth] = useState(0);

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
    const preferredGapStart = INSET_LABEL_LEFT - INSET_LABEL_NOTCH_PADDING;
    const preferredGapEnd =
      INSET_LABEL_LEFT + Math.max(labelWidth, 24) + INSET_LABEL_NOTCH_PADDING;

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
  }, [labelWidth, shellHeight, shellWidth]);

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
      <View pointerEvents="none" style={styles.insetFieldLabelChip}>
        <Text
          onLayout={(event) => {
            setLabelWidth(event.nativeEvent.layout.width);
          }}
          style={[styles.insetFieldLabelText, { color: toOpaqueColor(color) }]}
        >
          {label}
        </Text>
      </View>
    </View>
  );
}

function RequirementRow({ label, isMet }: { label: string; isMet: boolean }) {
  const color = isMet ? palette.success : "rgba(190,204,226,0.72)";
  return (
    <View style={styles.requirementRow}>
      <Ionicons name={isMet ? "checkmark" : "close"} size={14} color={color} />
      <Text style={[styles.requirementText, { color }]}>{label}</Text>
    </View>
  );
}

export default function RegisterScreen() {
  const routeParams = useLocalSearchParams<{ email?: string | string[] }>();
  const prefilledEmail = useMemo(() => {
    const rawEmail = Array.isArray(routeParams.email) ? routeParams.email[0] : routeParams.email;
    return typeof rawEmail === "string" ? rawEmail.trim().toLowerCase() : "";
  }, [routeParams.email]);

  const registerMutation = useRegisterMutation();
  const googleSignIn = useGoogleSignIn();
  const { playSuccess } = useFeedbackSound();
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState(prefilledEmail);
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [captchaToken, setCaptchaToken] = useState<string | null>(null);
  const [errors, setErrors] = useState<FormErrors>({});
  const [focusedField, setFocusedField] = useState<FocusField>(null);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [confirmPasswordVisible, setConfirmPasswordVisible] = useState(false);
  const [agreedToTerms, setAgreedToTerms] = useState(false);
  const [socialAuthMessage, setSocialAuthMessage] = useState<string | null>(null);
  const authApiDebugDetail = getAuthApiDebugDetail();

  useEffect(() => {
    if (!prefilledEmail) {
      return;
    }

    setEmail((current) => current || prefilledEmail);
  }, [prefilledEmail]);

  const passwordContainsPersonalInfo = useMemo(
    () => containsPersonalInfo(password, fullName, email),
    [email, fullName, password]
  );
  const passwordStrength = useMemo(
    () => evaluatePasswordStrength(password, fullName, email),
    [email, fullName, password]
  );

  const passwordRequirements = useMemo<PasswordRequirement[]>(
    () => [
      {
        key: "personalInfo",
        label: "Cannot contain your name or email address",
        isMet: password.length > 0 && !passwordContainsPersonalInfo
      },
      {
        key: "minLength",
        label: "At least 8 characters",
        isMet: password.length >= 8
      },
      {
        key: "numberOrSymbol",
        label: "Contains a number or symbol",
        isMet: /\d/.test(password) || /[^A-Za-z0-9]/.test(password)
      }
    ],
    [password, passwordContainsPersonalInfo]
  );

  const allPasswordRequirementsMet = passwordRequirements.every((requirement) => requirement.isMet);
  const hasConfirmInput = confirmPassword.trim().length > 0;
  const passwordsMatch = hasConfirmInput && password === confirmPassword;
  const showPasswordRequirements = focusedField === "password";

  const canSubmit = useMemo(
    () =>
      normalizeFullName(fullName).length > 0 &&
      email.trim().length > 0 &&
      allPasswordRequirementsMet &&
      password === confirmPassword &&
      agreedToTerms &&
      Boolean(captchaToken),
    [agreedToTerms, allPasswordRequirementsMet, captchaToken, confirmPassword, email, fullName, password]
  );

  const validate = () => {
    const nextErrors: FormErrors = {};

    if (!normalizeFullName(fullName)) {
      nextErrors.fullName = "Full name is required.";
    }

    if (!email.trim()) {
      nextErrors.email = "Email is required.";
    }

    if (!allPasswordRequirementsMet) {
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
    const displayName = normalizeFullName(fullName);

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

  const handleGoogleSignIn = async () => {
    setSocialAuthMessage(null);
    const result = await googleSignIn.signInWithGoogle();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setSocialAuthMessage(result.message ?? "Google sign-in failed.");
      }
      return;
    }

    playSuccess();
    router.replace("/(tabs)");
  };

  const handleMicrosoftSignIn = () => {
    setSocialAuthMessage("Microsoft sign-in is coming soon.");
  };

  const fullNameBorderColor =
    errors.fullName ? palette.negative : focusedField === "fullName" ? palette.primaryGlow : INSET_BORDER_IDLE;
  const emailBorderColor =
    errors.email ? palette.negative : focusedField === "email" ? palette.primaryGlow : INSET_BORDER_IDLE;
  const passwordBorderColor =
    errors.password ? palette.negative : focusedField === "password" ? palette.primaryGlow : INSET_BORDER_IDLE;
  const confirmPasswordBorderColor =
    errors.confirmPassword
      ? palette.negative
      : focusedField === "confirmPassword"
        ? palette.primaryGlow
        : INSET_BORDER_IDLE;

  return (
    <AuthScreen focusedInputExtraClearance={focusedField === "confirmPassword" && passwordsMatch ? spacing[24] : 0}>
      <View style={styles.topRow}>
        <View style={styles.headerTextWrap}>
          <Text style={styles.title}>Create an account</Text>
          <Text style={styles.subtitle}>Join us on this amazing journey!</Text>
        </View>
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
            <InsetFieldShell label="Full name" color={fullNameBorderColor}>
              <TextField
                label="Full name"
                value={fullName}
                onChangeText={setFullName}
                placeholder="John Doe"
                showLabel={false}
                dense
                containerStyle={styles.insetFieldContainer}
                style={styles.authFieldInput}
                error={errors.fullName}
                onFocus={() => setFocusedField("fullName")}
                forceFocused={focusedField === "fullName"}
              />
            </InsetFieldShell>
          </View>

          <View style={styles.narrowBlock}>
            <InsetFieldShell label="Email" color={emailBorderColor}>
              <TextField
                label="Email"
                value={email}
                onChangeText={setEmail}
                autoCapitalize="none"
                keyboardType="email-address"
                placeholder="you@example.com"
                showLabel={false}
                dense
                containerStyle={styles.insetFieldContainer}
                style={styles.authFieldInput}
                error={errors.email}
                onFocus={() => setFocusedField("email")}
                forceFocused={focusedField === "email"}
              />
            </InsetFieldShell>
          </View>

          <View style={styles.narrowBlock}>
            <InsetFieldShell label="Password" color={passwordBorderColor}>
              <PasswordField
                label="Password"
                value={password}
                onChangeText={setPassword}
                placeholder="Create a password"
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
          </View>

          {showPasswordRequirements ? (
            <View style={[styles.passwordRequirementCard, styles.narrowBlock]}>
              <View style={styles.requirementRow}>
                <Ionicons
                  name={passwordStrength.score >= 60 ? "checkmark" : "close"}
                  size={14}
                  color={passwordStrength.score >= 60 ? palette.success : "rgba(190,204,226,0.72)"}
                />
                <Text style={styles.requirementText}>
                  Password strength: <Text style={[styles.passwordStrengthValue, { color: passwordStrength.color }]}>{passwordStrength.label}</Text>
                </Text>
              </View>

              {passwordRequirements.map((requirement) => (
                <RequirementRow key={requirement.key} label={requirement.label} isMet={requirement.isMet} />
              ))}
            </View>
          ) : null}

          <View style={styles.narrowBlock}>
            <InsetFieldShell label="Confirm password" color={confirmPasswordBorderColor}>
              <PasswordField
                label="Confirm Password"
                value={confirmPassword}
                onChangeText={setConfirmPassword}
                placeholder="Repeat your password"
                showLabel={false}
                dense
                containerStyle={styles.insetFieldContainer}
                style={styles.authFieldInput}
                error={errors.confirmPassword}
                onFocus={() => setFocusedField("confirmPassword")}
                forceFocused={focusedField === "confirmPassword"}
                isPasswordVisible={confirmPasswordVisible}
                onPasswordVisibilityChange={setConfirmPasswordVisible}
                autoHideOnBlur={false}
              />
            </InsetFieldShell>
          </View>

          <View style={styles.narrowBlock}>
            <View style={styles.termsConsentRow}>
              <Pressable
                accessibilityRole="checkbox"
                accessibilityState={{ checked: agreedToTerms }}
                onPress={() => setAgreedToTerms((current) => !current)}
                style={({ pressed }) => [
                  styles.termsCheckbox,
                  agreedToTerms ? styles.termsCheckboxChecked : null,
                  pressed ? styles.termsCheckboxPressed : null
                ]}
              >
                {agreedToTerms ? <Ionicons name="checkmark" size={14} color={palette.appBackground} /> : null}
              </Pressable>

              <View style={styles.termsConsentTextRow}>
                <Text style={styles.termsConsentText}>I agree to the </Text>
                <Pressable
                  accessibilityRole="link"
                  onPress={() => router.push("/legal/terms" as never)}
                  style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                >
                  <Text style={styles.termsConsentLink}>Terms</Text>
                </Pressable>
                <Text style={styles.termsConsentText}> & </Text>
                <Pressable
                  accessibilityRole="link"
                  onPress={() => router.push("/legal/privacy-policy" as never)}
                  style={({ pressed }) => [pressed ? styles.linkPressed : null]}
                >
                  <Text style={styles.termsConsentLink}>Privacy Policy</Text>
                </Pressable>
              </View>
            </View>
          </View>

          <CaptchaGate token={captchaToken} onTokenChange={setCaptchaToken} showLabel={false} />
        </View>

        <View style={[styles.ctaGroup, styles.narrowBlock]}>
          <PrimaryButton
            label="Sign Up"
            onPress={() => void handleRegister()}
            isLoading={registerMutation.isPending}
            disabled={!canSubmit}
            style={styles.authButton}
          />

          <View style={styles.loginLinkRow}>
            <Text style={styles.loginPrompt}>Already have an account? </Text>
            <Pressable onPress={() => router.push("/login" as never)} style={({ pressed }) => [pressed ? styles.linkPressed : null]}>
              <Text style={styles.loginLink}>Log In</Text>
            </Pressable>
          </View>

          <View style={styles.socialDividerRow}>
            <View style={styles.socialDividerLine} />
            <Text style={styles.socialDividerText}>Or</Text>
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

          {socialAuthMessage ? <Text style={styles.socialAuthMessage}>{socialAuthMessage}</Text> : null}
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
    marginTop: spacing[28],
    gap: spacing[20]
  },
  narrowBlock: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360
  },
  form: {
    gap: spacing[16]
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
    zIndex: 4
  },
  insetFieldLabelText: {
    ...typography.caption,
    fontWeight: "600"
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
  passwordRequirementCard: {
    gap: spacing[6],
    marginTop: -spacing[4]
  },
  requirementRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  requirementText: {
    ...typography.caption,
    color: "rgba(190,204,226,0.72)"
  },
  passwordStrengthValue: {
    ...typography.caption,
    fontWeight: "600"
  },
  termsConsentRow: {
    minHeight: 28,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-start",
    gap: spacing[10]
  },
  termsCheckbox: {
    width: 20,
    height: 20,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(21,21,21,0.74)",
    alignItems: "center",
    justifyContent: "center"
  },
  termsCheckboxPressed: {
    opacity: 0.86
  },
  termsCheckboxChecked: {
    borderColor: palette.success,
    backgroundColor: palette.success
  },
  termsConsentTextRow: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap"
  },
  termsConsentText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  termsConsentLink: {
    color: palette.primaryGlow,
    ...typography.body2,
    fontWeight: "600"
  },
  ctaGroup: {
    gap: spacing[14]
  },
  authButton: {
    flex: 1,
    borderRadius: 6,
    minHeight: 44
  },
  loginLinkRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center"
  },
  loginPrompt: {
    color: palette.textSecondary,
    ...typography.body2
  },
  loginLink: {
    color: palette.primaryGlow,
    ...typography.body2,
    fontWeight: "600"
  },
  socialDividerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[10]
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
  socialAuthRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[10]
  },
  socialAuthMessage: {
    color: palette.negative,
    ...typography.caption
  },
  linkPressed: {
    opacity: 0.75
  }
});
