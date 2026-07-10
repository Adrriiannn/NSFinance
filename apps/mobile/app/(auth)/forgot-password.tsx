import { Ionicons } from "@expo/vector-icons";
import { router } from "expo-router";
import { useMemo, useState, type ReactNode } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import Svg, { Path } from "react-native-svg";
import { ErrorState } from "../../src/components/feedback/ErrorState";
import { AuthScreen } from "../../src/components/layout/AuthScreen";
import { PrimaryButton } from "../../src/components/ui/PrimaryButton";
import { TextField } from "../../src/components/ui/TextField";
import { useForgotPasswordMutation } from "../../src/features/auth/useAuthMutations";
import { formatUnknownError } from "../../src/lib/api/errors";
import { palette, spacing, typography, createRuntimeStyleSheet } from "../../src/theme/tokens";

const INSET_OUTLINE_RADIUS = 6;
const INSET_OUTLINE_WIDTH = 1;
const INSET_LABEL_LEFT = 20;
const INSET_NOTCH_OFFSET_X = -10;
const INSET_LABEL_NOTCH_PADDING = -3;
const INSET_LABEL_NOTCH_SAFETY_BUFFER = 0;
const INSET_LABEL_TOP = -8;
const INSET_LABEL_CHAR_WIDTH_ESTIMATE = 7.6;

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
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

export default function ForgotPasswordScreen() {
  const forgotMutation = useForgotPasswordMutation();
  const [identity, setIdentity] = useState("");
  const [focusedIdentity, setFocusedIdentity] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const identityBorderColor = focusedIdentity ? palette.primaryGlow : palette.borderStrong;

  const handleRequest = async () => {
    const response = await forgotMutation.mutateAsync({
      email: identity.trim().toLowerCase()
    });
    setMessage(response.message);
  };

  return (
    <AuthScreen>
      <View style={styles.centerWrap}>
        <View style={styles.header}>
          <Text style={styles.title}>Forgot password?</Text>
          <Text style={styles.subtitle}>Enter your registered email or phone number to reset your password.</Text>
        </View>

        {forgotMutation.isError ? (
          <ErrorState
            title="Request failed"
            message={formatUnknownError(forgotMutation.error)}
            onRetry={handleRequest}
            retryLabel="Try again"
          />
        ) : null}

        <View style={styles.form}>
          <InsetFieldShell label="Email or phone" color={identityBorderColor}>
            <TextField
              label="Email or phone"
              value={identity}
              onChangeText={setIdentity}
              autoCapitalize="none"
              keyboardType="default"
              placeholder="Enter your email or phone"
              dense
              showLabel={false}
              containerStyle={styles.insetFieldContainer}
              style={styles.authFieldInput}
              onFocus={() => setFocusedIdentity(true)}
              onBlur={() => setFocusedIdentity(false)}
              forceFocused={focusedIdentity}
            />
          </InsetFieldShell>
        </View>

        {message ? <Text style={styles.message}>{message}</Text> : null}

        <View style={styles.actions}>
          <PrimaryButton
            label="Send OTP Code"
            onPress={() => void handleRequest()}
            isLoading={forgotMutation.isPending}
            disabled={!identity.trim()}
            style={styles.otpButton}
          />
          <Pressable
            onPress={() => router.push("/login" as never)}
            style={({ pressed }) => [styles.backToLogin, pressed ? styles.backToLoginPressed : null]}
          >
            <Ionicons name="arrow-back" size={16} color={palette.primaryGlow} />
            <Text style={styles.backToLoginText}>Back to login</Text>
          </Pressable>
        </View>
      </View>
    </AuthScreen>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  centerWrap: {
    flex: 1,
    marginTop: spacing[32],
    alignItems: "center"
  },
  header: {
    width: "88%",
    maxWidth: 360,
    gap: spacing[8],
    alignItems: "center"
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
  form: {
    marginTop: spacing[20],
    gap: spacing[12],
    width: "88%",
    maxWidth: 360
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
    paddingVertical: 0
  },
  message: {
    marginTop: spacing[16],
    color: palette.textSecondary,
    ...typography.body2,
    width: "88%",
    maxWidth: 360
  },
  actions: {
    marginTop: spacing[20],
    gap: spacing[12],
    width: "88%",
    maxWidth: 360,
    alignItems: "stretch"
  },
  otpButton: {
    width: "100%",
    minHeight: 44,
    borderRadius: 6
  },
  backToLogin: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[6],
    minHeight: 32,
    alignSelf: "center"
  },
  backToLoginPressed: {
    opacity: 0.8
  },
  backToLoginText: {
    color: palette.textPrimary,
    ...typography.body2
  }
}));

