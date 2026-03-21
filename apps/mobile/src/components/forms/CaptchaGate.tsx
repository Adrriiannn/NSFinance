import { Ionicons } from "@expo/vector-icons";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

// TODO: Replace this placeholder gate with real Turnstile/reCAPTCHA mobile flow once provider integration is selected.
type CaptchaGateProps = {
  isVerified: boolean;
  onVerify: () => void;
  showLabel?: boolean;
};

export function CaptchaGate({ isVerified, onVerify, showLabel = true }: CaptchaGateProps) {
  return (
    <View style={styles.wrap}>
      {showLabel ? <Text style={styles.label}>Security check</Text> : null}
      <Pressable style={({ pressed }) => [styles.card, pressed ? styles.pressed : null]} onPress={onVerify}>
        <View style={[styles.checkbox, isVerified ? styles.checkboxVerified : null]}>
          {isVerified ? <Ionicons name="checkmark" size={14} color={palette.appBackground} /> : null}
        </View>
        <View style={styles.body}>
          <Text style={styles.title}>Verify you are human</Text>
          <Text style={styles.meta}>Captcha foundation placeholder (Turnstile/reCAPTCHA mobile integration next).</Text>
        </View>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing[8]
  },
  label: {
    color: palette.textPrimary,
    ...typography.caption
  },
  card: {
    alignSelf: "center",
    width: "88%",
    maxWidth: 360,
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    borderRadius: 14,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[12]
  },
  checkbox: {
    width: 20,
    height: 20,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(255,255,255,0.04)",
    alignItems: "center",
    justifyContent: "center"
  },
  checkboxVerified: {
    borderColor: palette.success,
    backgroundColor: palette.success
  },
  body: {
    flex: 1,
    gap: spacing[4]
  },
  title: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  meta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  pressed: {
    opacity: 0.9
  }
});
