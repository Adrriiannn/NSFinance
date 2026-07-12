import { Ionicons } from "@expo/vector-icons";
import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Path } from "react-native-svg";
import { CaptchaGate } from "../../src/components/forms/CaptchaGate";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { AuthLegalLinks } from "../../src/components/ui/AuthLegalLinks";
import { PasswordField } from "../../src/components/ui/PasswordField";
import { Button } from "../../src/components/ui/buttons/Button";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { checkPasswordPolicy } from "../../src/features/auth/authApi";
import {
  type PasswordBreachStatus,
  type PasswordStrengthResult,
  evaluatePasswordStrength,
  hasNumberOrSymbol,
  enforcePasswordMaxLength,
  isLengthWithinPolicy,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from "../../src/features/auth/passwordPolicy";
import { useRegisterMutation } from "../../src/features/auth/useAuthMutations";
import { useGoogleSignIn } from "../../src/features/auth/useGoogleSignIn";
import { useMicrosoftSignIn } from "../../src/features/auth/useMicrosoftSignIn";
import { stageEmailVerification, stageMfaLogin } from "../../src/features/auth/pendingAuthFlow";
import { usePrivacyPolicyQuery, useTermsPolicyQuery } from "../../src/features/policies/usePolicies";
import { formatUnknownError } from "../../src/lib/api/errors";
import { buildDeviceContext } from "../../src/lib/device/deviceIdentity";
import { getLocaleLocationProfile } from "../../src/lib/device/deviceLocationProfile";
import { useFeedbackSound } from "../../src/lib/sound/useFeedbackSound";
import { useAuthSession } from "../../src/providers/AuthProvider";
import { controls, palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";

type FormErrors = Partial<Record<"fullName" | "email" | "password" | "confirmPassword", string>>;
type FocusField = "fullName" | "email" | "password" | "confirmPassword" | null;

type PasswordRequirement = {
  key: "breached" | "length" | "numberOrSymbol";
  label: string;
  state: "neutral" | "pending" | "met" | "unmet" | "error";
};

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function normalizeFullName(value: string): string {
  return value.replace(/\s+/g, " ").trim();
}

function stripWhitespace(value: string): string {
  return value.replace(/\s+/g, "");
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

type InsetFieldShellProps = {
  label: string;
  color: string;
  children: ReactNode;
};

const INSET_OUTLINE_RADIUS = 6;
const INSET_OUTLINE_WIDTH = 1;
const INSET_LABEL_LEFT = 20;
const INSET_NOTCH_OFFSET_X = -2;
const INSET_LABEL_NOTCH_PADDING = 5;
const INSET_LABEL_NOTCH_SAFETY_BUFFER = 0;
const INSET_LABEL_TOP = -8;
const INSET_LABEL_CHAR_WIDTH_ESTIMATE = 7.6;

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

function getStrengthColor(strength: PasswordStrengthResult | null): string {
  if (!strength) {
    return palette.textSecondary;
  }

  switch (strength.tier) {
    case "very_weak":
      return palette.negative;
    case "weak":
      return palette.caution;
    case "fair":
      return palette.caution;
    case "strong":
      return palette.success;
    case "very_strong":
      return palette.success;
    default:
      return palette.textSecondary;
  }
}

function RequirementRow({ label, state }: { label: string; state: PasswordRequirement["state"] }) {
  const iconName =
    state === "met" ? "checkmark" : state === "pending" ? "time-outline" : "close";
  const color =
    state === "met"
      ? palette.success
      : state === "error"
        ? palette.negative
        : state === "pending"
          ? palette.caution
          : palette.textSecondary;

  return (
    <View style={styles.requirementRow}>
      <Ionicons name={iconName} size={14} color={color} />
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
  const microsoftSignIn = useMicrosoftSignIn();
  const termsQuery = useTermsPolicyQuery();
  const privacyQuery = usePrivacyPolicyQuery();
  const { applyAuthTokenResponse, isAuthTransitioning } = useAuthSession();
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
  useEffect(() => {
    if (!prefilledEmail) {
      return;
    }

    setEmail((current) => current || prefilledEmail);
  }, [prefilledEmail]);

  const [passwordBreachStatus, setPasswordBreachStatus] = useState<PasswordBreachStatus>("idle");
  const [passwordBreachMessage, setPasswordBreachMessage] = useState<string | null>(null);

  const passwordStrength = useMemo(() => evaluatePasswordStrength(password), [password]);
  const hasNumberOrSymbolRequirement = useMemo(() => hasNumberOrSymbol(password), [password]);
  const isLengthValidRequirement = useMemo(() => isLengthWithinPolicy(password), [password]);
  const hasPasswordReachedMaximum = password.length >= PASSWORD_MAX_LENGTH;

  useEffect(() => {
    if (!password || !isLengthValidRequirement || !hasNumberOrSymbolRequirement) {
      setPasswordBreachStatus("idle");
      setPasswordBreachMessage(null);
      return;
    }

    let cancelled = false;
    const timer = setTimeout(async () => {
      setPasswordBreachStatus("checking");
      setPasswordBreachMessage(null);

      try {
        const response = await checkPasswordPolicy({ password });
        if (cancelled) {
          return;
        }

        if (response.breachStatus === "compromised") {
          setPasswordBreachStatus("compromised");
          setPasswordBreachMessage("This password has appeared in known data breaches.");
          return;
        }

        if (response.breachStatus === "unavailable") {
          setPasswordBreachStatus("unavailable");
          setPasswordBreachMessage("Could not verify compromised-password status right now.");
          return;
        }

        setPasswordBreachStatus("safe");
        setPasswordBreachMessage(null);
      } catch {
        if (!cancelled) {
          setPasswordBreachStatus("unavailable");
          setPasswordBreachMessage("Could not verify compromised-password status right now.");
        }
      }
    }, 550);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [hasNumberOrSymbolRequirement, isLengthValidRequirement, password]);

  const passwordRequirements = useMemo<PasswordRequirement[]>(() => {
    const breachedState: PasswordRequirement["state"] =
      !password
        ? "neutral"
        : !isLengthValidRequirement || !hasNumberOrSymbolRequirement
        ? "neutral"
        : passwordBreachStatus === "safe"
          ? "met"
          : passwordBreachStatus === "checking"
            ? "pending"
            : passwordBreachStatus === "compromised" || passwordBreachStatus === "unavailable"
              ? "error"
              : "unmet";

    const lengthState: PasswordRequirement["state"] =
      !password
        ? "neutral"
        : password.length > PASSWORD_MAX_LENGTH
          ? "error"
          : isLengthValidRequirement
            ? "met"
            : "unmet";

    const numberState: PasswordRequirement["state"] =
      !password ? "neutral" : hasNumberOrSymbolRequirement ? "met" : "unmet";

    return [
      {
        key: "breached",
        label: "Not found in common or breached password lists",
        state: breachedState
      },
      {
        key: "length",
        label: `${PASSWORD_MIN_LENGTH} to ${PASSWORD_MAX_LENGTH} characters`,
        state: lengthState
      },
      {
        key: "numberOrSymbol",
        label: "Contains a number or symbol",
        state: numberState
      }
    ];
  }, [hasNumberOrSymbolRequirement, isLengthValidRequirement, password, passwordBreachStatus]);

  const allPasswordRequirementsMet =
    isLengthValidRequirement &&
    hasNumberOrSymbolRequirement &&
    passwordBreachStatus === "safe";
  const hasConfirmInput = confirmPassword.trim().length > 0;
  const passwordsMatch = hasConfirmInput && password === confirmPassword;
  const showPasswordRequirements = focusedField === "password" || password.length > 0;

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

    if (!isLengthValidRequirement) {
      nextErrors.password = `Use ${PASSWORD_MIN_LENGTH} to ${PASSWORD_MAX_LENGTH} characters.`;
    } else if (!hasNumberOrSymbolRequirement) {
      nextErrors.password = "Add a number or symbol.";
    } else if (passwordBreachStatus === "compromised") {
      nextErrors.password = "This password has appeared in known data breaches.";
    } else if (passwordBreachStatus === "unavailable" || passwordBreachStatus === "checking") {
      nextErrors.password = "Could not verify password safety right now. Please try again.";
    }

    if (password !== confirmPassword) {
      nextErrors.confirmPassword = "Passwords do not match.";
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleRegister = async () => {
    if (isAuthTransitioning) {
      return;
    }

    if (!validate()) {
      return;
    }

    const localeProfile = getLocaleLocationProfile();
    const timezone = localeProfile.timezone ?? (Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC");
    const locale = localeProfile.localeTag ?? (Intl.DateTimeFormat().resolvedOptions().locale || "en-US");
    const preferredCurrency = localeProfile.currencyCode ?? "EUR";
    const displayName = normalizeFullName(fullName);
    const termsVersion = termsQuery.data?.version;
    const privacyVersion = privacyQuery.data?.version;
    if (!termsVersion || !privacyVersion) {
      setSocialAuthMessage("Could not load the current Terms and Privacy Policy. Please try again.");
      return;
    }

    const delivery = await registerMutation.mutateAsync({
      email: email.trim().toLowerCase(),
      password,
      displayName,
      timezone,
      locale,
      preferredCurrency,
      captchaToken,
      deviceContext: buildDeviceContext(),
      acceptPolicies: true,
      termsVersion,
      privacyVersion
    });

    stageEmailVerification({
      ...delivery,
      email: email.trim().toLowerCase()
    });
    router.push("/(auth)/verify-email" as never);
  };

  const handleGoogleSignIn = async () => {
    if (isAuthTransitioning) {
      setSocialAuthMessage("Finishing sign-out. Please try again in a moment.");
      return;
    }

    if (!agreedToTerms) {
      setSocialAuthMessage("Agree to the Terms and Privacy Policy to continue.");
      return;
    }

    setSocialAuthMessage(null);
    const result = await googleSignIn.signInWithGoogle();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setSocialAuthMessage(result.message ?? "Google sign-in failed.");
      }
      return;
    }

    const flow = result.flow;
    if (flow?.status === "authenticated" && flow.session) {
      await applyAuthTokenResponse(flow.session);
      playSuccess();
      router.replace("/(tabs)");
      return;
    }

    if (flow?.status === "email_verification_required" && flow.emailVerification) {
      stageEmailVerification(flow.emailVerification);
      router.push("/(auth)/verify-email" as never);
      return;
    }

    if (flow?.status === "mfa_required" && flow.mfaChallenge) {
      stageMfaLogin(flow.mfaChallenge);
      router.push("/(auth)/mfa" as never);
      return;
    }

    setSocialAuthMessage("Google sign-in returned an incomplete response. Please try again.");
  };

  const handleMicrosoftSignIn = async () => {
    if (isAuthTransitioning) {
      setSocialAuthMessage("Finishing sign-out. Please try again in a moment.");
      return;
    }

    if (!agreedToTerms) {
      setSocialAuthMessage("Agree to the Terms and Privacy Policy to continue.");
      return;
    }

    setSocialAuthMessage(null);
    const result = await microsoftSignIn.signInWithMicrosoft();
    if (!result.succeeded) {
      if (!result.cancelled) {
        setSocialAuthMessage(result.message ?? "Microsoft sign-in failed.");
      }
      return;
    }

    const flow = result.flow;
    if (flow?.status === "authenticated" && flow.session) {
      await applyAuthTokenResponse(flow.session);
      playSuccess();
      router.replace("/(tabs)");
      return;
    }

    if (flow?.status === "email_verification_required" && flow.emailVerification) {
      stageEmailVerification(flow.emailVerification);
      router.push("/(auth)/verify-email" as never);
      return;
    }

    if (flow?.status === "mfa_required" && flow.mfaChallenge) {
      stageMfaLogin(flow.mfaChallenge);
      router.push("/(auth)/mfa" as never);
      return;
    }

    setSocialAuthMessage("Microsoft sign-in returned an incomplete response. Please try again.");
  };

  const fullNameBorderColor =
    errors.fullName ? palette.negative : focusedField === "fullName" ? palette.primaryGlow : palette.borderStrong;
  const emailBorderColor =
    errors.email ? palette.negative : focusedField === "email" ? palette.primaryGlow : palette.borderStrong;
  const passwordBorderColor =
    errors.password ? palette.negative : focusedField === "password" ? palette.primaryGlow : palette.borderStrong;
  const confirmPasswordBorderColor =
    errors.confirmPassword
      ? palette.negative
      : focusedField === "confirmPassword"
        ? palette.primaryGlow
        : palette.borderStrong;

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
                onChangeText={(value) => setEmail(stripWhitespace(value))}
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
                onChangeText={(value) => {
                  const sanitized = stripWhitespace(value);
                  setPassword(enforcePasswordMaxLength(sanitized));
                }}
                placeholder="Create a password"
                maxLength={PASSWORD_MAX_LENGTH}
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
                  name={passwordStrength ? "checkmark" : "remove"}
                  size={14}
                  color={getStrengthColor(passwordStrength)}
                />
                <Text style={styles.requirementText}>
                  Password strength:{" "}
                  <Text style={[styles.passwordStrengthValue, { color: getStrengthColor(passwordStrength) }]}>
                    {passwordStrength?.label ?? "Not calculated yet"}
                  </Text>
                </Text>
              </View>

              {passwordRequirements.map((requirement) => (
                <RequirementRow key={requirement.key} label={requirement.label} state={requirement.state} />
              ))}

              {hasPasswordReachedMaximum ? (
                <Text style={styles.passwordLengthLimitWarning}>
                  You&apos;ve reached the maximum password length of {PASSWORD_MAX_LENGTH} characters.
                </Text>
              ) : null}

              {passwordBreachMessage ? <Text style={styles.passwordBreachMessage}>{passwordBreachMessage}</Text> : null}
            </View>
          ) : null}

          <View style={styles.narrowBlock}>
            <InsetFieldShell label="Confirm password" color={confirmPasswordBorderColor}>
              <PasswordField
                label="Confirm Password"
                value={confirmPassword}
                onChangeText={(value) => {
                  const sanitized = stripWhitespace(value);
                  setConfirmPassword(enforcePasswordMaxLength(sanitized));
                }}
                placeholder="Repeat your password"
                maxLength={PASSWORD_MAX_LENGTH}
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
                {agreedToTerms ? <Ionicons name="checkmark" size={14} color={palette.primary} /> : null}
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
            disabled={!canSubmit || isAuthTransitioning}
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
    color: palette.textSecondary
  },
  passwordStrengthValue: {
    ...typography.caption,
    fontWeight: "600"
  },
  passwordLengthLimitWarning: {
    ...typography.caption,
    color: palette.negative
  },
  passwordBreachMessage: {
    ...typography.caption,
    color: palette.negative
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
    backgroundColor: "transparent",
    alignItems: "center",
    justifyContent: "center"
  },
  termsCheckboxPressed: {
    opacity: 0.86
  },
  termsCheckboxChecked: {
    borderColor: palette.primary,
    backgroundColor: controls.activeFill
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
}));
